# B3：隐藏编辑器后旧 Try 迟到首次生效 — 修复记录

2026-10-16，本轮仅修复 B3 及必要回归。未执行 commit/push。
HEAD 保持 `7e68f8cc709fdec7f624d3e061c0fb3e43e7e058`。

## 实际基线与根因

开工时 tracked 工作区无修改，原有三个未跟踪文件
`pelican-bicycle.html`、`rightpad-src.txt`、`rightpad-win-src.txt` 保留。
原审计目录及历史失败证据未修改、未删除。

本轮实际旧进程为 PID19152（普通 quoted EXE 启动），不是审计结束时的
PID7152。部署 DLL SHA256 仍为
`F889ADE35BCA6A780DBC09A1E70FE945A45E8D511847F504D7D1535435505793`。
审计已记录部署 PDB 与 HEAD 的 RemoteLayoutView 空白差异，本轮不将其
称为整份 HEAD 的逐字节源码构建。修复前另行从当前全部托管源码/XAML
独立构建，复核 B3，不使用部署版本身份代替源码验证。

旧客户端仅以三次发送计数限制 operation，没有单调总寿命；编辑窗口的
隐藏、关闭、最小化、Unloaded 停止 timer，却保留可重发的 operation。
重新打开先 force Poll，120 秒后仍能重发原 request/token 的 Try。
Android 正确接纳尚未消耗的 next token，从实际应用时起算 60 秒。
因此根因在 Receiver 请求生命周期；手机独立恢复机制没有失效。

修复前两项证据均保留在 `windows/test-results/b3-fix-20261016/`：

- `baseline-repro.log`：当前旧源码构建在虚拟 120 秒后发送 kind=2，原 request=2、token=1。
- `baseline-ui-failure.log`：真实 WPF Hide 接点期望转 Query，旧行为实际仍为 Try，测试失败。

## 修改与规则

- `RemoteLayoutClient.cs`：记录 operationStartedAt / operationExpiresAt；固定寿命 3 秒，
  保留最多三次、间隔一秒的发送。截止瞬间即不可重发，force 不豁免。
  Capture、Begin、Accept、Poll、Suspend 统一检查过期。
- `RemoteLayoutView.cs`：进入非活动显示状态先 Suspend，再停原 timer、发送核对 Query。
  Suspend 将未确认操作移入 reconciliation，保留原 run/route、epoch、revision、geometry、token。
  之后只能 Query。重复生命周期事件不创建新操作、timer 或订阅。
- `RemoteLayoutEditorWindow.cs`：向 View 透传可选单调时钟，生产默认仍是 Stopwatch；
  用于真实 WPF 生命周期的 120 秒虚拟时间回归，没有窗口尺寸/颜色/布局调整。
- `RemoteLayoutTests.cs`、`RemoteLayoutWindowTests.cs`：总寿命/边界、真实宿主、身份与资源回归。
- `RemoteLayoutJavaTests.cs`、`Fixtures/LayoutPeer.java`、测试 Program 的显式
  `--remote-layout-java` 入口：实际 C# 客户端/编码与未改动的 Java 生产事务类核对。
  Java 适配器放在 Windows 测试目录，仅使用 stdin/stdout，不接触手机。
- `REMOTE_LAYOUT_PROTOCOL.md`：同步客户端寿命和核对约定。

Query 携带原 token/base。手机未见过 Try 时消耗该 token 后报告 Restored；
Try 已先应用时返回实际 Waiting/Trial/终态，不重启试用、不自动 Keep/Revert。
Keep 未确认时同样转 Query，按手机 Saved/Restored/Failed 报告。
未得到权威确认时保留 Uncertain，禁止新 Try，不能声称已恢复或已封存。

操作前 Query 的响应不能解除后来的未知事务；同一 base 的较低 token Ready
也不能确认封存。确认新 epoch 时丢弃其他旧 Query correlation，避免旧 epoch
响应反向污染当前身份。临时断线保留同 route/run 的 reconciliation；新 route/run
清除旧 capability，只查询新身份。最多一个未知事务、八个 Query correlation。
可见时按原每秒节奏继续安全查询，隐藏时不持续轮询；没有新增产品线程/服务。

## 验证

最终代码完成以下检查：

| 检查 | 结果 |
|---|---|
| 全部当前托管生产源码/XAML Release 隔离构建 | 0 warnings / 0 errors |
| Windows 原完整入口与新增回归 | 734/734 |
| 实际 C#/Java 编码核对，显式独立入口 | 五个场景全部通过 |
| 原 JVM 安全集合 | 19 个入口完成，其中 RemoteLayoutTests 307 checks |
| git diff --check | 通过 |

Windows 完整入口在运行前检查了网络、输出和文件接点；使用 loopback 临时监听、
fake Mouse/Xbox、临时设置路径。隔离目录没有复制 native DLL，未调用真实 HID。
部分既有 Runtime 测试仍会向 loopback 的 UDP50002 发 best-effort 包，不绑定
产品监听端口，也不向手机发送。Launcher 回归只解析参数，不调用 Task Scheduler。
未运行会绑定手机产品端口的 JVM listener、Gradle、APK 安装或真机布局 smoke。

