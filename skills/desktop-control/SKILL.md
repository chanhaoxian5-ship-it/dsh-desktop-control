---
name: desktop-control
description: Windows 桌面控制，带可见的大黑指针、屏幕边缘发光和状态提示；优先 UI Automation 控件定位，校验窗口与进程 ID，支持动作回读和中断。用于用户要求操作本机软件、点击、输入或预览可视操作反馈。
---

# 可见桌面控制

使用本包的 desktop_observe、desktop_action、desktop_stop、desktop_preview。旧 computer-use/cu.ps1 是另一条操作路径，不会自动获得本包的可视提示。

## 操作循环

1. 简短说明本次要操作的应用和目标。沿用用户已给的授权范围；发送、删除、购买等若缺少明确授权，先询问。
2. desktop_observe(mode: windows) 查找窗口，再用 inspect 读取目标控件。记下返回的 hwnd、pid 和 elementId；不要猜句柄或复用过时的定位。
3. 优先 desktop_action(command: set-value/invoke)，传 hwnd、expectedPid 和唯一控件定位。控件需启用、可见且中心不被其他窗口遮挡；不支持的控件会报错，不会静默降级成点击。
4. 需要坐标操作时先 screenshot，再调用当前模型可用的 read_image。看不见图就不猜坐标。使用截图原始物理像素，包含 offset，负坐标在多显示器中可能有效。
5. 坐标点击、拖动、滚动、键盘输入要求目标在前台；先显式 focus 并核对结果。系统拒绝焦点时停止，不能向当前其他应用继续发输入。
6. 每次只做一个可验证动作，再观察结果。set-value 会回读值；invoke 可用 expectText 检查预期控件文字。delivery=sent/invoked 不等于业务完成；applicationOutcome=not-verified 时继续观察，不能报成功。

## 可视与停止

所有 desktop_action 都先建立可视反馈。准备、操作中、已发送/已核对、停止使用不同状态标题；UIA 动作的指针定位到目标控件，坐标动作的标记跟随真实鼠标。显示层点击穿透、不占前台焦点、不修改系统鼠标样式。

Ctrl+Alt+Esc 停止本次动作；desktop_stop 可停止本工作区的请求。用户取消、信号中断、窗口变更或超时后停止并重新观察。已发出的点击/调用不能撤销，结果未知时不盲目重试。

没有真实输入需求时，可用 desktop_preview 看短暂预览。预览是合成指针运动，不移动真实鼠标。

## 能力边界

仅 Windows、普通用户桌面。后台可见控件可通过 UIA 定向操作；原始鼠标和键盘输入必须前台。密码控件不读取或填写。受保护/管理员窗口、锁屏与安全桌面可能不可访问，报错后不绕过系统限制。

本包不含 Codex/Sky 运行时或其应用授权系统；使用 DSH 当前的工具授权和用户任务范围。只覆盖通过本插件发起的动作，不会检测其他控制工具，也不会给旧 cu.ps1 自动加特效。

截图只返回路径，图像进入模型需走 DSH 的 read_image 及其模型能力检查。查看截图可能把所读图片发送给当前配置的模型服务。
