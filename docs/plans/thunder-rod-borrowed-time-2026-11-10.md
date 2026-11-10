# 电球机激发意图 / 引雷针 / 预借时间 —— 三个 bug（v0.2.541，2026-11-10）

## 1. 电球机「暴风雨」的激发意图闪一下就没了

**用户原话**：「电球机暴风雨激发电球的 8×2 意图被吃掉了（回合开始时闪了一下）」

**根因（一句话）**：重算意图时没把「X 费牌的 X」传给球的模拟器。

`Core/BossOrbSim.cs` 的签名是：
```csharp
public Result Predict(CardModel card, int xEnergy = 0)   // ← X 是**参数**，默认 0
{
    ...
    else if (BossOrbMath.TryChannels(card, xEnergy, out var orbType, out int channelCount))
        Channel(orbType, channelCount, result);
```
- **规划期** `Core/BossScriptPlanner.cs:463`（对的那条）：
  ```csharp
  int xEnergyForOrbs = 0;
  try { if (card.EnergyCost.CostsX) xEnergyForOrbs = Math.Max(1, cost); } catch { xEnergyForOrbs = 0; }
  var evoke = orbSim.Predict(card, xEnergyForOrbs);
  ```
- **出牌期/每帧重算** `Monsters/CharBossMonster.Turn.cs:420`（错的那条）：`sim.Predict(play.Card)` ⇒
  X = 0 ⇒ 「暴风雨」（`TEMPEST`，X 费、生成 X 个闪电充能球）被当成**生成 0 颗** ⇒ 球槽满了才会
  产生的那条「激发 8×2」意图整条消失。

所以表现就是：玩家回合一开始按规划画出来（能看到 8×2），紧接着任何一次重算（`BossFrameTickPatch`
里就有 4 处调 `RefreshPlannedIntents`）就把它抹掉了 —— 用户看到的「闪了一下」。

**修法**：两处重算都补上 X，口径与规划期完全一致 —— 对 X 费牌，规划期存的 `play.Cost`
就是那个 X（`BossScriptPlanner` 里 `if (IsXCost(card)) cost = Math.Max(0, energy);`）：

| 文件 | 改动 |
| --- | --- |
| `Monsters/CharBossMonster.Turn.cs` `RefreshPlannedIntents` | `sim.Predict(play.Card, play.Card.EnergyCost.CostsX ? Math.Max(1, play.Cost) : 0)` |
| `UI/BossOrbDisplay.Highlight.cs` `RecomputeEvoking` | 同上（不补的话「待激发」的球不会变蓝） |

## 2. 引雷针：打三张牌集中不掉

**根因（一句话）**：玩家回合开始时把计数 `Apply(-Amount)` 减到 0，原版顺手**删掉了整条 Power**。

反编译 `PowerCmd.ModifyAmount` 末尾：
```csharp
power.SetAmount(newAmount, silent);
if (power.ShouldRemoveDueToAmount()) await Remove(power);   // ← 数量到 0 ⇒ 整条 power 移除
...
```
而 `BossLightningRodPower.AfterSideTurnStart` 原来的写法就是
`await PowerCmd.Apply<BossLightningRodPower>(null, owner, -Amount, ...)`
⇒ **每回合刚开始这层特殊效果就被删了**，之后玩家出牌没人计数 ⇒ 集中永远不掉。
（`BorrowedTimeBossPower` 早就踩过同一个坑，所以它把图标数字钳在 1..10。）

**修法**：
- 计数改存**字段** `_playedThisTurn`，不再依赖 `Amount`；
- `Amount` 只当显示用，**永远 ≥ 1**：`SyncAmount()` 写「**还差几张牌**触发」（3 → 2 → 1）；
- `AfterSideTurnStart` 只清字段 + `SyncAmount()`，**不再** `Apply(-Amount)`；
- `AfterApplied` 把图标刷成 3。

## 3. 预借时间：被「历史课」这类自动打出的攻击牌错误计数

**根因（一句话）**：计的是**牌面耗能**，不是**真正花掉的能量**。

用户说的遗物是**历史课**（`HISTORY_COURSE`）：「在你的回合开始时，打出一张你上一回合最后打出的攻击牌的复制品」。反编译确认它走的是 `CardCmd.AutoPlay(choiceContext, cardModel.CreateDupe(player), null)` ⇒ `CardModel.OnPlayWrapper(..., bool isAutoPlay, ResourceInfo resources, ...)` ⇒ `new CardPlay { IsAutoPlay = isAutoPlay, Resources = resources, ... }`（`IsAutoPlay = true`），所以按 `IsAutoPlay` 跳过是准的。`n`n原实现 `CostOf(card) = card.EnergyCost.GetAmountToSpend()` —— 只读牌面，与「这张牌到底扣没扣能量」
无关。被别的东西**免费/自动打出**的牌（`CardCmd.AutoPlay` 走的那类，例如「历史课」回合开始时帮你打一张攻击牌复制品）
一分能量没花，却被记了一笔。

**修法**（`Powers/BorrowedTimeBossPower.cs`）：
- `cardPlay.IsAutoPlay` ⇒ **直接跳过**（原版字段，自动打出的牌）；
- 计费用新的 `ActualEnergySpent()`：优先读 `cardPlay.Resources.EnergySpent`（真实扣费记录），
  它是 0 时退回牌面耗能（防止某个版本不填这个字段导致预借时间彻底不触发）。

## 4. 顺带：铁甲战士语音音量

`VolumeDb = -4` ⇒ `-0.5`（线性 ≈ 0.63 → 0.945，正好**振幅 ×1.5**，用户「有点小，调大 50%」）。
换算：`-4 + 20·log₁₀1.5 ≈ -0.48`。

## 实机验证要点

- 暴风雨那回合：面板上「暴风雨」下面应该**稳定**有一条 `8×2`（或 N×M）的激发意图，不再闪；
  对应的球角标应变成**激发值**（蓝色）。
- 引雷针：玩家回合开始后图标数字是 **3**；打 1 张变 2、再打变 1、第 3 张时她掉 1 点集中
  （日志 `引雷针：玩家本回合已打出 3 张牌 ⇒ 她本回合失去 1 点集中`）；她回合结束还回来
  （`引雷针：她回合结束 ⇒ 把本回合扣掉的 N 点集中还回去`）。
- 预借时间：日志 `预借时间：XXX 是**自动打出**的（没花能量，不计入预借时间）`。
