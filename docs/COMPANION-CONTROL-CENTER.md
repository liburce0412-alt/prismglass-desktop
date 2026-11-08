# 伴随屏控制中心布局

主页使用对齐的连接、专注、时钟天气、音乐、小组件和待办模块。横屏中音乐跨两行，竖屏中音乐跨两列；歌词沉浸仍沿用原布局。电脑页的连接与专注位于左列，播放位于右列，音量横跨两列，窗口操作、外观和电源依次排列。

布局参考 [Apple 官方控制中心界面](https://support.apple.com/en-ca/guide/mac-help/mchl50f94f8f/27/mac/27) 的胶囊入口、双行播放模块和横向滑块，以及 [WhiteSur GTK 原项目](https://github.com/vinceliuice/WhiteSur-gtk-theme) 的圆角、紧凑间距与选中反馈。已查看两者原始界面图；实现使用现有 HTML/CSS/JavaScript，没有复制第三方代码或图片，没有增加框架。WhiteSur 当前仓库声明 MIT 许可，但本次只作视觉研究。

连接入口仍打开现有配对与显示设置。音乐、音量、专注和窗口按钮复用原请求、权限判定和状态；没有添加 Wi-Fi、蓝牙或亮度等未实现的开关。音量拖动显示实时数值，提交仍通过原 `volume.set`，不在拖动过程中连续发送。权限撤销时控件禁用，锁屏时新增音乐摘要随其他私人内容清空。

验证：`TestCompanionUi.ps1 -ControlCenterOnly` 在隔离 WebView2 夹具中检查 1024×768、768×1024、390×844、模块对齐、44px 操作目标、连接弹层焦点返回、既有投屏入口、音量请求、权限撤销及锁屏。截图位于 `artifacts/companion-control-center`。既有 `TestCompanionWeb.js` 通过。新布局纳入 1.1.1，尚未取得 iPadOS 14.2 实机验收；升级保留配对、证书和网络配置。
