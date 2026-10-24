# Ring 菜单真实光标可见标记交付 — 2026-10-22

时间均为 Asia/Shanghai。本轮在当前含已有修改与未跟踪文件的工作区直接实施，没有从
Git HEAD 重建。只增加菜单真实位置的可见提示，修正设置页及交接文档中的过期关闭
描述；保留此前已交付的移动、点按、槽位完成、确认、退出及配置规则。

## 实现与范围

| 文件 | 本轮变更 |
|---|---|
| `C:\rightpad\windows\Rightpad.Receiver\Views\ModeMenuWindow.cs` | 同一 WPF 绘制层最后画白色主体、黑色外轮廓的 20×27 DIP 小箭头；外轮廓尖端为几何 (0,0)，使用同一 `ModeMenuSnapshot.CursorX/CursorY` 和 Ring 屏幕坐标变换，按 `Placement.Area.Scale` 缩放；明确裁切到现有 Overlay 区域 |
| `C:\rightpad\windows\Rightpad.Receiver\Views\ModeMenuSettingsWindow.cs` | 修正“动作后菜单保持”为当前已绑定完成／未绑定有效点按确认后关闭、中央保持规则；静态预览继续不绘制标记 |
| `C:\rightpad\windows\Rightpad.Receiver.Tests\ModeMenuTests.cs` | 新增 3 个离线 WPF 渲染检查：坐标/DPI/热点与 DIP 尺寸、顶层绘制/边缘自然裁切、静态预览/无快照透明；保留既有菜单回归 |
| `C:\rightpad\docs\MODE_SHORTCUT_MENU.md` | 当前可见标记契约、生命周期和无独立位置源说明；纠正上一轮报告已补部署证据的交接文字 |
| `C:\rightpad\docs\ARCHITECTURE.md` | 当前只读位置标记与 Windows 唯一坐标权威说明 |
| `C:\rightpad\docs\USER_PREFERENCES_AND_WORKFLOW.md` | 当前未绑定确认并关闭、中央保持、隐藏/受限可用及本轮可见标记交接 |
| 本报告及本轮证据目录 | 构建、测试、部署、恢复与只读释放证据 |

箭头在 Ring、中央填充、选中项说明条之后绘制；菜单打开时显示，关闭、故障退出、
Receiver Stop 或 Presenter Dispose 时随原窗口隐藏/关闭。固定锚点、12 DIP 中性区、
六方向及无外圈距离上限不变。坐标只来自同一份快照，不再次采样、不保存私有位移、
不预测/平滑/延迟/补偿、不夹在环或屏幕边缘；超出当前监视器浮层区域自然裁切。
设置页 `Preview=true` 跳过说明条和标记，不读桌面光标。沿用 Topmost、NOACTIVATE、
TOOLWINDOW、TRANSPARENT 和既有呈现故障处理，无新增线程、计时器、窗口或设置。

没有修改 Motion/M/C、灵敏度、量化、HID 路由、菜单移动一次分流、Gesture 隔离、
点按阈值、25 ms 已绑定按下/释放、未绑定确认并关闭、中央保持、RPHF/CONFIRM/身份/
去重、Mode 原单键/手动开关、B/X、Android、正式设置、手机布局、设备、驱动、Broker
和许可。没有调用改变系统光标的 API、Hook 或游戏注入；没有研究或操作 leftpad。

## 构建与自动化

证据根：`C:\rightpad\windows\test-results\mode-menu-cursor-marker-20261022`。

| 检查 | 实际结果 |
|---|---|
| .NET SDK | 8.0.422；Release；`--no-restore` 复用既有依赖，独立 OutDir |
| Receiver 隔离构建 | 0 警告、0 错误，`build-receiver.log` |
| Tests 隔离构建 | 0 警告、0 错误，`build-tests.log` |
| 菜单专项 `--mode-menu` | **33/33**，exit 0，`tests-targeted.log` |
| Windows 全量 | **776/776**，exit 0，`tests-full.log` |
| 测试 DLL 与部署候选 | Receiver DLL SHA256 相同 |
| 离线像素与图像检查 | 非零/负原点、100/125/150/200/300% DPI、小装饰区；箭头热点、20×27 DIP 尺寸、覆盖扇区/中央/说明条、超边自然裁切、预览对坐标变化像素不变、无快照全透明通过；已查看离线扇区图 |

