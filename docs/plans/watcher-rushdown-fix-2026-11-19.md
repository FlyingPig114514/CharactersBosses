# 红蓝观（WATCHER_RUSHDOWN）4 条 bug 的根因与修法 · v0.2.771

> 写于 2026-11-19。**每条都先用 `%APPDATA%\SlayTheSpire2\logs\mod_log.txt` 证实根因才改**（用户要求）。
> 证据时间段：`2026-11-19 14:15:31 ~ 14:17:21`（最后一场）与 `14:04 ~ 14:11`（打到第 12 回合那两场）。

---

## 1. 「节制」第二回合 6 张牌就 +2 力量 + buff 上没有数字

### 证据：每张牌被数了**两次**

最后一场她逐张出牌的日志（`PLAY <id>`）与 `节制：满 12 张` 的时间点：

| 回合 | 她打的牌（PlayOne 记的） | 累计 PlayOne 张数 | 节制触发 |
| --- | --- | --- | --- |
| 1 | 猛虎下山 / 渐强 / 警惕 | 3 | — |
| 2 | 心灵堡垒 / 暴怒+ / 腰带抽打 / 安宁 | 7 | 14:15:43、14:16:07 |
| 3 | 义愤填膺 / 供奉 / 碰撞连击 / 打击 / 冥想 | 12 | 14:15:54、14:16:43 |
| 4 | 暴怒+ / 打击 / 防御 / 不惧妖邪 | 16 | 无 |
| 5 | 当头棒喝 | 17 | 无 |
| 6 | 打击 / 内心宁静 | 19 | 14:17:05 |

- 第 2 回合**只打了 7 张**就触发过一次 ⇒ 不可能是「1 张记 1 次」；
- 按「**每张记两次**」重算：T1 = 6、T2 第 3 张（累计第 6 张）= 12 ⇒ 正好是用户说的「**第二回合 6 张牌就加力量**」；
  之后 T2 剩 1 张 + T3 五张 = 6 张 ×2 = 12 ⇒ 14:16:43 那次；再之后 T4(4)+T5(1)+T6(2) = 7 张 ×2 = 14 ⇒ 14:17:05 那次。
  **三个触发点全部对上**。
- 「回合开始复燃」那些牌（`回合开始复燃：… 已按其自身钩子打出`）**没有被数**：如果数了，第 5 回合（复燃×2 + 当头棒喝）就会到 12 而实际没有触发。

⇒ 两条计数路径**同时**在跑：`CharBossMonster.PlayOne → BossTemperancePower.NoteCardPlayedAsync`
＋ `BossTemperancePower.AfterCardPlayed`（v0.2.768 判断「Power 钩子在她身上不派发」是**错的**，
上面这张表恰好证明它**会派发**）。

### 证据：数字不显示 ← 不是同一个原因

反编译 `MegaCrit.Sts2.Core.Nodes.Combat.NPower.RefreshAmount`：

```csharp
_amountLabel.SetTextAutoSize((Model.StackType == PowerStackType.Counter)
    ? Model.DisplayAmount.ToString() : string.Empty);
```

也就是说：`StackType == Counter` 时显示的是 **`DisplayAmount`**（这一半本来就对），
但这个私有方法只在**收到 `DisplayAmountChanged` 事件**时被调（`NPower.OnDisplayAmountChanged`）
⇒ 从不调 `InvokeDisplayAmountChanged()` 的话，图标永远停在挂载那一刻的值。

### 修法

- 计数**只留一条路**：`NoteCardPlayedAsync`（`PlayOne` 显式记账）。`AfterCardPlayed` 里那半段 `_played++` 删掉；
- `NoteCardPlayedAsync` / 恐惧模式的 `BossTimeWarpPower` 每记一次都 `InvokeDisplayAmountChanged()`
  （和 `IroncladRagePower` / `KnowThyPlacePower` 同款）；
- 顺手加日志 `节制：计数 X → Y/12（牌名）`，46 行限流，下次一眼对账。

**预期（设计口径）**：T1 3 + T2 4 = 7，T3 五张后 = **12 ⇒ 第 3 回合冥想那一刻触发**
（原文：「冥想（捞暴怒+。此时她的特殊效果触发，自己的回合也恰巧结束）」）；
第二次在**第 8 回合的腾越**那一张（T4 4 + T5 1 + T6 3 + T7 2 + T8 2 = 12，原文：「奇迹、腾越（此时正好第二次打 12 张牌…）」）。

---

## 2. 第 4 回合玩家打了攻击牌，她那记「不惧妖邪」还是不让她进「平静」

### 证据：`PlayerAttacked` 根本没被置位（而且**静默**）

- 14:16:43 有 `她的计划里有「不惧妖邪」→ 给她挂上预示 buff`（buff 挂上了）；
- 但这场**没有** `不惧妖邪 buff：被锁定的 静默猎手 打出了攻击牌「…」` 这一行
  —— 而更早的场次（11:04:12 / 12:50:47 / 15:16:48 / 16:42:23 / 17:53:53）**有**这一行。
