# Mode 单键绑定

当前契约：2026-10-17。实现、测试与受控部署阶段已经完成；该阶段未执行 Git commit
或 push。后续源码审查与 Git 交付沿用本契约及交付报告中的验证边界。

## 使用方式

在 Receiver 的 **Controls → Mode · 单键绑定** 中点击“选择键盘键”或“选择手柄键”，
通过分类大按钮选择一个键，再选择“普通按钮”或“切换按住”。点击选择器中的
“完成选择 · 返回统一 Save”，最后点击主窗口底部的 **Save**。

选择、行为切换只改变草稿。“已保存”摘要与运行状态来自正式配置及实际运行状态，
不随草稿提前变化。选择器没有独立保存入口，不需要实体键盘、键名输入、右键或滚轮。
选中的键以边框及粗体区分，行为按钮也有选中边框。

复用 Android 原 stable ID `rightpad.input.mode`，保留其正式矩形、位置、大小及布局属性。
手机仍保留既有外观和静态 M 标签；该标签不是当前键盘绑定或 M/C 选择状态。
绑定与行为的摘要显示在 Receiver。Motion 页的 Receiver-local M/C 选择保持独立。

旧文件没有 `modeControl` 时，默认 **未绑定 / 普通按钮**。交付时也恢复并保存此状态，
测试绑定不作为用户的默认游戏动作。

## 两种行为

| 事件 | 普通按钮 | 切换按住 |
|---|---|---|
| 新的有效 DOWN | 按下所绑定的键 | 在按下与释放之间切换一次 |
| MOVE、静止接触、同一接触重复消息 | 保持本次状态 | 不再次切换 |
| 正常 UP | 释放；极短按压完成固定的最小可见时间后释放 | 保持切换后的状态 |
| 已结束接触的重复 UP / MOVE | 消耗，不转交鼠标或其他控件 | 消耗，不清除已保持状态 |
| CANCEL、生命周期或连接安全中断 | 立即请求释放并清除接触 | 立即请求释放并清除保持 |

DOWN 一次确定接触归属，移出矩形也不转交。Mode 不调用 Touch 采集、鼠标单击或
双击拖动路径，也不执行 B/X 的本地手势或通用触觉反馈。

普通按钮在成功按下提交完成后，使用固定 25 ms 最小可见时间；正常 UP 若早于该边界，
由 Receiver 的维护检查完成释放。安全释放绕过该时间。这里没有长按识别等待，
也没有按网络状况延长保持。维护检查约每 10 ms 及收包时执行，允许调度造成的正向超时，
不承诺恰好 25.000 ms。前一次短按仍待释放时，下一次 DOWN 暂不确认，由原有重传重试。

键盘提交真实 HID 按下/释放，不运行连点、宏或组合键定时器。Windows 仍可能对保持中的
键产生常规键盘重复；这与应用自行生成多个按压周期不同。Caps/Num/Scroll Lock、
Print Screen、Pause、Windows、Application 等键遵守 Windows 的特殊语义，不能将
“提交按下状态”理解为它们都具有普通字母键相同的应用层效果。

## 完整可选清单

键盘目录共 **117 项**：常规 104 键范围，加 F13–F24 和 ISO 额外符号键。

| 分类 | 可选键 |
|---|---|
| 字母（26） | A–Z |
| 数字与符号（22） | 主键区 0–9；`- _`、`= +`、`[ {`、`] }`、反斜杠/竖线、`; :`、单/双引号、反引号/波浪号、`, <`、`. >`、`/ ?`；ISO 额外反斜杠/竖线键 |
| 常用与锁定（11） | Space、主键区 Enter、Tab、Esc、Backspace、Caps Lock、Num Lock、Scroll Lock、Print Screen、Pause、Application |
| 左右修饰键（8） | 左/右 Shift、左/右 Ctrl、左/右 Alt、左/右 Windows |
| 功能键（24） | F1–F24 |
| 方向与导航（10） | Left、Up、Right、Down、Insert、Delete、Home、End、Page Up、Page Down |
| 数字小键盘（16） | 数字小键盘 0–9、乘、加、减、小数点、除、数字小键盘 Enter |

符号标签表达物理键位置及其常见美式键帽，不表示一次绑定可以输出两个字符或自动加
Shift。实际字符由当前 Windows 键盘布局、修饰键和锁定状态决定。左右修饰键使用独立
VK；数字小键盘 Enter 使用 `VK_RETURN + scan 0xE01C`，与主键区 Enter 分开。
其他数字小键盘键和导航键使用各自明确的 HID 映射，不按显示文字推断编码。

手柄共 **16 项**，复用原有单个 Xbox360 输出设备：

