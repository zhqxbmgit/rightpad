# Receiver 手机布局预览显示调整 — 2026-10-15

本次仅调整 Receiver 的 `RemoteLayoutView.cs` 与 `ControlsView.xaml` 显示。
Android、协议、草稿/试用/保留/恢复状态机、布局持久化、Motion 和输入输出实现
均未修改。未执行 Git commit 或 push，未重装 Android。

鼠标预览底色为不透明 `#000000`，图例为“黑色：鼠标区域”。保留绿色按键、
选中白框和橙色固定工具。手机外缘增加 1 DIP 灰框，灰框在 Canvas 外，
手机坐标仍统一按同一个 scale 映射到 Canvas。

移除原 180 DIP 预览列、174×290 DIP 缩放上限。电脑页面采用左侧预览、右侧
284 DIP 参数列，预览按可见宽高与实际 View 比例完整缩放。外层 ScrollViewer
仍承载下方原行为参数，但预览页高度取它的有限 ActualHeight，扣除页面和卡片
内边距，不取内容 extent、无限 Measure 高度或预览自身 DesiredSize。
标题/状态并排，操作行保持在预览上方，1/10/50 px 步长集中为一行。
B/X/Mode 标签为 14 DIP、单行，不能放入真实矩形时使用外侧引线标注，
不修改手机矩形大小。相邻外侧文字按侧分组错开。

## 本次实际验证

- Release 构建：0 warnings / 0 errors；现有 Windows 完整回归：726/726。
  包括 RPLY 草稿隔离、期限/重试/旧消息拒绝、真实 UDP 复用和 Stop 生命周期。
  没有新增复述颜色常量的产品测试。
- 隔离 WPF 渲染检查：860×600、1100×720、1177×786、1920×1050 DIP，
  各以 100% / 125% / 150% / 200% 渲染；19 个操作按钮均完整位于视口内，
  预览比例一致。对应测试视口的预览约 112×250、166×370、196×436、315×700 DIP。
- 当前真实桌面为 200%（GetDpiForWindow=192），普通窗口、最小 860×600 DIP
  和最大化均已实机检查。原预览高度始终为 290 DIP；新普通窗口约 370 DIP，
  最大化截图中约 690 DIP，约为原高度的 2.4 倍。最小窗口主动缩小预览，
  仍保留全部布局操作。未切换系统 DPI；其他 DPI 结果仅为隔离渲染检查。
- 最小窗口实际点击 B X 减 1：只变更草稿。点击试用后手机确认 Trial，
  倒计时从 60 秒递减，“保留此布局”“立即恢复原布局”均可见且启用。
  实际点击恢复后手机确认 Restored，B X 从 1044 恢复 1045，草稿清空。
  本次未点击保留写盘，也未重新等待完整 60 秒到期；既有期限/保存回归通过。
- Android→Sender→UDP→Receiver→libvirtualhid→Windows 的既有惰性窗口检查通过：
  10 px 小移动、单击、双击拖动，465 次原生移动，LEFT DOWN/UP 各 3 次。
  这是设备注入功能验证，不代表真人手感验收。

## 更新、数据保护和最终状态

更新前备份原 Receiver 产物、当前 settings.json 和手机当前布局；准备独立隐藏的
临时恢复进程。先点击现有 UI Stop，确认 Runtime Stopped，再用项目既有 launcher
停止宿主、替换五个托管产物并启动。未替换 native bridge、驱动或 Android APK。
新进程恢复 Running/Connected 后，临时保护进程自动退出；无常驻新增进程。

最终 Receiver PID 29088，M-R1，libvirtualhid Mouse / Xbox 正常，输出失败均为 0，
无活动 Touch，LastError 空。已恢复用户原布局，无未完成试用或待应用草稿。
settings.json、手机布局及 native bridge 均与本次更新前备份逐字节一致。
除本次 Controls 显示文件外，开工时已有的 23 份 tracked diff 均原样保留。

证据在 ignored `windows/test-results/remote-layout-display/`：构建/回归日志、
`layout-check.log`、原/新界面截图、`trial.txt`、`restored.txt`、`actual-input.log`、
`preserved-changes.json`、`runtime-final.json` 和 `recovery/`。
