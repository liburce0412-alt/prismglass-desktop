"""Opt-in Codex window snapshot experiment; images stay in memory, no input forwarding."""
import argparse
import ctypes as ct
from ctypes import wintypes as wt
import json
from pathlib import Path
import queue
import threading
import time

import cv2
import numpy as np
from PIL import Image, ImageTk

from main import PrismNpu


def codex_package(name):
    return name.startswith("OpenAI.Codex_") and name.endswith("__2p2nqsd0c76g0")


def window_identity(hwnd):
    user, kernel = ct.windll.user32, ct.windll.kernel32
    user.GetWindowThreadProcessId.argtypes = [wt.HWND, ct.POINTER(wt.DWORD)]
    user.IsWindowVisible.argtypes = user.IsIconic.argtypes = [wt.HWND]
    user.GetWindowRect.argtypes = [wt.HWND, ct.POINTER(wt.RECT)]
    user.GetWindowLongPtrW.argtypes = [wt.HWND, ct.c_int]
    user.GetWindowLongPtrW.restype = ct.c_ssize_t
    kernel.OpenProcess.argtypes = [wt.DWORD, wt.BOOL, wt.DWORD]
    kernel.OpenProcess.restype = wt.HANDLE
    kernel.CloseHandle.argtypes = [wt.HANDLE]
    kernel.GetPackageFullName.argtypes = [wt.HANDLE, ct.POINTER(ct.c_uint32), wt.LPWSTR]
    if not user.IsWindowVisible(hwnd) or user.IsIconic(hwnd):
        raise ValueError("请让目标 Codex 窗口保持打开且未最小化。")
    if user.GetWindowLongPtrW(hwnd, -20) & 0x80:
        raise ValueError("请选择 Codex 主窗口，不能选择工具浮层。")
    pid = wt.DWORD()
    if not user.GetWindowThreadProcessId(hwnd, ct.byref(pid)):
        raise ValueError("Codex 窗口已关闭。")
    process = kernel.OpenProcess(0x1000, False, pid.value)
    if not process:
        raise ValueError("无法核对 Codex 窗口所属进程。")
    try:
        length = ct.c_uint32(512)
        name = ct.create_unicode_buffer(length.value)
        if kernel.GetPackageFullName(process, ct.byref(length), name) != 0 or not codex_package(name.value):
            raise ValueError("仅允许捕获已安装的 OpenAI Codex 窗口。")
    finally:
        kernel.CloseHandle(process)
    bounds = wt.RECT()
    if not user.GetWindowRect(hwnd, ct.byref(bounds)):
        raise ValueError("无法读取 Codex 窗口边界。")
    size = bounds.right - bounds.left, bounds.bottom - bounds.top
    if min(size) < 16 or size[0] * size[1] > 4194304:
        raise ValueError("实验支持最多 419 万像素的窗口，请先缩小 Codex 窗口。")
    frame = wt.RECT()
    dwm = ct.windll.dwmapi.DwmGetWindowAttribute
    dwm.argtypes = [wt.HWND, wt.DWORD, ct.c_void_p, wt.DWORD]
    frame_size = size
    if dwm(hwnd, 9, ct.byref(frame), ct.sizeof(frame)) == 0:
        frame_size = frame.right - frame.left, frame.bottom - frame.top
    return pid.value, size, frame_size


def find_codex():
    found = []
    callback = ct.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)

    @callback
    def visit(hwnd, _):
        try:
            window_identity(hwnd)
            found.append(hwnd)
        except ValueError:
            pass
        return True

    ct.windll.user32.EnumWindows(visit, 0)
    if len(found) != 1:
        raise ValueError("请只保留一个打开的 Codex 窗口，或用 --window 指定目标窗口。")
    return found[0]


def capture_codex(hwnd):
    from windows_capture import WindowsCapture
    identity = window_identity(hwnd)
    frames, finished = [], threading.Event()
    capture = WindowsCapture(window_hwnd=hwnd, cursor_capture=False, secondary_window=False)

    @capture.event
    def on_frame_arrived(frame, control):
        try:
            frames.append(np.array(frame.frame_buffer[:, :, :3], copy=True))
        finally:
            control.stop()
            finished.set()

    @capture.event
    def on_closed():
        finished.set()

    session = capture.start_free_threaded()
    try:
        if not finished.wait(8) or not frames:
            raise ValueError("Windows 未返回 Codex 窗口画面，请恢复窗口后重试。")
    finally:
        session.stop()
        session.wait()
    pixels = frames[0]
    size = pixels.shape[1], pixels.shape[0]
    if window_identity(hwnd) != identity or size not in identity[1:]:
        raise ValueError("捕获期间窗口变化，请重新采集。")
    if float(pixels.std()) < 1:
        raise ValueError("窗口捕获没有返回有效画面，请恢复 Codex 后重试。")
    return pixels