- 差别在闸门：`WatcherFearNoEvilAimPower.AfterCardPlayed` 里走的是
  `WatcherOmegaMechanics.OmegaOwnerOf(this)` → `OurOmegaBoss`，判据是「身上必须有**斋戒**」
  ⇒ 那只 Boss 只会是 **Ω观**。红蓝观身上是「节制」⇒ 在这里 `return`，`PlayerAttacked` 永远 false，
  连一行日志都不打（v0.2.759 只把 `OnPlay` 后缀那一半放宽成 `FearNoEvilBoss`，**记号的这一半忘了改**）。
- 所以后缀拿到了 buff，但 `aim.PlayerAttacked == false` ⇒ 不进平静。

### 修法

- `OmegaOwnerOf` 改成走放宽后的 `FearNoEvilBoss`（斋戒**或**节制）；
- 两种静默 return 各补一行日志（不在名单 / 打出攻击牌的人不是被锁定的那位）；
- `PlayOne` 里那记兜底（`Core.PlayerAttackWatch.Flag`）也补三条诊断日志（标记真 / 标记假但 buff 记着 / 两边都没有）。

### 附带发现：`PlayerAttackWatch` 这条 Harmony 本身就打不响

反编译实证 `MegaCrit.Sts2.Core.Commands.CardCmd.AutoPlay(ctx, card, target, type, skipXCapture, skipCardPileVisuals)`
**只覆盖「自动打出」**（奇巧/被弃后免费打出这种），真人从手里出牌并不经过它
⇒ 挂在它上面的 `PlayerAttackWatch.Postfix` 收不到玩家的普通出牌。
现在由 `BossTemperancePower.AfterCardPlayed`（已证会派发）同时置 `SwitchRequested` 与 `PlayerAttackWatch.Flag`。

---

## 3. 第 12 回合玩家打了攻击牌，还是不切换成「不惧妖邪无限」

### 证据：切换日志打了，手牌仍是默认那套

`14:05` 那一场（14:10 同）：

```
14:05:19 12 回合：玩家打出过攻击牌 ⇒ 切换成「不惧妖邪无限」计划
14:05:21 剧本第 12 回合: 打 [WATCHER-ERUPTION,WATCHER-CONSECRATE,WATCHER-INNER_PEACE,
                            WATCHER-ERUPTION,WATCHER-CONSECRATE,WATCHER-INNER_PEACE,
                            WATCHER-ERUPTION,WATCHER-CONSECRATE,WATCHER-VIGILANCE,
                            WATCHER-ERUPTION,WATCHER-CONSECRATE,WATCHER-VIGILANCE] 跳过 [WATCHER-FEAR_NO_EVIL,PURITY] 余费=4
```

那串是**默认**的 12 张（暴怒+/供奉/内心宁静×2 + 警惕×2），整回合 `PLAY` 里**一张不惧妖邪都没有**；
切换段应该是「暴怒+、供奉、不惧妖邪」×4 + 警惕/内心宁静/净化+（不出）。

### 根因：`SwitchRequested` 是「取一次就清掉」，而 `TurnAt` 有 ~25 个调用点

`TurnAt` 被 `PrepareTurn`（取能量/药水）、`BossScriptPlanner`（造手牌/自检日志）、`BossIntentBuilder`、
`BossCardSelector`（点名名单）、`BossPurify`… 反复调用 ⇒ **第一个调用者**把标记吃掉了，
真正拿去造手牌的那一次读到的是 `false` ⇒ 手牌永远是默认那套，只有那行日志看起来像切了。

### 修法

`BossScript` 加实例字段 `_switchLocked`：「切换」对**回合号**是**黏住**的 ——
一旦切换，之后所有 `TurnAt(该回合)` 都返回 `SwitchTurn`；回合号掉回 `SwitchFromTurn` 之前
（= 新的一场战斗）才解锁。

---

## 4. T7 / T8 应该是「碰撞连击」

v0.2.769 那次「所有粉碎关节 → 腰带抽打」是**全局字符串替换**，把 T7 与 T8（额外回合）里本来就有的
**碰撞连击**也换成了腰带抽打。现在两张都改回 `WATCHER_BOWLING_BASH`。

**用户 2026-11-19 确认的口径**：

| 回合 | 位置 | 现在 |
| --- | --- | --- |
| T2 | 打出去的那张 | **腰带抽打**（保留） |
| T3 | 碰撞连击 | **碰撞连击**（位置不变） |
| T7 | 第一张 | **碰撞连击**（改回） |
| T8（额外回合） | 第三张 | **碰撞连击**（改回） |
| T10 | 「不出」那张 | **腰带抽打**（保持） |
| T11 | 净化+ 消耗名单 | **腰带抽打**（保持） |

能量账不变（碰撞连击与腰带抽打都是 1 费）。

---

## 5. v0.2.773：第 12 回合「当回合就换」+ 净化烧牌特效（用户 2026-11-19 第二轮实测）

### 5.1 「打了攻击牌只换下回合的意图，当回合不换」

**证据**（v0.2.772 那一场，`mod_log.txt`）：

