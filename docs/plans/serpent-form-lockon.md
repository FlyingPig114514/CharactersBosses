# 群蛇形态 · 多人跟踪锁定（v0.2.884~887 收口记录）

> 给下一个会话/接手者。这条 bug 连续踩了三个坑，全部有日志或反编译证据。

## 设计意图（用户 2026-10-26）
群蛇形态（`SerpentFormPower`）每打出一张牌就「随机对一名敌人造成 N 点无来源伤害」。
原版是 `rng.NextItem(CombatState.HittableEnemies)` —— 多人时等于随机挑玩家，无视跟踪锁定。
要求：**只打被她锁定的那位玩家**。

## 机制（反编译 + 我们自己的补丁）
1. `SerpentFormPower` 的 `AfterCardPlayed` 状态机在挑目标前先
   `await Cmd.CustomScaledWait(0.1f, 0.2f)`，然后 `rng.NextItem(HittableEnemies)`，
   拿到非 null 才 `CreatureCmd.Damage(...)` ⇒ **拿不到目标 = 一点伤害都不掉，且日志无痕**。
2. 我们用 `HittableEnemiesSidePatch.RequestNarrowOnce(locked)` 做「一次性收窄」，
   在 `CombatState.get_HittableEnemies` 里**优先级最高**（在一切豁免判断之前）：
   `↳ if (ConsumeNarrowOnce(out var once) && once.IsAlive) { __result = new List<Creature> { once }; return false; }`
   ⇒ 只要收窄活着，原版就一定会打到它。

## 三个坑（都修了）
| 版本 | 坑 | 证据 | 修法 |
|---|---|---|---|
| v0.2.879 | 自作聪明加了「先问 `IsHittable`、不可命中就跳过」 | 日志 `[16:21:16] 群蛇形态：锁定的玩家与其它玩家都不可命中 ⇒ 本次跳过` ⇒ 完全不掉血 | v0.2.884 **删掉预判**，无条件收窄 |
| v0.2.885 | 一次性收窄有效期只有 **2 帧（33ms）**，而状态机挑目标前要等 0.1~0.2 秒 | 反编译那段 `CustomScaledWait` | 窗口放到 8 帧 |
| v0.2.887 | 状态机**每次 MoveNext 都进前缀**（含伤害打完后的收尾帧），窗口放宽后收尾帧可能串到别人的随机目标 | 代码结构 | ①反射读 `amountsForPlayedCards`，**只有还有待结算伤害才收窄** ②窗口放到 **20 帧（≈0.33s@60fps）** |

## 怎么验证（不必开联机）
`ConsumeNarrowOnce` 成功且「她正在出手」时会打一行（限流 10 行）：

```
[CharBoss] 选靶收窄被消费：<谁>（她要打的就是这一位）
```

- 单人打一场**炼药猎**（唯一会打群蛇形态的设计），让她开出群蛇形态：
  - 有这行 ⇒ 收窄 → 原版读走 → 伤害落到锁定者，链路通；
  - 只有 `群蛇形态：收窄到锁定玩家 X` 而没有被消费 ⇒ 收窄仍被丢掉，下一步改为
    **绕过 `HittableEnemies`，直接对锁定者 `CreatureCmd.Damage`**（注意两端确定性：目标取自
    `BossLockOn.TargetFor`，两端一致）。
## 最终方案（v0.2.888，收官）
前三版都在修「收窄为什么没生效」。但**只要收窄以任何理由没被原版读走**，`NextItem` 拿到 null
就是静默零伤害 —— 这个设计不可靠。现在改成**自己结算**：

```csharp
// BeforeSegment（SerpentFormPower.AfterCardPlayed 状态机前缀）
var dict = PendingDamageDict(power);            // 反射拿 amountsForPlayedCards
取走并 Remove 全部待结算条目 → total       // 原版那句 Remove 因此返回 false，不会重复打一次
if (total > 0) CreatureCmd.Damage(ctx, BossLockOn.TargetFor(boss) 的玩家, total, Unpowered, power.Owner);
```

- 日志：`群蛇形态：自行结算 N 点无来源伤害 → <谁>（不再依赖 HittableEnemies 随机挑目标）`
- 兜底：反射失败会打 `群蛇形态自行结算失败（退回原版随机）`，行为回到旧路径（收窄 + 原版随机）。
- 两端确定性：目标取自 `BossLockOn.TargetFor`、伤害取自同一张待结算表、两端跑同一份代码。
- `RequestNarrowOnce(locked)` 保留（此时原版已经没有待结算条目，不会双份），作为极端情况下的第二道保险。
