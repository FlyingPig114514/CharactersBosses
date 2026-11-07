# Boss 被「击晕」时的整套行为（实现计划）

> 需求来源：`docs/plans/todo-2026-11-04-stun-and-echo-slash.md` 第 1 条（用户 2026-11-04 原话）。
> 本次只做这一条；「回响斩击显示斩杀段数」不在本轮范围。
> 计划结构：①反编译结论 ②决策点 ③实现 ④验收。

---

## 1. 反编译结论（都是本次新查的，`references/sts2.dll`）

| 事实 | 出处 |
|---|---|
| `Whistle`（吹哨）→ `CreatureCmd.Stun(cardPlay.Target)` | 上一场会话已查 |
| `CreatureCmd.Stun(creature, nextMoveId=null)` → `Stun(creature, _ => Task.CompletedTask, nextMoveId)` | `CreatureCmd.Stun` 反编译 |
| ⇒ **stunMove 是个空 Task**，击晕本身什么都不做；真正起作用的是「把移动状态机切到 STUNNED」 | 同上 |
| `Creature.StunInternal(stunMove, nextMoveId)`：`nextMoveId` 为空时取 `Last(MoveStateMachine.StateLog).Id`；新建 `MoveState("STUNNED", stunMove, new StunIntent()){ FollowUpStateId = nextMoveId, MustPerformOnceBeforeTransitioning = true }`；然后 `Monster.SetMoveImmediate(state)` | `Creature.StunInternal` |
| **`Creature.IsStunned => Monster?.NextMove.Id == "STUNNED"`** | `Creature.get_IsStunned` |
| ⇒ `IsStunned` 的清除条件是「`NextMove` 不再是那个 STUNNED 状态」，而 `NextMove` **只由 `MonsterModel.RollMove` 赋值** | `MonsterModel.RollMove` / `NextMove` |
| `MonsterModel.SetMoveImmediate`：仅当 `NextMove.CanTransitionAway` 才生效；生效时 `NextMove = state` + `MoveStateMachine.ForceCurrentState(state)` + `RefreshIntents()` | `MonsterModel.SetMoveImmediate` |
| `MoveState.CanTransitionAway = MustPerformOnceBeforeTransitioning ? _performedAtLeastOnce : true`；`PerformMove` 一进方法就 `_performedAtLeastOnce = true` | `MoveState` |
| ⇒ **击晕期间 `RollMove` 不会推进状态机**（`FindNextMoveState` 第一行就 return），所以 `BossThinkState.GetNextState`（= `PrepareTurn()`）**不会被调** | `MonsterMoveStateMachine.FindNextMoveState` |
| `Creature.TakeTurn()` → `Monster.PerformMove()` → `NextMove.PerformMove(targets)`（`TakeTurn` 里 `SpawnedThisTurn` 才跳过） | `Creature.TakeTurn` |
| 原版在**玩家回合开始**给每只敌人 `PrepareForNextTurn()` → `RollMove` + `RefreshIntents`；额外回合（`isExtraPlayerTurn`）跳过这一段 | `CombatManager.StartTurn` |
| 原版**没有** `StunPower`；`StunIntent` 只是图标 + 悬浮说明 | `StunIntent` |

### 我们的 Boss 在这套机器里会怎么走（关键推演）

她的状态机只有两个状态：`THINK`（`BossThinkState`，`GetNextState` 里调 `PrepareTurn()` 然后返回 `PLAY`）
和 `PLAY`（`MoveState(PlayStateId, PlayPlannedCards){ FollowUpStateId = THINK }`）。

正常一回合：

```
玩家回合 N 开始 → RollMove: PLAY→THINK(调 PrepareTurn: TurnNumber++, 造手牌, 算 Plan)→PLAY
                → NextMove = PLAY
玩家出牌（可能吹哨 → NextMove = STUNNED, FollowUp = Last(StateLog) = "THINK"）
她的回合      → TakeTurn → PerformMove(STUNNED) → 空 Task ⇒ PlayPlannedCards 根本没进
                ⇒ Plan / PlanReady / TurnNumber / 手牌 全部原封不动
玩家回合 N+1  → RollMove: STUNNED→(CanTransitionAway 已为 true)→THINK→PLAY
                ⚠ 这一步会经过 THINK.GetNextState ⇒ 如果不拦，PrepareTurn 会把 Plan 重建掉
她的回合      → PerformMove(PLAY) → PlayPlannedCards 用**保留下来的 Plan** 出牌
玩家回合 N+2  → RollMove: PLAY→THINK→PrepareTurn（TurnNumber 才 ++）
```

