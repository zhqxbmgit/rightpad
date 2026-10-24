# Ring 呈现停顿证据与窗口软件绘制候选 — 2026-10-22

时间为Asia/Shanghai。用户在诊断版自然复现地平线卡顿后反馈“已复现”。本轮首先
保存并分析该片段，随后只对Ring窗口选择固定SoftwareOnly绘制。**候选已部署，
是否改善实际游戏体验仍待用户复测；不把回归通过写成卡顿已修复。**

## 复现数据

原PID15748（DLL `D303F503…2819DC`）在17:22的两次打开周期，有19条统计，覆盖
20.7120726秒：原坐标读取 **4789次**、位置变化 **2085次**、呈现回调 **510次**。
现有flight环境记录确认该片段前台为HorizonForbiddenWest，输出失败0。

| 统计结束时间 | 窗口时长 | 原读取 | 位置变化 | 回调 | 最大更新间隔 |
|---|---:|---:|---:|---:|---:|
| 17:22:23.188 | 1.697秒 | 392 | 171 | 15 | **809.967ms** |
| 17:22:31.800 | 1.005秒 | 265 | 127 | 4 | **589.019ms** |
| 17:22:38.737 | 1.068秒 | 238 | 102 | 4 | **785.088ms** |

调用内更新绘制内容通常0.02–0.1ms；排除打开时约2ms的窗口准备，样本最大0.110ms。
可确认：位置变化并未停止，但两次呈现调用完成之间出现数百毫秒空档，足以造成标记
跳动。回调数不是最终屏幕呈现数/游戏FPS。旧统计不包括取得MenuObservation之前
的锁等待，所以尚不能把全部空档都归因于GPU或WPF调度；本轮补齐该测量。

原始片段保留在证据根 `before\receiver.log`、`menu-frame-reproduction.json`、
`reproduction-environment.json`，显式汇总在 `summary.json`。首次PowerShell时间
字符串过滤因自动转换DateTime得到空集合，错误结果保留在
`summary-invalid-timefilter.json`；正确结果改用DateTimeOffset范围筛选，19条。

## 候选变更

生产修改仅 `C:\rightpad\windows\Rightpad.Receiver\Views\ModeMenuWindow.cs`：

- SourceInitialized时对当前Ring窗口自己的HwndTarget固定设置
  `RenderMode.SoftwareOnly`。这是窗口局部CPU光栅化选择，不改变进程级RenderOptions、
  Receiver主窗口、设置预览或其他窗口的偏好，不改Windows/显卡/游戏设置。
  桌面最终合成仍可能使用GPU，因此不能把本候选宣称为绕过所有GPU/DWM等待。
- 保留同一窗口、WPF、原Rendering回调、保留箭头、快照坐标和裁切；不加框架、窗口、
  线程、定时器、轮询器或自适应分支，不改变光标或输入。
- 在现有每秒统计中加入实际窗口 `renderMode` 和 `maxSnapshotFetchMs`，从调用
  session/getter之前计时，把取得快照等待与Present更新调用开销分开。

局部CPU绘制是针对已测得呈现空档的受控候选，不是已确认GPU根因。保留统计便于
用户同一场景复测前后比较。API已与本机.NET8.0.28 Reference XML核对，真实隐藏
HWND自动检查也验证实际HwndTarget值；不只靠源码字符串判断。

没有修改Motion/M/C、灵敏度、量化、HID、移动一次分流、Gesture隔离、点按阈值、
槽位按下释放/确认关闭、未绑定确认关闭、中央保持、Mode单键/手动开关、B/X、
Android/手机布局、正式配置、协议、设备、驱动、Broker或许可。无预测/平滑/补偿/
私有位移、ShowCursor/SetCursorPos/ClipCursor/SetCapture、Hook或游戏注入。

## 验证

- Receiver/Tests隔离Release：均0警告、0错误。
- 菜单专项 **37/37**，Windows全量 **780/780**，exit0。通过后未重复全量。
- 新增隐藏HWND检查：Ring实际目标为SoftwareOnly；普通窗口及进程渲染偏好未变；
  窗口未显示、ShowActivated/Focusable/命中测试等保持禁止、Topmost保持。
