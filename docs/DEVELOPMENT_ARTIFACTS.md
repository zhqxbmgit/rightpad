# 当前开发产物流程（2026-10-22）

源码为 `C:\rightpad`。未来开发产物默认根目录由仓库根部 `artifact-root.txt`
统一定义为 `C:\rightpad-artifacts`。同在 C 盘只改变归属，不减少 C 盘总占用。
长期设计与正式交付报告仍在 `docs`。当前工具不迁移、删除、压缩或改写旧资料。
本轮路径实施不授权部署、停止/重启 Receiver、操作手机、真实输入或 Git 写入。
未来任务仍需按当轮明确授权处理；隔离构建成功本身不授权部署。

## 推荐入口

Windows PowerShell 5.1 可用。任何终端工作目录均可使用绝对入口：

```powershell
$task = '20261023-example' # 每轮选新身份；不要照抄已完成任务身份
& C:\rightpad\tools\Invoke-Development.ps1 -Action NewTask -Task $task
& C:\rightpad\tools\Invoke-Development.ps1 -Action BuildWindows -Task $task
# 下条是显式执行普通 fake/loopback 回归；不执行设备 smoke 选项
& C:\rightpad\tools\Invoke-Development.ps1 -Action TestWindows -Task $task
# 只备份现场正式程序/配置并预检，不停止、部署或执行 Recover
& C:\rightpad\tools\Invoke-Development.ps1 -Action Backup -Task $task
```

`BuildWindows` 构建 Tests 项目及其 Receiver 引用、原生依赖，记录构建日志，
复制 Receiver 输出为该任务候选并生成 SHA256 清单。已有候选拒绝覆盖，重做应换任务身份。
候选绝不可冒充当前已运行版本。`TestWindows` 只执行同一任务/配置已构建的测试 DLL，
找不到则报错；不会回退到旧 bin。普通测试不等于本次任务必需全跑，按改动选适用验证。
例如 `-Action TestWindows -Task $task -TestArguments @('--artifact-paths')` 只运行五项
源码定位/离屏渲染检查。TestWindows 在子进程期间将 TEMP/TMP 指向该任务 evidence 下
temporary-fixtures，结束后恢复原进程值；没有修改系统环境。直接运行测试的临时夹具
仍遵守原系统临时目录契约。测试用构建元数据定位源码，不从外部二进制路径向上猜测；
源码依赖测试应在源码路径变化后重建。测试渲染图默认写该任务 evidence/render 与 panel-preview。

2026-10-24 增加显式 `BuildWindows -ReuseDeployedNative`（仅 Release）：当本轮未改原生
桥接且正式桥接已经匹配时，将正式原生 DLL 和两份许可复制到本任务原生目录并逐一
核对哈希，记录 `evidence/native-reuse.json`，通过 `RightpadReuseNative=true` 跳过原生
重建。默认构建流程不变；该选项不授权部署或升级依赖。候选清单改为递归记录相对
文件名，完整覆盖候选中实际存在的发布文件。此前声音资源已按用户决定移除，
该通用候选清单与原生复用入口继续供当前开发使用。

```powershell
& C:\rightpad\tools\Invoke-Development.ps1 -Action BuildNative -Task $task -Configuration Debug
& C:\rightpad\tools\Invoke-Development.ps1 -Action TestAndroidJvm -Task $task
# 下条只读采集 PC；只有单独授权后才加 -IncludeAndroid
& C:\rightpad\windows\tools\FreezeRightpadFlightRecorder.ps1 -Task $task
& C:\rightpad\windows\tools\RightpadReceiverTask.ps1 -Mode Status
```

Android 构建用 Windows Wrapper（没有设备访问、安装或重启）：

```powershell
Set-Location C:\rightpad\android
.\gradlew.bat "-PRightpadArtifactScope=$task" assembleDebug
# 仅配置/路径检查：
.\gradlew.bat "-PRightpadArtifactScope=$task" -I ..\tools\gradle-paths.init.gradle rightpadArtifactPaths
```

## 路径关系和隔离

