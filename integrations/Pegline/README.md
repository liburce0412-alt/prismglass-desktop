# Pegline 截图晾绳（可选）
上游：https://github.com/2829632740/Pegline
补丁基准提交：2b99cd12463de5999f0e135d0c120cd5a856da02

本项目提供接入脚本，不捆绑 Pegline 可执行文件。
1. 下载上游源码。在其目录执行 git apply，并传入本目录 hit-test.patch 的绝对路径。
2. 执行 pwsh -NoProfile -File ./build.ps1，生成 build/Pegline.exe。
3. 将 Pegline.exe 的完整路径写入 PrismGlass 安装目录下的 pegline-path.txt（一行纯文本）。
4. 下一次开启美化时会启动 Pegline。退出美化时只关闭本次美化启动的实例，不关闭已由用户独立启动的实例。不增加开机启动项。

Ctrl+Alt+T 显示／收起；鼠标在上沿停留也可展开。剪贴板截图接管由 Pegline 首次提示征求用户选择。
外部截图点 × 只是取下；接管目录内的截图点 × 会移入回收站。

hit-test.patch 让点击区使用 WPF 实际坐标变换，覆盖旋转、缩放、下滑和 DPI，避免角落按钮落在穿透区。
补丁已编译，真实悬浮窗的点击验收尚未完成。
