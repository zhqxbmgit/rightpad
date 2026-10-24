# Mode 菜单速度与误关闭修复记录 — 2026-10-19

本轮时间为 Asia/Shanghai。在已有未提交实现上增量修改；没有 Git commit、push、历史
修改、源码回退或工作区清理。旧交付报告、旧运行日志和本轮失败证据保留。

## 结论与未完成目标

已部署直接使用成功 Save 后正式 SensitivityX/Y 的菜单导航，并去掉“一次写后坐标
不同就立即关闭”的判定。真实生产菜单无移动/无点击保持 77.807 秒，持续六方向导航、
重落、真实输出及普通输入恢复通过。但不能宣称原先所有自动关闭的根因已经定位。

速度仍未完全达到打开前后的屏幕距离一致：菜单从原来的 80×40 提高为 240×120，
普通路径仍为 480×240。用户在看到实测差异后明确选择：
“严格保持不加任何转换，记录速度仍未完全达标”。因此没有添加补偿、额外坐标转换
或 DPI 运动倍数，没有改变普通鼠标或 Windows 参数。

## 本轮现场

- 开始时实际 Receiver PID 19092，Session 2，Medium / Default，UDP 50000 归它所有。
  正式 DLL 为 A29580B22A63CDAFE22C3BFB4D8608ED58154DDE562EDA591AF2AF035724CF2C。
- 正式设置为 Sensitivity 12/12、M_R1；菜单用途 / Ring / 上槽 A，其余五槽未绑定；
  原 G / 普通按钮保留。C 保存值 Amax160000、Tau30 及其他参数按现场原样保留。
- Android 既有 PID 20316，设备 192.168.110.127:5555。MainActivity 保持前台，未构建、
  安装或重启 APK。布局直接读取本轮文件，不使用历史矩形代替现场。
- 当前显示环境为 3840×2160、192 DPI。静态读取交互 Session 的 SPI_GETMOUSESPEED=10，
  SPI_GETMOUSE=[0,0,0]。注册表及生效 API 均只读，未修改系统鼠标设置。

证据根：`C:\rightpad\windows\test-results\mode-menu-fixes-20261019`。

## 速度证据与实现

用同一既有 Android View/Sender 路径注入手机坐标从 (900,1200) 到 (920,1210)，
持续 300 ms。没有在正式配置中写入测试灵敏度。Raw Input 与实际系统坐标由独立安全
目标记录；不是用写死比例的测试推算真实屏幕响应。

| 路径 | Receiver/HID 位移 | 实际屏幕起止 | 屏幕位移 |
|---|---|---|---|
| 修改前普通 M-R1 | HID 240,120 | (1919,753) → (2399,993) | 480,240 |
| 旧菜单，2×DPI=4/4 | 无 HID 鼠标 | (2399,993) → (2479,1033) | 80,40 |
| 修复后菜单，正式 12/12 | 无 HID 鼠标 | (2914,1405) → (3154,1525) | 240,120 |
| 修复后普通 M-R1 | HID 240,120 | (1919,753) → (2399,993) | 480,240 |

`VerifyEvidence.ps1` 从 stages.log 与 actual-input.log 重新计算，结果保存于
verified-evidence.json。普通路径前后屏幕及 HID 总位移均相同，菜单速度提高三倍，
仍只有本机普通路径屏幕位移的一半。后续系统处理存在实测差异，但本轮未证明其
完整原因，不能把它直接归因于 DPI 或给出未经验证的倍率。

ModeMenu.Process 使用该包的正式 RuntimeSettings 快照；SettingsViewModel 草稿不进入
此路径。原 TouchSessionProcessor/RawMotionProcessor 保留不足一单位的分数余量，
DOWN、UP、Reset/退出均清除，不跨接触补发。实际系统位置仍决定高亮和执行。
没有私有操作指针、请求目标位置累积、Motion/Gesture/HID 鼠标导航、独立菜单速度设置、
运行时适应、吸附、预测或系统参数修改。DPI 只参与外观及 12 DIP 中性半径。

## 自动关闭：已知证据和限制

