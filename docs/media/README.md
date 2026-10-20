# 效果素材说明

采集日期：原有桌面素材为 2026-10-05；歌词效果素材为 2026-10-20。

这些素材来自本项目真实 C# / WebView2 / WebGL 组件的独立预览窗口。使用生成的渐变壁纸；不使用用户桌面的截图、私人壁纸、实际笔记或登录额度。

| 文件 | 内容 |
|---|---|
| `top-island.gif` / `.mp4` | 同一段约 6.6 秒的真实渲染帧序列：顶部栏、灵动岛、展开面板、还原 |
| `top-bar.png` / `top-expanded.png` | 顶部两个静态状态 |
| `garden-sky.png` / `garden-astro.png` | 左侧栏、生态瓶与双主题；演示存档 |
| `garden-collection.png` | 六植物收藏；演示存档已解锁 |
| `shelf-quota.png` | 额度 72% 为演示数据，界面中也明确标注 |
| `shelf-notes.png` / `shelf-manage.png` | 演示笔记、真实管理界面 |
| `dock-lyrics.gif` / `.mp4` | 约 13 秒：胶囊收缩、四种文字风格/入场动效、恢复 Dock 玻璃 |
| `lyrics-ice.png` / `lyrics-aurora.png` / `lyrics-rose.png` / `lyrics-ink.png` | 实际歌词组件在四种预设下的局部截图 |
| `lyrics-settings.png` / `lyrics-controls.png` | 音乐面板中的预设预览、字体和动效设置 |

动效以真实采样间隔编码，再转成 16 fps GIF 以便 README 自动播放；并非性能基准，也不是全桌面录屏。MP4 不含音轨：展示的是显示效果，本项目没有因此新增语音功能。

`tools/CaptureShowcase.cs` 是采集工具，需与 `src/LiquidDesktop/*.cs` 一起编译，入口指定为 `CaptureShowcase`，并引用构建输出的 WebView2 DLL。依次运行 `<组件目录> <输出目录> island|garden|shelf`。工具会启动独立预览窗口，演示存档只写入预览目录。当前左侧局部截图按采集机 125% DPI 设置，换 DPI 后需相应调整裁切参数。

请使用干净构建目录复现，不要将工具指向个人运行目录。它会在独立预览配置中写入演示数据。原始帧和浏览器缓存不提交到仓库。

## 歌词效果素材

歌词素材由 `tools/CaptureLyricsShowcase.cs` 加载当前 `index.html`、`desktop.js`、`lyrics-style.js` 和 `sidedock.html` 采集，未改写生产动画或用静态图模拟动画。歌词文字和曲目信息为原创演示数据，不连接真实音乐服务。动图顶部标题与底部说明是演示注释，不是新增产品控件。

录制对象是 WebView 的玻璃与歌词图层，没有运行 Rainmeter 应用图标层，也没有操作正在使用的 PrismGlass 桌面。状态变化通过与原生程序相同的消息入口触发；它展示组件效果，不是完整鼠标操作教学或性能测试。设置截图按逻辑像素截取，不含私人配置。字体使用采集机已安装的系统字体。

安装 FFmpeg 并复用已有 WebView2 SDK 缓存后，在 PowerShell 7 运行：

```powershell
./scripts/CaptureLyricsShowcase.ps1 -CacheRoot 'D:\Cache\PrismGlass'
```

脚本在仓库 `artifacts/lyrics-showcase` 中建立独立预览，按实际采样间隔编码 MP4，再导出 16 fps GIF。输出只写入本目录的 `dock-lyrics.*` 和 `lyrics-*.png`；结束时移除原始帧、临时可执行文件与浏览器缓存，不改变桌面安装。