| 可选键 | bridge 逻辑状态 | XInput 状态 |
|---|---|---|
| A / B / X / Y | bits 0 / 1 / 2 / 3 | 0x1000 / 0x2000 / 0x4000 / 0x8000 |
| View / Back；Menu / Start | bits 4 / 5 | 0x0020 / 0x0010 |
| 左/右摇杆按下 | bits 7 / 8 | 0x0040 / 0x0080 |
| LB / RB | bits 9 / 10 | 0x0100 / 0x0200 |
| 方向上/下/左/右 | bits 11 / 12 / 13 / 14 | 0x0001 / 0x0002 / 0x0004 / 0x0008 |
| LT / RT | 对应 trigger 字节 | 按下 255，释放 0 |

本轮不提供 Fn、媒体/消费控制键、厂商专用硬件键或鼠标键。键盘 bridge 白名单仅开放
已经核对固定版本 WindowsHidKeyboard 键盘页映射的键，不以库的 Win32 fallback 冒充 HID。
Guide 的逻辑位原来就存在于 bridge，但不属于标准公开 XINPUT_GAMEPAD 的已定义按钮
清单，本轮没有完成其 Windows/应用行为验证，因此明确不提供 Guide 选项；不声称
底层完全不能编码它。参见 [Microsoft XINPUT_GAMEPAD 定义](https://learn.microsoft.com/en-us/windows/win32/api/xinput/ns-xinput-xinput_gamepad)。

## 配置、归属与保存

正式配置位于 Receiver 现有 `settings.json` 的顶层字段：

```json
"modeControl": {
  "kind": 0,
  "key": "",
  "behavior": 0
}
```

`kind` 为 0=未绑定、1=键盘、2=手柄；`behavior` 为 0=普通按钮、1=切换按住。
`key` 是目录中的稳定标识，不是让用户输入的 VK/扫描码。示例：
`{"kind":1,"key":"RightCtrl","behavior":1}`。读取时验证整个绑定；非法记录整体回到
未绑定/普通按钮，保留其他设置。显式 Save 沿用现有递归合并，保留未知属性。

SettingsViewModel 只编辑不可变草稿；成功原子替换磁盘文件后才调用
RuntimeSettingsStore.Publish。失败保留原正式配置和草稿，显示未应用的失败信息。
新旧 Mode 配置相等时不做输出操作；无关设置 Save 不清除保持、不重复按下，也不推进
Mode 接触流。有效绑定或行为变化则先释放旧输出，清除旧保持，替换配置并切换接触代次。
正在进行中的接触不会按新绑定重新解释；新的同步完成后，等待下一次明确 DOWN。

键盘使用 Mode 独有的 HID 键盘设备和当前按键记录，只发送自身按下与释放。
原生失败时尝试释放并关闭该设备，不释放其他键盘设备或其他来源的全部按键。
手柄在 ReceiverRuntime 内保留原 Screen Controls 与 Mode 两份独立贡献：按钮 OR，
LT/RT 取最大值，原有摇杆值由 Screen Controls 保留。撤销 Mode 只撤销其贡献；原 B/X
的序号、最小 dwell 和 300 ms lease 继续由 GamepadSessionProcessor 管理。

Receiver 显示“未连接”“等待同步 / 新接触”“已释放”“保持中（已提交）”或真实输出错误。
Held 只在后端成功提交后改变；“已提交”不宣称某个游戏已处理输入。键盘按需创建，
未绑定时不创建键盘；键盘失败不停止鼠标。Mode 所用手柄后端失效时也清除 Mode 保持。

## 生命周期与恢复

暂停/退出前台、目标或 Sender run 变化、编辑/输入模态、布局试用与恢复、取消/指针
丢失、Mode 租约失效、Receiver presence/run 变化、Stop、Safe Restart、Dispose 和
Mode 输出错误均执行释放和失效处理。普通鼠标或 B/X 的下一次正常 DOWN 本身不撤销
已经正常 UP 的 Mode 切换保持；进入设置/编辑等安全中断仍撤销。

保持不写盘，重启或重新同步后从释放状态开始。Mode 接触流的固定重传、确认与失效
代次见 [MODE_CONTACT_PROTOCOL.md](MODE_CONTACT_PROTOCOL.md)。该机制用于可靠性与
生命周期，不是认证、配对或安全握手。

原生扩展保持 mouse/gamepad ABI2，增量增加 `rightpad_vhid_keyboard_api_version=1` 及
create/set/destroy。依赖仍是固定 libvirtualhid revision
`53e1a949fc0784af716b782ddfa6c647cafd1f05`，没有升级驱动、broker、许可组件或供应商源码。
create 检查本地许可和实际驱动设备节点；目录白名单在每次 set 前再次校验，拒绝未知键
和未授权扫描码，阻止库内不支持键的 SendInput fallback。DLL 缺失、扩展不匹配、
许可/创建/输出错误显示为 Mode 输出失败，不转换成其他后端。

## 验证范围

本轮的构建、故障/去重测试、真实 Raw Input / XInput 和鼠标恢复结果，以及部署与恢复
路径见 [MODE_CONTROL_DELIVERY_20261017.md](MODE_CONTROL_DELIVERY_20261017.md)。自动化
MotionEvent 注入不等于真人盲操作、游戏手感或所有游戏/反作弊兼容性验收。
