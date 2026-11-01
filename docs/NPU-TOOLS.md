# 本机 NPU 文字提取（本地预览）

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
