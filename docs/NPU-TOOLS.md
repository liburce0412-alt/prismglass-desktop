# 本机 NPU 文字提取与窗口超分实验

在 **设置 → 文字提取** 点击 **提取当前屏幕文字**。程序会短暂隐藏设置窗口，识别该窗口所在屏幕，然后展示可选取的结果。点击 **复制文字** 才写入剪贴板；清除、关闭或离开该页面会丢弃当前结果。识别可能有漏字、错字或把图形当成文字，请核对。

截图和结果通过本机内存管道处理，不上传图像、不保存截图或识别文本文件。不会持续监听屏幕，也不会自动把结果输入其他软件。编译缓存仅包含模型，不含截图。当前仅用于电脑设置页，不增加 iPad 的权限。

## 可选运行环境

需要 Python 3.13、Intel NPU 驱动和可用的 Intel AI Boost。构建只携带小型调用助手；模型与 Python 依赖不随普通安装包下载，未配置时入口显示原因。

复用已安装的 Python，并为这些可移动工具选择 D: 上的目录：

```powershell
pwsh -File scripts/SetupNpuTools.ps1 -Python 'C:/Path/To/python.exe' -Root 'D:/DevTools/PrismGlass/npu' -CacheRoot 'D:/Cache/PrismGlass/pip'
```

此命令显式下载固定版本的 OpenVINO、图像处理依赖、PP-OCRv5 mobile 检测/识别模型和超分实验模型，并核对上游模型摘要；不安装驱动。OCR 模型来自 RapidAI 官方转换发布，超分来自 Intel Open Model Zoo。

在现有安装 `LiquidDesktop/runtime-data/npu.json` 中配置：

```json
{
  "python": "D:/DevTools/PrismGlass/npu/venv/Scripts/python.exe",
  "root": "D:/DevTools/PrismGlass/npu"
}
```

程序明确选用 `NPU`，没有自动退回 CPU 后仍显示 NPU 的行为。模型不支持、驱动不可用或超过 30 秒时在原位置报错。下载后的正常识别不联网。

## 当前实测与画质实验

本机 Core Ultra 9 275HX / Intel AI Boost，驱动 32.0.100.5540，OpenVINO 2026.4.1，已实际编译并推理两个 OCR 模型。合成测试图上的四行中英文完整识别；小字存在大小写误认，圆形图标也出现误识别。完整准确性不能由这组样本保证。

2026-10-31 原位安装后，实际点击文字提取按钮，确认设置窗口隐藏、当前屏幕采集、NPU 返回中英文和窗口恢复；离开页面后重新进入，结果为空，复制按钮禁用。测试使用合成文字画面，没有复制到系统剪贴板。屏幕上的其他浮层和图标也会参与识别；较长的单行、小字和复杂排版尚未全面验证。

超分实验使用 `single-image-super-resolution-1032`，480×270 输入到 1920×1080 输出。当前 NPU 预热后的纯推理中位数约 64 ms，CPU 约 45 ms；NPU 完整首次图像调用约 120 ms。测试图 PSNR 从双三次插值的 26.20 dB 提高到 28.82 dB，但小字和笔画边缘仍有失真。这些结果不支持将此模型放进目标 30 帧的实时投屏，因此保留原生分辨率采集及 NVENC 编码，超分仅作为可复现实验。未测功耗，不声称节能。

复现：`tests/TestNpuModels.py` 对照 CPU/NPU 执行设备、推理时间和数值；`tests/TestNpuImages.py` 生成非私人图片并检验识别及画质；`scripts/TestNpuText.ps1` 检查实际 C# → Python → NPU 返回路径。原有玻璃渲染、音频编码和触控不属于模型推理，保持现有实现。

## 仅对 Codex 窗口超分

源码工作区另有独立的快照预览工具，未包含在 1.1.1 安装包中。它通过 Windows Graphics Capture 按窗口句柄采集，并核对进程属于已安装的 `OpenAI.Codex` 软件包；不支持其他应用，也不回退到整屏截图。请保持一个 Codex 窗口打开且未最小化，窗口最多支持 419 万像素。

在上述运行环境已准备好的情况下，额外安装固定版本的窗口采集依赖，然后从仓库目录启动：

```powershell
& 'D:/DevTools/PrismGlass/npu/venv/Scripts/python.exe' -m pip install --disable-pip-version-check --only-binary=:all: --no-deps --cache-dir 'D:/Cache/PrismGlass/pip' windows-capture==2.0.1
pwsh -File scripts/PreviewCodexNpu.ps1 -Root 'D:/DevTools/PrismGlass/npu'
```

预览首次打开会处理一张快照，之后点击“采集 Codex / 刷新”才重新采集。下拉菜单可切换“原图 1×”“普通放大 2×”“NPU 超分 2×”，滚动查看细节。停止按钮中止后续分块处理；关闭预览退出实验。图片仅存在本机内存中，不保存、不上传，不向 Codex 转发操作，也不会替换其实际界面。

处理保留窗口原始像素，按模型的 480×270 输入尺寸重叠分块，再裁去边缘并输出 2 倍图像；没有先把整窗缩到 480×270。只接受 NPU 执行，失败直接报错。可选 `--report` 只保存尺寸、用时和执行设备等统计，不包含图像或识别文字。

2026-11-03 本机实际处理 1629×1065 的 Codex 窗口，输出 3258×2130，共 20 块；两次快照总耗时分别为 3.23 秒和 3.16 秒，纯 NPU 推理分别约 2.11 秒和 2.14 秒。上述时间包含窗口采集与图像处理，不包含首次模型编译。用户在预览中对比后确认“NPU 更清晰，文字没有明显变形”；这只代表该窗口样本的主观验收，未证明所有字号、语言和代码符号均无失真。当前速度适合静态细节对比，尚不适合实时覆盖层或 30 帧投屏，功耗未测。

`tests/TestNpuWindow.py` 使用非私人像素检查原始分块、边缘重建、取消和窗口范围校验。窗口采集依赖来自 [windows-capture 官方仓库](https://github.com/NiiightmareXD/windows-capture)，只作为这个独立实验的可选依赖。
