# Mode 原系统光标与方向菜单交付 — 2026-10-19

时间均为 Asia/Shanghai。本轮在已有未提交菜单实现上增量开发，完成隔离构建、全量回归、
受控部署、实际输入验证和恢复。没有 commit、push、历史修改、回退源码、git clean 或
资料清理。此前未提交/未跟踪文件均保留。2026-10-18 交付报告保持为历史版本证据。

## 当前操作契约

- 有效 Mode DOWN 开关，正常 UP、保持、重传、乱序、迟到转换不额外开关。关闭不发
  Esc、暂停键、取消键或普通鼠标键。点按动作后菜单保持，中央/未绑定无输出。
- 唯一操作指针是 Windows 系统光标。菜单拥有独立 IModeMenuCursor：SetCursorPos 绝对
  定位后用 GetCursorInfo 读取实际位置；选择、点按均重新读系统位置。不绘制或累积私有
  操作指针，不将菜单 Touch 放回普通 Motion/Gesture/HID mouse 路径。
- 打开位置＝固定选择锚点＝视觉中心；打开不移动光标。手机抬起并重新落指可继续导航。
  原菜单固定 2 DIP/手机像素导航比例保留，不改正常 Motion 或保存灵敏度。
- 中性半径固定 **12 DIP**，含边界；100/125/150/200/300% 为12/15/18/24/36物理像素。
  与默认480 DIP装饰直径独立。越过后按角度选最近方向，上/右上/右下/下/左下/左上，
  无外圈上限。平分线30/90/150/210/270/330度归顺时针槽；角度保留10位小数以稳定边界。
  Ring/Islands完全共用选择，不使用装饰扇区/按钮的范围命中。
- 单击识别继续使用接触DOWN时快照的现有Tap时限/位移阈值，UP后清双击资格。
  动作保持原25ms最小可见脉冲、原维护释放和16项有界串行队列，无Toggle/宏/连点。
- 边缘采用**裁切装饰，保持锚点**。使用原监视器完整屏幕区域和打开DPI，导航时不跟随。
  固定说明条被夹在屏幕内，显示当前方向/绑定或中性；小圆轮廓标明真实中性半径。
  靠边/角落朝屏幕外的方向可能不可达，可关闭后用普通输入移向屏幕内再打开。
  没有用跳光标、移动锚点、私有备用指针隐藏屏幕边界的实际限制。
- GetCursorInfo隐藏/抑制、内部ClipCursor约束小于打开监视器、读写失败、写后位置不同、
  原监视器区域/DPI变化均退出，清理/释放并要求新Touch DOWN恢复。既有2s呈现健康期限
  保留。环境恢复后可在重新同步后的新Mode DOWN重试；真实设备输出失败仍锁住，包含
  “环境故障时设备释放也失败”的组合情况。不解除游戏约束或强制显示系统光标。

