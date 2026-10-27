# PrismGlass 图标素材

设计日期：2026-10-27。用户要求保留原菱形三面结构，重新打磨材质与配色；本次测试版使用细化稿作为发布图标。P 字形改形方向已放弃。

## 当前细化稿

![菱形三面结构细化稿](prismglass-icon-refined-256.png)

- [原尺寸 PNG](prismglass-logo-refined.png)：保留轮廓，减少碎切面，统一青蓝、淡紫与银蓝，弱化底座外沿。
- [Windows ICO](prismglass-icon-refined.ico)：含 16、20、24、32、48、64、128、256 像素帧；已检查各帧解码、尺寸和透明边缘，并通过 Windows 图标加载。系统实际选择的尺寸由显示环境决定，尚未进行安装后图标验收。
- [32 像素预览](prismglass-icon-refined-32.png)与 [256 像素预览](prismglass-icon-refined-256.png)。
- [内置 imagegen 编辑提示词](prismglass-refined.prompt.md)。

初稿作为参考保留；测试版的 EXE、托盘、安装快捷方式和伴随屏使用细化稿。以下旧文件名仍对应初稿，不用于本次安装包。现有已安装程序未因此替换。

## 初稿参考

本套素材通过内置 imagegen 生成，作为软件整合发布的图标设计。此轮仅制作素材，未替换运行中的程序、未部署或发布；伴随屏仍在独立修改。

| 文件 | 用途 |
|---|---|
| `prismglass-logo.png` | 保留生成结果原尺寸与透明通道的主图 |
| `prismglass-icon.ico` | Windows 多尺寸图标，含 16、20、24、32、48、64、128、256 像素的 32 位 PNG 帧 |
| `prismglass-icon-32.png` / `prismglass-icon-64.png` | 小尺寸辨识度检查与可复用 PNG |
| `prismglass-icon-256.png` | 文档和界面预览 |

ICO 与小 PNG 使用 Windows System.Drawing 从主图等比例缩小并编码；没有重绘或改色。主图保留真实透明边缘，不把透明区域烘焙成白色。

后续整合范围：主程序 EXE 图标、托盘图标、安装快捷方式和安装器，以及伴随屏网页/主屏幕图标。Safari/主屏幕图标需单独检查裁切、留白与背景，不能把 Windows 素材转换视为已完成适配。小尺寸托盘若出现辨识问题，需制作相同轮廓的简化版后再验收。

## 生成提示词

使用内置 imagegen，`transparent_background: true`，未使用 CLI/API 回退。以下为实际提示词。

```text
Use case: logo-brand. Asset type: production desktop application icon for PrismGlass, square 1024 x 1024. Create ONE exceptionally refined finished icon, not a presentation board. Center a bold sculptural optical-glass prism emblem inside a deep midnight-blue rounded-square app tile, with truly transparent pixels OUTSIDE the tile. Tile occupies about 88% of the canvas, perfectly front facing, centered. Emblem: a single distinctive softly bevelled crystalline rhombus built from three broad interlocking glass facets, a precise luminous inner aperture, subtle suggestion of a P through geometric negative space without writing a letter. Keep a very simple strong recognizable silhouette that remains readable at 32 px. Material is clear polished liquid glass, visibly thick bevels with icy white specular highlights, saturated glacier cyan on one facet and cool periwinkle/violet on another, a very small warm silver-pink refracted accent. Broad smooth glass planes, elegant physical refraction, exceptional optical precision. Dark ink-blue tile has a restrained blue pool of light and almost invisible fine edge; no scenery and no frosted white haze. The emblem fills most of tile with generous balanced margins and gently floats above it. Premium Windows/macOS app icon craftsmanship, restrained and memorable, beautifully balanced at a tiny size. Sharp silhouette rather than spiky or complicated geometry, no rainbow all around, no neon halo, no sparkle stars, no letters, no text, no name, no watermark, no device mockup, no grid, no multiple alternatives, no cast shadow outside tile. The image is itself the usable final icon. Preserve genuine transparent corners and outer margin.
```
