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

## 第 4 轮（2026-11-15）—— 16 语言本地化

新增 9 个键 × 16 语言（check-localization.ps1 全绿）：

| 表 | 键 |
|---|---|
| monsters.json | WATCHER_BOSS.name / .title / .moves.CHARBOSS_PLAY.title / .devotion / .devotion_dread |
| powers.json | BOSS_DEVOTION_POWER.title / .description / .description_dread |
| encounters.json | WATCHER_BOSS_ENCOUNTER.title / .loss |

- 设计名：zhs/zht **神格观**，eng **Divinity Watcher**，jpn 神格観，kor 신격관，rus Божественный Наблюдатель，deu Göttlicher Beobachter，fra Observateur Divin，ita/esp/spa/ptb Observador Divino，pol Boski Obserwator，tur Kutsal Gözcü，ind Pengamat Ilahi，tha ผู้เฝ้ามองสวรรค์。
- 特殊效果正文已按用户要求分两版（普通 −5 真言 / +3 力量；恐惧 −3 / +4 力量），并在**末尾空一行**加「神格」那一段：

```
[gold]神格[/gold]：进入这一姿态时，获得[img]res://images/packed/sprite_fonts/watcher_energy_icon.png[/img]×3 能量。处于这一姿态时，你的攻击造成三倍伤害。
```

  三个能量图标用的就是用户点名的**观者能量图标**（watcher_energy_icon.png，本体 pck 自带，已核对存在）；文本里图标用 [img]res://…[/img] 这种写法（观者模组自己的卡面也是这么写的，见 WATCHER_FOLLOW_UP.description）。

- 16 语言逐个翻译（esp 与 spa 同西语、ptb 葡语）。

## 第 5 轮（2026-11-15）—— 「进了神格就不再退」+ 许愿选项的结论

### ① 「她不再会在回合开始时退出神格」已解决

反编译观者模组 WatcherMod.Divinity（303 行）：退出神格的钩子不在回合开始，而在
**AfterSideTurnEnd（回合结束）** 的 async 状态机 <AfterSideTurnEnd>d__11 里，逻辑只有两句：

```csharp
if (side == Owner.Side && Owner.Player != null)
    await WatcherCombatHelper.ExitStance(Owner.Player);   // ← 到点自动退
```

新增 src/Hooks/Patches/WatcherDivinityKeepPatch.cs（111 独占）：补**本体**的
PowerModel.AfterSideTurnEnd（不引用观者的类型，铁律不变），三个条件同时成立才短路：
__instance.GetType().Name == "Divinity" + owner 是「我们某只 Boss 的影子玩家」+ 她身上挂着 BossDevotionPower。
⇒ 只有神格观免退，观者原版玩家、其它 Boss、其它模组一律原样。
（注：用户口径说的是「回合开始时退出」，实现里其实是回合结束 —— 效果等价，都是「她进了神格就一直挂着」。）

### ② 许愿的「永生不死」选项 —— 机制已摸清，实现方式待定

反编译 WatcherMod.WatcherWish_P.OnPlay：

```csharp
optStrength = CombatState.CreateCard<WatcherWishAlmighty>(Owner);
optArmor    = CombatState.CreateCard<WatcherWishLiveForever>(Owner);    // ← 永生不死
optGold     = CombatState.CreateCard<WatcherWishFameAndFortune>(Owner);
var chosen  = await WatcherCombatHelper.ChooseOne(choiceContext, Owner, options,
                            new LocString("cards", "WATCHER_WISH_P.selectionScreenPrompt"));
// 选中哪个就 Apply 对应的 Power（永生不死 = WishPlatedArmorPower）
```

⇒ 要固定选「永生不死」，最干净的做法是**运行期给 WatcherCombatHelper.ChooseOne 挂 Harmony**
（我们自己没法编译期 [HarmonyPatch] 别人的类型），prefix 里判「owner 是我们的影子玩家 + 候选项里有 WatcherWishLiveForever」
就直接给出那一个。仓库里已有运行期挂补丁的先例（Core/MintySpireDamagePreviewCompat）。
代价：ChooseOne 返回 Task<CardModel?>，prefix 要构造 Task.FromResult，且要确认它没有别的调用方被误伤（观者自己的卡大量用它）。
**下一轮做这个（先只对「牌是 WATCHER_WISH_P」时生效，收得比 owner 更紧）。**

107 / 111 均 0 错误。