| 产物 | 默认路径（根目录下） |
|---|---|
| Windows 输出 | `builds\windows\<任务或local>\<项目>\bin\<配置>\net8.0-windows\` |
| .NET 中间文件、NuGet 项目 assets | `builds\windows\<任务或local>\<项目>\obj\<配置>\`，SDK 继续分 TFM/RID |
| 原生输出/中间文件 | `builds\native\<任务或local>\<配置>\bin` / `obj` |
| 原生固定依赖源码 | `builds\native\dependencies\libvirtualhid`；固定版本校验，多个轮次共用只读源 |
| Android 各项目输出 | `builds\android\<任务或local>\app\build`（根项目 `_root`）；AGP 继续隔离 debug/release 等 variant |
| Android JVM tests/fixtures | `builds\android\<任务或local>\jvm-tests` |
| Android 项目缓存 | `builds\android\gradle-cache`（Gradle 自身锁管理；不是用户级缓存） |
| 每轮任务 | `test-results\<日期-任务名>\evidence`、`candidate\<配置>`、`recovery\backup-<生成时刻>` |
| 以后新实例运行日志 | `receiver-runtime\<时间-PID>`，根部保存 `current.json`、`launch-options.json` |
| 新导出资料 | `exports`，使用共享 helper 取根目录后显式指定；现无通用导出生成器 |

直接 `dotnet build/run` 默认使用外部 `local` 范围；推荐带
`-p:RightpadArtifactScope=<新任务>`。Receiver 与 Tests 从源目录名取得稳定项目身份，
不会共用 obj；WPF 临时项目沿用所属项目的中间目录。旧 `bin/**`、`obj/**`
显式排除源码通配符，移动中间目录不会把历史生成 C# 重新编入。
同一任务/配置增量构建属于正常复用；独立轮次使用新身份，避免无必要的并行构建。

## 覆盖与错误处理

项目工具的 `-ArtifactRoot 'C:\path with spaces'` 只作用于该命令，不改环境。
直接 MSBuild 用 `-p:RightpadArtifactsRoot=...`（正常的 MSBuild 全局属性优先级），
`BaseOutputPath`、`OutputPath`、`BaseIntermediateOutputPath` 或 SDK `--artifacts-path`
仍按 SDK 原语义生效；跨多个项目不要传同一个扁平 `-o` 或共享 obj。
`--artifacts-path` 只覆盖托管 SDK 输出，桥接模块仍按 Rightpad 根目录/任务生成。
不要为任务覆盖参数修改系统环境变量、全局工具配置或统一默认文件。

Android 覆盖整个根目录使用当前命令的进程环境 `RightpadArtifactsRoot`，完成后恢复原值；
Wrapper 与 settings 读取相同值。`-PRightpadArtifactScope` 选择轮次；不支持用 `-P`
覆盖根目录，以免 Wrapper 缓存与 Gradle 输出分裂。显式 `--project-cache-dir` 优先于
Wrapper 默认缓存位置。SDK、Gradle 用户级缓存、NuGet 用户缓存、工具安装位置不变。

所有当前工具对目录创建/写入失败显式报错，没有项目内降级路径。根目录必须绝对；
任务身份限字母、数字、点、下划线和横线，拒绝 `.`/`..`。参数含空格需要整体引用。
不要自行猜测候选路径，再从父目录寻找源码、正式设置或 launcher。

## 正式运行与恢复

正式程序仍为 `C:\rightpad\windows\Rightpad.Receiver\bin\Release\net8.0-windows`；
独立启动器仍为 `C:\rightpad\windows\tools\RightpadReceiverTask.ps1`；
正式 settings 仍为 `%LOCALAPPDATA%\rightpad\settings.json`。
源码路径、外部候选和正式目录是三个独立概念。正式文件需要以后明确授权的部署，
普通构建不再覆盖正式程序，也不为了 EXE 锁定停止 Receiver。

目前运行实例继续写它原先打开的
`C:\rightpad\windows\test-results\receiver-runtime\...`；该实例的记录不迁移、不截断。
Status 同时读取新旧 current.json，优先核对真实存活身份（PID、创建时间、路径），
若两个记录都存活则报错，不自动处理进程。Status 不创建新运行目录或改计划任务。
以后获授权 Start 的新实例才用外部日志目录；独立任务依据持久源码配置读取默认值，
不依赖终端 cwd 或临时环境。现有计划任务命令、正式 EXE 路径保持兼容。

`Backup` 默认只复制现场正式目录、settings 和包内 `Recovery.ps1`，带相对文件清单/哈希。
包内文件依据 `$PSScriptRoot` 定位，可整体复制后离线 Preflight；正式部署目标和外部
launcher 仍作为绝对路径明确保留。脚本默认 Preflight，只有明确 `-Mode Recover`
才执行 Stop/恢复/Start。本轮只做 fake 包与真实快照的离线路径/哈希预检；未执行 Recover。
实际部署前仍必须验证独立计划任务下的恢复保障，不能把 Preflight 当作真实恢复成功。
脚本不会删除正式目录多出来的文件；涉及依赖集合变化的部署必须另行评估恢复方案。

## 保留旧位置的例外

- 已部署正式 EXE/依赖、settings、用户布局、正在写的日志均保持原位。
- Receiver 自身的 `%LOCALAPPDATA%\rightpad\diagnostics` 滚动诊断与正式配置属于产品运行数据，
  本轮不改产品代码；Freeze 复制出来的新证据默认在外部任务目录。
- 手机端诊断仍是手机内部运行文件；本轮未操作 Android，没有改变其路径或策略。
- 旧 windows/test-results、measurements、各轮 Deploy/Guard/Recover/phone-verify、旧恢复包
  和 android 旧 build/.gradle 都是历史资料，未批量改写，原脚本仍可能写旧目录。
  不要将历史报告命令当成当前开发默认入口；重新开发使用本页入口。
- `CheckVirtualHidCompatibility.ps1` 是只读兼容性工具，仍可读项目内旧固定依赖源码，
  缺失时按既有逻辑读远端；它不创建构建或证据输出。
- POSIX `android/gradlew` 是标准历史 Wrapper，非当前 Windows 11 推荐入口；直接使用时
  Gradle build 目录仍遵从 settings，但项目缓存需显式 `--project-cache-dir`。未新增其他平台支持。
- 手工命令/第三方工具显式指定的输出、旧 adb pull/导出指令无法由默认配置拦截，必须自行
  指向任务目录或 exports；没有新增全局拦截或自动清理机制。

本轮交付与实测清单见 [DEVELOPMENT_ARTIFACTS_DELIVERY_20261022.md](DEVELOPMENT_ARTIFACTS_DELIVERY_20261022.md)。