```
14:50:55  剧本第 12 回合: 打 [暴怒+,供奉,内心宁静,…,警惕,警惕] 跳过 [不惧妖邪(保留在手),净化+]
14:50:57  节制钩子：真人玩家打出了攻击牌「STRIKE_SILENT」⇒ 已记下
14:50:59  节制钩子：真人玩家打出了攻击牌「STRIKE_SILENT」⇒ 已记下
14:51:01  PLAY WATCHER-ERUPTION → CONSECRATE → INNER_PEACE …（12 张，一张不惧妖邪都没有）
14:52:18  PLAY WATCHER-ERUPTION → CONSECRATE → FEAR_NO_EVIL …（**下一回合**才换成切换段）
```

**根因**（两条，都要修）：

1. **时序**：她那一回合的**手牌 + 计划**是 `PrepareTurn`（挂 `MonsterModel.RollMove`，
   由**玩家回合开始**那一刻触发）一次性算死的 ⇒ 玩家在这**之后**才打攻击牌，切换只能等到
   下一个 `PrepareTurn` = 下一回合。这正是「只换下回合」。
2. **锁被清掉**：`TurnAt` 的解锁条件写成了 `turn < SwitchFromTurn`，而同一帧里既有人问第 12 回合
   （造手牌/出意图）、也有人问第 11 回合（点名名单、自动选择器、`BossPurify`…）⇒ 刚锁上就被清。
   日志铁证：`14:50:53` 打过一次「已锁」、`14:51:00` 又打了一次**同样的**日志（能打第二次 ⇒ 中间被清过）。

**改法**：

- `BossScript.TurnAt`：解锁条件改成 **`turn <= 1`**（= 新的一场战斗）；
- 新增 `CharBossMonster.RebuildForSwitch(why)`：把**这一回合**的手牌按切换段重建（走 `PrepareTurn`
  同一条造手牌的路）→ `Plan` 重排 → `BladesGrowth` / `PublishVanillaIntents` / `BossHandUi.RefreshForce`。

  ⚠ **只换 `Plan` 不够**：默认段的手里根本没有「不惧妖邪」，`BossScriptPlanner.Matches` 会全判
  「不在手牌」⇒ 一张都打不出来。所以必须**连手牌一起重建**。
- 触发：`BossTemperancePower.AfterCardPlayed`（真人玩家打出攻击牌，已证会派发）置
  `SwitchReplanPending` → `BossFrameTickPatch` 4Hz 消费（**不在出牌钩子里直接动 UI**）；
  同一回合号只重建一次，且只在 `CurrentSide == Player`（她还没开始出牌）时做 ——
  她出牌中途换计划只会变成「不在手牌」的空打。

### 5.2 「净化+ 烧牌没有特效」

反编译拿到两处根因（都在**我们自己**这一侧）：

1. 以前先 `RemoveInternal(card, true)` 再 `CardPileCmd.Add(…, Exhaust, …)`，而 `Add` 自己就会
   `card.RemoveFromCurrentPile(skipVisuals)` + 播换堆动画 ⇒ 我们先摘一次把 `oldPile` 变成 null、
   卡面节点也一起没了，动画整段走不到。
2. 就算不先摘也没用：`CardPileCmd.GetTweenForCardsChangingPiles` 里有一句
   `if (!LocalContext.IsMe(card.Owner) && …) continue;` —— 她的牌属于**影子玩家**，
   不是「本机玩家的牌」⇒ 永远不飞、也不响音效。

**改法**（照原版 `PileType.Exhaust` 且 `oldPile == Hand` 那条分支自己做三步）：

```csharp
var vfx = NCardExhaustQuickVfx.Create(cardNode);              // 自己挂到卡面的 CardVfxContainer
NDebugAudioManager.Instance?.Play("card_exhaust.mp3");         // 烧牌音效
TaskHelper.RunSafely(vfx.PlayAnimation());                     // 蓄力 → 吸进消耗堆 → 烧毁粒子
```

新增 `BossHandPlayFx.Exhaust(cell)`（先把卡面从格子搬到 `HudLayer`，否则面板重建会拆掉节点）
+ `BossHandPanel.PlayExhaustFx(card)`；数据改走原版 `CardCmd.Exhaust`（含 `History.CardExhausted`
与 `AfterCardExhausted` 钩子）。

---

## 待实机验证（下一场打红蓝观时看这几行）

```
节制：计数 11 → 12/12（WATCHER-MEDITATE）      ← 第 3 回合冥想那一刻才满
节制钩子：真人玩家打出了攻击牌「…」⇒ 已记下
不惧妖邪 buff：被锁定的 … 打出了攻击牌「…」⇒ 她这一手不惧妖邪会进「平静」
不惧妖邪：被锁定的玩家这回合打过攻击牌 ⇒ 她进入「平静」
12 回合起玩家打出攻击牌 ⇒ 第 12 回合当场换成切换段：12 张 [ERUPTION,CONSECRATE,FEAR_NO_EVIL,…]
剧本第 7 / 8 回合: … WATCHER-BOWLING_BASH …                                       ← 该有碰撞连击
净化+：按剧本点名消耗了 N/M 张 + 屏幕上卡面被「吸进消耗堆并烧掉」+ 烧牌音效
```
