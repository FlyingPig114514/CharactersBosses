# 三层储君 3 号「抉择储」（`REGENT_CHOICE`）

> 用户 2026-11-10 设计。状态：**代码已落地，待实机验证**（v0.2.559）。

## 1. 用户原话（逐字，唯一权威）

```
3层3号储君boss抉择储
特殊效果：指导：每当储君打出抉择抉择时，你替他选择要被重复打出的牌
遗物：天赋君权、涅奥的祝福、准备背包、燃烧木棍、玻璃眼珠、击剑指南、王室印章、钗、锚
进阶8：升级群星之子、收集光辉。进阶9：升级淬炼刀刃、共鸣、震荡波
意图
1.群星之子、崇拜、胜券在王、抉择抉择+（附魔王室认证）：隐秘藏品或铸墙或星位序列或共鸣或震荡波（附魔灵魂之力）、天际钻头（如果选择星位序列的话就出，否则没费出）、君王之剑（没费）
2.大爆炸、辉光、收集光辉、辐射、淬炼刀刃、抉择抉择+（附魔王室认证，不出）
3.招架、君王之剑、星尘、防御（没费）、抉择抉择+（附魔王室认证，不出）
4.隐秘藏品、汇流+、防御（没费）、抉择抉择+（附魔王室认证，不出）
5.洗牌。抉择抉择+（附魔王室认证）：隐秘藏品或铸墙或星位序列或共鸣或震荡波（附魔灵魂之力）、天际钻头（如果选择星位序列的话就出，否则没费出）、君王之剑（没费）、防御（没费）
6.辉光、收集光辉、辐射、淬炼刀刃
7.君王之剑、汇流+、崇拜（没费）
8.崇拜、隐秘藏品、星尘、防御
9.洗牌。汇流+、隐秘藏品、隐秘藏品（没费）
10.辐射、铸墙、淬炼刀刃、隐秘藏品（没费）
11.共鸣、君王之剑、防御（没费）
12.星位序列、辉光、震荡波、收集光辉
13.崇拜、星尘、天际钻头、防御（没费）
之后9～13循环
PS：他打出抉择抉择时要用类似知识恶魔的选择界面，不过会有5张牌。与知识恶魔相同，被他锁定的玩家选完后他才会打后面的牌（后面没打牌的boss得等储君打完再打）
拦掉原版的抽牌，但不拦燃烧木棍的印牌
藏品：天际钻头
```

后续订正（同一天，问答形式）：

- **意图**：「都摆出来，但是被选的那五张牌暗着（不显示对应意图）」。
- **遗物**：「全难度升级崇拜，把胜券在王换成王国资产，锚换灯笼」。
- **能量**：「1、2、3、5、7、8、10、11 回合 3 费，12 回合 4 费，其它 2 费」；
  「第五回合的天际钻头应该一定会被打出」；
  「如果第一回合或第五回合玩家选择了星位序列，当回合费用会增加 6（然后都被天际钻头消耗）」。
- **天际钻头**：「与原版相同，如果 X≥4 则 X 翻倍，即伤害段数要翻倍」。

## 2. 卡牌数值（反编译 `sts2_111.dll` 核对）

