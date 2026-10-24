# Ring 标记卡顿诊断版 — 2026-10-22

时间为Asia/Shanghai。用户确认上一轮版本在《地平线：西之绝境》中白色标记仍非常
卡顿；能显示系统鼠标的游戏中，白色标记也流畅。**卡顿未修复，根因尚未确认**。
本轮诊断版补齐现有日志缺少的时间数据，不能当作又一次流畅度修复或实际游戏验收。

## 前置只读证据

见 `C:\rightpad\docs\MODE_MENU_CURSOR_STUTTER_AUDIT_20261022.md`。保存并分析已有
Receiver/flight日志；地平线36个约1秒快照中，10个近期有Motion输出，坐标各不同，
读取失败0；多数相邻坐标变化与HID位移吻合。没有足够证据断言游戏锁死/居中坐标，
也没有逐帧呈现数据可确认WPF/DWM根因。

Microsoft说明WPF Rendering事件频率会随系统和负载变化，不能视为屏幕刷新率保证：
[官方文档](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/how-to-render-on-a-per-frame-interval-using-compositiontarget)。

## 诊断实现与边界

| 文件 | 变更 |
|---|---|
| `C:\rightpad\windows\Rightpad.Receiver\ModeMenu.cs` | 仅在原Capture/Read成功后记录累计reads、位置变化changes、读取完成的Stopwatch时间；新增只读ModeMenuObservation，携带同一Snapshot引用，不额外读位置 |
| `C:\rightpad\windows\Rightpad.Receiver\ModeSession.cs` | 同一锁下返回Observation；统计日志best-effort，失败不控制输入 |
| `C:\rightpad\windows\Rightpad.Receiver\Views\ModeMenuWindow.cs` | 既有WPF回调里累计时间/计数，约每秒和关闭时一条mode_menu_presentation；没有逐帧日志/样本队列 |
| `C:\rightpad\windows\Rightpad.Receiver.Tests\ModeMenuTests.cs` | 新增2项：读诊断不额外采样、计数随原读变化并在关闭失效；伪时钟区分采样/回调停顿、跨统计窗口长间隔、关闭flush及新周期 |
| 当前菜单契约、交接和本报告 | 明确用户实测未通过及诊断版用途，不改历史交付报告 |

统计字段：wallTime、fromTicks/toTicks、callbacks、reads、positionChanges、snapshotChanges、
maxGapMs、maxReadAgeMs、meanReadAgeMs、maxPresentCallMs。readAge从原位置读取/显示
校验完成时算起；callbacks是WPF回调数，**不是最终屏幕呈现次数或游戏FPS**。
关闭/Dispose冲刷最后不足1秒的汇总并清空本周期统计；不对输入或位置产生动作。

无新增位置采样器、线程、定时器、轮询器、窗口、设置或协议。没有改光标位置来源、
绘制外观/变换、Motion/M/C、灵敏度、量化、HID、Gesture、菜单一次移动分流、阈值、
槽位按下释放/确认关闭、未绑定确认关闭、中央保持、Mode、B/X、Android/布局、正式
设置、驱动/Broker/许可。无预测/平滑/插值/私有位移、Hook或游戏注入。

## 自动化

证据根：`C:\rightpad\windows\test-results\mode-menu-cursor-frame-diagnostic-20261022`。

- Receiver与Tests隔离Release构建：均0警告/0错误。
- 菜单专项：**36/36**；Windows全量：**779/779**；exit0。
- 没有源码变更或失败后无故重复全量回归。
- 自动化证明诊断计数与菜单回归正确，不证明游戏卡顿已消除。

## 受保护部署

- 重新核对旧PID11692和创建时间、DLL `AA7525E3…3E6C6B`、设置与生产路径。
- 新备份：
  `C:\rightpad\windows\test-results\mode-menu-cursor-frame-diagnostic-20261022\recovery\backup-20261022-171329`。
  含完整现场Release、正式settings、manifest/checksums；仅候选DLL/PDB与现场不同。
- 独立任务 **Rightpad Cursor Diagnostic Guard 20261022**，PID3268，父2432/svchost.exe，
  当前用户Interactive/Limited、Session5、无触发器、隐藏启动。**17:14:01.209** 在
  独立计划任务环境完成本轮备份/候选/现场健康预检并ready，早于 **17:14:15.531** Stop。
- 批准启动器Stop exit0，无强制停止；**17:14:27.471** 确认旧进程退出、UDP50000释放。
  覆盖测试过的DLL/PDB，经项目独立启动器Start，exit0。
- 新PID **15748**，创建 **2026-10-22 17:14:30.4466842 +08:00**
  (`2026-10-22T09:14:30.4466842Z`)，生产EXE路径：
  `C:\rightpad\windows\Rightpad.Receiver\bin\Release\net8.0-windows\Rightpad.Receiver.exe`。
- 实际映射Receiver DLL **520192字节**，SHA256：
  **`D303F503D823FED54691FDF3DA9BAAC2E4B0B27C71E0BD094FABA24DCE2819DC`**，
  与候选/测试产物一致；apphost EXE及原生产native bridge不变。
- 用户BAODINE77777777\zhq，Session5与explorer相同，Medium/Default，包装器独立由
  计划任务服务启动，无即时Job限制；UDP0.0.0.0:50000归新PID；libvirtualhid、M_R1，
  Android自然重连。部署核对 **23/23**。
- 正式设置1348字节，SHA256
  `C3E4B9CBE21246FD004BC55DDEA360C32B88D5D13CA9F5AE20D36B7F69B4C7F1`，
  与本轮备份一致，无临时配置或布局改动；只读输入释放检查通过。
- Guard健康计数目标120秒，每5秒检查；最终结果/临时任务收尾以guard-result.json、
  final-state.json为准。**17:18:04.180正常完成，24次健康检查、累计120秒，exit0，
  rollback=none**。未故意制造故障，未真实触发本轮自动回滚，恢复包保留。

## 当前所需的用户操作

诊断版部署后，用户在地平线中打开Ring，连续移动约10–15秒复现卡顿，再按Mode关闭，
告知“已复现”即可。执行代理读取日志，无需用户命令、录屏、截图或ADB注入。
可对照自然记录中的reads/positionChanges与callbacks/gap/readAge；即使回调连续，也
不能直接声称最终屏幕呈现连续，必要时仍需另做受控呈现测量。

本轮没有启动、操作或截图游戏，没有操作手机/ADB；未进行真人移动/点击/拖动或
手机振动验收。没有Git写入、文件清理、迁移或历史修改；仅注销本轮已完成临时Guard。

证据索引：
`C:\rightpad\windows\test-results\mode-menu-cursor-frame-diagnostic-20261022\README-evidence.md`。
运行日志：
`C:\rightpad\windows\test-results\receiver-runtime\20261022-171430-409-22084\receiver.log`。
