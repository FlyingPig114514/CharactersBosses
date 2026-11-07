# 两项待实现需求（用户 2026-11-04，原始口径）

> 状态：**第 1 条已实现并实机验收通过（v0.2.384）**，第 2 条尚未动手。
> 写这份文件是因为当时会话上下文已用尽，来不及动手。
> 下一个会话请把这两条当成独立任务，各自先读反编译再写代码（本项目规矩：结论必须有反编译/日志依据）。

---

## 1. Boss 被「击晕」时的整套行为 —— ✅ 已完成（v0.2.383 实现 / v0.2.384 移除自检钩子）

> **实现与验收记录全在 [`stun-behavior.md`](stun-behavior.md)**（含反编译结论、为什么只需一道
> `PrepareTurn` 闸门、以及 v0.2.383 那次实机 `mod_log.txt` 原文）。
> 一句话总结：vanilla 的 `STUNNED` 空 MoveState 天然替我们跳过了 `PlayPlannedCards`
> ⇒ 计划/手牌/TurnNumber 原封不动；唯一会毁掉「补打」的是玩家回合开始时那次 `PrepareTurn`，
> 在那里加闸门即可。UI 侧按 `Creature.IsStunned` 全暗 + 撤意图。

用户原话：

> 当 boss 被吹哨等牌击晕时，他应该立刻使意图中的所有牌变暗，撤掉所有意图，并且本回合不行动。
> 在下回合，他应该尝试使用上回合没有成功完成的意图
> （不一定是上回合，因为他有可能被连续击晕好几回合，具体要看他之前执行过哪些回合的行动）

拆成四条可测判据：

1. **立刻**：一旦她获得「击晕」状态（不论来源是「吹哨」还是别的牌/能力），
   当前手牌栏里**所有牌立刻变暗**（= 显示成「本回合不会打」的样子）；
2. **撤掉所有意图**：那张牌下面挂的意图行全部消失（包括「充能球被动」「抱抱先生」这类独立意图？——待与用户确认，建议**也**撤掉，因为「本回合不行动」）；
3. **本回合不行动**：`PlayPlannedCards` 直接跳过出牌（不是跳过整段回合开始钩子）；
4. **下回合补打**：下一次能行动的回合，她尝试使用**上一次没能执行完的那份计划**。
   ⚠ 关键细节：**不能被「连续击晕」吃掉计划**。如果被晕了 N 个回合，恢复时补的应该还是
   「最后一次真正行动过之后应做的那一套」，也就是需要一个「**待执行计划**」的快照 + 游标，
   而不是简单地把 `TurnNumber` 回退一格。

### 实现前要先查清（反编译）

> **已查（2026-11-04 深夜，成果直接可用）**：
> - 「吹哨」（`WHISTLE`）反编译：先 `DamageCmd.Attack(33).FromCard(...).Targeting(target)`，
>   然后调 **`CreatureCmd.Stun(cardPlay.Target)`** —— 这就是通用的「击晕」入口；
> - `CreatureCmd.Stun(Creature, string nextMoveId)` → `Creature.StunInternal(stunMove, nextMoveId)`；
> - **检测面：`Creature.IsStunned`（public bool 属性）** —— 用它判断「她这回合被晕了」；
> - 原版**没有** `StunPower`：击晕 = 把这只怪物的**移动状态机切到一个「被晕」的 MoveState**，
>   播 `NStunnedVfx`，之后自动转到 `nextMoveId`；
> - ⚠ **下一步第一件事（待查）**：`Creature.StunInternal` / `IsStunned` 的实现 ——
>   关键是「`IsStunned` 什么时候被清掉」。原版靠**执行那个 stun move** 来清，
>   而我们的 Boss 不走原版 `PerformMove` ⇒ 很可能**永远为 true**（那她就再也不动了）。
>   要么在她被晕时让原版的移动机制真的跑一次，要么我们在跳过这一回合后自己把状态还原。
> - 还要扫一遍：除了吹哨，还有哪些牌/能力会调 `CreatureCmd.Stun`。
- 「吹哨」卡（`Whistle`?）实际施加的是什么。
- `CharBossMonster.PlayPlannedCards` 里合适的早期返回点（现在结构：回合开始钩子 → 情感芯片 → 喝药 →
  偏差认知 → `ReplanAtPlayStart` → `RefreshCompiledIntents` → 逐张 `PlayOne`）。
- 计划是怎么存的：`CharBossMonster.Plan`（`List<PlannedPlay>`，打一张 `Plan.Remove(play)`）。
  「补打上一套」很可能要做成：被晕时**不重建** `Plan`（跳过 `BossScriptRunner.BuildHand` / `ReplanAtPlayStart`），
  并把 `TurnNumber` **不前进**（或前进但记住「待补的回合号」）。
- UI：手牌栏变暗/撤意图的开关在哪（`BossHandUi` / `BossHandPanel` 里已有「本回合不会打 → 压暗」的能力，
  见 `BossHandPanel` 的 `willPay` 逻辑），以及独立意图行（`BossHandPanel.Extra` 的 `BuildExtraIntents`）。

### 验收
- 用控制台/脚本让她吃一次击晕：手牌立刻全暗、意图全没、这一回合没有任何 `PLAY` 日志；
- 下一回合：她照原计划出牌（日志里的 `剧本第 N 回合` 应该是被打断的那个 N，而不是 N+1）；
- 连续晕两回合也一样（恢复后补的还是被打断的那一套）。

