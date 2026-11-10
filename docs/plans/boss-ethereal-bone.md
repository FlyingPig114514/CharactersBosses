# 二层亡灵契约师 2 号「虚无骨」（`NECROBINDER_ETHEREAL`）

> 用户设计（2026-11-09 口述，逐字原话保存在 `src/Hooks/Core/EtherealBoneScript.cs` 的类注释里）。
> 代码是唯一权威：本文只记「为什么这么写」和「踩过的坑」，方便下一轮改意图数值时不要再重新推一遍。

## 1. 名字口径

- **「虚无」= Ethereal**（卡牌关键字 `CardKeyword.Ethereal`）⇒ 内部 Key 用 `NECROBINDER_ETHEREAL`。
- **`void`（虚空）只属于特殊效果「虚空之唤」**（`BossVoidCallPower` / `BOSS_VOID_CALL_POWER.*` /
  `NECROBINDER_BOSS.void_call`）。**Boss 名字和特殊效果是独立的**，不要混用（用户 2026-11-09 纠正过）。

## 2. 实现落点

| 东西 | 文件 |
| --- | --- |
| 剧本 / 卡组 / 遗物 | `src/Hooks/Core/EtherealBoneScript.cs` |
| Boss 定义与变体挂载 | `src/Hooks/Core/BossDefs.cs`（`Necrobinder.VariantsByAct` → `NECROBINDER_ETHEREAL`） |
| 出阵 / 名单 / 名字 | `src/Hooks/Core/BossKeys.cs`、`BossLineups.EtherealActive()` |
| 特殊效果「虚空之唤」 | `src/Hooks/Powers/BossVoidCallPower.cs`（首次女妖之嚎 ⇒ 全体玩家挂恶咒；她离场/死亡时清掉） |
| 配置界面能力面板 | `src/Hooks/Core/BossInfoTable.cs` |
| 遭遇 / 怪物 | `Encounters/TierBossEncounters.cs`（`NecrobinderEtherealBossEncounter`）、`Monsters/TierBossMonsters.Act2.cs` |

## 3. 意图数值的两个坑（v0.2.538 收口）

用户实测：**第 5 回合「亡魂牵引」预览显示 7×3，实际打出 6×4**。两个独立问题。

### 3.1 段数少一段：「致死性」本身就是虚无牌

反编译 `PullFromBelow.CalculatedHits` = 战斗历史里
`e.CardPlay.Player == card.Owner && e.WasEthereal` 的条目数。

第 5 回合剧本是 `致死性 → 亡魂牵引 → 防御`，而「致死性」（`Lethality`）**自带
`CardKeyword.Ethereal`**（反编译确认：`CanonicalKeywords => Ethereal`）⇒ 它会给亡魂牵引加一段。

但意图是在**玩家回合**里算的，那一刻她本回合一张牌都还没打，历史里没有致死性 ⇒ 永远少一段。

两条来源，取**较大值**（不能相加，同一份会被两边都数到）：

- **规划期**：`BossScriptPlanner.Plan` 里 `boss.Plan` 还是空的（先算 intents、最后才写 Plan），
  所以由规划器按剧本顺序累加成 `etherealPlanned`，作为 `IntentsFor(..., etherealBefore:)` 传进来；
- **出牌期每帧重算**（`BladesGrowth.RecomputeAll` / `RefreshPlannedIntents`）：没有调用方上下文，
  由 `BossIntentBuilder.PlannedEtherealBefore(boss, card)` 按 `boss.Plan` 里它**前面**的虚无牌数。
  打完的牌会被 `CharBossMonster.PlayOne` 从 `Plan` 摘掉，而历史里已经记上它 ⇒ 两个来源在时间上互斥。

### 3.2 每段伤害多 1 点：致死性的倍率必须排在力量**之后**

真实结算 `(5 + 力量 −1) × 1.5 = 6`（力量是加法、致死性是乘法，日志也印证：
`牌面 5 → 6（力量=-1）`）。旧写法是「先把牌面 ×1.5 再走管道减力量」：
`5 × 1.5 = 7.5 → 8`，`8 − 1 = 7` ⇒ 预览 7、实际 6。

现在：致死性的 +N% 不再改 `damage`，而是记成 `pendingLethalityPct`，到建意图时用
`ApplyPct(FinalDamage(...), pct)` —— **先跑完整条原版伤害管道，再乘倍率，向下取整**
（一律 floor，`Math.Round` 会在 `.5` 处多给 1 点）。

- 只对「**她身上还没有** `LethalityPower`」的情况补（= 本回合计划里排在她前面还没打出去的），
  power 真上身之后走原版 `Hook.ModifyDamage`，绝不双份。
- **奥斯提**那几张走 `Raw` 意图、全程不过伤害管道，仍然当场乘（那里没有「之后」可言）。

## 4. 已收口的其它项（同一批）

- 「女妖之嚎」不再多画一条「强化」意图（`BossIntentBuilder.NoBuffIntentCards` 点名 `BANSHEES_CRY`）
  ⇒ 第 4 回合 = 攻势 + 策略，第 7 回合 = 只有攻势。
- 两张女妖之嚎的费用**写死**（第 4 回合 3 费 / 第 7 回合 0 费，`ScriptCard.CostThisTurn`）——
  它的卡面费是靠「本场打出过的虚无牌数」减的，而她的牌是每回合重建、由 `AutoPlay` 打出的，
  历史计数口径和玩家不一样，不能信。
- 恶咒（`HEX_POWER.description`）的描述文案只在**虚无骨这一战**里换成「亡灵契约师存活时……」，
  否则会波及幽灵骑士（`VoidHexTextPatch` 判定 `AnyAliveVoidBoss()`）。
