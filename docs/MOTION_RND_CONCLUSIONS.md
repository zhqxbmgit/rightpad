# Motion R&D 结论

更新：2026-10-01。本文是唯一保留的 Motion R&D 历史总结。
结论包含此前离线结果及用户真人游戏反馈；不把模型正确性或自动 smoke
等同于游戏镜头质量收益。已按明确授权的清单永久删除全部 43 个审计目标，
包括实验实现、专用 tests/fixtures、独立研究文档、harness 和原始数据目录。
未扩大删除清单。本文是唯一保留的 Motion R&D 结论文档。
H1、正式 production regression 与产品协议继续保留。

## 当前产品 baseline

Raw Touch → live committed Sensitivity → 12 ms reconstruction →
product Finite-Critical → Q0-C → libvirtualhid。

- Motion/output opportunity 为固定 1000 Hz，生命周期为 Earned-Settle。
- Tau/Support 使用每次 Receiver run 启动时的 current Saved snapshot；本次实际
  Active 值为 18/90 ms。18/90 是当前配置观察值，不是 hardcoded 产品常量。
- M 是 production；C 保留为未来实验槽，但目前仅是另一标签，行为完全等同于 M。
- 相同 raw input、timing、settings 下，两者 continuous position、logical output、
  native submit sequence/timing、settle、missed ticks 和 endpoint 均一致。

## 各研究分支

| 分支 | 已得到的结论 | 决定 |
|---|---|---|
| DLS | synthetic lateral wobble 明显衰减；真人几乎无感 | 不继续 |
| Stronger Finite-Critical / C4 | 离线更平滑；真人主要感觉更糊、更慢 | 不继续 |
| Minimum-Jerk | 无整体 camera-quality 收益 | 不进入真人候选 |
| Fixed-Lag B-spline | tracking/reversal 更快；acceleration/jerk 明显恶化 | 不继续 |
| zhq dynamics | spring 同属 critically-damped 家族；acceleration/velocity shaping 可改变动态；symmetric acceleration cap 可造成 braking overshoot；glide 会产生 artificial displacement | 不采用 |
| C7 brake-safe servo | nominal 1 ms 正确；large actual-dt 会产生人工往返路径 | 不采用 |
| ETAP | correctness 优秀；真人主要感觉更快、更灵敏，没有明显更高级镜头感 | 不继续 |
| JETAP / S-curve | finite jerk 成立，endpoint/位移安全；真人肉眼难以和 M 区分。这不表示完整系统在方向变化与 terminal clamp 处都具有连续 jerk | 不继续调 A/J |
| Native cadence isolation | M/C Motion 完全相同，仅比较 1000 Hz 与 120 Hz batched；真人基本无区别 | 此轮证据表明 sub-frame/native cadence 不是主要体验变量；移除 batching |
| Camera measurement | frame-bucket 层 M/C 差异仍存在；HID replay 最终完整可靠；WGC capture identity 可定义；camera image tracking 即使提高到约 120 Hz 仍未达到资格 | CLOSED / INCONCLUSIVE；不继续投入 camera-image tracking 工具链 |

Native cadence 的结论限于本次设备、配置和真人比较，不建立所有系统或游戏的普遍定律。
Camera branch 的 INCONCLUSIVE 不证明 M/C 游戏画面没有差异；它表示 image-tracking
证据未达到可靠定量判断资格。

## 总体决定

暂停新的 1 ms Motion planner 研发。当前产品继续使用 Finite-Critical baseline。
波浪问题以后单独处理。保留结论，不保留实验算法、实验专用测试、实现或原始数据。

```text
M MOTION: PRODUCT FINITE-CRITICAL
C MOTION: IDENTICAL TO M
ACTIVE EXPERIMENTAL MOTION ALGORITHMS: NONE
EXPERIMENTAL DATA RETAINED: NO
EXPERIMENTAL CONCLUSION DOCUMENT RETAINED: YES

MOTION R&D: PAUSED
WOBBLE: DEFERRED
CAMERA IMAGE-TRACKING BRANCH: CLOSED / INCONCLUSIVE
```
