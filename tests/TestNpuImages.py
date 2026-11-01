"""Real-model accuracy and enhancement checks using a generated, nonprivate screen."""
import argparse
import json
import sys
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont

sys.stdout.reconfigure(encoding="utf-8")
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools" / "PrismNpu"))
from main import PrismNpu

parser = argparse.ArgumentParser()
parser.add_argument("--root", type=Path, required=True)
parser.add_argument("--output", type=Path, required=True)
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)
canvas = Image.new("RGB", (1920, 1080), (235, 242, 250))
draw = ImageDraw.Draw(canvas)
font = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 54)
small = ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 28)
expected = ["冰川测试124", "PrismGlass NPU 2026", "中文识别与屏幕文字提取", "设置保留，配对正常。"]
for i, line in enumerate(expected):
    draw.text((100, 100 + i * 110), line, fill=(20, 36, 55), font=font)
draw.text((100, 650), "小字清晰度测试：abcdefghijklmnopqrstuvwxyz 0123456789", fill=(30, 50, 75), font=small)
for i in range(16):
    draw.ellipse((1100 + i % 4 * 150, 520 + i // 4 * 120, 1210 + i % 4 * 150, 610 + i // 4 * 120), fill=(30 + i * 10, 120, 210 - i * 6))
canvas.save(args.output / "source.png")
source = cv2.cvtColor(np.array(canvas), cv2.COLOR_RGB2BGR)
report = {}
for device in ["CPU", "NPU"]:
    engine = PrismNpu(args.root, device)
    result = engine.ocr(source)
    report[device] = result
    print(json.dumps(result, ensure_ascii=False), flush=True)
    for line in expected:
        assert line in result["text"], f"{device} OCR missed generated text: {line}"
enhanced, bicubic, elapsed = engine.enhance(source)
cv2.imwrite(str(args.output / "enhanced-npu.png"), enhanced)
cv2.imwrite(str(args.output / "bicubic.png"), bicubic)
def psnr(image):
    mse = np.mean((source.astype(np.float32) - image.astype(np.float32)) ** 2)
    return round(float(10 * np.log10(255 ** 2 / mse)), 3)
report["enhancement"] = {"inferenceMs": elapsed, "sourceSize": [1920, 1080], "modelInputSize": [480, 270],
    "bicubicPsnr": psnr(bicubic), "npuPsnr": psnr(enhanced), "realTime30fpsBudgetMs": 33.33}
(args.output / "images.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(report["enhancement"], ensure_ascii=False))