**结论：vanilla 那套 STUNNED 移动状态机已经自动给了我们「本回合不行动 + 计划不被清掉 +
TurnNumber 不前进」，唯一会毁掉「下回合补打」的就是玩家回合 N+1 那次 `PrepareTurn`。**

## 2. 决策点：跑原版 stun move，还是在 `PrepareTurn` 里拦？

**选：跑原版 stun move（不接管），只在 `PrepareTurn` 里加一道「被晕就别重建」的闸门。**

- 不去 patch `CreatureCmd.Stun` / `Creature.StunInternal`：
  vanilla 的行为正是我们要的（跳过她的出牌、保留 Plan），改动面越小越好；
- 不「跳过后自己还原 IsStunned」：那需要手改 `NextMove`，而 `NextMove` 由 `RollMove` 管理，
  手改会和下一次 `RollMove` 打架；
- 唯一必须新增的是 `PrepareTurn` 的闸门。它天然只在「她上一轮被晕」时成立：
  `PrepareTurn` 只在玩家回合开始的 `RollMove` 里跑（`BossThinkState.GetNextState`），
  而玩家这一回合的吹哨一定发生在 `RollMove` 之后 ⇒ 那个时刻 `Creature.IsStunned`
  为 true ⟺ 她上一回合确实被晕过。

（这一道闸门对「FollowUp 恰好是 PLAY 而不是 THINK」的首回合也是幂等无害的。）

## 3. 实现

| # | 文件 | 改动 |
|---|---|---|
| 1 | `src/Hooks/Monsters/CharBossMonster.cs` | 新增 `internal bool Stunned`（读 `Creature.IsStunned`，异常一律当 false） |
| 2 | `src/Hooks/Monsters/CharBossMonster.Turn.cs` | `PrepareTurn()` 开头：`if (Stunned) { 日志; return; }` —— **在 `Plan.Clear()` 之前** |
| 3 | `src/Hooks/UI/BossHandPanel.cs` | 被晕时：`IsBright()` 一律 false；`BuildCell` 传 `planned = null`（⇒ 卡面压暗 + 不建意图行）；增量分支里同样把 `planned` 置空、`Bright=false` |
| 4 | `src/Hooks/UI/BossHandPanel.Extra.cs` | `BuildExtraIntents` 在清完旧节点后直接 return（冰雹/炸弹/球被动/抱抱先生全撤） |
| 5 | `src/Hooks/UI/BossPotionRow.cs` | 被晕时不画「本回合要喝」的药水意图箭头（图标本身照旧显示） |
| 6 | `src/Hooks/Patches/BossIntendsToAttackPatch.cs` | 被晕时 `IntendsToAttack = false`（她这回合真的不会攻击，别让「眼部攻击」这类牌白上虚弱） |
| 7 | `src/Hooks/Patches/BossFrameTickPatch.cs` | 每帧比对 `boss.Stunned`（`LastStunned` 字典，每只一份），翻转即 `BossHandUi.RefreshForce(boss)` + 一条日志；`ResetForNewFight` 里 `LastStunned.Clear()` |

不做的：不给 `CreatureCmd.Stun` / `StunInternal` 挂补丁（不需要）；不抬任何 `ZIndex`；
不隐藏原版那条 STUNNED 意图节点以外的任何东西（原版 `NIntent` 本来就被
`HideVanillaBossIntentPatch` 藏掉了，所以头顶不会有额外图标——压暗就是唯一提示）。

## 4. 验收（实机）

**✅ 已实机验证通过（v0.2.383，2026-11-04 22:14～22:17，日志实证）**

验证方法（很重要，下次照抄）：「吹哨」在玩家卡组里，靠抽牌凑不齐「连晕两回合」——
所以临时加了 `src/Hooks/Patches/StunSelfTest.cs`：mod 目录放 `charbosses_stun.txt`（内容=回合号），
她打到那几个回合时在**玩家回合内、回合开始 1.2 秒后**调 `CreatureCmd.Stun(Creature)`（与吹哨同一入口）。
配合 `charbosses_debug.txt`（第一场普通战斗变成铁甲战士 Boss）就能一分钟复现。
**验完已删除该自检文件、两处开关文件，并重 build 成 v0.2.384。**

