"""Local, explicitly requested image inference. No networking or screen capture."""
import argparse
import base64
import io
import json
import sys
import time
from pathlib import Path

import cv2
import numpy as np
import openvino as ov
from PIL import Image


class PrismNpu:
    def __init__(self, root, device="NPU"):
        self.root = Path(root)
        self.core = ov.Core()
        if device not in self.core.available_devices:
            raise RuntimeError("所选推理设备不可用，请检查 NPU 驱动。")
        self.device = device

    def compile(self, name, shape=None):
        model = self.core.read_model(self.root / "models" / name)
        if shape:
            model.reshape({model.input(0): shape})
        compiled = self.core.compile_model(model, self.device, {
            "PERFORMANCE_HINT": "LATENCY", "CACHE_DIR": str(self.root / "cache")})
        devices = compiled.get_property("EXECUTION_DEVICES")
        if isinstance(devices, str):
            devices = [devices]
        if not devices or any(not d.startswith(self.device) for d in devices):
            raise RuntimeError("模型未在所选设备上执行。")
        return model, compiled

    def ocr(self, image):
        started = time.perf_counter()
        _, detector = self.compile("ocr-det.onnx", [1, 3, 960, 960])
        model, recognizer = self.compile("ocr-rec.onnx", [1, 3, 48, 640])
        characters = [""] + model.get_rt_info()["framework"]["character"].value.splitlines() + [" "]
        height, width = image.shape[:2]
        resized = cv2.resize(image, (960, 960)).astype(np.float32) / 255
        normalized = (resized - np.array([.485, .456, .406], dtype=np.float32)) / np.array([.229, .224, .225], dtype=np.float32)
        probability = detector([normalized.transpose(2, 0, 1)[None]])[detector.output(0)][0, 0]
        contours, _ = cv2.findContours((probability > .3).astype(np.uint8), cv2.RETR_LIST, cv2.CHAIN_APPROX_SIMPLE)
        boxes = []
        for contour in contours:
            (cx, cy), (w, h), angle = cv2.minAreaRect(contour)
            if min(w, h) < 3:
                continue
            mask = np.zeros(probability.shape, np.uint8)
            cv2.fillPoly(mask, [contour], 1)
            if cv2.mean(probability, mask)[0] < .5:
                continue
            padding = w * h * 1.5 / (2 * (w + h))
            points = cv2.boxPoints(((cx, cy), (w + 2 * padding, h + 2 * padding), angle))
            points *= [width / 960, height / 960]
            points[:, 0] = np.clip(points[:, 0], 0, width - 1)
            points[:, 1] = np.clip(points[:, 1], 0, height - 1)
            boxes.append(points)
        if len(boxes) > 256:
            raise RuntimeError("文字区域过多，请缩小要识别的画面。")
        boxes.sort(key=lambda p: (round(float(p[:, 1].min()) / 12), float(p[:, 0].min())))
        lines = []
        for points in boxes:
            points = points[np.argsort(points[:, 0])]
            left = points[:2][np.argsort(points[:2, 1])]
            right = points[2:][np.argsort(points[2:, 1])]
            ordered = np.array([left[0], right[0], right[1], left[1]], np.float32)
            w = max(2, round(max(np.linalg.norm(ordered[0] - ordered[1]), np.linalg.norm(ordered[2] - ordered[3]))))
            h = max(2, round(max(np.linalg.norm(ordered[0] - ordered[3]), np.linalg.norm(ordered[1] - ordered[2]))))
            transform = cv2.getPerspectiveTransform(ordered, np.array([[0, 0], [w - 1, 0], [w - 1, h - 1], [0, h - 1]], np.float32))
            crop = cv2.warpPerspective(image, transform, (w, h), borderMode=cv2.BORDER_REPLICATE)
            if h > w * 1.5:
                crop = cv2.rotate(crop, cv2.ROTATE_90_COUNTERCLOCKWISE)
            rw = min(640, max(1, int(np.ceil(48 * crop.shape[1] / crop.shape[0]))))
            tensor = np.zeros((1, 3, 48, 640), np.float32)
            tensor[0, :, :, :rw] = (cv2.resize(crop, (rw, 48)).astype(np.float32).transpose(2, 0, 1) / 127.5) - 1
            prediction = recognizer([tensor])[recognizer.output(0)][0]
            if prediction.shape[1] != len(characters):
                raise RuntimeError("识别模型与字符表不匹配。")
            tokens = prediction.argmax(axis=1)
            keep = (tokens != 0) & np.r_[True, tokens[1:] != tokens[:-1]]
            text = "".join(characters[int(token)] for token in tokens[keep])
            confidence = float(prediction.max(axis=1)[keep].mean()) if keep.any() else 0
            if text.strip() and confidence >= .5:
                lines.append({"text": text, "confidence": round(confidence, 4), "box": ordered.round().astype(int).tolist()})
        return {"device": self.device, "text": "\n".join(line["text"] for line in lines), "lines": lines,
                "elapsedMs": round((time.perf_counter() - started) * 1000)}

    def enhance(self, image):
        _, model = self.compile("super-resolution.xml")
        low = cv2.resize(image, (480, 270), interpolation=cv2.INTER_AREA)
        bicubic = cv2.resize(low, (1920, 1080), interpolation=cv2.INTER_CUBIC)
        inputs = [picture.astype(np.float32).transpose(2, 0, 1)[None] for picture in (low, bicubic)]
        started = time.perf_counter()
        output = model(inputs)[model.output(0)][0].transpose(1, 2, 0)
        return np.clip(output * 255, 0, 255).astype(np.uint8), bicubic, round((time.perf_counter() - started) * 1000, 2)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, required=True)
    args = parser.parse_args()
    raw = sys.stdin.buffer.read(24 * 1024 * 1024 + 1)
    if len(raw) > 24 * 1024 * 1024:
        raise ValueError("图片请求过大。")
    request = json.loads(raw)
    if set(request) != {"image"} or not isinstance(request["image"], str):
        raise ValueError("图片请求无效。")
    encoded = base64.b64decode(request["image"], validate=True)
    with Image.open(io.BytesIO(encoded)) as header:
        if min(header.size) < 16 or header.width * header.height > 16777216:
            raise ValueError("图片尺寸无效。")
    image = cv2.imdecode(np.frombuffer(encoded, np.uint8), cv2.IMREAD_COLOR)
    if image is None or min(image.shape[:2]) < 16 or image.shape[0] * image.shape[1] > 16777216:
        raise ValueError("图片尺寸无效。")
    return PrismNpu(args.root).ocr(image)


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    try:
        print(json.dumps({"ok": True, **main()}, ensure_ascii=False))
    except Exception as error:
        print(json.dumps({"ok": False, "message": str(error)}, ensure_ascii=False))
        sys.exit(1)
