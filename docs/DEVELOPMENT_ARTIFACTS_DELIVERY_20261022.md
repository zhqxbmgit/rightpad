# 开发产物默认外置交付（2026-10-22，Asia/Shanghai）

## 结果与范围

源码仍为 `C:\rightpad`；未来产物默认根目录已实际接入为 `C:\rightpad-artifacts`。
当前推荐流程见 [DEVELOPMENT_ARTIFACTS.md](DEVELOPMENT_ARTIFACTS.md)。本轮只调整开发
工具、输出与测试路径，没有部署候选、停止/重启/替换 Receiver、操作 Android 或真实输入。
没有迁移、删除、重新压缩历史资料，没有执行 Git commit/push/reset/checkout/clean。

## 实际修改的当前入口

| 当前入口 | 改动 |
|---|---|
| `artifact-root.txt`、`tools/ArtifactPaths.ps1` | 单一默认根目录、绝对路径/身份校验、目录创建与写入失败显式报错 |
| `Directory.Build.props/targets` | .NET 项目、配置、任务范围隔离；保留显式输出/SDK artifacts 覆盖；排除旧 bin/obj 源码通配符 |
| `windows/Rightpad.Receiver/Rightpad.Receiver.csproj` | 原生构建传递相同根目录与任务身份；从外部原生目录复制 DLL/许可证 |
| `windows/tools/BuildVirtualHid.ps1` | 外部固定依赖源码、各任务/配置的 obj/bin；新增只读 Plan |
| `android/settings.gradle`、`android/gradlew.bat` | 外部项目 build 及项目缓存；明确显式缓存覆盖优先；保持用户级缓存/SDK 不变 |
| `android/tests/Test-ProtocolV2Encoder.ps1` | JVM classes/诊断夹具默认进入外部构建目录，支持本次命令覆盖 |
| `tools/Invoke-Development.ps1` | 新任务、Windows 构建/测试、原生构建、Android JVM、备份入口；日志、候选与恢复同属任务目录 |
| `tools/New-RecoveryBackup.ps1`、`tools/Recovery.ps1` | 现场只读复制/哈希清单；包内依自身位置定位，正式目标/launcher 明确外部引用；默认仅 Preflight |
| `windows/tools/FreezeRightpadFlightRecorder.ps1` | 新 PC 诊断证据默认外部任务目录；Android 读取需显式 IncludeAndroid；修正已有 PS5.1 语法不兼容 |
| `windows/tools/RightpadReceiverTask.ps1` | 正式 EXE 路径不变；未来新实例外部日志；Status 优先核对新旧记录中的真实存活身份 |
| Tests 项目的 csproj、`TestPaths.cs` 及相关路径调用 | 构建元数据记录源码/产物根，不再从 bin 向上猜源码；三处源码契约检查、C panel 和 Ring 渲染路径接入任务 evidence |
| `tools/Test-ArtifactPaths.ps1`、`tools/gradle-paths.init.gradle` | 可重复的离线故障/路径/配置验证入口 |
| 当前 README、AGENTS、架构、用户工作说明 | 更新当前入口与约束；未改写历史交付报告 |

修改 Tests 目录中的既有 Program/ModeMenuTests 时保留了其他任务已有的内容，仅增加路径验证选择和输出位置调整。

## 实际验证

本轮证据根：`C:\rightpad-artifacts\test-results\20261022-artifact-defaults`。

- Windows PowerShell **5.1.26100.9168** 下完成 26 项路径/脚本检查，包括九份当前脚本解析、
  绝对路径、驱动器根保留、空格根目录、覆盖参数、任务身份穿越拒绝、文件挡住目录创建、
  临时夹具 ACL 拒绝写文件、旧实例优先、缺新记录仍识别旧实例、双存活记录报错、原生 Plan、
  恢复包整体复制后的 Preflight、篡改哈希拒绝、JVM 命令 fake 的离线入口执行。
  临时 ACL 在 finally 中恢复；fake 测试没有调用 Java、ADB 或真实输入后端。
- Windows/原生外部 Release 隔离构建通过，**0 警告、0 错误**。发现并修正测试源码定位后，
  再次增量构建通过，仍为 0/0。未运行候选 Receiver。
- 托管路径实际核对 Receiver 与 Tests 各自独立的 obj/output、引用项目路径、
  Debug 与 win-x64 路径、含空格根目录、显式 OutputPath 与 SDK ArtifactsPath 优先级。
  Compile 项目清单中没有纳入源码目录里的旧 bin/obj 生成文件；相对及驱动器相对根目录被拒绝。
- 只运行 `--artifact-paths` 的 **5/5** 源码契约/离屏渲染检查，未执行无关全量回归或设备 smoke。
  三个 Ring PNG 与两个 C panel PNG 实际生成于本轮 evidence。普通 TestWindows 的临时
  TEMP/TMP 仅对子进程设置，finally 恢复，不修改系统环境。