本轮开始备份的原运行日志中，实际计数为 18 次 menu_navigation_error，
menu_presentation_timeout 与 lease_expired 均为 0。旧日志没有具体异常和坐标，因此
无法从历史记录区分 API 失败、坐标差异、隐藏/约束等触发条件。

先只补诊断并隔离构建，757/757 回归通过后部署诊断 PID31448，再用现有手机做
40 次导航与抬手重落；没有复现原关闭。生产一致的 PerMonitorV2 真实光标探针
4,000 次 worker 移动也未复现。

最初 DPI 未感知探针在第214次移动出现 IOException：move_readback 请求(1422,228)，
实际(1422,229)，flags=1、可见/未抑制、clip=(0,0,1920,1080)，显示未变，win32=0。
它证明旧严格比较可被一像素差异触发，但该探针的 DPI 上下文与生产不同，不能作为
生产事故根因。其原始日志保存在 cursor-diagnostic-unaware.log；一致 DPI 的复测结果
在 cursor-diagnostic.log，不能混用两者。

因此本轮修复了明确不合理的严格判据并增加诊断，而没有虚构“已定位唯一根因”或
真实游戏兼容结论。若原场景再发生，新的异常日志可给出具体故障。

新判据：

1. 单次写后差异采用实际位置；下一次移动从实际位置开始，不重写旧请求，不积累未
   实现整数位移，也不以后补发。每次打开只记录第一次差异，避免高频输入日志。
2. 对屏幕范围内可实现的非零请求，连续至少8次、持续至少250 ms，始终没有实际位置
   进展才退出。任何进展、或超过600 ms的请求间隔重置计数。普通屏幕边缘夹成零、
   静止、抬手和无点击不构成故障；不使用该计数改变增益或正常手感。
3. 真实读写失败、隐藏/受抑制、明确内部 ClipCursor 约束和显示区域/DPI改变仍退出、
   释放并围栏旧输入。原600 ms Mode lease、presence、2 s呈现健康、输出失败门闩保持。
4. mode_menu_fault 记录导航、点按读回或维护阶段，异常类型/消息、关闭原因、请求与
   实际坐标、可见/抑制、约束、打开及当前显示信息，及可用的 Win32 错误码。读取失败
   时实际坐标/标志标为 unavailable，不伪造成功数据。

## 自动化和受控实机验证

- 最终 Release 构建0警告/0错误；Windows **760/760**，原生 fake **1/1**。
  build.log、tests-passed-final.log、native-tests.log 保存结果。
- 新增三组回归覆盖不同正式X/Y、成功Save、未保存草稿、DPI独立性、分数余量及跨
  接触清零；65秒模拟健康静置/重落；单次差异实际权威/无重试/无欠账，持续零进展，
  空闲间隔、屏幕边缘、读取/写入/约束/显示API失败和隐藏/抑制。
- 可替换原生接口明确验证请求(510,400)而实际(509,400)时保持并读实际位置，后续
  请求不偿还差值；持续零进展达到有界条件才失败。真实API失败码通过可替换接口
  注入验证，没有故意破坏用户的实际API、显示或输出设备。
- **生产菜单静置77.807秒**：用户明确同意暂不操作后，18:34:16至18:35:33期间，
  观察器0鼠标事件、0键盘事件、0光标变化；菜单没有关闭。idle-result.json、
  idle-observer.log和生产日志共同支持。此前独立探针62秒期间有外部移动，该段只证明
  保持打开，不冒充无移动静置验收。
- 生产菜单36次有计划的方向往返，涵盖六向及抬手重落；未意外关闭。选上槽后真实
  A键scan001E flags0000→0001，菜单仍打开，再由明确Mode关闭。该段 **HID鼠标事件0**。
- 独立实机环境探针60次六方向重落通过；真实2×2内部clip退出并在解除/新同步后重开；
  真实隐藏flags=0退出，恢复可见后重开。environment-recheck/overlay-probe.log含完整
  故障上下文，environment-exit=0。首次探针目标失去前台，设置目标Cursor=None没有
  真正隐藏系统光标，断言失败；原overlay-probe.log、probe-error.txt、probe-exit保留。
  重测先激活安全目标，按flags确认实际隐藏，未删掉安全检查来通过测试。