| 牌 | 英文类 | 费 | 星费 | 关键变量 |
|---|---|---|---|---|
| 抉择，抉择 | `DecisionsDecisions` | 0 | **6** | 抽 `Cards`（3，升级 **5**）张；选一张技能牌打出 `Repeat`（3）次；带「消耗」 |
| 崇拜 | `Venerate` | 1 | 0 | +2 星（升级 +3） |
| 王国资产 | `Royalties` | 1 | 0 | 能力牌；战斗结束 +30 金币（对本 Boss 只是「占一格」） |
| 群星之子 | `ChildOfTheStars` | 1 | 0 | 每花 1 星 → 2 格挡（升级 3） |
| 隐秘藏品 | `HiddenCache` | 1 | 0 | +1 星；下回合 +1 星 |
| 铸墙 | `Bulwark` | 2 | 0 | 12 格挡 + 铸造 10 |
| 星位序列 | `Alignment` | 0 | 2 | +2 能量（升级 +3） |
| 共鸣 | `Resonance` | 1 | 2 | 自己 +1 力量、所有敌人 −1 力量（升级 +2） |
| 震荡波 | `Shockwave` | 2 | 0 | 所有敌人 3 层虚弱 + 3 层易伤（升级 5） |
| 天际钻头 | `HeavenlyDrill` | **X** | 0 | 8 伤害 ×X（升级 10）；X ≥ 4 时 X 翻倍 |
| 大爆炸 | `BigBang` | 0 | 0 | 抽 1、+1 费、+1 星、铸造 5 |
| 辉光 | `Glow` | 1 | 0 | +1 星、抽 1 |
| 收集光辉 | `GatherLight` | 1 | 0 | 8 格挡 + 1 星（升级 11 格挡） |
| 辐射 | `Radiate` | 0 | 0 | 3 伤害 ×（**本回合每获得 1 星**打一次） |
| 淬炼刀刃 | `RefineBlade` | 1 | 0 | 铸造 8、下回合 +1 费 |
| 招架 | `Parry` | 1 | 0 | 能力：君王之剑额外给格挡 |
| 星尘 | `Stardust` | 0 | 0 | 5 伤害随机 ×X |
| 汇流 | `Convergence` | 1 | 0 | 本回合保留手牌；下回合 +1 费 +1 星（升级 +2 星） |
| 防御 | `DefendRegent` | 1 | 0 | 5 格挡 |
| 君王之剑 | `SovereignBlade` | 2 | 0 | Token（击剑指南每回合铸造 10 并把它放进手里） |

附魔：**王室认证**（`RoyallyApproved`）= 固有 + 保留（只能给攻击/技能牌）；
**灵魂之力**（`SoulsPower`）= 去掉「消耗」（只对带消耗的牌合法；本套那 5 张候选**都没有**消耗，
所以这个附魔在引擎里是**空操作**，只作为设计标注保留 —— 真正的原因见 §5 第 3 条）。

## 3. 实现

| 部分 | 位置 |
|---|---|
| 剧本 / 卡组 / 遗物 | `src/Hooks/Core/ChoiceRegentScript.cs` |
| 选择记账（(Boss,回合) → 挑中的牌） | `src/Hooks/Core/ChoiceRegent.cs` |
| 选牌接管（`CardSelectCmd.FromHand` → 知识恶魔那套界面） | `src/Hooks/Patches/ChoiceRegentPickPatch.cs` |
| 选择落地后的补打（天际钻头） | `src/Hooks/Monsters/CharBossMonster.Choice.cs` + 出牌循环里一行调用 |
| 特殊效果 Power | `src/Hooks/Powers/BossGuidePower.cs` |
| 注册 | `BossKeys`（`(4, 2, 3)`、3 层候补、短名「抉择储」、遭遇映射、互补链）、`BossDefs.ExtraVariants[2]`、`BossLineups.ChoiceActive`、`TierBossEncounters.RegentChoiceBossEncounter`、`TierBossMonsters.Act3.RegentChoiceBoss`、`BossInfoTable`、`DownfallCollectibleCompat`（藏品 = 天际钻头） |

### 3.1 选牌接管为什么这样写

反编译「抉择，抉择」的 `OnPlay`：

```csharp
await CardPileCmd.Draw(ctx, DynamicVars.Cards.IntValue, Owner);   // 抽 5 张（升级后）
var picked = await CardSelectCmd.FromHand(ctx, Owner, prefs,      // ← 唯一的分叉点
        c => c.Type == Skill && !c.Keywords.Contains(Unplayable), this);
for (int i = 0; i < DynamicVars.Repeat.IntValue; i++)
    await CardCmd.AutoPlay(ctx, picked, null);                    // 重复打出 3 次
```

她出牌期间栈顶压着 `BossCardSelector`（自动选择器）⇒ 原版这次选牌会被**自动**挑走。
所以补丁只接管这一次选牌（条件：来源是「抉择，抉择」+ 选牌的是我们的影子玩家）：

1. 候选 = 她手里通过原版过滤的那些牌 —— 第 1 回合正好是那 5 张技能牌
   （同一只手里还有「天际钻头」「君王之剑」，都是攻击牌，被过滤掉 ⇒ 正好 5 张，与用户口径一致）；