## 第 6 轮（2026-11-15）—— 许愿固定选「永生不死」

新增 src/Hooks/Patches/WatcherWishChoiceCompat.cs（111 独占）：**运行期**给观者的
WatcherCombatHelper.ChooseOne(PlayerChoiceContext, Player, IReadOnlyList<CardModel>, LocString, bool)
挂 Harmony prefix（别人的类型不能编译期 [HarmonyPatch]，先例见 Core/MintySpireDamagePreviewCompat）。

命中条件（两个都满足才短路）：
1. owner 是我们某只 Boss 的**影子玩家**（ReferenceEquals(boss.Shadow, owner)）；
2. 候选项里存在 WatcherWishLiveForever（**只有「许愿」会开出这张**，所以不会误伤观者其它几十张也用 ChooseOne 的牌）。

命中后 __result = Task.FromResult(liveForever)、eturn false ⇒ 她永远许「永生不死」（WishPlatedArmorPower）。

安装时机：幂等，挂在每帧慢车道（BossFrameTickPatch 里 MintySpireDamagePreviewCompat.TryInstall() 旁边）——
观者模组可能比我们晚加载，所以必须能重试。日志：许愿固定选项：已挂上观者的 ChooseOne。

107 / 111 均 0 错误。

### 恐惧支配的设计（下一轮实现）

用户：「恐惧模式时观者的支配：**所有敌人获得 2 层虔信**。」
结合斗蛐蛐那条「观者在战斗开始时获得 3 层**虔信**（buff，回合开始时获得 3 真言）」可知：
**虔信 = 一个「回合开始时产生真言」的 buff**。所以支配的含义是：战斗开始时给**每位玩家** 2 层虔信，
他们每人回合开始 ⇒ 观者得到真言（支配是给 Boss 加码的，和愚者「塞 2 张悲伤」同向）。
落地需要：WatcherFaithPower（挂在玩家身上、回合开始给观者 +N 真言）+ WatcherDevotionDominion
+ CharBossEncounterBase.GetDominion() 按角色分流 + 16 语言 scension.json 文案。

## 第 7 轮（2026-11-15）—— 虔信 buff + 恐惧支配

### 新增 src/Hooks/Powers/WatcherFaithPower.cs（111 独占）

「**虔信**」buff = 挂在**持有者**身上，**持有者回合开始时给观者 Amount 层真言**。
一个 power 同时服务两条需求（语义本来就一致）：
- 斗蛐蛐：观者自己身上挂 3 层 ⇒ 她回合开始 +3 真言（**下一轮**接那条规则）；
- 恐惧支配：挂在**每位玩家**身上 ⇒ 他们各自回合开始，观者收真言。

结算走 BossDevotionPower.AddMantraAsync（由 private 放开成 internal），所以
「加真言 / 满 10 层结算 / 进神格 / 刷新意图」这一整条链完全复用，没有第二份实现。

### 恐惧模式「观者的支配」

- Core/CharDominions.cs：新增 DreadCharacters.Watcher = 6 + WatcherDevotionDominion
  （AfterRoomEntered：给每位真人玩家 2 层 WatcherFaithPower，影子玩家与怪物跳过）。
- CharBossEncounters.GetDominion() 的分流加一行 Core.DreadCharacters.Watcher => typeof(Core.WatcherDevotionDominion)。
- 16 语言 scension.json 新增 DREAD.WATCHER.description（观者的支配：每场战斗开始时，所有玩家获得 2 层虔信…）。check-localization 全绿。

107 / 111 均 0 错误。
## 第 8 轮（2026-11-15）—— 斗蛐蛐两条规则 + 进阶 9 手摇鼓

- 新增 `src/Hooks/Core/WatcherCricketRule.cs`（111 独占）：对手是角色 Boss ⇒ 他出牌 +1 真言（`BossDevotionPower.AfterCardPlayed` 本来就覆盖，对方 Boss 的牌 Owner 是对方影子玩家）；否则开局给她 3 层 `WatcherFaithPower`（每回合开始 +3 真言）。判据 = 场上除自己外还有没有第二只 `CharBossMonster`；只在 `CricketCompat.Installed` 时生效；接线点在 `FireCombatStart`。
- 进阶 9 手摇鼓：新增 `BossDefs.RefreshWatcherExtras()`（幂等，解析到 `WatcherMod.Damaru` 后追加 `(9, 3, damaru, null)`），由 `BossDefValidator` 调。
- 107 / 111 均 0 错误。