- 原去重、lease、呈现超时、输出错误、生命周期、原单键与B/X隔离回归全部保留。

## 实际输入恢复和释放

全部手机输入沿既有实例/正式矩形/既有Sender、Touch/type8与真实后端完成。
这些是ADB单指自动化功能验证，不是用户真人盲操作或游戏手感验收。

- Mode明确关闭后，新的普通DOWN恢复HID移动，实测普通位移与部署前一致。
- 安全按钮记录真实单击；双击拖动目标记录mouse_drag_up moves=119，鼠标完整释放。
- 原B和X分别XInput buttons2000/4000→0000，slot0仍为单个Xbox360，LT/RT均0。
- 观察器正常退出后，18:40:15独立读取真实状态：左右鼠标键、A/G、Shift/Ctrl/Alt均未
  保持；Xbox buttons/LT/RT和摇杆为0。见native-final-release.json。
- Diagnostics最终为Running/Connected、M-R1、Touch Session=None、Gamepad Available、
  Failures=0、LastGamepadError空、Presence Timeouts=0。Input Timeout只是既有无触摸
  诊断计数，不等于断线或关闭原因。最终生产日志没有新增导航、lease、呈现或输出错误。

## 部署与现场保护

部署前备份实际运行目录全部文件、依赖、settings.json和手机正式布局记录；不采用旧
报告里的未绑定配置。Guard.ps1由当前用户InteractiveToken/Limited、无触发器的临时
任务启动，实际PID19016、Parent2432、Session2，独立于Receiver和执行shell。
guard-ready.json记录身份。部署前检查保护确实存活。

本轮保护显式识别120秒有界部署窗口；窗口外进程持续缺失30秒或进展租约300秒过期
才恢复本轮现场备份，防止计划Stop/替换期间抢先回退。两次部署均确认旧进程退出和
UDP50000释放，使用RightpadReceiverTask.ps1独立启动，复制后/启动后比对候选DLL。
没有发生本轮回退，未故意破坏唯一设备来测试整套故障恢复。Guard.ps1 -RestoreOnly
保留恢复入口；不要求用户在失去输入后自己执行。结束前完成实际控制与释放检查，
再退出临时保护和观察器，保留恢复包及日志。

最终 **PID30132，wrapper17740，2026-10-19 18:32:54启动**：

- 用户BAODINE77777777\zhq，与explorer同Session2；Medium S-1-16-8192，Default桌面。
- 0.0.0.0:50000归PID30132，生产libvirtualhid，M_R1。独立启动器无后端覆盖。
- Receiver DLL SHA256：EFE0EE408A7A9A3D6BFE16C298D6C1BDE98BCD3B7AE0510929408EF30E99EF3F。
- 原生DLL SHA256仍为1F1D0C4F40D1171BD6A6153593C6C9DC24FA785EA4A56133CA126E089F1E83CC。
- 既有Android自然Heartbeat连接，Sender run CDC78881F2B1F565；Android PID20316未变。
  Receiver重启期间Sender目标转换可轮换run，不等于APK重启。
- 正式生产日志：
  `C:\rightpad\windows\test-results\receiver-runtime\20261019-183254-392-17740\receiver.log`。

正式settings开始/结束逐字节SHA256均为
62A4369497358BA7BA99B5DEB9583B8BEA8AB14377DC72DA92F689A298F041F0；手机布局均为
4C2EF09915914409BAA91A7AE69A8E81B5EF61DB30BA6081AAB38069E5294C33。本轮没有临时
修改正式配置，全部绑定、C参数、B/X、未知属性、静态M和正式布局保留。

恢复包在证据根recovery/；deploy-diagnostic.log、deploy-final.log、final-status.json、
verified-evidence.json、stages.log、actual-input.log和两份生产日志快照保留。

尚未完成：用户原先自动关闭场景的具体异常重现、实际游戏/独占全屏/反作弊兼容、
真人盲操作与长期体验，以及用户已明确选择本轮不继续补偿的速度完全衔接目标。