2. `CardSelectCmd.SuspendSelectorForTest()` **临时把自动选择器摘下来**，
   否则 `FromChooseACardScreen` 开头 `if (Selector != null)` 会直接走选择器、界面根本不出现；
3. `CardSelectCmd.FromChooseACardScreen(ctx, 候选, 被锁定的玩家, canSkip: false)` ——
   与知识恶魔同一个入口，await 他选完；她这一手就此暂停，后面的牌才会继续打；
4. 挑中的那张记进 `Core/ChoiceRegent`（第 1 回合的天际钻头靠它判断该不该补打）。

### 3.2 能量 / 星光

能量逐回合写死（用户给的表），因为「没费」是设计的一部分。第 1 回合的账正好把 3 点花光：

```
群星之子(1) + 崇拜(1) + 王国资产(1) = 3  ⇒ 抉择(0) 之后剩 0 费
```

于是「天际钻头」只有玩家选了**星位序列**（被重复打出 3 次 × +2 费 = +6）才打得出 ——
这与用户那句「如果选择星位序列的话就出，否则没费出」完全对上。

星光（不清零、跨回合累积）：

```
起手 3（天赋君权）→ T1 崇拜+ 给 3 ⇒ 6 → 抉择抉择+ 付 6 ⇒ 0
T2 大爆炸/辉光/收集光辉 各 +1 ⇒ 3 … T5 累积到 7，付 6 ⇒ 1
```

### 3.3 天际钻头的 X

- **第 5 回合**：剧本里就是普通一张牌，规划期 X = 出手时的剩余能量（3）⇒ 8×3；
  若玩家选了星位序列，星位序列的 3 次会把能量补到 9 ⇒ 实际 X = 9 ⇒ **X≥4 翻倍 ⇒ 18 段**。
- **第 1 回合**：`ScriptCard.OnlyIfChosenCard = typeof(Alignment)` —— 规划期（玩家回合）选择还不知道，
  所以这张牌**不规划**（手牌里压暗、没有意图）；选择落地后由
  `CharBossMonster.RunChoiceFollowUpsAsync` 按剧本补打（X = 当时剩余能量 = 6 ⇒ 6 ≥ 4 ⇒ 12 段）。

## 4. 与知识恶魔一致的行为

- 只有**被锁定的那位玩家**选（单人就是唯一玩家）；`NChooseACardSelectionScreen` 弹出期间整场等着。
- 标了「不出」的回合（2/3/4）用 `ScriptCard.HoldInstead` + 王室认证的「保留」，
  牌留在手里、不产生意图。

## 5. 第二轮订正（用户 2026-11-10，v0.2.560）

1. **第 6 回合改 2 费**：用户「我算错了，第六回合确实应该是 2 费，改成 辉光、辐射、淬炼刀刃、收集光辉（没费）」
   ⇒ 剧本已按这个顺序落地（淬炼刀刃 1 费打得出来、收集光辉 1 费压暗）。
2. **那 5 张候选牌：压暗 + 画意图**。用户：「可以选的那 5 张牌是暗的，但是显示意图：
   铸墙：守势+强化，隐秘藏品：强化，共鸣：策略+强化，震荡波：策略，星位序列：显示会使天际钻头增加的伤害/段数」。
   - `ScriptCard.ForceIntents`（`BossIntentTag` = 守势 / 强化 / 策略）由剧本点名，规划期按 **守势 → 策略 → 强化** 造意图；
   - `PlannedPlay.DimWhenShown`：进了 `DisplayPlays` 但**要压暗** —— 手牌栏原来「在 DisplayPlays 里就画亮」，
     现在按这个标记区分（`BossHandPanel.IsAutoPlayed` / `BuildCell` / `Cell.Bright` 三处一起改）。
   - 星位序列：`ScriptCard.ShowsDrillGain` → 规划期算出「选它之后天际钻头会**多打**几段」
     （= 变更后段数 − 原本段数；星位序列 3 次 × +2 费 ⇒ X 多 6，X≥4 时照原版翻倍），
     用**原版意图节点**画出来（`MultiAttackIntent(8, 12)` → `NIntent`，如 **8×12**）。
     用户口径：「**你用原版的，别自己瞎搞。只改一下不让薄荷尖塔记录就行了**」。所以：
     · 用的是 `BossHandCard.AddDrillGainIntent`（走同一个 `AddIntent`），不是自造 Label；
     · 它**不放进 `PlannedPlay.Intents`** ⇒ 不会被 `PublishVanillaIntents` 写进原版 `MoveState.Intents`，
       而薄荷尖塔只读那里（见 `Core/MintySpireDamagePreviewCompat` 顶部注释）⇒ 不会重复统计。
     实测口径（当前实现）：第 1 回合 8×12（余费 0 ⇒ 原本 0 段，选后 X=6 ⇒ 12 段）；
     第 5 回合 8×15（余费 3 ⇒ 原本 3 段，选后 X=9 ⇒ 18 段）。第 5 回合的钻头是常驻的、
     没有 `OnlyIfChosenCard`，`BossScriptPlanner.FindChoiceDrill` 按「X 费牌」也认得出它。