## 第 9 轮（2026-11-15）—— X 费段数联动 + 神格能量入预算

### 聚能成刃 → 灭除之刃 的段数

聚能成刃（`WATCHER_CONJURE_BLADE`）是 X 费牌，打出时生成一张灭除之刃（`WATCHER_EXPUNGER`），那张牌的段数 = 打聚能成刃时的 X。

- `BossScriptPlanner`：新增局部 `conjuredX`。规划到她打 **聚能成刃** 时记下 `cost`（= 那一刻剩余能量，已含奇迹给的 1 费），之后遇到 **灭除之刃** 就把 `xHits = X − 1` 递进去（`IntentsFor` 对非 X 牌也认 `xHits`，按 `1 + xHits` 还原段数）。
- 用户口径对上：第 3 回合奇迹 +1 与回合开始 2 费 ⇒ X=3 ⇒ 3 段；进了神格多 3 费 ⇒ X=6 ⇒ 6 段；多次进神格同理。

### 进神格给的 3 点能量要真的能花

她的回合能量是 `PrepareTurn` 里硬写进去的（`pcs.Energy = 预算`）⇒ 观者模组**当场**加的 3 点会被下次覆盖掉。

- `BossDevotionPower.TriggerDivinity`：进神格后 `boss.NextTurnEnergyBonus += 3`（和「星星 / 佩尔之泪」同一条通道）；
- `BossDefs.Watcher`：加 `HonorsNextTurnEnergyActs = new[] { 2 }`（三层 ⇒ act 索引 2）让这笔能量真的进预算。

107 / 111 均 0 错误。

## 第 10 轮（2026-11-15）—— 缺模组时换人（多人一致）

用户：「如果玩家在一局游戏中遇到观者 boss，但他退出游戏并关闭了观者 mod，用一个别的 boss 替换原本的观者 boss，保证游戏能正常进行（多人时要保持同步）」。

- BossKeys.SubstituteIfUnavailable(key)：key == WATCHER_BOSS 且**现在**探测不到观者模组 ⇒ 返回写死的 `FOOL_BOSS`（同为三层、同为 0.113 独占）。**不用随机数、不看本地设置** ⇒ 多人两端各跑一遍结果相同，不会分叉。
- 接线点：`BossKeys.EncounterOf(key)` 开头先过一遍替换（`key = SubstituteIfUnavailable(key);`）⇒ 读档后要生成这只 Boss 时直接换成塔罗愚，房间能正常建立。
- 日志：`观者 Boss：这一局是装过观者模组时开的，但现在探测不到它 ⇒ 换成塔罗愚继续`。

107 / 111 均 0 错误。

### 仍等用户确认

「怪物获得 10 层真言也进神格」里的「怪物」指她自己还是别的怪物（见第 9 轮末尾的提问）。在此之前不动伤害侧代码，避免把她的 ×3 做成两份。

## 第 11 轮（2026-11-15）—— 神格观登场 + 两处补丁改成运行期挂

### ① 恢复登场

`BossKeys.WatcherRoster()` 不再返回空：装了观者模组（工坊 3747526116）时返回 `["WATCHER_BOSS"]`
⇒ **三层 1 号候选里会有神格观**；没装时行为和以前完全一致。

### ② 两处补丁挂载失败的根因与修法（重要）

第一次装上后日志出现：

```
补丁类 WatcherDivinityKeepPatch 挂载失败: HarmonyException: Patching exception in method null
补丁类 WatcherWishChoiceCompat 挂载失败: HarmonyException: Patching exception in method null
```

两个原因都找到了：

1. **`AfterSideTurnEnd` 是 `AbstractModel` 上的虚方法，而观者的 `WatcherMod.Divinity` 是覆写**
   ⇒ 用 `[HarmonyPatch(typeof(PowerModel), nameof(...))]` 去补**基类声明**，Harmony 在 `PowerModel` 上
   找不到**声明**的方法（method null）；而且就算补上基类也拦不到派生类的覆写。
2. `WatcherWishChoiceCompat` 本来就没有 `[HarmonyPatch]` 特性（它是运行期挂的），但 `ModEntry` 会扫
   `CharacterBosses.Patches` 命名空间里所有类去 `PatchAll` ⇒ 必然抛一次（无害但污染日志）。

修法：两处合并成 `src/Hooks/Core/WatcherRuntimePatches.cs`（放在 `Core` 命名空间，不被扫），**都用运行期 Harmony**：