- 原坐标/DPI/热点/顶层/裁切/预览及输入/确认/安全退出回归通过。
- 原时间统计测试补充取得快照等待，与Present耗时分开计量。

测试源码：`C:\rightpad\windows\Rightpad.Receiver.Tests\ModeMenuTests.cs`。
当前契约/交接：`MODE_SHORTCUT_MENU.md`、`USER_PREFERENCES_AND_WORKFLOW.md`；历史
交付报告原样保留。测试只创建不显示的HWND和离线图，不操作真实桌面光标。

## 恢复与部署

- 重新核对当前旧实例15748、创建时间、实际DLL、正式设置；完整现场备份：
  `C:\rightpad\windows\test-results\mode-menu-cursor-presentation-fix-20261022\recovery\backup-20261022-173100`。
  Release全文件、settings、manifest/checksums保留；候选只有DLL/PDB不同。
- 独立任务 **Rightpad Cursor Software Guard 20261022**，PID27876，父2432/svchost.exe，
  Session5、当前用户Interactive/Limited、无触发器、隐藏启动。
  **17:32:15.068** 本轮预检成功并ready，早于 **17:32:32.320** 开始Stop。
- 批准启动器Stop exit0，无强制停止；**17:32:44.137** 旧进程退出并释放UDP50000。
  覆盖候选DLL/PDB，经项目批准独立交互启动器Start，exit0。
- 新实例、实际映射DLL、上下文、端口、libvirtualhid、M_R1、Android自然重连、设置
  一致等 **23/23** 通过，具体字段见 `deployment-verify.json`；不以Connected冒充
  实际游戏/真人控制验收。
- 新PID **22204**，创建 **2026-10-22 17:32:46.9978643 +08:00**
  (`2026-10-22T09:32:46.9978643Z`)；生产EXE路径
  `C:\rightpad\windows\Rightpad.Receiver\bin\Release\net8.0-windows\Rightpad.Receiver.exe`。
  实际映射Receiver DLL **521216字节**，SHA256
  **`C3ACC098EBB5A6DE92684E1685505E9C6EDA627F851FB5579B4EC13917F0A88C`**，
  与隔离候选/测试产物一致；用户BAODINE77777777\zhq、Session5=explorer、Medium/
  Default，UDP0.0.0.0:50000归22204，原生产native DLL未变。
- 正式settings1348字节、SHA256
  `C3E4B9CBE21246FD004BC55DDEA360C32B88D5D13CA9F5AE20D36B7F69B4C7F1`，
  与本次备份一致，无临时配置或布局改动。
- 只读键盘/鼠标/XInput释放观察通过；没有合成任何输入。
- Guard累计健康目标120秒，每5秒检查，部署窗口90秒；最终结果与临时任务收尾见
  `guard-result.json`、`final-state.json`。**17:36:18.942正常完成，24次健康检查、累计
  120秒，exit0、rollback=none**。未故意故障演练或真实触发本轮回滚，备份保留。

## 待用户复测

在地平线同一场景打开Ring，连续移动约10–15秒，关闭后告知仍卡/明显改善/正常；
执行代理读取同一统计字段比较，另看新maxSnapshotFetchMs。无需录屏或命令。
本轮没有启动、操作或截图游戏，没有操作手机/ADB，没有真人移动/单击/拖动或手机
振动验收。若回调仍停顿且快照取得等待不高，仍需进一步定位呈现路径，不能说已修好。

没有Git写入、文件清理、迁移或历史修改；所有已有内容保留。仅收尾本轮临时Guard。
证据根：`C:\rightpad\windows\test-results\mode-menu-cursor-presentation-fix-20261022`。
证据索引：该目录 `README-evidence.md`。
运行日志：
`C:\rightpad\windows\test-results\receiver-runtime\20261022-173246-963-3668\receiver.log`。
