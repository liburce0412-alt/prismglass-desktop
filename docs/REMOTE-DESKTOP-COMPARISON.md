# Safari 伴随屏与 EasyTier + Windows 远程桌面

核查日期：2026-11-01。结论：继续以现有方案承担“同一台电脑的可触控、有声音副屏”；EasyTier + RDP 可作为异地办公的另一条路线，暂不替换现有投屏。此次仅调研，没有安装组网软件、启用 RDP 或改变防火墙。

| 项目 | 当前 PrismGlass | EasyTier + Windows RDP |
| --- | --- | --- |
| 各自作用 | 采集本机主屏或虚拟显示器，经 WebRTC 播放；控制通道向本机会话注入输入 | EasyTier 负责设备网络互通；RDP 负责远程 Windows 会话的画面、输入与设备重定向 |
| 主要用途 | 桌边副屏、电脑与 iPad 连续操作 | 远端访问应用、文件和桌面；不是给本机控制台直接增加一个显示器 |
| iPad 入口 | Safari；已有 iPadOS 14.2 实机触摸、中文和可听声音记录 | 需要兼容的 RDP 客户端；跨网还需合适的 VPN 客户端/接入节点 |
| 分辨率 | 主屏保持比例或显式拉伸；独立副屏受当前虚拟显示驱动支持模式约束 | 远程会话分辨率可随客户端设置/方向调整，不等于本机扩展屏模式 |
| 输入与声音 | 已接直接点选、Windows 双触点、中文手动提交和系统声音；部分组合仍待验收 | 官方客户端提供远程输入、音视频与剪贴板等重定向；旧 iPad 客户端兼容性需单独核验 |
| 换网络 | 当前地址、授权设备、防火墙目标与采集状态需要重新确认；USB 调试连接本身不等于视频数据走 USB | 稳定虚拟地址可减少业务端改地址的次数；底层换 Wi-Fi 仍会短断并重建隧道，不保证无缝 |
| 延迟与流畅度 | 已有本机测试，但未与 RDP 在同一条件下测过 | 由编码、网络、P2P/中继、客户端解码共同决定，不能只凭“组网”断言更流畅 |

现有链路依据仓库 `CompanionStream.cs`、`CompanionControls.cs`、`companion-screen.js` 与本机验收记录；“更适合作副屏/异地办公”是基于两种架构的判断，并非性能对跑结论。

## 这套设备的实际限制

- 本机注册表回读 `EditionID=CoreCountrySpecific`，即 Windows 家庭中文版。微软规定 Home 不能作为内置远程桌面的接收端，Pro/Enterprise/Education/Server 才支持；有 `mstsc.exe` 只说明可以当客户端。[微软支持范围](https://learn.microsoft.com/en-us/windows-server/remote/remote-desktop-services/remotepc/remote-desktop-allow-access)
- 本项目最近一次已确认的 iPad 为 Air 2 / iPadOS 14.2，本轮未再次读取设备版本。当前美国 App Store 的 Windows App Mobile 页要求 iPadOS 26；微软通用文档仍列出较低的 17，两个来源更新不同，但都不能证明这台旧 iPad 可以安装当前版本。已购旧版或其他客户端是否可用要单独确认，不能承诺可以从商店直接下载兼容版。[App Store 当前兼容性](https://apps.apple.com/us/app/windows-app-mobile/id714464092)、[微软客户端要求](https://learn.microsoft.com/en-us/windows-app/get-started-connect-devices-desktops-apps?pivots=remote-pc)
- EasyTier 官方目前的下载列表没有 iOS 原生客户端，文档提供 WireGuard 接入方式：iPad 先接入一个 EasyTier WireGuard 网关，再访问其网络。官方说明该方式的流量经过接入节点，通常需要可达公网地址。因此不能把 PC 间的 P2P 优点直接当成 iPad 端已具备的能力。[支持平台](https://www.easytier.cn/guide/download.html)、[WireGuard 接入及网络条件](https://www.easytier.cn/guide/network/use-easytier-with-wireguard-client.html)

EasyTier 和当前投屏也可以组合：组网只负责网络可达性，Safari 仍连接现有服务。但 WebRTC 候选地址、HTTPS 名称/证书和限定设备的网络规则均需适配与实测，不能假设加一个虚拟网卡就全部可用。[EasyTier 能力定位](https://www.easytier.cn/guide/introduction.html)

## 本轮产品范围

用户已明确 Wi-Fi 指电脑端快速选无线网络：在左侧网络面板提供“切换 Wi-Fi”，复用系统网络选择界面，密码与连接由 Windows 处理。

桌面小组件为可选项，默认空列表，保留当前顶栏、Dock 和两侧栏。根据用户 9 月 5 日截图复用天气、性能、时钟、日历、电池、常用位置、音乐的原组件，另保留专注和工具。允许添加、移除和拖动位置：松手或恢复位置时避开开启的侧栏，隐藏侧栏后释放空间。右侧“管理”直达“设置 → 桌面组件”。用户新发截图中的两侧选中材质、宽度、顶部焦点框和遮挡提示按 01 / 03 修正。
