# 三层 1 号 · 观者 Boss「神格观」—— 完整需求（用户 2026-11-15 原话，逐字保留）

> 长目标：`goal-eb139f67-49e8-425c-a1e7-4413717556e1`（用户要求「列成 1 个长目标，一次性做完」）。

## 0. 出现的条件

- **只有装载观者模组时才出现**（本机已核对工坊序号：**3747526116**，`D:\SteamLibrary\steamapps\workshop\content\2868840\3747526116\`，程序集 `Watcher.dll` / `Watcher.pck`，命名空间 `WatcherMod`）。以后所有的观者 Boss 都遵循这条。
- 玩家在局中遇到观者 Boss 之后**退出游戏、关掉观者模组**再读档 ⇒ 必须用**别的 Boss 替换**掉原本的观者 Boss，保证这局能正常继续；**多人时要保持同步**（两端都替换成同一个，不能分叉）。

## 1. 特殊效果「虔信」

- 你（玩家）**每打出一张牌** ⇒ 观者获得 **1 层真言**。
- 在观者的**回合开始时**，她**失去 5 层真言**（最多失去全部，不会变成负数）。
- 每当她获得 **10 层真言**时：**退出当前姿态 → 获得 3 点力量 → 进入神格**，而且她**不再会在回合开始时退出神格**。
- 顺序必须是「先退出当前姿态」：因为她本来就在神格里时再获得 10 真言不算「进入神格」，就拿不到那 3 点能量。所以先退出、再进 ⇒ 每次满 10 层都能真的拿到能量。
- 她因进入神格拿到额外能量的那一刻，标了「有费就打」的牌**也会被她打出来** ⇒ **进入神格时要刷新意图**。
- 说明框里在特殊效果下面加一段（面向没玩过一代的玩家）：

```
神格：进入这一姿态时，获得 3 能量（这里用 3 个观者的能量图标）。处于这一姿态时，你的攻击造成三倍伤害。
```

（观者本来会在回合开始退出神格，但特殊效果让她不退，所以这句不用写。）

## 2. 恐惧模式

- 特殊效果改为：回合开始时**失去 3 层真言**；获得 **10 层**时退出当前姿态、获得 **4 点力量**、进入神格且此后回合开始不退神格。
- 恐惧模式的支配：**所有敌人获得 2 层虔信**。
- PS（怪物侧通用）：怪物获得 10 层真言时**也让它们进入神格**，效果 = **当回合造成的伤害变为原来的 3 倍，下回合退出**。

## 3. 遗物

圣水、涅奥的祝福、灯笼、佩尔的士兵、意外光滑的石头、布质果实、护喉甲。

- 进阶 8：升级 **摆手**。
- 进阶 9：获得 **手摇鼓**。

## 4. 剧本（她的回合打什么）

格式：回合 = 牌表；标「有费就打」的牌：能量不够就不打（顺序按列出顺序尝试）。

| 回合 | 牌 |
|---|---|
| 1 | 许愿（选择永生不死）、打击（有费就打）、警惕（有费就打） |
| 2 | 光辉、防御、敬拜（有费就打） |
| 3 | 奇迹、聚能成刃、供奉、贪婪 |
| 4 | 化拳为空、摆手、欺瞒现实（有费就打）、平安（有费就打） |
| 5 | 灭除之刃、虔信、旋身（有费就打） |
| 6 | 打击、圣洁、结末（有费就打） |
| 7 | 洗牌、光辉、敬拜（有费就打）、贪婪 |
| 8 | 打击、供奉、警惕（有费就打） |
| 9 | 灭除之刃、防御、旋身（有费就打） |
| 10 | 打击、圣洁、结末（有费就打） |
| 11 | 化拳为空、摆手、欺瞒现实（有费就打）、平安（有费就打） |

**之后 7～11 循环**（`LoopFrom = 7`，`LoopLength = 5`）。

### X 费牌的段数（用户特别强调）

- 第 3 回合她会打 **聚能成刃（X 费牌）**：那回合先打的**奇迹**给她 1 费，加上回合开始的 2 费 ⇒ 最少 **3 费** ⇒ 之后所有**灭除之刃**的伤害段数 = **3**。
- 若玩家在这回合让她**进入神格**（拿到 3 能量）⇒ 聚能成刃会吃到 **6 费** ⇒ 后面所有灭除之刃段数 = **6**。
- 若玩家让她**多次**进入神格 ⇒ 聚能成刃吃到的费用更多，段数也更多。
  ⇒ 意图预览必须按「本回合实际可用的能量（含神格给的）」算 X，并据此显示后续灭除之刃的段数。

## 5. 斗蛐蛐（CricketFight）模式

- 对手是**角色 Boss** ⇒ 对方每打出一张牌，观者获得 1 层真言（同「虔信」）。
- 否则（对手不是角色 Boss）⇒ **战斗开始时观者获得 3 层虔信 buff**（该 buff：回合开始时获得 3 真言）。

## 6. 已存在的地基（本轮直接用，别重写）

- `src/Hooks/Core/BossDefs.cs` → `BossDefinition Watcher`（Key `WATCHER_BOSS`，VisualScene `watcher`，HP 72 / Energy 3 / HandSize 6，Deck/Relics/Script 目前是**占位 null**）。
- `src/Hooks/Core/BossKeys.cs` → `new("WATCHER_BOSS", 6, 2, 1, typeof(WatcherBoss))`（幕 2 = 三层 1 号）、`DesignNames["WATCHER_BOSS"] = "观者"`、`EncounterOf` 分支，以及 **`WatcherRoster()` 目前返回空数组**（用户之前要求「别放半成品」）：
  ```csharp
  // if (Core.WatcherModPresence.Installed) return new[] { "WATCHER_BOSS" };
  ```
  本轮改成真名 **神格观** 并恢复这一行。
- `src/Hooks/Core/WatcherModPresence.cs`（探测装了观者模组）、`src/Hooks/Core/WatcherVfx.cs`（观者特效反射桥）。
- `src/Hooks/Core/BossDefinition.cs` 已有 `KeepsNextTurnEnergyPower` 等字段；`SpecialLocDreadByAct` / `SpecialPowerDreadByAct` 支持恐惧版特殊效果。

## 7. 落地顺序（避免一次改太多）

1. 观者卡牌引用层：按 `WatcherMod` 的类型名反射取牌（**绝不编译期引用**，见 `WatcherModPresence`），包括姿态相关（神格/平静/愤怒）、真言层数（`MantraPower`）、`聚能成刃` / `灭除之刃` / `摆手` 等剧本用到的牌。
2. `WatcherScript`（仿 `TarotFoolScript`）：11 回合表 + 有费就打 + 7~11 循环 + 进阶 8/9 的升级与遗物。
3. 特殊效果 `BossDevotionPower`（虔信）：`AfterCardPlayed`（玩家侧）+1 真言；`AfterSideTurnStart`（她）−5（不转负）；满 10 层 → 退姿态 → +3 力量 → 进神格 → 标记「不再退神格」→ **刷新意图**；说明框下方追加「神格」那一段（3 个观者能量图标）。
4. 意图：X 费（聚能成刃）按本回合实际能量算段数，并把结果带进后续灭除之刃的段数。
5. 恐惧版特殊效果 + 恐惧支配（所有敌人 2 层虔信）+ 怪物满 10 层进神格（当回合 ×3 伤害、下回合退）。
6. 斗蛐蛐两条规则。
7. 缺模组时的替换：读档时若原本是 `WATCHER_BOSS` 而现在没装观者模组 ⇒ 换成同幕的另一个 Boss（多人两端用同一套判定，靠已有的种子/设置同步通道保证一致）。
8. 本地化 16 语言（boss 名 / 特殊效果标题+正文 / 恐惧版 / 神格段 / 遭遇名）、`BossInfoTable` 一行、`docs/plans/dread-mode.md` 进度。


---

# 轮次记录

## 第 1 轮（2026-11-15）—— 牌与姿态 API 的引用层

### 卡牌对照（观者模组 es://Watcher/localization/zhs/cards.json 反查出来的内部 id）

| 中文 | 内部 id | CLR 类型（WatcherMod.*） |
|---|---|---|
| 许愿 | WATCHER_WISH_P | WatcherWish_P（子选项 WatcherWishLiveForever = 永生不死） |
| 打击 | WATCHER_STRIKE_P | WatcherStrike_P |
| 防御 | WATCHER_DEFEND_P | WatcherDefend_P |
| 警惕 | WATCHER_VIGILANCE | WatcherVigilance |
| 光辉 | WATCHER_BRILLIANCE | WatcherBrilliance |
| 敬拜 | WATCHER_WORSHIP | WatcherWorship |
| 奇迹 | WATCHER_MIRACLE | WatcherMiracle |
| 聚能成刃 | WATCHER_CONJURE_BLADE | WatcherConjureBlade（X 费） |
| 供奉 | WATCHER_CONSECRATE | WatcherConsecrate |
| 化拳为空 | WATCHER_EMPTY_FIST | WatcherEmptyFist |
| 摆手 | WATCHER_WAVE_OF_THE_HAND | WatcherWaveOfTheHand |
| 欺瞒现实 | WATCHER_DECEIVE_REALITY | WatcherDeceiveReality |
| 平安 | WATCHER_SAFETY | WatcherSafety |
| 灭除之刃 | WATCHER_EXPUNGER | WatcherExpunger |
| 虔信 | WATCHER_DEVOTION | WatcherDevotion |
| 旋身 | WATCHER_SWIVEL | WatcherSwivel |
| 圣洁 | WATCHER_SANCTITY | WatcherSanctity |
| 结末 | WATCHER_CONCLUDE | WatcherConclude |
| 贪婪 | GREED（**本体**牌） | MegaCrit.Sts2.Core.Models.Cards.Greed |
| 洗牌 | **查不到** | 观者 zhs 与本体 zhs 两份 cards.json 里都没有这张，**待用户确认**（第 7 回合那张） |

注：有几张牌在观者本地化里同时存在 *_V2 变体（打击 / 防御 / 光辉 / 供奉 / 欺瞒现实 / 许愿 / 旋身），本表取**不带 _V2** 的那份（观者卡池 WatcherCardPool.GenerateAllCards() 里列的就是它）。

### 姿态 / 真言 API（反编译 WatcherMod.WatcherCombatHelper）

`csharp
public static Task EnterDivinity(Player owner, CardModel? source)      // 进神格：+3 能量、攻击三倍
public static Task ExitStance(Player owner)                            // 退出当前姿态
public static Task GainMantra(Player owner, int amount, CardModel? source)
public static int  GetTotalMantraGained(Player owner)                  // 本场累计真言（WatcherStatePower）
public static int  GetMantraGainedThisTurn(Player owner)
public static bool IsInStance<T>(Creature creature) where T : PowerModel
`

要点：这些接口的 owner 是 **Player** ⇒ 我们的 Boss 走 **影子玩家 oss.Shadow**（观者状态挂在它身上）。
WatcherStatePower 是观者自己记状态的地方（TotalMantraGainedThisCombat / MantraGainedThisTurn / AttacksPlayedThisTurn …）。

### 本轮产物

- 新增 src/Hooks/Core/WatcherCards.cs（#if STS2_111）：按短名反射取观者程序集里的类型（T("WatcherStrike_P") → WatcherMod.WatcherStrike_P），暴露 18 张观者牌 + 本体 Greed，以及对 WatcherCombatHelper 的四个调用封装（EnterDivinityAsync / ExitStanceAsync / GainMantraAsync / TotalMantraGained）。**全部运行期反射，没装观者模组时返回 null / no-op**，不违反「不编译期引用别的模组」这条铁律。
- 111 构建 0 错误。

### 下一轮

WatcherScript（11 回合牌表 + 有费就打 + 7~11 循环 + 进阶 8 升级摆手 / 进阶 9 手摇鼓），并把它接到 BossDefs.Watcher 的 Script。

## 第 2 轮（2026-11-15）—— 剧本 + 定义接线

### 新增 src/Hooks/Core/WatcherScript.cs（111 独占）

- 11 回合牌表**逐字照用户顺序**；标「有费就打」的排后面（BossScriptPlanner 按顺序出牌、能量不够自动跳过，等同「有费就打」，与 SoulBoneScript 同一口径）。
- LoopFrom = 7 / LoopLength = 5 ⇒ 之后 7～11 循环。
- 进阶 8：**摆手**用 ScriptCard.UpgradeMinAscension = 8（第 4、11 回合那两张）。
- Deck()：3 张打击 + 3 张防御占位（每回合手牌由剧本生成）。
- Relics()：圣水（WatcherMod.HolyWater，反射）+ 涅奥的祝福（我们自己的 CharacterBosses.Relics.NeowBlessing）+ 灯笼 + 佩尔的士兵 + 意外光滑的石头 + 布质果实 + 护喉甲。
  内部 id 已核对（本体 zhs relics.json）：GORGET / LANTERN / LOOMING_FRUIT / ODDLY_SMOOTH_STONE / PAELS_LEGION；观者 zhs relics.json：HOLY_WATER / DAMARU。
- AscensionRelics()：手摇鼓 = WatcherMod.Damaru（进阶 9，待接进定义的 AscensionRelicsByAct）。
- 观者的牌全部来自运行期反射，取不到就从那一回合里剔掉，不会让整场崩。

### BossDefs.Watcher 接线

`csharp
Energy    = { 2, 2, 2, 2 }      // 用户：「回合开始时的 2 费」
DeckByAct   = { null, null, WatcherScript.Deck(),   null }
RelicsByAct = { null, null, WatcherScript.Relics(), null }
ScriptByAct = { null, null, WatcherScript.Build(),  null }
BlocksVanillaDraw = true        // 手牌完全由剧本生成
`

BossKeys.DesignNames["WATCHER_BOSS"] 由「观者」改为「**神格观**」。

### 仍未接

1. **特殊效果「虔信」**（下一轮的主菜）—— SpecialLocByAct / SpecialPowerByAct 仍是 null。
2. 进阶 9 的手摇鼓还没进 AscensionRelicsByAct（要等运行期解析 WatcherMod.Damaru，写法照 RefreshWatcherCharacterType）。
3. BossKeys.WatcherRoster() 仍返回空 ⇒ **这只 Boss 现在还不会出现在任何一局里**（用户明确要求别放半成品，等特殊效果做完再开）。

### 待用户确认

- 第 7 回合的「**洗牌**」查不到（观者 + 本体两份 cards.json 都没有）。
- 第 1 回合「许愿（**选择永生不死**）」需要指定选项 —— 观者的 WatcherWish_P 里有 WatcherWishLiveForever / Almighty / FameAndFortune 三个子选项，要确认走哪个 API 指定。

## 第 3 轮（2026-11-15）—— 特殊效果「虔信」落地

### 新增 src/Hooks/Powers/BossDevotionPower.cs（111 独占）

- PowerType.Buff / StackType.Counter（层数就是**真言**层数，显示成图标上的数字）。
- AfterCardPlayed：任意**真人玩家**打出的牌 ⇒ 真言 +1（她自己的影子玩家打的牌不算）。
- AfterSideTurnStart(side == Enemy)：她的回合开始 ⇒ 真言 −5（**恐惧 −3**），Math.Min(当前, 损失) 保证不转负。
- 每次加层后 while (当前 >= 10) 结算：**① 先 ExitStance → ② +3 力量（恐惧 +4）→ ③ EnterDivinity**，然后刷新意图（RefreshPlannedIntents + BossHandUi.RefreshForce）—— 进神格给的 3 点能量可能让「有费就打」的牌打得出去，所以必须重算。
- 顺序是先退姿态：用户明确说过「保证她进神格时能拿到能量」，本来就在神格里时再满 10 层不算「进入神格」、拿不到能量。

### 为什么真言不用观者模组的 Mantra power

反编译 WatcherCombatHelper.GainMantra：它只是「记两个计数器 + Apply<Mantra>」，而**满 10 层的结算在 Mantra power 自己身上** —— 时机与顺序都不归我们管。用户要求「先退姿态」，所以真言层数改由**我们自己记**（本 Power 的 Amount），但姿态切换仍走观者的 ExitStance / EnterDivinity（神格的「+3 能量、攻击三倍」还是模组实现的）。

### BossDefs.Watcher 接线

`csharp
SpecialLocByAct          = { null, null, "WATCHER_BOSS.devotion", null }
SpecialLocDreadByAct     = { null, null, "WATCHER_BOSS.devotion_dread", null }
SpecialPowerByAct        = { null, null, typeof(Powers.BossDevotionPower), null }
SpecialPowerDreadByAct   = { null, null, typeof(Powers.BossDevotionPower), null }
`

107 / 111 均 0 错误。

### 下一轮

本地化 16 语言（WATCHER_BOSS.name / title / moves.CHARBOSS_PLAY.title / devotion / devotion_dread、BOSS_DEVOTION_POWER.title/description/description_dread、WATCHER_BOSS_ENCOUNTER.title/loss），以及特殊效果说明下方那段「神格」。