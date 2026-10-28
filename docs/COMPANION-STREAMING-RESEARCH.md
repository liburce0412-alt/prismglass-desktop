# iPad 局域网投屏调研

日期：2026-10-28。以下保留方案比较；用户已明确要求留在 Safari 中，最终实施改为 FFmpeg 硬件编码 + MediaMTX WebRTC，见 [Safari 投屏配置与验证](COMPANION-SCREEN.md)。已按用户批准的 WLAN、电脑和 iPad 地址限定开放 UDP 31890；真实 iPad 的 720p 与 1080p 短时解码均约 30 fps。

## 当前设备与建议

USB 读回设备为 iPad Air 2（iPad5,3），iPadOS 14.2（18B92）。本轮已加入 Safari 内桌面画面、独立鼠标/键盘权限和可收起悬浮面板；虚拟显示器已配置为独立扩展屏。

独立 App 方案中，**Windows Sunshine + iPad Moonlight** 符合设备系统门槛：Moonlight 当前商店最低版本为 iOS/iPadOS 12。它不符合本次「必须留在 Safari」的使用要求，因此没有安装。

| 方案 | 本机系统适配依据 | 用途与限制 |
| --- | --- | --- |
| Sunshine + Moonlight | Moonlight 商店最低 iOS 12 | 低延迟桌面/应用串流，触屏模拟鼠标；Windows 捕获显示器，应用条目不是权限隔离 |
| RustDesk | 商店最低 iOS 13 | 办公远控；可使用内网 IP 直连，ID 连接可能使用外部服务器或中继 |
| noVNC 最新版 | 官方最低 Safari 15 | 可嵌入网页，但不能承诺在本机 Safari 14.2 工作；还需 VNC 服务与 WebSocket 网关 |
| Deskreen CE | 未核实 Safari 14.2 兼容性 | 支持分享应用窗口；公开 FAQ 仍将远控列为未来功能，不能视为已验证的触屏控制方案 |

商店系统门槛不等于本机当前账号地区已经安装成功。RealVNC 的商店兼容栏与历史版本说明存在 iOS 14 支持冲突，暂不优先选择。

## 流量与控制边界

局域网直连仍产生 Wi-Fi/以太网数据传输，但画面不经过运营商公网或蜂窝网络。软件下载安装、版本检查等另有外网请求。RustDesk 等方案须明确使用直连并验证链路，不能把所有远控都称为零外网流量。

8 Mbps 的码率换算约为每小时 3.6 GB 局域网数据，未包含协议开销；这只是算术示例，非实机测量。

Moonlight 提供直接触摸与触控板式鼠标操作，支持点击、拖动、右键、双指滚动及软键盘；不应将这些手势描述为已验证的 Windows 原生多点触控。

Sunshine 选择某个 EXE 可用于启动应用，但捕获和输入仍作用于显示器/Windows 会话；通知、弹窗或切换后的桌面可能可见。若要求严格只能控制固定软件，需要另外实现窗口捕获、坐标映射、前台与窗口归属验证、输入范围约束及权限边界。

## 建议的首次试验

- H.264、SDR、720p30、5–8 Mbps；电脑接有线、iPad 接 5GHz Wi-Fi。这些是保守起点，不是性能承诺。
- 验证旧 iPad 的实际解码路径、延迟、触摸拖拽/滚动和长时间发热；再尝试提高分辨率或帧率。
- 禁用不需要的公网自动映射；只允许需要的局域网连接。任何新防火墙规则或服务暴露须先明确范围并取得授权。
- 若必须在同一 Safari 页面内完成：单独评估浏览器视频兼容、媒体捕获与编码服务、授权及输入边界。现有伴随屏权限不能自动扩展为任意桌面控制。

## 一手资料

- [Moonlight 商店兼容性](https://apps.apple.com/us/app/moonlight-game-streaming/id1000551566?platform=ipad)
- [Moonlight 设置及触摸操作](https://github.com/moonlight-stream/moonlight-docs/wiki/Setup-Guide)
- [Sunshine 项目及硬件编码](https://app.lizardbyte.dev/Sunshine/)
- [Sunshine 应用配置](https://docs.lizardbyte.dev/projects/sunshine/latest/md_docs_2getting__started.html)
- [RustDesk 商店兼容性](https://apps.apple.com/us/app/rustdesk-remote-desktop/id1581225015)
- [RustDesk Direct IP 设置](https://rustdesk.com/docs/en/self-host/client-configuration/advanced-settings/)
- [noVNC 浏览器要求](https://github.com/novnc/noVNC)
- [Deskreen](https://deskreen.com/) / [FAQ](https://deskreen.com/faq/)
- [Apple iPad Air 2 视频规格](https://support.apple.com/en-gb/112017)

未验证：端到端操作延迟、硬解状态、长期功耗及稳定性。未实现：Windows 原生多点触控、固定软件的安全隔离。
