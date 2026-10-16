# Windows 远程屏幕按键布局交付记录 — 2026-10-15

功能已接入真实 Android/Receiver 链路并原位部署。未执行 Git commit、push、
源码回退或用户数据重置。最终 Receiver PID **34296**，Runtime Running，
Android Connected，libvirtualhid Mouse/Xbox 正常。最终检查 LEFT released、
Xbox buttons=0、无活动 Touch，Mouse/Xbox 输出失败计数均为 0。

## 1. 基线与本次改动

初始 HEAD 为 `0a73b046d98198235d3bd8bbbc648c5f222d7e06`，结束时未变化。
Windows 实际基线完整测试为 719/719；Android 原有 JVM 入口通过。
初始运行版本已有 Receiver 本地 M/C 和 C 参数面板，当前选择 M-R1。
已保存 Sensitivity 12/12、M/C 选择、C 参数及 B/X 行为参数均保留。

初始 DLL 仍含历史 FIR 类型；源代码已在此前独立任务撤除 FIR/Tau10 扩展。
本次候选使用可识别的当前源代码基线加布局功能，没有恢复 FIR/Tau10，
没有修改 Motion/Settings 算法文件，没有删除历史失败检查或证据。
原有未提交/未跟踪内容保留；9 个未参与本功能修改的既有文件，其原始
Git diff 与开工备份逐项相同。共享文件仅增加本功能必要接点。

主要实现：

- `windows/Rightpad.Receiver/Layout/RemoteLayoutProtocol.cs`：独立 RPLY framing。
- `RemoteLayoutClient.cs`：当前 route、一次操作、有限重试、结果查询及旧包封存。
- `RemoteLayoutDraft.cs`：无传输/磁盘依赖的 Windows 草稿。
- `windows/Rightpad.Receiver/Views/RemoteLayoutView.cs`：点击式编辑和真实比例示意图。
- `ControlsView.xaml`、`UdpReceiver.cs`、`ReceiverRuntime.cs`：页面、既有 UDP 和生命周期接入。
- Android `RemoteLayoutProtocol.java`、`RemoteLayoutSession.java`、`RemoteLayoutBridge.java`：
  framing、手机事务权威、View/UI dispatcher 接入。
- `MainActivity`、`TouchCaptureView`、`ScreenControlLayoutEditor`、`ScreenControls`、
  `ScreenControlEditorPanel`、既有 Sender/listener：必要的布局/生命周期/本地版本接点。

协议与状态机详见 `docs/REMOTE_LAYOUT_PROTOCOL.md`。

## 2. 所有权、版本与事务

Android 的 `files/screen-controls.properties` 是唯一正式布局；Windows 不持久化
布局缓存。读取/升级/草稿不写文件。保存沿用原 normalized ratios、稳定 ID、
未知属性保留及 tmp + fd.sync + ATOMIC_MOVE 机制。

身份包含当前目标/Sender run、Android Activity epoch、正式 revision、View
geometry、transaction 和 snapshot serial。Android 本地 Save 共用 revision；
本地编辑入口先结束远程试用。旧基线/几何草稿拒绝，需显式刷新。

Ready → Waiting（接触结束，最长 10 秒）→ Trial（实际应用时开始 60 秒）→
Saved 或 Restored/Failed。Keep 仅在有效 Trial、提交入口尚未到期时开始。
UI 线程串行化提交与 timeout，成功原子写盘后才报告 Saved；迟到 timeout
不能回滚已成功保存的事务。重复包不续期、不重复保存。

Windows 每次操作最多三次尝试，然后查询手机实际状态。若首次 Try 从未到达，
结果查询先封存那个未使用 token，再报告未应用，防止迟到首包后来生效。
Keep 结果丢失通过查询/幂等响应恢复，不猜测成功。重连只读，不自动重放草稿。

## 3. 草稿隔离与交互实测

实际 Controls 页面读取 View **1200 × 2670**，B 为 **1045/506/155/2164 px**，
X 和保留 Mode 沿用真实当前布局。Preview 使用 View px → WPF DIP 等比映射。
固定 Settings/Power 只读，未变更 B/X 映射、Mode 现有含义或命中优先级。

选择、X/Y/W/H +/-、1/10/50 px 步长只改草稿。数字只读，所有操作可左键完成。
关键 Try/Keep/Restore 放在预览上方，当前窗口下无需滚轮即可点击。
Waiting/Trial 时“放弃草稿”禁用，恢复使用明确的 Restore 操作。
普通设置 Save 仍只调用 Settings 保存流程，与布局事务独立。

真实 GUI 将 B X 改为 1044 时，手机正式文件哈希不变，界面标记草稿未应用。
点击 Try 后才收到手机 Trial 确认。实际点击 Keep 后界面显示手机已确认持久化，
回读文件 B xRatio=0.87，即 X=1044；之后已恢复用户测试前的原始文件字节。

## 4. 本地倒计时与恢复

Android 使用 elapsedRealtime，重复 Try 不延长期限；View timer 按剩余期限调度，
新 DOWN 前先检查到期。View/inset/tool 几何变化立即推进 geometry，旧候选
不能抢在下一个 tick 或 DOWN 前应用。真实 View resize 拒绝旧完整候选已通过。

真实 GUI 观察到 60 秒递减；期限结束前离开布局页，停止该页查询，随后重新
进入看到 Restored、X=1045、草稿已清除。立即 Restore 也实测通过。
试用期间不改正式文件。Activity pause/destroy、目标/run 变化、本地编辑进入
会结束试用；重建仅加载正式文件。绘制/命中共用整份 map，当前接触不重新归属。