def tiled_enhance(image, infer, cancel, progress=lambda current, total: None):
    """Keep native pixels; overlap fixed 480x270 model tiles and return a 2x image."""
    height, width = image.shape[:2]
    if min(height, width) < 1 or height * width > 4194304:
        raise ValueError("Invalid window image dimensions")
    result = np.empty((height * 2, width * 2, 3), np.uint8)
    step_x, step_y, margin = 448, 238, 16
    padded = np.pad(image, ((margin, 270), (margin, 480), (0, 0)), mode="edge")
    total = ((width + step_x - 1) // step_x) * ((height + step_y - 1) // step_y)
    current = 0
    for y in range(0, height, step_y):
        for x in range(0, width, step_x):
            if cancel.is_set():
                raise InterruptedError("已停止")
            tile = padded[y:y + 270, x:x + 480]
            high = infer(tile)
            if high.shape != (1080, 1920, 3) or high.dtype != np.uint8:
                raise ValueError("Invalid model output")
            w, h = min(step_x, width - x), min(step_y, height - y)
            cropped = high[margin * 4:(margin + h) * 4, margin * 4:(margin + w) * 4]
            result[y * 2:(y + h) * 2, x * 2:(x + w) * 2] = cv2.resize(cropped, (w * 2, h * 2), interpolation=cv2.INTER_AREA)
            current += 1
            progress(current, total)
    return result, total


class WindowModel:
    def __init__(self, root):
        self.engine = PrismNpu(root, "NPU")
        _, self.model = self.engine.compile("super-resolution.xml")
        self.inference_ms = 0

    def infer(self, low):
        high = cv2.resize(low, (1920, 1080), interpolation=cv2.INTER_CUBIC)
        inputs = [p.astype(np.float32).transpose(2, 0, 1)[None] for p in (low, high)]
        start = time.perf_counter()
        output = self.model(inputs)[self.model.output(0)][0].transpose(1, 2, 0)
        self.inference_ms += (time.perf_counter() - start) * 1000
        if not np.isfinite(output).all():
            raise ValueError("NPU 返回了无效像素。")
        return np.clip(output * 255, 0, 255).astype(np.uint8)


def snapshot(hwnd, model, cancel, progress):
    if cancel.is_set():
        raise InterruptedError("已停止")
    start = time.perf_counter()
    original = capture_codex(hwnd)
    capture_ms = (time.perf_counter() - start) * 1000
    model.inference_ms = 0
    enhanced, tiles = tiled_enhance(original, model.infer, cancel, progress)
    height, width = original.shape[:2]
    ordinary = cv2.resize(original, (width * 2, height * 2), interpolation=cv2.INTER_CUBIC)
    stats = {"device": "NPU", "windowOnly": True, "sourceSize": [width, height],
             "outputSize": [width * 2, height * 2], "tiles": tiles,
             "captureMs": round(capture_ms, 1), "inferenceMs": round(model.inference_ms, 1),
             "totalMs": round((time.perf_counter() - start) * 1000, 1), "savedImages": False}
    return [Image.fromarray(cv2.cvtColor(p, cv2.COLOR_BGR2RGB)) for p in (original, ordinary, enhanced)], stats


def preview(args, hwnd):
    import tkinter as tk
    from tkinter import ttk
    root = tk.Tk()
    root.title("Codex · NPU 超分实验")
    root.geometry("1100x760")
    root.minsize(700, 450)
    images, messages, cancel = [], queue.Queue(), threading.Event()
    model, photo, busy = None, None, False
    bar = ttk.Frame(root, padding=10)
    bar.pack(fill="x")
    mode = ttk.Combobox(bar, values=["原图 1×", "普通放大 2×", "NPU 超分 2×"], state="readonly", width=20)
    mode.current(2)
    mode.pack(side="left", padx=(0, 10))
    status = tk.StringVar(value="仅采集 Codex 窗口；正在准备首次快照。")
    ttk.Label(root, textvariable=status, padding=(10, 5)).pack(fill="x")
    ttk.Label(root, text="快照预览不向 Codex 传递操作。可滚动检查细节；关闭窗口会停止实验，图片不保存。", padding=(10, 5)).pack(fill="x")
    frame = ttk.Frame(root)
    frame.pack(fill="both", expand=True)
    frame.rowconfigure(0, weight=1)
    frame.columnconfigure(0, weight=1)
    canvas = tk.Canvas(frame, background="#151822", highlightthickness=0)
    canvas.grid(row=0, column=0, sticky="nsew")
    for orient, command, row, column, sticky in [("horizontal", canvas.xview, 1, 0, "ew"), ("vertical", canvas.yview, 0, 1, "ns")]:
        scroll = ttk.Scrollbar(frame, orient=orient, command=command)
        scroll.grid(row=row, column=column, sticky=sticky)
        canvas.configure(**{("xscrollcommand" if orient == "horizontal" else "yscrollcommand"): scroll.set})

    def display(_=None):
        nonlocal photo
        if not images:
            return
        position = canvas.xview()[0], canvas.yview()[0]
        photo = ImageTk.PhotoImage(images[mode.current()])
        canvas.delete("all")
        canvas.create_image(0, 0, image=photo, anchor="nw")
        canvas.configure(scrollregion=(0, 0, photo.width(), photo.height()))
        canvas.xview_moveto(position[0])
        canvas.yview_moveto(position[1])

    def work():
        nonlocal model
        try:
            if model is None:
                model = WindowModel(args.root)
            result = snapshot(hwnd, model, cancel, lambda n, total: messages.put(("progress", f"NPU 正在处理 {n}/{total} 块…")))
            if not cancel.is_set():
                messages.put(("done", result))
            else:
                messages.put(("error", "已停止"))
        except Exception as error:
            messages.put(("error", str(error)))

    def refresh():
        nonlocal busy
        if busy:
            return
        busy = True
        cancel.clear()
        capture.configure(state="disabled")
        status.set("正在准备 NPU 模型…")
        threading.Thread(target=work, daemon=True).start()

    capture = ttk.Button(bar, text="采集 Codex / 刷新", command=refresh)
    capture.pack(side="left", padx=(0, 10))
    ttk.Button(bar, text="停止", command=cancel.set).pack(side="left")
    mode.bind("<<ComboboxSelected>>", display)
    canvas.bind("<MouseWheel>", lambda event: canvas.yview_scroll(-int(event.delta / 120), "units"))

    def poll():
        nonlocal images, busy
        while not messages.empty():
            kind, value = messages.get_nowait()
            if kind == "done":
                try:
                    images, stats = value
                    display()
                    stats["previewReady"] = True
                    if args.report:
                        args.report.write_text(json.dumps(stats, ensure_ascii=False, indent=2), encoding="utf-8")
                    status.set(f"NPU · {stats['sourceSize'][0]}×{stats['sourceSize'][1]} → 2× · {stats['tiles']} 块 · 总耗时 {stats['totalMs'] / 1000:.2f} 秒")
                except Exception as error:
                    status.set(f"预览失败：{error}")
            else:
                status.set(value)
            if kind != "progress":
                busy = False
                capture.configure(state="normal")
        root.after(100, poll)

    root.protocol("WM_DELETE_WINDOW", lambda: (cancel.set(), root.destroy()))
    root.after(100, poll)
    root.after(200, refresh)
    root.mainloop()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, required=True)
    parser.add_argument("--window", type=int)
    parser.add_argument("--report", type=Path)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    try:
        ct.windll.user32.SetProcessDpiAwarenessContext(ct.c_void_p(-4))
        hwnd = args.window or find_codex()
        window_identity(hwnd)
        if args.check:
            _, stats = snapshot(hwnd, WindowModel(args.root), threading.Event(), lambda n, total: None)
            print(json.dumps(stats))
            if args.report:
                args.report.write_text(json.dumps(stats, indent=2), encoding="utf-8")
        else:
            preview(args, hwnd)
    except Exception as error:
        if args.check:
            raise
        from tkinter import messagebox
        messagebox.showerror("Codex · NPU 超分实验", str(error))