参考了leftpad指定提交的真实光标、固定锚点、中性区、角度最近方向、无外圈上限机制：
[RadialSelectionEngine.cs](https://github.com/zhqxbmgit/leftpad/blob/6dd1664f643c736593f163b5a5e8c75c214aa16c/pc_ds4_server/PcDs4Server/RadialSelectionEngine.cs)、
RadialMenuController.cs、MainForm.cs。本轮从固定提交读取三文件并保存在证据reference/，
在rightpad内独立实现；没有迁移leftpad的触发、再次按键确认、持续保持、左键取消、框架、
传输、主题或设备。没有Android、haptic、协议/端口/worker、设备、driver/Broker/license变化。

## 修改范围

- ModeMenu.cs：固定锚点几何、12 DIP中性区、六方向选择、实际光标读取与独立导航。
- 新增WindowsModeMenuCursor.cs：最小Win32适配、DPI/监视器/可见性/约束/读写检查。
- ModeSession.cs：注入光标、既有维护循环检查、环境故障重试与设备失败门闩区分；原锁、
  代次、租约、脉冲、进入/退出清理和新DOWN屏障继续负责归属。
- ModeMenuWindow.cs：删除私有指针与旧夹中心捕获，保留两外观和非激活透明浮层；加固定
  中性轮廓和边缘可见选择说明。ModeMenuSettingsWindow.cs更新使用说明。
- ModeMenuTests.cs补充选择/真实位置权威/失败组合等测试；ModeMenuOverlayProbe.cs改成
  受控系统光标检查；ModeInputObserver.cs增加系统位置日志。普通回归使用fake接口。
- AGENTS及当前架构/Mode功能/协议说明更新；旧交付报告不改。

## 构建与自动化

证据根：`C:\rightpad\windows\test-results\mode-system-cursor-20261019`。

| 检查 | 结果/证据 |
|---|---|
| 最终Release含固定原生依赖 | 0警告、0错误；release-build.log |
| 最终Windows全量 | **757/757，0失败**；release-tests.log |
| 原生fake接口 | ctest 1/1通过；native-tests.log；原生DLL哈希不变 |
| 菜单14组 | 六方向/中性含边界/顺时针平分线/间隙和远外圈、两外观一致、旧接触隔离、脉冲、去重、lease、配置/断线/显示/输出故障、原B/X屏障与恢复 |
| 光标注入 | 请求未应用时不选中、点按取最终实际位置、抬起重落与固定锚点、capture/read/move失败；故障恢复重试；释放故障仍锁住 |
| 几何/渲染 | 负坐标、边缘/角落、小视口、1/1.25/1.5/2/3比例；Ring/Islands 280/480/720渲染在render/ |
| 受控真实光标探针 | 本机192 DPI，40物理像素半径依次选六向；打开中性、不跳转、锚点不变、前台仍为目标 |
| 受控隐藏/约束 | 2x2内部Clip后退出；解除后新同步可开；隐藏后退出、恢复显示后可开；Mode关闭。overlay-probe.log |

普通自动化不移动真实光标。真实探针是独立交互任务，不占生产端口、不创建输出设备，
结束恢复原光标和原Clip状态。首次探针发现环境错误门闩阻止重开，已修复并重测；
overlay-probe-first.log和probe-error.txt保留首次失败，当前成功见overlay-probe.log。
最初沙箱构建因供应商仓库ownership检查失败，改在获批隔离目录构建；不是改Git配置。

## 实际输入与输出

均用现有Android实例的ADB单指input，经生产View/Sender/type8/RPMD/Touch及真实后端。
没有重启Android、安装APK或改布局。这是自动化功能注入，**不是用户真人盲操作或游戏
手感验收**。actual-input.log含设备路径、Raw Input、XInput和真实系统坐标；阶段时间见
verification-stages.log，校验脚本verify_evidence.ps1和结果final-output-and-settings.json。

- 首次新候选PID2076上完成Ring与Islands、六向短移、未绑定点按、外圈外执行，以及
  键盘A和菜单LT255→0。菜单导航/点按区间没有HID鼠标相对位移或左键。
- 最终候选仅补环境异常同时设备释放失败的门闩边界；最终PID19092再次复验Ring、
  中央消耗、短移上方向、两次A完整脉冲、Mode关闭以及普通鼠标恢复。
- 最终锚点(1881,1131)，真实光标记录到(1881,331)，距锚点800物理像素，装饰外半径480；
  该位置点按仍选上并输出A。两次A的真实设备均为VID_1209/PID_0002，scan001E，
  flags0000→0001；最终菜单验证段HID Mouse PID_0003事件数**0**。
- 关闭后新落指产生真实HID移动；安全按钮记录mouse_click及PID_0003左键0001→0002。
- 正常双击拖动：Receiver第二DOWN距前UP46ms，dragging=True；安全目标记录
  mouse_drag_up moves=232，真实HID左键完整释放。
- 原G普通按钮通过统一Save临时选择，真实scan0022 flags0000→0001；原Toggle、目录、
  保存和生命周期同时受全量回归覆盖。测试后统一Save恢复初始菜单用途。
- 最终B/X分别XInput2000/4000→0000，仍为slot0单个Xbox360，既有映射和参数未改。
- 从日志重算所有观察到的键盘键、左/右鼠标键均已释放，最终XInput buttons0000、LT=RT=0。

本机B动作仍同时出现handle0的系统右键附带消息，曾令前台系统菜单出现；与历史报告
现象一致，不是PID_0003鼠标输出。没有推断具体外部映射程序或修改系统映射。
A键在中文输入法环境可唤出候选条，这是真实键盘输出的系统结果，不是“游戏已处理”。

## 游戏输入边界

SetCursorPos不提交HID相对移动/Raw Input设备报告，受控目标和实际日志已核对；普通
WM_MOUSEMOVE/系统位置仍能被前台程序看到。不能因为不走Motion/HID就声称所有游戏
隔离已解决。隐藏/约束测试用受控目标完成，检测到后明确退出和恢复，不启用备用指针。
本轮没有合适的已运行Steam游戏安全场景，未新增安装/购买/进入游戏；真实游戏、
无边框/独占全屏、反作弊、多物理显示器/所有实机DPI、真人盲操作与长期体验仍未验收。
边缘裁切和朝外方向不可达是明确策略限制，几何模拟不冒充多显示器实机通过。

## 现场保护、失败和最终版本

任务前实际运行PID30900/Session2，与历史PID不同。本轮先备份整个实际运行目录、
依赖和当前settings.json，记录手机正式布局与哈希；无历史配置覆盖。
最初独立保护PID30324/Parent2432/Session2，InteractiveToken/Limited、无触发器。

第一次部署GUI Stop未实际生效，最终只对核对路径/Session的旧PID30900终止，确认退出和
UDP50000释放后，通过批准RightpadReceiverTask.ps1启动PID2076。deployment.log记录。

第二次替换期间，最初保护的“部署后进程消失立即回退”误把计划内空窗当故障，实际
自动恢复本轮备份旧DLL和初始正式设置。部署后哈希核对发现B3353B...属于旧版，因此
没有把回退PID1460冒充最终新版本。deployment-final.log、restored、watch-finished保留。
旧保护消息写了“不触动settings”，实际因temporary-settings标记恢复了本轮现场配置；
该消息不准确，正式设置哈希及本报告以实际文件为准。

随后新独立RecoveryGuard.ps1由任务启动：PID12112/Parent2432/Session2，进程持续缺失
30s才回退，另保留300s无进展租约，不再争抢正常重启空窗。确认独立运行后重新部署。
最终退出旧PID1460、确认端口释放、复制后和启动后都逐字节核对候选DLL哈希。

**最终Receiver PID19092，wrapper30932，2026-10-19 13:24:58启动**：

- 当前用户BAODINE77777777\zhq，explorer及Receiver同为Session2；Medium S-1-16-8192，
  Default desktop；0.0.0.0:50000归PID19092，独立计划任务运行。
- 生产libvirtualhid、M_R1；当前Diagnostics显示Running/Connected、Gamepad Available、
  Gamepad Failures=0、LastGamepadError空、Touch Session=None；未记录receiver_error。
- Receiver DLL SHA256：`A29580B22A63CDAFE22C3BFB4D8608ED58154DDE562EDA591AF2AF035724CF2C`。
- 原生DLL SHA256保持：`1F1D0C4F40D1171BD6A6153593C6C9DC24FA785EA4A56133CA126E089F1E83CC`。
- 最终启动日志自然接纳Android run `3EF0D0B00B496414`。Android进程始终PID20316；
  Receiver停启期间发现目标转换可以轮换Sender run，不能把它说成APK重启。
- 最终运行证据deployment-verified.log、final-status.txt；完整生产日志在
  `windows/test-results/receiver-runtime/20261019-132458-840-30932/receiver.log`。

所有实际释放、控制恢复和正式文件核对完成后，以completed结束Watch2并正常退出观察器。
独立保护不是产品服务/常驻watchdog。原项目Receiver Dev任务继续运行。恢复实际触发证明
本轮包能恢复控制，但未故意破坏唯一设备进行整套故障注入。

## 正式设置、手机布局与恢复资料

最终settings.json与初始现场备份**逐字节相同**：
`62A4369497358BA7BA99B5DEB9583B8BEA8AB14377DC72DA92F689A298F041F0`。
用途菜单、Ring、上槽A、其余五槽未绑定；原G/普通按钮保留。Sensitivity12/12、
M_R1、保存C Amax160000/Tau30、其余C/B/X值及未知属性全部保留。临时LT和单键用途撤销。

手机正式布局SHA256开始/结束均为：
`4C2EF09915914409BAA91A7AE69A8E81B5EF61DB30BA6081AAB38069E5294C33`。
Android仍为原前台MainActivity、静态M、原正式矩形，未重启、构建、部署或写布局。

恢复包：证据根的recovery/（现场程序全目录、依赖、正式settings、布局记录和哈希）。
恢复入口Recovery.ps1 -Mode Restore，只恢复本轮六个备份产物；若temporary-settings标记
存在则恢复本轮初始settings，再调用批准的独立启动器。不要求用户失去输入后手工运行。
新保护脚本RecoveryGuard.ps1、启动脚本StartRecoveryGuard.ps1及所有失败/恢复日志保留。

观察器关闭于13:32:45，在测试工具handle0合成左键DOWN关闭窗口时先退出，因此未能
记录该次工具UP；不是HID鼠标保持。最终只读GetAsyncKeyState/XInput在13:33:18确认
系统左右键、A/G均未按住，Xbox slot0 buttons/LT/RT均零，见native-final-release.json。
verify_evidence.ps1因此分别校验设备事件完整释放和关闭后的真实系统状态，保留该观测
窗口退出边界，未把缺失的合成UP伪造成日志事件。