---

## 2. 「回响斩击」按原版显示多段（能斩杀时）—— ✅ 已完成（v0.2.385）

> 用户 2026-11-04 追加口径：**提前计算这一手会杀死的玩家方单位（包括因为替主人承伤而死的奥斯提），
> 提前显示多段意图，并且动态改变。**
>
> 实现：`BossDamagePredictor.EchoingSlashHits`（斩杀链推演）+ `BossIntentBuilder` 里
> `entry == "ECHOING_SLASH"` 走 `LiveMultiAttackIntent`（每帧重算）。
> 反编译 `EchoingSlash.OnPlay` 其实是个**循环**：
> `int attackCount = 1; while (attackCount > 0) { …Damage(HittableEnemies, Damage)…; attackCount += 击杀数; attackCount--; }`
> ⇒ 段数 = 1 + 这一手会杀死的玩家方单位数。
> 三个必须建模的点（都写在代码注释里）：只打真人玩家、格挡先扣（打宠物时格挡取自主人）、
> 「为你而死」的改派（`DamageResult` 用改派后的目标建 ⇒ 宠物死也算击杀 ⇒ 多打一段）。
>
> **实机验证（v0.2.385，`silent@2` + 亡灵契约师）通过，但过程中被用户纠了一次**：
> 一开始只推演「此刻」的局面 ⇒ 显示 2 段；用户看屏幕指出「回响斩击**之前**那张攻击牌就把奥斯提打死了，
> 所以并没有 2 段」。⇒ 补了 `PriorPlannedAttackDamage`：按 `Plan` 顺序把**同一回合排在它前面、
> 还没打出来**的攻击牌伤害先扣到模拟状态上（`PlayPlannedCards` 打一张就 `Plan.Remove`，
> 所以已经打过的牌天然不会双算）。同一局面 A/B：改之前 2 段、改之后 1 段，与真实结算一致。

用户原话：

> 当静默猎手的回响斩击可以杀死敌人时（如击败一只奥斯提），该牌应该显示为多段。
> 和原版一样，要计算附加的一段伤害会不会杀死更多敌人，如果有的话就要显示正确的段数。

要点：
- 这张牌（Echo Slash / 回响斩击）在原版是「**如果这一击会杀死目标，就再打一次**」这类机制
  —— 具体要反编译确认（`CalculatedHits`？还是 OnPlay 里循环）。
- 我们的意图预览必须**模拟斩杀链**：
  1. 算出第 1 段的伤害；
  2. 看它对每个候选目标是否**足以致死**（比较 `target.CurrentHp` 与伤害，注意格挡、易伤、以及 Osty 这类随从）；
  3. 若致死 ⇒ 追加一段，再判断新的一段能否杀死**更多**敌人；循环到不再新增致死为止；
  4. 段数 = 实际会发生的最多次数（上限？原版可能有上限，要反编译确认）。
- 现在的问题：意图恒显示 1 段（`BossDamagePredictor.Hits` 拿不到这类信息），
  于是「能斩杀时应该 2 段」显示成 1 段。
- 落点：`BossIntentBuilder.IntentsFor` 里和「拆卸（DISMANTLE）按目标易伤决定段数」同一类处理
  （那里已经有一个先例：`entry == "DISMANTLE" && HasVulnerable(target) ? 2 : 1`），
  外加一个 helper 放在 `BossDamagePredictor`（例如 `EchoSlashHits(card, target, alreadyDealt)`）。

### 实现前要先查清（反编译）
- 回响斩击的真实类名与 `OnPlay`（是否 `AttackCmd … .RepeatWhileLethal()` 之类）；
- 原版意图里段数是怎么算的（`GetHitCount` / `CalculatedHits` / 写死？）；
- Osty/随从怎么算「杀死」（`ShouldDie` 钩子、`DieForYouPower` 会让它不死 —— 我们自己的「不死」机制会干扰这条判定！）。

### 验收
- 目标血量 ≤ 单段伤害时，意图显示 2 段；能连杀两个时显示 3 段；
- 目标有格挡/易伤时数字随之变化；
- 只有当**真的会**多打一段时才多显示（不会平白多一段）。

---

## 附：相关代码位置（2026-11-04 时的现状）

| 用途 | 文件 |
|---|---|
| 手牌栏 / 意图绘制、压暗 | `src/Hooks/UI/BossHandPanel.cs`、`BossHandPanel.Extra.cs`、`BossHandCard.cs` |
| 她的回合流程 / 出牌 | `src/Hooks/Monsters/CharBossMonster.cs`（`PlayPlannedCards` / `PlayOne`）、`CharBossMonster.Turn.cs`（`PrepareTurn`、`IntentsDirty`） |
| 计划与剧本 | `src/Hooks/Core/BossScript.cs`（`BossScriptRunner.BuildHand`）、`BossScriptPlanner.cs`（`Plan`）、`BossPlanner.cs` |
| 意图段数 / 伤害 | `src/Hooks/Core/BossIntentBuilder.cs`（`IntentsFor`、`Hits`）、`BossDamagePredictor.cs`（`Hits`、`FixedHitCounts`、`FinalDamage`、`OstyFinalDamage`） |
| 奥斯提「不死」 | `src/Hooks/Powers/BossOstyUndyingPower.cs` |
