# Git 源码交付（2026-10-24）

用户在只读检查后明确授权将当前项目提交并推送到既有仓库
`https://github.com/zhqxbmgit/rightpad.git` 的 `main` 分支。本轮以
`8b4565a9a21f1feaa9addefc2286295d6fbaaf33` 为原基线，汇总其后的现有工作树成果。
提交前已获取远端，确认原本地 HEAD 与 `origin/main` 一致，采用普通提交和非强制推送。

## 当前交付内容

- Receiver Ring 菜单、系统光标选择、原 Motion 移动路由、槽位完成与确认、软件绘制及相关诊断。
- Mode 鼠标左／右／中键绑定、普通按钮／Toggle 和鼠标按键来源合并。
- 普通 Touch 明确取消、反馈监听恢复、周期性 heartbeat 失败后的有效 Touch 发送。
- Android 显式开启且有界的触摸诊断录制。
- 开发产物外置、当前构建／测试／候选／备份入口和配套路径测试。
- Mode Android 本地 PRESS 反馈：接纳后复用 B 的 10 ms／255 策略。
- 当前契约、交接文档、各轮交付记录、适用回归与专用恢复脚本。

当前产品不含 Mode 电脑声音反馈。其实现、撤销和恢复材料保留历史事实，
`tools/ModeSoundDeployment` 已明确标记为历史用途，不是当前自动部署入口。
这些历史脚本没有接入当前生产程序，也不意味着声音功能重新发布。

## 验证边界

本轮执行工作树／暂存差异检查、文件用途与引用检查、必要新文件完整性检查和
`git diff --check`。不因 Git 交付重建程序、运行设备测试或重复部署。

首次暂存检查指出四份新增 Markdown 中共 13 处行尾双空格；逐项核对均为原文的
Markdown 硬换行，保留其排版和历史字节，不为通过空白检查批量重写历史文档。
其余暂存文件的默认空白检查通过；全部差异另以忽略行尾空格的临时命令配置检查，
不修改仓库或全局 Git 设置。

既有验证证据分别见：

- [声音移除交付](MODE_SOUND_REMOVAL_DELIVERY_20261024.md)：最终 Windows Release
  0 警告／0 错误，796/796 回归通过。当前 Receiver 保持声音移除后的产品状态。
- [Mode 本地 PRESS 交付](MODE_LOCAL_PRESS_DELIVERY_20261024.md)：27 个 Android
  main 测试类通过，新增夹具 94 项检查通过，assembleDebug／lintDebug 成功，
  lint 0 错误／14 项既有警告。该轮已更新 Android，未修改或重启 Windows Receiver。

这些数字来自相应轮次日志，不是本次 Git 操作重新执行的结果。
实际振动体感、正常操作与游戏验收沿用用户后续反馈，Git 推送不产生新的验收结论。

## 本地资料和运行边界

无关根目录文件 `pelican-bicycle.html` 原样保留且不纳入本次提交。
既有 Android／Windows 忽略规则继续排除构建输出、缓存、本机 local.properties、
正式运行包、bin 下的 holds 和 windows/test-results。历史已跟踪 measurements 不删除。
外部产物、APK、签名私钥、用户正式设置、手机布局和恢复包不加入本次源码提交。

本轮不改输入逻辑，不调整参数，不停止／重启 Receiver，不操作 Android 或游戏，
不清理历史资料，不改写既有 Git 历史。恢复脚本保留各自任务的身份限制和现场前提，
不得将旧计划直接用于以后实例。
