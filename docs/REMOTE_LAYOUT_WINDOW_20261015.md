# Receiver 独立大布局编辑窗口 — 2026-10-15

Controls 仅保留“打开大布局编辑器”入口。MainWindow 延迟创建并持有唯一的
RemoteLayoutEditorWindow / RemoteLayoutView / RemoteLayoutDraft，复用同一个
Runtime.Layout 客户端；没有第二个 Receiver、Runtime、网络 worker 或虚拟设备。

## 显示和生命周期

编辑器是正常 Windows 窗口，每次打开默认最大化，可还原、调整大小、移动、
最小化或关闭；顶部始终有“返回 Receiver”。关闭和返回只隐藏同一窗口并
显示主窗口，不发送 Try/Keep/Revert，不丢弃草稿。独立窗口显式引用原 Window
主题，未重做主窗口主题、导航或设置页。

视图只有原来的一个 200 ms DispatcherTimer。隐藏、最小化和 Unloaded 停止它；
可见时幂等恢复并查询现有客户端，不新增 Tick 订阅。重新打开不再无条件
Load 草稿，改由已有 Observe 规则处理手机终态或冲突。显式刷新/放弃仍可
重载草稿。Receiver 正式退出时关闭宿主，Runtime 清理保持原路径。

有限 Grid 高度来自窗口客户端，完全移除旧 ScrollViewer 高度耦合。顶部
WrapPanel 可换行；下方预览与 284 DIP 参数列作为相邻的一组居中。
缩放取预览实际可用宽高与手机 View 宽高之比的较小值，扣除细框及左右
42 DIP 外侧标注区。整机比例、坐标和命中矩形不变；无局部放大、平移或
滚轮功能。900 px 级高度来自释放窗口空间，不是固定预览高度。
View/版本为参数区小字，底部仅一条可换行短说明。

预览继续纯黑、绿色按键、白色选中框、橙色固定工具。标签为单行；外侧标注
按侧错开并约束在完整预览高度内。静止视口和相同快照不重建预览子元素。

## 验证

- Release 构建零 warnings/errors。现有回归加 4 个真实 WPF 检查共 730/730。
- 新增测试使用离屏 WPF 宿主和假客户端，不启动 Runtime 或写用户文件。
  覆盖 12 次关闭/打开保留同一草稿；隐藏/最小化停止计时器；只读宿主操作；
  重新打开不延长期限；手机终态/冲突核对；Keep 等待手机确认；有限视口、
  按钮/标签边界、正确比例、布局稳定和静止预览复用。
- 隔离渲染：800×560、1100×800、1920×1032 DIP，各 100/125/150/200%。
  所有操作按钮完整、至少 42 DIP 高，预览及标签均在窗口内。
- 实机桌面缩放 200%（当前 Receiver GetDpiForWindow=192），已检查首次默认
  最大化、还原 1100×800、缩小至 800×560、关闭和再次打开。其他 DPI 仅为
  隔离渲染验证，未更改系统缩放。
- 同屏 1920×1032 截图中的预览背景：更新前约 311×693，更新后约 411×916，
  宽高各增加约 32%，面积约增加 75%。数据来自 JPEG 截图容差像素测量，
  不包含外框，存在少量边缘像素误差。还原窗口约 301×670，最小窗口约 173×384。
- 实际只编辑草稿 B X=1045→1044，关闭再打开仍保留草稿；正式文件哈希不变。
- 实际最小窗口点击试用，手机确认 Trial 后显示递减倒计时，Keep/Restore 启用。
  关闭窗口超过原期限后重新打开，查询得到 Restored/expired，X 恢复 1045，
  无未应用草稿。随后又实际完成一次 Try→立即恢复。
- 未实机点击 Keep 写盘；新窗口 Keep 发送/等待确认/重新打开核对由新增
  WPF 假客户端回归覆盖，原真实 UDP 和手机事务历史回归证据继续保留。
  不把截图当成完整保存业务或真人手感验收。

## 部署与数据

仅替换五个 Receiver 托管产物。两次更新均在现有 UI Stop 后使用原 launcher，
更新前准备本次原版本备份和隐藏临时恢复保护。首次实机发现新 Window 未显式
继承深色样式，已修正并重新构建、完整回归和更新；未遗留白底候选。
Android 未修改或重装，手机正式文件没有写入，settings 与 native bridge 未改。

最终进程 PID 23688。编辑器开关期间 Runtime RunId 和 Xbox 设备身份一致，
Running/Connected 保持正常。最终版本 Android→Windows 原生移动/点击/双击拖动
检查已通过，详细计数见 actual-input-final-build.log 和 actual-input-after-lifecycle.log。
结束时无活动试用、无待应用草稿，原布局字节保持不变。

本次仅涉及显示宿主及必要接点、窗口回归和相关文档；没有改 RemoteLayoutClient、
RemoteLayoutDraft、UDP、Android、Motion 或设置实现。已有未提交内容保留；
未参与的 21 份 tracked diff 与开工备份一致。未执行 Git commit 或 push。

证据在 ignored `windows/test-results/remote-layout-window/`：完整前/后截图、
最小窗口试用截图、构建/回归/渲染日志、trial-before-close.txt、expired-reopened.txt、
actual-preview-sizes.txt、device-lifecycle.json、protection-final.json、runtime-final.json
及 recovery/。最终可用性和文件校验以这些证据为准。
