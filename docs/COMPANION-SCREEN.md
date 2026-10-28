# Safari 内局域网投屏

保持在 PrismGlass 伴随屏的「投屏 / 触控」页：选择显示器和 720p / 1080p，然后开始投屏。播放后铺满页面，右上角按钮展开或收起画质、屏幕选择和输入面板；停止后回到原来的排布。视频保留比例，16:9 主屏投到 4:3 iPad 会留边；4:3 扩展屏能更充分使用画面。画面区域沿用触控板手势，移动的是鼠标指针；不是触点到桌面坐标的一对一映射。

## 电脑端准备

### 下载与安装路径

已有可用工具可以直接复用，不要求搬动。下面是建议的可移动工具目录；没有 D 盘时可改为自己的非系统工具目录，并在配置中填写真实路径。

| 组件 | 官方入口与操作 | 建议文件位置 |
|---|---|---|
| FFmpeg | [FFmpeg 下载页](https://ffmpeg.org/download.html) → Windows → gyan.dev 的 Windows full build，解压整个目录并保留配套文件 | `D:\Tools\PrismGlass\ffmpeg\bin\ffmpeg.exe` |
| MediaMTX 1.21.3 | [Windows amd64 ZIP](https://github.com/bluenviron/mediamtx/releases/download/v1.21.3/mediamtx_v1.21.3_windows_amd64.zip)，按 [官方校验文件](https://github.com/bluenviron/mediamtx/releases/download/v1.21.3/checksums.sha256) 核对后解压 | `D:\Tools\PrismGlass\mediamtx\mediamtx.exe` |
| NVIDIA NVENC | 是兼容 NVIDIA GPU 的硬件编码能力，不是单独可安装的程序；安装适用显卡驱动，并检查 FFmpeg 的 `h264_nvenc`。参见 [NVIDIA 官方说明](https://docs.nvidia.com/video-technologies/video-codec-sdk/13.1/ffmpeg-with-nvidia-gpu/index.html) | 不需要填写单独的编码器路径 |

不需要把这些目录加入系统 PATH，也不要手动启动 MediaMTX。PrismGlass 在开始投屏后启动它们，并在停止时清理所属进程。源码与安装包不附带这些工具或驱动。

默认安装的投屏配置文件位于：

```text
%LOCALAPPDATA%\PrismGlass\LiquidDesktop\runtime-data\companion\screen-stream.json
```

若安装时选了其他位置，则使用 `<PrismGlass安装目录>\LiquidDesktop\runtime-data\companion\screen-stream.json`。按下面的 JSON 创建或编辑这个文件，只填本机路径和实际局域网地址；不要直接照抄示例 IP。不要把文件存成 `.json.txt`。可先在 PowerShell 中检查：

```powershell
& 'D:\Tools\PrismGlass\ffmpeg\bin\ffmpeg.exe' -hide_banner -encoders | Select-String 'h264_nvenc'
& 'D:\Tools\PrismGlass\mediamtx\mediamtx.exe' --version
```

列出 `h264_nvenc` 只说明该构建包含编码器，不代表显卡与驱动已经可用；首次启动投屏仍需验证。完整配置还包括下文的局域网规则和设备授权，解压文件本身不会开启屏幕共享。

- 复用现有 HTTPS 9443、设备配对、Origin/CSRF 校验。
- FFmpeg 需要 `h264_nvenc`，电脑需要兼容 NVIDIA 编码器。当前实现没有软件编码或其他 GPU 的回退。
- MediaMTX 1.21.3；从官方发布页取得 Windows amd64 包并核对官方 SHA256。二进制不随仓库分发。
- 本地安装的 `runtime-data/companion/screen-stream.json` 使用下列结构，填入真实可用的私有 IPv4 地址及绝对路径：

```json
{
  "enabled": true,
  "address": "192.168.1.10",
  "ffmpeg": "D:\\Tools\\PrismGlass\\ffmpeg\\bin\\ffmpeg.exe",
  "mediamtx": "D:\\Tools\\PrismGlass\\mediamtx\\mediamtx.exe"
}
```

视频使用该地址的 **UDP 31890**。取得用户同意后，为 MediaMTX 程序建立仅限所选局域网网卡、电脑地址、指定 iPad 地址的入站规则。换 IP 后需要相应更新配置及规则，不能扩大到所有网卡或公网。

RTSP 31854 与 WHEP 31889 只绑定 `127.0.0.1`，不用开放防火墙。没有外部 STUN/TURN、路由器端口映射或中继；视频会占用局域网带宽，不是零数据传输。

在电脑管理窗为已有设备另行勾选「屏幕画面（投屏）」并保存。原有配对和其他权限保留，新增屏幕权限默认关闭。鼠标控制由「鼠标触控板」、文字与按键由「键盘输入」分别授权，默认关闭。

启用触控后点击「键盘」，会直接聚焦 HTML 输入框以调用 iPad 原生键盘。中文选词在 iPad 完成，点击「发送」后以 Unicode 输入电脑当前窗口；每次最多 256 个 UTF-16 单元，不自动补回车。常用按键有 Enter、退格、方向键、Tab、Esc、全选、复制、粘贴、撤销及切换窗口。发送失败不重试，权限撤销或页面隐藏时清除草稿；不读取或同步电脑剪贴板。

## 行为与边界

- H.264 Baseline、30 fps 目标，720p 约 6 Mbps、1080p 约 10 Mbps；没有音频。
- 捕获所选**整块显示器**，包含其通知和其他窗口；不提供单软件隔离。
- 仅点击开始后启动采集与路由进程；一个观看者。停止、权限撤销、Windows 锁定、网页离开或隐藏都会停止；失联心跳最多 15 秒过期。
- 子进程归入带关闭清理的 Windows Job；电脑端退出时终止，不安装自启动服务，不录制视频。
- 仅共享获准内容；网页与浏览器资源清理不会清除用户配对和小组件偏好。

## 本轮验证

2026-10-28：真实 iPad Air 2 / iPadOS 14.2 短时采样：720p 30.02 fps / 6.06 Mbps、主屏 1080p 30.03 fps / 10.00 Mbps，两次采样的网络丢包增量均为 0。Safari 14 未提供解码丢帧字段，不能据此声称零解码丢帧；这些数据也不代表触摸延迟或长期稳定性。电脑回环对照为 720p 30.13 fps、1080p 29.96 fps。

Virtual Display Driver 已安装并独立扩展为 1440×1080 / 30Hz，保留主屏及两块实体屏原来的复制关系。Safari 14 不支持 `inset`，全屏容器使用明确的 top/left/width/height；实机从 215×66 的错误尺寸恢复到 1024×768。用户确认正式页面全屏和原生键盘正常，并在电脑专用记事本完整收到「冰川测试123」。键盘权限持久化、中文/表情校验、重复提交、原生输入框聚焦与失败保留草稿均通过隔离测试。

连接恢复后曾残留旧的「连接超时」提示，现仅在确认同步成功后清除只读连接错误；不清除写入结果未知的警告。真实 iPad 的受控单次只读超时测试已验证提示出现及重连后消失。未测长期功耗、持续数小时的串流稳定性和端到端触摸延迟。

参考：[MediaMTX 浏览器读取](https://mediamtx.org/docs/read/web-browsers)、[WebRTC](https://mediamtx.org/docs/read/webrtc)、[官方发布](https://github.com/bluenviron/mediamtx/releases/tag/v1.21.3)。