3. **震荡波附魔「灵魂之力」**：用户「震荡波具有消耗，给它附魔灵魂之力」
   —— 反编译确认 `Shockwave.CanonicalKeywords` 就是 `Exhaust` ⇒ 附魔合法（`SoulsPower` 去掉消耗）。
   候选那张与第 12 回合那张都加了。
4. **选牌不许被打断**（用户：「如果我选的太慢，他的这个回合会结束并使我的下个回合开始。应该让储君的这张牌
   不会被提前打断，玩家选完之前所有其他进程都应该停着。也不要因为这张牌卡了很久就把它当bug（只让抉择抉择受到这些豁免）」）。
   三个地方一起改，**只豁免 `DECISIONS_DECISIONS` 这一张**：
   - `CharBossMonster.PlayOne` 的 5 秒 `Task.WhenAny(playTask, Task.Delay(5000))` 兜底（本来是防球动画不返回
     把整回合挂死的）对它**不计时** ⇒ 一直 await 到玩家选完，期间她整个回合停在这一句，其它 Boss 与玩家回合
     自然都排在后面；
   - `StrayBossCardQueuePatch.Reap()` 在等待期间**暂停**（新增 `HoldReap`）—— 慢车道 4Hz 会调它，
     而它把 `DECISIONS_DECISIONS` 当成「怪物打出的残留卡面」用 `RemoveCardFromQueueForCancellation` 摘掉，
     等于提前打断她这一手（这就是「选慢一点她回合就结束了」的另一半原因）；
   - `BossFrameTickPatch.WatchPlayQueue` 不再对它报「⚠ 出牌队列滞留」（和数据收集里
     「玩家自己在选牌」那条豁免同一性质），所以等再久也不会被当成 bug。

## 6. 第三轮订正（用户 2026-11-10，v0.2.563）

1. **星尘 / 辐射的段数**：「记得计算他在本回合会获得的辉星」。
   - 反编译：星尘 `Stardust` 有 `HasStarCostX => true`，`WithHitCount(ResolveStarXValue())`
     ⇒ **把身上所有辉星花光、一星一段**；
     辐射 `Radiate` 的 `CalculatedHits` 数的是历史里**本回合的正数 `StarsModifiedEntry`** ⇒ 段数 = 本回合获得过的辉星数。
   - 两条在规划期都算不出来（历史空 / 星还没花）⇒ 新增 `IntentsFor(starHits:)`，由
     `BossScriptPlanner` 按剧本顺序把数递进去（`starsGainedThisTurn` + X 星费的星数）。
     0 段时连伤害也清零，不再画一条 0 点攻击意图。
   - ⚠ **只传规划期是没用的**（用户第二轮：「辐射仍没有提前计算…星尘仍然没有正确显示段数」）：
     `PrepareTurn` 规划完**紧接着**就调 `BladesGrowth.RecomputeAll` 重算、出牌期每张牌前还会再重算
     ⇒ 全被「历史口径」覆盖回 1 段。现在把它**定死存进 `PlannedPlay.StarHits`**，
     两条重算路径（`BladesGrowth.RecomputeAll` / `CharBossMonster.RefreshPlannedIntents`）都原样再用。