- `WatcherCards.T("Divinity").GetMethod("AfterSideTurnEnd")` → 拿到**覆写本身**的 MethodInfo 再挂 prefix；
- `WatcherCombatHelper.ChooseOne` 同上；
- 统一由慢车道 `Core.WatcherRuntimePatches.TryInstall()` 幂等调用（观者模组可能后加载 ⇒ 可重试）。

修完重启验证：**那两行挂载失败消失了**（只剩既有的 MintySpire 兼容补丁那条）。

### 现状

107 / 111 均 0 错误；`[verify] v0.2.668 OK`；16 语言检查通过；已安装并重启游戏。
早前日志里已经出现过她真的在打牌的证据：`[Cinematic] play '打击' attacker=观者`（`WATCHER_STRIKE_P` 6 点伤害）。

### 仍等用户确认

「怪物获得 10 层真言也进神格」里的「怪物」指她自己还是别的怪物。

## 第 12 轮（2026-11-15）—— 内部 id 改名 + 地图图标

### 内部 id：WATCHER_BOSS → WATCHER_DIVINITY

用户：「你这怎么直接 WATCHER_BOSS，把神格的英文加进去，类似其它 boss 的内部 id，又不是只会有这一个观者 boss。」

按别的 Boss 的命名规律（<角色>_<设计名>）改成：

| 项 | 旧 | 新 |
|---|---|---|
| BossKeys key | WATCHER_BOSS | **WATCHER_DIVINITY** |
| 遭遇类 | WatcherBossEncounter | WatcherDivinityBossEncounter |
| 调试用普通战斗遭遇 | WatcherBossNormalEncounter | WatcherDivinityBossNormalEncounter |
| 控制台 id | WATCHER_BOSS_ENCOUNTER | **WATCHER_DIVINITY_BOSS_ENCOUNTER** |
| 遭遇本地化键（16 语言） | WATCHER_BOSS_ENCOUNTER.* | WATCHER_DIVINITY_BOSS_ENCOUNTER.* |

怪物模型类仍是 WatcherBoss（⇒ 怪物本地化键仍是 WATCHER_BOSS.name / .title / .devotion / .devotion_dread）——
和愚者那边一样的处理（愚者的 roster key 是 FOOL_TAROT、怪物键是 FOOL_BOSS.*）。

控制台：ight WATCHER_DIVINITY_BOSS_ENCOUNTER

### 地图图标：换成一**代崩坠三层神格观**那张

- 源文件（工作区自带）：`.assets-sts1-downfall\09-界面与地图 UI-Map\downfall-通用\ui\map\icon\watcher_divinity.png`
  + 描边 `.assets-sts1-downfall\05-遗物图 Relics\downfall-通用\ui\map\outline\watcher_divinity_outline.png`
- 拷到 `tools\art\import_proj\CharacterBosses\images\map\watcher_divinity{,_outline}.png` → Godot 导入 → 6 行追加进 `pck_extras.txt`
- 遭遇上新增 `public override string BossNodePath => "res://CharacterBosses/images/map/watcher_divinity";`

验证：PCK 里已含 `res://CharacterBosses/images/map/watcher_divinity.png` / `..._outline.png`（打包 extra 106 个文件）。

107 / 111 均 0 错误 · 16 语言检查通过 · `[verify] v0.2.669 OK` · 已安装。
## 第 13 轮（2026-11-15）—— 遗物订正 + 前两回合补「奇迹（不出）」

用户三条：

1. **初始遗物是「至纯之水」不是「圣水」** ⇒ `WatcherScript.Relics()` 把 `WatcherCards.T("HolyWater")` 换成 `WatcherCards.T("PureWater")`
   （观者 zhs relics.json：`圣水 = HOLY_WATER`、`至纯之水 = PURE_WATER`；dll 里是 `WatcherMod.PureWater`）。
2. **第 1、2 回合末尾各补一张「奇迹（不出）」** ⇒ `W(WatcherCards.Miracle, holdInstead: true)`；
   `ScriptCard.HoldInstead` 的口径是「**即使有费也不出**，留在手里、下回合照常按剧本出」（规划期跳过：不耗能、不出牌、不产生意图）。
   `W()` 加了 `holdInstead` 参数。
