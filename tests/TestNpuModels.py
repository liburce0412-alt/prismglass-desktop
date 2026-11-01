"""Opt-in real NPU/CPU comparison; synthetic inputs only, no screen capture."""
import argparse
import json
import sys
import time
from pathlib import Path

import numpy as np
import openvino as ov

sys.stdout.reconfigure(encoding="utf-8")
parser = argparse.ArgumentParser()
parser.add_argument("--root", type=Path, required=True)
parser.add_argument("--output", type=Path, required=True)
args = parser.parse_args()
core = ov.Core()
report = {"runtime": ov.__version__, "devices": core.available_devices, "models": []}
assert "NPU" in core.available_devices, "NPU unavailable; CPU fallback is not a pass"
report["npu"] = core.get_property("NPU", "FULL_DEVICE_NAME")
for name, shape in [("ocr-det.onnx", [1, 3, 960, 960]), ("ocr-rec.onnx", [1, 3, 48, 640]), ("super-resolution.xml", None)]:
    model = core.read_model(args.root / "models" / name)
    if shape:
        model.reshape({model.input(0): shape})
    rng = np.random.default_rng(42)
    inputs = [rng.random(list(port.shape), dtype=np.float32) for port in model.inputs]
    baseline = None
    for device in ["CPU", "NPU"]:
        record = {"model": name, "device": device}
        print(json.dumps({"compiling": name, "device": device}), flush=True)
        try:
            started = time.perf_counter()
            compiled = core.compile_model(model, device, {"PERFORMANCE_HINT": "LATENCY", "CACHE_DIR": str(args.root / "cache")})
            record["compileMs"] = round((time.perf_counter() - started) * 1000, 2)
            record["executionDevices"] = compiled.get_property("EXECUTION_DEVICES")
            if isinstance(record["executionDevices"], str):
                record["executionDevices"] = [record["executionDevices"]]
            assert all(d.startswith(device) for d in record["executionDevices"]), "Unexpected execution device"
            request = compiled.create_infer_request()
            request.infer(inputs)
            times = []
            for _ in range(10):
                started = time.perf_counter()
                request.infer(inputs)
                times.append((time.perf_counter() - started) * 1000)
            output = request.get_output_tensor().data.copy()
            assert np.isfinite(output).all(), "Non-finite inference output"
            record.update(medianMs=round(float(np.median(times)), 2), p95Ms=round(float(np.percentile(times, 95)), 2), outputShape=list(output.shape))
            if device == "CPU":
                baseline = output
            else:
                record["meanAbsoluteDifferenceFromCpu"] = float(np.mean(np.abs(output - baseline)))
            record["ok"] = True
        except Exception as error:
            record.update(ok=False, error=str(error))
        report["models"].append(record)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
        print(json.dumps(record, ensure_ascii=False), flush=True)
assert all(item["ok"] for item in report["models"]), "One or more real inference checks failed; inspect report"