保存、写盘失败、读取、View resize、Activity 重建检查使用 LayoutTestFileGuard；
测试结束均还原原始文件 SHA256：
`1B01E16F3C177F395D5CA362CB4A94855D60ED90ABF2C931192671945D847958`。

## 5. 故障与验证边界

自动化覆盖超时/Keep 同时刻竞争、提交入口二次时钟检查、实际临时文件路径
故障、丢失结果查询、重复和迟到消息、未到达 Try 的 token 封存、几何及本地
Save 版本冲突、未知属性保留、完整性/溢出拒绝、连接/run fencing，以及布局
消息不能建立/续租 Touch presence。Runtime Stop 立即撤销布局编辑通道。

真实设备测试包含 View/router 共享矩形、当前接触等待、真实 atomic Save、
真实 IOException 恢复、成功保存后 Activity 重建、原始字节恢复。
没有在唯一输入设备上额外实施物理断网或强制 Receiver 崩溃；这些恢复条件
由独立手机状态机和故障测试覆盖。自动化不等于真人盲操作/游戏体验验收。
不同 Windows 系统缩放比例的额外实机检查尚未执行；当前实际桌面布局已检查。

## 6. 测试结果

| 检查 | 实际结果 |
|---|---|
| Windows 初始完整基线 | 719/719 |
| 最终 Debug / Release build | 均 0 warnings / 0 errors |
| 最终 Debug / Release 完整测试 | 均 726/726 |
| Android 新布局 JVM 逻辑/协议/存储检查 | 307，通过 |
| Android 其他 JVM 回归 | 全入口通过 |
| RemoteLayoutSmoke 真机专项 | 22，通过 |
| 原有 ScreenControlsSmoke 真机回归 | 275，通过 |
| Android Debug、Release、test APK | 构建成功 |
| Android lintDebug | 0 errors，14 条既有 warnings |
| Native Debug / Release bridge_fake_api | 各 1/1 |
| git diff --check | 通过 |

最初 lint 发现 ignored `android/local.properties` SDK 路径冒号未转义；已修正
转义而保留同一路径，最终 lint 通过。未抑制 lint 规则或删除项目检查。

一次实际输入检查紧接 Activity 启动执行，未观察到 movement，日志保留为
`actual-input-resume-too-early.log`。加入 Connected/恢复就绪前置等待后，原测试
断言不变，最终真实 Android→Sender→UDP→Receiver→libvirtualhid→Windows 检查通过：
477 次 native move，3 次 LEFT DOWN，3 次 LEFT UP，覆盖小移动、单击、双击拖动。
真实 XInput 观察到 X=0x4000、B=0x2000 保持，UP 后均为 0；LEFT released。
这是设备注入功能证据，不是实际人手或游戏感受的验收结论。

## 7. 部署与数据保护

中断前备份原 Receiver 全部产物、settings、已安装原 APK、手机原始布局，
并准备隐藏独立恢复进程及恢复命令。通过现有 UI Stop 清理 Runtime，再用项目
原 launcher 启动；Android install -r 原位更新，没有卸载/清数据。
首次测试组件的 USB“继续安装”倒计时提示已按用户明确要求代点，安装成功，
没有修改手机安全设置。Driver/Broker/许可保持不变。

最终安装身份：Android versionName 0.1 / versionCode 1，APK 354759 bytes。
主 APK signer 与原版相同。回读安装包及运行 DLL 均与最终候选逐字节匹配：

- APK SHA256 `F25F397D3E08F81591458A53B2E30B00B416FADA8AB39DF9D9E735C6E9BE7D7C`
- Receiver DLL SHA256 `2278A40E920F25B712FA8ECB8E3959A2CA59138E5913A5DC6A4E107AD78A3B93`
- 未替换的 native DLL SHA256 `5C0AD7AA482D0A6A1A91A6665C2703D410E7A9B6444ABAEBB2099CA07A9C3006`
- 原设置 SHA256 `32CB4487021708105E83E45B20FAE769F9C78E4CF8511693436A9B1D03FB6ADA`

恢复产物及脚本保存在 ignored `windows/test-results/remote-layout/recovery/`
及相邻的 Recover.ps1 / RecoverAndroid.ps1 / RestoreLayout.ps1。恢复保护进程
在成功标记后自动退出；没有创建长期服务、计划自动化或新增产品网络端口。
未进行实际整套版本回退演练，恢复路径的命令/产物/签名与字节备份已核对。

## 8. 最终可用状态与证据

Receiver **34296** 正常运行，libvirtualhid Mouse/Xbox，M-R1，Android 已连接。
实际 Mouse、单击、双击拖动及 B/X 保持释放已验证；无未结束试用、无草稿，
当前布局为用户测试前原布局。Controls 页面保留在布局入口，读到实际 1200×2670
View 和 B X=1045。用户设置、布局字节、原生桥均与备份匹配。

完整日志在 `windows/test-results/remote-layout/`，包括：
`windows-*-final.log`、`android-tests-final.log`、`android-device-layout-final.log`、
`android-device-regression-final.log`、`gui-{draft,trial,expired,reverted,saved,final}.txt`、
`actual-input-final.log`、`actual-gamepad-final.json`、`protection-final.json`、
`runtime-final.json`、`existing-change-verification.json` 及原版本恢复目录。
