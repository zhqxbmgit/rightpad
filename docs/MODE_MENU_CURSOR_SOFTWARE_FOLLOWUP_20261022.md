# Ring 软件绘制版本用户复测记录 — 2026-10-22

用户针对此前《地平线：西之绝境》白色箭头卡顿问题，在部署菜单窗口局部
SoftwareOnly版本后反馈：**“明显改善”**。保留当前版本，不继续调整、构建或重启。
本记录补充此前交付报告；历史报告与当时的待验收结论原样保留。

## 对应统计

| 既有菜单回调统计 | 改动前17:22复现 | 本次17:38–17:39复测 |
| --- | ---: | ---: |
| 统计时长 | 20.712秒 | 14.066秒 |
| 回调次数 | 510 | 1304 |
| 平均每秒回调次数 | 24.623 | 92.708 |
| 最大回调空档 | 809.967ms | 39.489ms |

新记录14条均为SoftwareOnly；排除首次打开及关闭区间的12条持续移动统计，平均
96.628次回调/秒、最大回调空档34.151ms，最大快照读取年龄10.733ms，最大取得
快照等待0.471ms。首次打开调用最高53.800ms，未隐去此开窗开销。

上述是原读取/呈现路径的回调统计，不是游戏FPS或屏幕最终呈现帧数；两次为用户
分别操作的记录。反馈与统计一致支持本次绘制调整有效，不能据此确认唯一GPU根因、
所有场景完全流畅，或普通移动/单击/双击拖动、手机反馈已全面验收。代理没有启动、
操作或截图游戏，没有操作手机/ADB或合成输入。

## 保留版本与核对

复核时间2026-10-22 17:47:16 +08:00。Receiver仍为PID **22204**，创建时间
**2026-10-22 17:32:46.9978643 +08:00**，Responding=true；路径：
`C:\rightpad\windows\Rightpad.Receiver\bin\Release\net8.0-windows\Rightpad.Receiver.exe`。

生产Receiver DLL SHA256：
`C3ACC098EBB5A6DE92684E1685505E9C6EDA627F851FB5579B4EC13917F0A88C`。
正式设置SHA256：
`C3E4B9CBE21246FD004BC55DDEA360C32B88D5D13CA9F5AE20D36B7F69B4C7F1`。
均与该次受保护部署一致。本次仅只读核查与补充记录，没有再次构建、测试或重启。
原隔离Release、菜单37/37、Windows780/780及部署23/23证据仍适用，未重复执行。

此前独立Guard在Stop前就绪，完成24次检查/累计120秒健康观察，exit0，无回滚；
临时任务已安全收尾，当前Receiver继续运行。恢复备份保留于：
`C:\rightpad\windows\test-results\mode-menu-cursor-presentation-fix-20261022\recovery\backup-20261022-173100`。
未在本次反馈后重新停止Receiver或启动部署工具。

## 证据

- 交付报告：`C:\rightpad\docs\MODE_MENU_CURSOR_SOFTWARE_DELIVERY_20261022.md`。
- 原始统计：`C:\rightpad\windows\test-results\mode-menu-cursor-presentation-fix-20261022\user-improvement-20261022-173945.json`。
- 显式对照与现场哈希：`C:\rightpad\windows\test-results\mode-menu-cursor-presentation-fix-20261022\user-improvement-confirmation.json`。
- 部署/恢复索引：`C:\rightpad\windows\test-results\mode-menu-cursor-presentation-fix-20261022\README-evidence.md`。

本次没有Git写入、文件清理、迁移或修改生产配置；保留全部既有修改与未跟踪资料。