3. **拦掉原版的抽牌印牌** ⇒ `BossDefs.Watcher.BlocksVanillaDraw = true` 本来就有（走 `ShadowBossNoDrawPatch`，
   和塔罗愚同一条通路），这次确认在位上。

107 / 111 均 0 错误 · `[verify] v0.2.670 OK` · 已安装。
## 第 14 轮（2026-11-15）—— 至纯之水印牌 / 说明框上移 / 布质果实血量

用户三条（截图实测反馈）：

1. **拦掉「至纯之水」开局印的那张奇迹**（剧本里已经有奇迹了）
   ⇒ `WatcherRuntimePatches` 加第三处运行期补丁：`WatcherMod.PureWater.BeforeCombatStart` 的 prefix，
   只在 `BossRelicHookBridge.IsOurBossRelicPublic(relic)` 成立（= 她身上那件）时短路并返回 `Task.CompletedTask`。
   观者原版玩家、其它 Boss 的至纯之水照旧印牌。
2. **说明框太长了，往上移**（只动神格观）
   ⇒ `BossSpecialTip.UsesLongSpecialText` 里加 `boss.Def.Key == "WATCHER_DIVINITY"` ⇒
   复用炼药猎那条偏移 `BossUiTuning.BrewSpecialY`（默认 **−120**，`charbosses_ui.txt` 里 `brewSpecialY` 可热调）。
   **其它 Boss 的说明框位置一个像素都没动。**
3. **最大生命值没算布质果实**
   ⇒ 反编译 `LoomingFruit`：`CanonicalVars = MaxHpVar(31)`，靠 `AfterObtained` → `CreatureCmd.GainMaxHp`。
   影子玩家身上那件收不到这个钩子 ⇒ 照「根除骨 (66 → 97 = 66 + 31)」的先例**直接写进血量**：
   `Hp = { 103, 103, 103, 103 }`（72 + 31）。

107 / 111 均 0 错误 · `[verify] v0.2.671 OK` · 已安装。
## 第 15 轮（2026-11-15）—— 用户实机七条

| # | 用户反馈 | 处理 |
|---|---|---|
| 1 | 灯笼第一回合的加费没算 ⇒ 许愿不会被优先打出 | `CharBossMonster.PrepareTurn` 新增「观者加费」：`TurnNumber <= 1 && HasRelic("LANTERN")` ⇒ +1（原版挂 `AfterSideTurnStart`，影子玩家收不到） |
| 2 | 「打出卡牌并没有让她**真的**获得真言，而是积累特殊效果的层数。这个特殊效果应该是没有层数的」 | `BossDevotionPower` 重写：`StackType.Single`（**不再显示层数**）；出牌 ⇒ `WatcherCards.GainMantraAsync`（观者自己的 `WatcherMod.Mantra` power = 界面上的真言图标）；她的回合开始 ⇒ 把那个 power 层数 −5（恐惧 −3）并**钳到 0** |
| 3 | 进神格后「有费就打」的牌没打出去 | `WatchDivinity`：观察到「她这一下真的进了神格」⇒ +力量（3/4）、记本回合 +3 能量、`RefreshPlannedIntents` + `BossHandUi.RefreshForce` ⇒ 预算变大后下一张就打得出去 |
| 4 | 第一回合手里仍有两张奇迹（至纯之水没拦住） | 按用户口径：**第 1 回合剧本里那张奇迹去掉**（至纯之水自己印的那张留着） |
| 5 | 全难度升级许愿 | 第 1 回合 `W(WatcherCards.Wish, upgraded: true)` |
| 6 | 费用口径怪 | 第 1 回合 3（2 + 灯笼）、之后每回合 2、**每进一次神格当回合 +3**（`TakePendingDivinityEnergy` 在 `PrepareTurn` 里兑现并清零） |
| 7 | 打聚能成刃要展示生成的灭除之刃（像羽化那样，段数要对） | `PlayOne` 打完牌后：`IsConjureBlade` 且手里有 `WATCHER_EXPUNGER` ⇒ `BossCardPreview.ShowAsync`（原版预览界面）。牌是观者模组按当时 X **现场造的实体**（`WithHitCount(X)` 写在牌上）⇒ 段数天然正确 |

新增 `src/Hooks/Monsters/CharBossMonster.Watcher.cs`（partial）：`IsConjureBlade` / `GeneratedExpungers` / `SurpriseWatcherConjureShown` / `WatcherGeneratedCards`。

107 / 111 均 0 错误 · `[verify] v0.2.672 OK` · 已安装。