新增覆盖：首次 Try 丢失，真实 Hide/Close/Minimize/Unloaded 停止轮询，虚拟
推进 120 秒重开，旧 Try 总发送数保持 1；持续/稀疏/暂停 Poll、截止前一 tick、
截止瞬间、force、重复生命周期；重复/丢失/乱序响应、临时断线、route/run/epoch
变化、100 次核对仍至多八个 correlation、新用户点击使用新 token。

跨语言结果：

- Query 先到：Restored，原 X=700，写盘次数 0；投递原迟到 Try 后仍不能应用；
  重复 Query 幂等，新明确 Try 用下一 token 正常进入 Trial。
- Try 先到、结果丢失：2000 ms 查询剩余 58000 ms；重复 Try 在 3000 ms
  只返回剩余 57000 ms，不重复发布；60000 ms 恢复原布局并同步 PC。
- Keep 已保存、结果丢失：查询 Saved，X=650、写盘一次；迟到 Keep/Try 不回退。
- Keep 首包未到：120000 ms 查询 Restored，X=700、写盘 0；迟到 Keep 不复活。
- Keep 保存失败、结果丢失：查询 Failed，X=700；不猜测成功或重复保存。

详细日志：`fixed-build.log`、`full-build.log`、`full-regression.log`、
`production-java-regression.log`、`java-regression.log`。构建/复现工程及 Java
编译脚本也保存在同一证据目录，未覆盖原审计文件。

跨语言回归可先编译 Android 的 RemoteLayoutTests 及其纯 Java 依赖到独立目录，
再将 Windows 测试目录的 Fixtures/LayoutPeer.java 编译到同一 classpath，运行：

```text
Rightpad.Receiver.Tests.exe --remote-layout-java <java.exe路径> <独立Java类目录>
```

该入口不启动 Receiver，不绑定 socket，不连接 ADB，不自动运行于普通完整测试。

## 受控部署与数据保护

更新前保存五个已知可用托管产物、settings.json、手机正式布局原始字节及哈希。
现用 Receiver 隐藏在托盘，自动窗口工具不能直接取得它；用户仅用现有 rightpad
打开主窗口，之后全部停止/更新/恢复由工具完成，没有要求键盘或额外鼠标。

独立隐藏恢复保护先启动。通过正常 UI Stop 等待 Runtime Stopped 后，确认旧
Runtime 已清理资源，再结束 PID19152 的停止态进程，沿用现有 launcher 启动。
launcher 在 Start 前检查 UDP50000 已释放。仅替换五个托管产物，未替换 native DLL、
Driver、Broker、许可、设置、APK 或手机布局。更新失败恢复旧五个文件并重新启动的
脚本为证据目录 `Deploy.ps1`；本轮成功分支已执行，未故意触发故障或回退演练。
独立保护收到健康标记后退出，没有增加常驻监控。

2026-10-16 16:16:33 +08:00 确认新 PID27184、Runtime RunId=1、Running/Connected。
部署 DLL 与最终验证构建均为 SHA256：
`2E26066C080F1A57BA09EEB55CBADEB48399B9BFFDDEC325A11BCD4F813A65E8`。
恢复旧 DLL 为上文 F889 开头的版本。

部署后未提交任何真实候选。实际打开/关闭/重开同一个编辑窗口，手机返回
Restored/expired、View 1200×2670、revision=5、geometry=1，B=1045/506/155/2164；
Try/Keep/Restore 禁用，无草稿修改。这里显示的是手机既有终态，不把查询发送
本身说成新完成的恢复，也不声称真机重现了 B3。

复用已审读的 GUI Android E2E，仅对手机已核对的鼠标区域 (300,1000) 做短 swipe、
tap 和正常双击拖动，Windows 使用临时惰性点击目标。实际结果：原生移动 480、
LEFT DOWN 3、LEFT UP 3。没有第二个 Receiver、真实布局操作或故意断连/卡键。
`actual-input.log` 为真实 Android→现用 GUI→libvirtualhid→Windows 输入证据，
属于功能自动化，不代表真人游戏手感/盲操作验收。

16:20:08 +08:00 的末次核对：PID27184、RunId=1、Running/Connected、
MouseOutputFailures=0、GamepadAvailable=true、GamepadFailures=0、LastError=null、
无活动 Touch。`runtime-final.json` 和 `protection-final.json` 保存了完整状态。

| 数据 | 更新前后 SHA256 一致 |
|---|---|
| settings.json | `32CB4487021708105E83E45B20FAE769F9C78E4CF8511693436A9B1D03FB6ADA` |
| 手机正式布局原始字节 | `0AAB5C746BD726CB6D32B0B40E5161238F7D91269E912F471F10B902328A8C9D` |
| Rightpad.VirtualHid.dll | `5C0AD7AA482D0A6A1A91A6665C2703D410E7A9B6444ABAEBB2099CA07A9C3006` |

## 边界

未在唯一输入设备上人为丢包/断网、试用真实候选、Keep 写盘或强制制造卡键。
120 秒隐藏与事务竞争由实际客户端/编码、真实 WPF 接点和 Java 事务类隔离验证。
原 Android 生产协议/状态机未改动，也未重新核对已安装 APK 的完整源码身份。
若所有核对包/响应持续丢失，只能保持结果待确认；在手机实际收到封存 Query 前，
不能承诺已封存仍在网络中的 Try。准备的回退路径未通过故意破坏部署来验证。
B1/B2/D1/D2/R1/R2、Motion、灵敏度、B/X 映射和触感、编辑器视觉设计均未修改。