本轮相较先前 773 项增加 3 个有效渲染检查。既有菜单输入、bound/unbound 一周期一次、
完整按下/释放再确认、未绑定反馈失败也关闭、中央不关闭、普通点击隔离、Motion 一次
路由、光标/显示/呈现故障及生命周期释放回归全部通过。没有在通过后重复全量回归。
离线图片保留于 `render\`；未改变真实桌面光标进行测试。

首次在受限沙箱中的构建尝试分别遇到 NuGet 用户配置读取限制、原生依赖仓库身份限制；
随后在当前用户正常环境完成隔离构建。保留 `build-receiver-no-restore.log` 中的失败，
首次 NuGet 错误记录见会话工具输出及保留的
`build-receiver-initial-sandbox-excerpt.log` 摘录（最初 `build-receiver.log` 被后续成功构建覆盖）。
部署只读核对首次用 `ReadAllText` 读取实时日志受共享锁阻止，改用允许并发写入的只读
流后 **23/23** 通过，原失败 `verify-deployment.log` 与成功 `verify-deployment-retry.log`
均保留。以上未影响 Receiver 输入，也没有因此重跑构建或全量回归。

## 受保护部署

- 本轮备份：`C:\rightpad\windows\test-results\mode-menu-cursor-marker-20261022\recovery\backup-20261022-160511`。
  备份全部现场 Release 文件和正式 `settings.json`；逐文件哈希、大小、候选身份在
  `manifest.json` / `checksums.sha256`，部署前再次核对当前 PID、创建时间、路径、设置。
- 复用上一轮已验证 `Guard.ps1`、`recover2.ps1`、`DeployCommon.ps1`、`Stop-Receiver.ps1`
  和临时任务注册器，在本轮独立目录保存副本。恢复脚本本轮只将“清空目录再复制”改为
  逐文件覆盖，保留所有既有文件；任务显式隐藏启动。未新增恢复框架、未故意制造故障。
- 独立计划任务 **Rightpad Cursor Marker Guard 20261022**，Interactive/Limited、无
  触发器；PID **34300**，父 **2432/svchost.exe**（计划任务服务），Session 5；
  **16:06:52.395** 完成本轮备份/依赖/候选/现场健康预检并 ready，早于 **16:07:06.177**
  开始停止旧实例。部署窗口 90 s，失败门限 40 s，健康计数目标 120 s，每 5 s 检查。
  保护独立于 Codex、当前终端与 Receiver 运行。
- 旧 PID **31232**，创建 `2026-10-22T05:38:38.8272886Z`，DLL `1E5B…B335`；
  启动器 Stop exit 0，无强制停止；**16:07:18.137** 确认退出且 UDP 50000 释放。
- 仅替换隔离候选 `Rightpad.Receiver.dll` 与 PDB；通过项目
  `C:\rightpad\windows\tools\RightpadReceiverTask.ps1` Start，exit 0。
- 新 PID **2664**，创建 **2026-10-22 16:07:21.051 +08:00**
  (`2026-10-22T08:07:21.0512861Z`)；实际 EXE 位于
  `C:\rightpad\windows\Rightpad.Receiver\bin\Release\net8.0-windows\Rightpad.Receiver.exe`。
- 实际映射的 Receiver DLL 路径与生产目录一致，SHA256 为
  **`6593DDB22249793D37BBDE403AB5A20BAB2EFF013DFD65DB30BFC275BD3C54AD`**，
  **510976 字节**，与候选及测试产物一致。EXE SHA256 仍为
  `B444947422873175F8B45A9206A9D870FC2853696FBB10496EE4417C546E33CE`；
  apphost 的版本/哈希不能单独区分这次 C# 更新，以 DLL 哈希为版本指纹。
- 用户 `BAODINE77777777\zhq`；Session **5** 与 explorer 相同；Medium
  `S-1-16-8192`；**Default** 桌面；启动包装器父进程为计划任务服务，ImmediateJobLimitFlags=0。
  UDP **0.0.0.0:50000** 唯一归属新 PID；实际加载原生产 `Rightpad.VirtualHid.dll`，
  哈希 `1F1D0C4F40D1171BD6A6153593C6C9DC24FA785EA4A56133CA126E089F1E83CC`；
  后端 libvirtualhid、M_R1。既有 Android 自然发现并以新 Sender run 重连，无 Android 操作。
- 正式设置 **1348 字节**，SHA256
  **`C3E4B9CBE21246FD004BC55DDEA360C32B88D5D13CA9F5AE20D36B7F69B4C7F1`**，
  与本轮现场备份逐字节哈希一致；无临时配置或手机布局改动。
- `deployment-verify.json` **23/23**：含进程/映射模块/创建时间、用户与桌面、端口、
  启动独立性、原后端、自然重连、设置、错误日志与释放观察。`input-release.json`
  为 20 次只读 GetAsyncKeyState 与 XInput 全按钮/扳机/轴采样，观察到全部释放样本；
  这不代表已经验证实际移动、单击或拖动体验。

Guard 于 **16:10:59.426** 正常完成，exit 0，健康计数 **120 s**（24 次健康检查，实际
监测耗时包含检查开销），`rollback=none`；最终临时任务收尾与新 Receiver 持续运行
核对见 `final-state.json`，证据索引链接如下。本轮未真实触发候选故障自动回滚；
既有恢复路径的历史实演不冒充本轮故障恢复。本轮保护实际执行的是独立预检、ready
与部署后健康观察。

## 用户验收与边界

自动化结论是源码、隔离 Release、菜单/Windows 回归、离线绘制、受保护部署与只读
运行状态检查通过。**实际桌面操作、游戏画面、盲操作、手机振动与长期手感待用户验收**。
本轮没有启动、操作或截图游戏，没有操作手机、使用 ADB 或进行手势注入；Connected
只证明自然重连，不证明实际控制验收完成。跨显示器架构与独占全屏可见性不在本轮扩展。

简短验收：桌面打开菜单观察标记 → 游戏隐藏系统光标时检查标记仍清楚 → 移动时标记
与高亮对应 → 有效点按或再按 Mode 后标记随菜单消失 → 确认普通移动、单击、双击拖动
与释放正常。中央点按仍保持菜单与标记，未绑定有效方向确认后关闭。

没有执行 Git commit/push/reset/checkout/历史修改，没有删除、清理、迁移或压缩既有
工作区内容与旧证据；仅按授权收尾本轮临时计划任务。所有已有修改与未跟踪内容保留。

证据索引绝对路径：
`C:\rightpad\windows\test-results\mode-menu-cursor-marker-20261022\README-evidence.md`。
运行日志绝对路径：
`C:\rightpad\windows\test-results\receiver-runtime\20261022-160721-015-8604\receiver.log`。