5. **进阶 9 不再升级共鸣**（用户 2026-11-10 订正）：`BossDefs` 的进阶表与第 11 回合剧本那张都改回未升级。
   进阶 9 现在只有：淬炼刀刃、震荡波。
6. **回合开始到账的「下回合 +N 辉星」也算进「本回合获得的星」**（用户：
   「第一回合选隐秘藏品的情况下，第二回合的辐射没有计算隐秘藏品在回合开始时给的星」）：
   `StarNextTurnPower` 是**她回合开始时**由 `PlayerCmd.GainStars` 到账的（影子玩家那条由
   `CharBossMonster.SettleStarNextTurnPower` 手动结算）⇒ 对本回合而言它就是「本回合获得的辉星」。
   规划期现在把它**同时**加进 `stars`（预算）与 `starsGainedThisTurn`（辐射的段数口径），
   星尘的星数口径也包含它。
7. **剧本第三轮订正**（用户 2026-11-10）：
   - 第 3 回合（3 费）= 招架、星尘、汇流+、防御
   - 第 4 回合（3 费）= 君王之剑、隐秘藏品、防御（没费）
   - 第 5 回合 = **2 费**（原来 3 费）⇒ 天际钻头 X=2；选星位序列则 X=2+6=8 ⇒ 16 段，
     星位序列下面的预告也随之变成 **8×14**（= 16 − 原本的 2）。
   - **星光账（用户 2026-11-10 亲自核过，我第一版算错了）**：反编译「隐秘藏品」= `StarsVar(1)`
     **当回合 +1** + `PowerVar<StarNextTurnPower>(3)` **下回合 +3**（升级后 +4），不是我先前以为的 +1。
     于是第 5 回合的星光正好 **6**，与「抉择，抉择+」的星费 6 严丝合缝：
     ```
     T1  3（天赋君权）+3（崇拜+）= 6 − 6（抉择）= 0   ｜选隐秘藏品：当回合 +3、下回合 +9
     T2  回合开始 +9（或 +0）→ 大爆炸 +1、辉光 +1、收集光辉 +1 ⇒ 12（或 3）
     T3  星尘把**身上所有星花光**（X 星费，掏空）⇒ 0 ｜汇流+ 下回合 +2 星
     T4  +2（汇流+）+1（隐秘藏品当回合）⇒ 3 ｜隐秘藏品下回合 +3
     T5  +3 ⇒ **6** ⇒ 抉择抉择+ 刚好打得出 ✓
     ```
     代码读的是**活的 `StarNextTurnPower` 层数**（不是牌面常量），所以 3 点 / 9 点都算得对。
2. **汇流 / 淬炼刀刃的「下回合 +1 能量」不再当成立刻到手**（`DeferredEnergyCard` 加 `CONVERGENCE`、`REFINE_BLADE`）
   ⇒ 第 9 回合（2 费：汇流+ 1、隐秘藏品 1）与第 10 回合（3 费：铸墙 2、淬炼刀刃 1）最后那张隐秘藏品正确压暗。
   顺带 `DeferredStarCard`：汇流的「下回合 +星」也不再算进本回合星预算。
3. **天际钻头把费花光**改由剧本 `ScriptCard.SpendsAllEnergy` 明说（不依赖 `EnergyCost.CostsX` 在规划期猜对）
   ⇒ 第 5 回合的「君王之剑」不会再被画成能打。
4. **`XHitBonus` 改成「规划期算出的 cost 优先」**（以前优先读已捕获的 `ResolveEnergyXValue()`，
   可能残留上一张牌的值 ⇒ 第 13 回合只剩 1 费却画 2 段），并给天际钻头补上「X≥4 时段数翻倍」。

## 7. 仍然待确认

1. **天际钻头预告的口径**：现在显示的是「多出来的段数」（用户原话「减去天际钻头原本的 8×2」）。
   若想改成显示「选它之后的总段数」，把 `AddDrillGainIntent` 里的 `DrillGainHits` 换成 `DrillTotalHits` 即可。
2. **能力面板**：`BossInfoTable["REGENT_CHOICE"]` 仍是占位数据，等用户给正式那几行。

