# 效果素材说明

采集日期：2026-10-05。

这些素材来自本项目真实 C# / WebView2 / WebGL 组件的独立预览窗口。使用构建脚本生成的渐变壁纸；不使用用户桌面的截图、私人壁纸、实际笔记或登录额度。

| 文件 | 内容 |
|---|---|
| `top-island.gif` / `.mp4` | 同一段约 6.6 秒的真实渲染帧序列：顶部栏、灵动岛、展开面板、还原 |
| `top-bar.png` / `top-expanded.png` | 顶部两个静态状态 |
| `garden-sky.png` / `garden-astro.png` | 左侧栏、生态瓶与双主题；演示存档 |
| `garden-collection.png` | 六植物收藏；演示存档已解锁 |
| `shelf-quota.png` | 额度 72% 为演示数据，界面中也明确标注 |
| `shelf-notes.png` / `shelf-manage.png` | 演示笔记、真实管理界面 |

动效以真实采样间隔编码，再转成 16 fps GIF 以便 README 自动播放；并非性能基准，也不是全桌面录屏。MP4 不含音轨：展示的是显示效果，本项目没有因此新增语音功能。

`tools/CaptureShowcase.cs` 是采集工具，需与 `src/LiquidDesktop/*.cs` 一起编译，入口指定为 `CaptureShowcase`，并引用构建输出的 WebView2 DLL。依次运行 `<组件目录> <输出目录> island|garden|shelf`。工具会启动独立预览窗口，演示存档只写入预览目录。当前左侧局部截图按采集机 125% DPI 设置，换 DPI 后需相应调整裁切参数。

请使用干净构建目录复现，不要将工具指向个人运行目录。它会在独立预览配置中写入演示数据。原始帧和浏览器缓存不提交到仓库。