- 原生 `bridge_fake_api` **1/1** 通过，使用本轮外部构建；不连接驱动或输出设备。
- 独立 launcher 参数离线测试通过；生产/开发覆盖及 M-R1 检查保持。
- Android 使用 `--offline --no-daemon --max-workers=1`，配置及 `compileDebugJavaWithJavac`
  成功（18 个执行任务）。实际 Java classes、AGP 报告及项目缓存均在外部目录。
  未生成/安装 APK、未查询或操作手机。编译仅有 Java 已弃用 API 提示。
- Gradle 额外纯配置验证覆盖空格根目录、显式缓存路径优先级、相对根拒绝、根路径被文件挡住
  时失败，最后再次验证修改后 Wrapper 的默认分支。每次 single-use daemon 在命令结束退出。
- PC Freeze 入口在 PS5.1 下实际运行并写入外部 evidence，未使用 IncludeAndroid。
- 新正式恢复快照完成包内/外部依赖哈希 Preflight，未执行 Recover，也未注册或运行独立恢复计划任务。
  正式包为 `recovery\backup-20261022-222539-400`。此前本轮生成的 fake 包和早期快照保持原样，
  由 `evidence\recovery-index.json` 区分；最终正式包是本次推荐复核对象。
- `artifact-manifest.json` 记录本轮六个代表产物的创建/修改时间、大小和 SHA256。
  时间检查确认它们在任务开始后生成；测试目录中的 Receiver.dll、Receiver 构建及候选 DLL 哈希一致。
  `candidate.json` 另列完整候选文件清单。

首次失败与修正均有记录：PS5.1 暴露 Freeze 既有 hash literal 解析问题；路径打印 init 脚本
曾误用 task 的 allprojects；JVM fake 计数作用域曾导致测试断言失败；Gradle 初版显式缓存
重复参数被拒绝，已改为检测显式参数后不插入默认值。SDK 显式 artifacts 覆盖也在配置核对中
修正。最后身份比对最初受 PowerShell JSON 日期自动转换影响，改按 UTC 日期值比较后通过；
现场 PID/创建时间实际一直一致。以上不涉及生产故障或输入中断。

## 当前实例和配置

前后核对：Receiver **PID 22204**，创建时间 **2026-10-22 17:32:46.9978643 +08:00**，
路径仍为正式 `windows\Rightpad.Receiver\bin\Release\net8.0-windows\Rightpad.Receiver.exe`。
Status 仍确认相同交互 Session 5、Medium、Default 桌面及 UDP 50000 归属。

正式 settings 前后 SHA256 相同：
`C3E4B9CBE21246FD004BC55DDEA360C32B88D5D13CA9F5AE20D36B7F69B4C7F1`。
旧 `current.json` 逐字符相同；仍定位到
`C:\rightpad\windows\test-results\receiver-runtime\20261022-173246-963-3668\receiver.log`。
新 receiver-runtime 根目录尚未创建，因为本轮没有新启动。

手机、布局、正式设置、Motion/M/C、B/X、菜单交互、输入协议及正式部署位置未修改。
没有 ADB、手机安装/重启、游戏、真实鼠标/键盘/手柄测试输出。本轮没有重新进行手机/游戏或
物理操作验收，也没有将这些未执行项目表述成已验收。

## 例外和后续边界

旧 test-results/measurements/历史构建与恢复脚本保持原行为；它们属于留档，不是当前默认入口。
产品 `%LOCALAPPDATA%\rightpad\diagnostics`、正式配置/程序、当前旧日志及手机内部文件仍在原位。
只读兼容性工具仍可能读取旧原生依赖；SDK/NuGet/Gradle 用户缓存与工具安装目录不在本轮范围。
POSIX Wrapper、绕过当前工具的手工 Gradle/CMake、显式指定旧路径的导出/adb pull 命令不能由
本次默认配置全局拦截。直接运行测试的系统临时目录仍按原契约，推荐入口会将其放入任务证据。
没有现行统一导出生成器；新导出应使用外部 exports 或本轮 evidence，旧导出文件保持原样。

原生固定依赖为本轮从原有固定版本源码**复制**到外部构建输入，原件及旧 bin/obj 没有迁移或
清理。此前六个异常 result.json、gzip 原件、维护 README 和 phone-verify.ps1 未重新处理。
已有未提交/未跟踪工作保留；本轮没有 Git 写操作、全局配置修改、安装服务、常驻监控或自动清理。

以后真正部署前，仍须按既有要求核对候选、现场正式设置与依赖，验证独立恢复保护，并取得该轮
对应授权。本轮的路径校验和 Preflight 不能代替独立计划任务环境验证或实际恢复成功。

## 证据索引

主索引：`C:\rightpad-artifacts\test-results\20261022-artifact-defaults\README.md`。
关键清单位于 `evidence`：`before.json`、`after.json`、`status-final.txt`、
`path-tests.json`、`msbuild-paths.json`、`artifact-manifest.json`、`candidate.json`、
`recovery-index.json`；原始构建、失败、重试、预检日志见同目录。当前源码不包含这些新构建证据。