另外实测发现玩家卡组里本来就带「吹哨」（`Whistle`），自动打牌时它自己也在反复击晕她 ——
所以下面这份日志里既有自检击晕、也有真牌击晕，两条路径表现完全一致。

`mod_log.txt` 原文（节选，`第 N 回合` 就是她剧本的回合号）：

```
22:14:45 剧本第 2 回合: 打 [BREAKTHROUGH,TRUE_GRIT] 跳过 [STRIKE_IRONCLAD(被烧掉)] 余费=0
22:14:46 击晕：本回合不行动 → 手牌全部压暗、撤掉全部意图（待执行计划仍保留 2 张，TurnNumber=2）
          ← ★ 这里到她下一个回合之间**没有任何**「敌方行动入口：PlayPlannedCards」
22:14:59 击晕：跳过本回合的规划重建（待执行计划保留 2 张，TurnNumber 停在 2，PlanReady=True）
22:14:59 击晕解除：恢复意图显示，补打待执行计划 2 张（仍是第 2 回合的那一套）
22:15:26 击晕：本回合不行动 → …（第二次击晕，连晕）
22:15:28 击晕：跳过本回合的规划重建（… TurnNumber 停在 2 …）
22:15:28 击晕解除：…
22:15:33 敌方行动入口：PlayPlannedCards（第 2 回合，Plan=2 张）      ← ★ 补打的是第 2 回合那一套
22:15:36 剧本第 3 回合: 打 [AGGRESSION,BRAND] 跳过 [STRIKE_IRONCLAD(被烧掉)] 余费=1   ← ★ 接着 N+1
```

`godot.log` 里对应的原版行：`Monster IRONCLAD_BOSS performing move STUNNED`（被晕的回合），
之后 `performing move CHARBOSS_PLAY`（补打）。

四条判据逐条对账：

| 判据 | 结果 |
|---|---|
| 立刻压暗所有牌 | ✅ 击晕当帧 `RefreshIfStunnedChanged` → `RefreshForce`；`BuildCell` 拿 `planned=null` ⇒ 卡面 `Modulate=(0.42,0.42,0.48)`、不建意图行 |
| 撤掉所有意图 | ✅ 同上；独立意图（球被动/冰雹/炸弹/抱抱先生）与药水箭头一并撤（`BuildExtraIntents` 直接 return、`BossPotionRow` 不画箭头、`BossStatusRow` 指纹含击晕位） |
| 本回合不行动 | ✅ 被晕的那一回合**零** `PlayPlannedCards` 日志（`godot.log` 是 `performing move STUNNED`，空 Task） |
| 下回合补打 | ✅ 恢复后 `PlayPlannedCards（第 2 回合，Plan=2 张）` —— 回合号没前进、计划张数一致；连晕两回合（22:14:46 + 22:15:26）也一样 |
| 面板无异常 | ✅ 全程 `可见=visible=True`、`面板重建 0.1～1.1ms`、`异常=0`，无 `NIntent failed` / 无 Exception |

⚠ 顺带记一笔（**与本次改动无关**）：这台机器上游戏窗口是 1707×1067，而 UI 坐标空间按 1920 宽排，
Boss 立绘/手牌栏贴着右缘 ⇒ 截图里手牌栏最右那张卡被屏幕边裁掉一点。原版布局问题，没动它。

## 5. 已知边界（都不是 bug，写清楚免得下次当 bug 修）

- **她自己的回合里被晕**（例如别人在她出牌过程中反击/遗物击晕她）：那一手已经跑起来了，
  `Plan` 的牌也已经被 `BossPiles.DiscardHand` 丢掉 ⇒ 下一个回合
  `PlayPlannedCards` 会发现「计划里的牌不在手牌里」而逐张 `Plan.Remove`，于是那回合她什么都不做、
  下一回合正常重建。**不会卡死**，但也不是「补打」。这个场景原版基本不会出现（击晕都来自玩家出牌）。
- **读档续战**时 `PlanReady == false` ⇒ 恢复后第一次 `PlayPlannedCards` 会 `PrepareTurn()`，
  于是补的是**当前 TurnNumber 再 +1** 的那一套（等于把这回合当成新回合）。同样不会卡死。
- 被晕的那一回合她**整段回合流程都不跑**：不喝药、不结算情感芯片、不触发回合结束的遗物/诅咒/惊逃/
  虚无消耗，也不丢手牌。这是「本回合不行动」的应有之义（用户口径），不是漏了什么。
