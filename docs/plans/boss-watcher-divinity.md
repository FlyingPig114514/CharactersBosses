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
## 第 16 轮（2026-11-15）

1. **进神格后「有费就打」的牌没亮也没打**：她的回合预算在 `PrepareTurn` 里就写进 `pcs.Energy` 了，而神格是**玩家回合**里进的 ⇒ 计划按旧预算排。现在 `WatchDivinity` 立刻把那 3 点加到当前 `pcs.Energy` 再 `RefreshPlannedIntents`（`_pendingEnergy` 仍留给「她回合还没开始」的情况）。
2. **说明框文案**：去掉特殊效果正文的首行白色「虔信」（上方黄色 buff 名已表达）；神格说明里三个能量图标后面的「能量」删掉。16 语言 ×（monsters 2 键 + powers 2 键）共 64 条一起改，`check-localization` 全绿。

107 / 111 均 0 错误 · `[verify] v0.2.673 OK` · 已安装。

## 第 17 轮（2026-11-15）

1. **第 2 回合默认 4 费**：`TurnNumber` 是 0 起，`TurnNumber <= 1` 在第 2 回合也成立 ⇒ 灯笼那 1 点又被算一遍。改成一次性标志 `_watcherLanternGiven`（给过就不再给）。
2. **进神格后打击/警惕不打**：原来「回合中又拿到资源 ⇒ 按剧本补打」这条门禁只给灵魂骨和三层第 3 回合开，观者进神格多出来的是**同回合**的能量，同样需要补打 ⇒ 门禁加上 `BossDevotionPower`（`#if STS2_111`）。

107 / 111 均 0 错误 · `[verify] v0.2.674 OK` · 已安装。

## 第 18 轮（2026-11-15）

1. **削弱：布质果实 → 空鸟笼**（`LoomingFruit` → `EmptyCage`）。用户指出果实的 +31 最大生命本来就没生效 ⇒ 血量不变（`Hp` 仍是 103）。
2. **补打之后刷新意图**：原来「回合中补打」这条通道只打牌、不重排意图 ⇒ 玩家看到的意图还是旧的（用户：「意图没有刷新她要打额外的牌」）。现在补打循环结束后 `RefreshPlannedIntents()` + `BossHandUi.RefreshForce`。

107 / 111 均 0 错误 · `[verify] v0.2.675 OK` · 已安装。

## 第 19 轮（2026-11-15）—— 联机不同步（玩家反馈，血肉骨）

日志 `mod_log (26).txt`（v0.2.667，Host）：
```n[22:23:12] 吞噬生命钩子：power=VULNERABLE_POWER amount=1 目标=亡灵契约师 … 钩子施加者=infrared
[22:23:12] 吞噬生命钩子：power=AWAKENED-MANABURN_POWER amount=8 目标=亡灵契约师 …
… ⚠ 不同步状态摘要（检测到不同步）：轮次 2
```

1. **根因**：`BossDevourLifePower.AfterPowerAmountChanged` 在每台机器上都会跑，而它在里面**直接改战斗状态**（`CreatureCmd.GainBlock` + 补血肉戏法伤害）⇒ 两端各算一遍、次序还可能不同 ⇒ Boss 格挡 / 玩家血量分叉。
   **修法**：在真正结算之前加门禁 —— `if (!NetRole.IsHostOrSolo) { 记一行日志; return; }`，只让房主（或单人）结算，客人只记录。
2. **顺带修**：日志里另一条独立问题 `勾选同步：广播失败（客人将退回整张候补表）: The given key 'CharacterBosses.Core.BossPicksMessage' was not present in the dictionary` —— 反编译 `BaseLib.Abstracts.CustomMessageWrapper`：它用 `_customMessages ??= ReflectionHelper.GetSubtypesInMods<ICustomMessage>()` **只扫一次并缓存**，我们这套「外壳 + Impl107/111 两份 DLL」的实现在扫描时还没被登记 ⇒ `CustomMessageToId` 里没有我们的消息 ⇒ `Send` 抛 KeyNotFound ⇒ 房主勾选广播失败（阵容候选两端就有机会不一致）。
   新增 `Core/BaseLibMessageRegistration.cs`（纯反射、不引 BaseLib）：发消息前清掉 `_customMessages` 并重跑公开的 `Initialize()`；BaseLib 的 id 分配是「按 FullName 排序 + 哈希避让」**确定性**的 ⇒ 同一个模组集合的两端重算结果一致，不会分叉。接在 `BossPicksSync.BroadcastIfHost` 的 Send 之前。

107 / 111 均 0 错误 · 16 语言通过 · `[verify] v0.2.676 OK` · 已安装（需两端同版本再测）。

## 第 20 轮（2026-11-15）—— 同时兼容两个观者模组

用户确认：两份观者模组「除了多人牌和先古牌以外大部分牌都是相同的（都从 1 代搬过来）」⇒ 只用**重叠**的牌就能同时适配。

`WatcherCards` 重构成 provider 结构：

| | A = 3747526116（Boninall，**优先**） | B = 3747492505（lamali） |
|---|---|---|
| 卡牌 | `WatcherMod.WatcherStrike_P` | `Watcher.Code.Cards.Basic.StrikeWatcher` |
| 模型 id | `WATCHER_STRIKE_P` | `WATCHER-STRIKE_WATCHER` |
| 姿态 | `WatcherCombatHelper.{EnterDivinity,ExitStance}`（只收 Player） | `StanceCmd.{EnterDivinity,ExitStance}`（多一个 ctx） |
| 真言 | `Mantra` | `MantraPower` |
| 神格 | `Divinity`（回合结束退） | `DivinityStance`（回合开始退） |
| 至纯之水 | `BeforeCombatStart` | `BeforeHandDraw` |

- 卡牌解析三级：**A 类名 → B 类名 → 模型 id**（`ModelDb.AllCards` 按 `Id.Entry` 查），任一命中即可；
- 剧本那 20 张牌**全部在两版里都有**（用户核对过），少量数值差异（许愿「永生不死」给覆甲/多重护甲、停顿升级的额外格挡）按用户说法无伤大雅；
- 姿态/真言/神格 全部按类型名双认（`Mantra`|`MantraPower`、`Divinity`|`DivinityStance`）；
- 「进了神格不再退出」同时压 A 的 `Divinity.AfterSideTurnEnd` 与 B 的 `DivinityStance.BeforeSideTurnStart`；
- 「至纯之水不印奇迹」同时压 `BeforeCombatStart` 与 `BeforeHandDraw`；
- 许愿三选一的固定只对 A 生效（B 没有 `ChooseOne` 入口）⇒ 会打一行日志说明，沿用 B 自己的选法；
- **两个都装时优先 3747526116**（`Probe()` 里先认 A 的招牌类型 `WatcherMod.WatcherCombatHelper`），日志会打 `观者引用层：使用 3747526116（Boninall）（两个观者模组都在 ⇒ 优先 3747526116）`。

107 / 111 均 0 错误 · `[verify] v0.2.677 OK` · 已安装。

## 第 21 轮（2026-11-15）

1. **至纯之水两版都不拦**（用户：A 拦不住、B 拦住了，干脆统一放开）⇒ 删掉 `InstallPureWaterBlock` 整个方法（含调用），改为**第 1 回合剧本放一张「奇迹（不出）」**（`holdInstead: true`）。
2. **lamali 版的许愿也固定选「永生不死」**：反编译 `Watcher.Code.Cards.Rare.WishWatcher` —— 它走**本体**的 `CardSelectCmd.FromChooseACardScreen(ctx, options, owner, false)`，候选项是 `BecomeAlmighty` / `LiveForever`（永生不死）/ `FameAndFortune`。新增编译期补丁 `Patches/WatcherWishLowLevelChoicePatch`：候选项里出现 `LiveForever`（或 Boninall 的 `WatcherWishLiveForever`）且 owner 是我们的影子玩家 ⇒ 直接返回它；其它牌不动。
3. **削弱**：进神格给的力量 **3 → 2**，恐惧 **4 → 3**（`BossDevotionPower.StrengthOnDivinity()`），16 语言的 4 个键同步改数字。
   ⚠ 改文案时踩了个坑：日/韩/泰三种语言的数字在 `[gold]力量[/gold]` **标记之后**，第一次按「标记前 24 字符找数字」的批量替换把「真言 10 层」误改成了 2/3 —— 已逐语言修回（真言仍是 10，力量 2/3），并复查其余 13 种语言无误。

107 / 111 均 0 错误 · 16 语言通过 · `[verify] v0.2.678 OK` · 已安装。

## 第 22 轮（2026-11-15）—— 用户六条

| # | 反馈 | 根因 / 处理 |
|---|---|---|
| 1 | 第 1 回合的奇迹都去掉 | 剧本里那张删掉（至纯之水印的那张按上一轮口径保留） |
| 2 | 灯笼加费重复计算 | 原版灯笼遗物钩子**自己会到账**，我又手动 +1 ⇒ 去掉手动那笔 |
| 3 | 进神格后意图预览仍不重算 | 补打的牌以前**不进 Plan** ⇒ RefreshPlannedIntents 只是拿旧 Plan 重算。现在补打前先把它们并进 Plan |
| 4 | 聚能成刃没展示灭除之刃 | id 匹配只认下划线形式，B 版是 WATCHER-EXPUNGER ⇒ 两套 id 都认 |
| 5 | 灭除之刃段数是 1 而非 3/6/9 | 同上（WATCHER-CONJURE_BLADE / WATCHER-EXPUNGER 匹配不到 ⇒ conjuredX 从没记上） |
| 6 | 第 5 回合不出虔信、第 6 回合不出圣洁；**后面回合只有 1 费** | 能量定义写 2，而**涅奥的祝福每回合 −1** ⇒ 实际只剩 **1**。改成定义 **3**（实际 2，第 1 回合灯笼 +1 ⇒ 3） |

107 / 111 均 0 错误 · `[verify] v0.2.679 OK` · 已安装。

## 第 23 轮（2026-11-16）—— 用户五条 + 平静退出（全部实机验收）

> 详细取证与反编译结论见 `docs/plans/watcher-divinity-fixes-2026-11-16.md`。

| # | 反馈 | 根因（反编译实证） | 处理 |
|---|---|---|---|
| A | 能量全错：第 2 回合 5 费、第 4 回合超；「你别给她补，用她原版的加费就行」 | 我们**补了两笔**：`BossDevotionPower` 进神格立刻 `pcs.Energy += 3`，`_pendingEnergy` 再在下回合预算里 +3；而观者 `Divinity.<AfterApplied>d__8` 里本来就是 `PlayerCmd.GainEnergy(3m, Owner.Player)`（Owner = 影子玩家） | 两笔全删（连同 `TakePendingDivinityEnergy`、`_watcherLanternGiven` 与 `PrepareTurn` 里那块 `#if STS2_111` 观测者加费）；`PrepareTurn` 对观者**不再手动加灯笼**（`Relics.Lantern.AfterSideTurnStart` 真给她 +1，实测 her turn 1 = 5 → Wish 后 6）；「奇迹」也不再走通用「回能」补偿（`WatcherMiracle.OnPlay` 自己 `GainEnergy(1)`） |
| B | 进神格后意图预览不重算 | `RefreshPlannedIntents` 只拿**旧 Plan** 重算，而计划是玩家回合开始前按 2 费排的 | 新增 `CharBossMonster.ReplanForExtraEnergy(why)`：按此刻真实能量**整块重跑剧本规划** → `BladesGrowth.RecomputeAll` → `PublishVanillaIntents` → `BossHandUi.RefreshForce` |
| C | 打出聚能成刃不展示灭除之刃 | 生成的 token 牌进的是 **`(PileType)1` = 抽牌堆**，而 `GeneratedExpungers()` 只扫 `pcs.Hand` | 改扫 `pcs.AllPiles` 并把「在哪个堆」打进日志；预览副本额外抄 `Repeat`（`BossCardPreview` 是 `ModelDb.GetById + CreateCard` 造的原版新牌，段数会掉回 1） |
| D | 灭除之刃段数恒为 1（应 3/6/9…） | 第 5/9 回合那张是 `BossScriptRunner.BuildHand` **按类型新造**的（`Repeat` 默认 1），带 X 的实体躺在抽牌堆里 | 打完聚能成刃时读实体牌的 `Repeat`（= 实付 X，升级 +1）存进 `WatcherExpungerHits`；规划前由 `ApplyWatcherConjuredHits()` 写进手牌那张的 `Repeat` ⇒ 意图（`BossDamagePredictor.Hits`）与真实结算（牌自己的 `WithHitCount(Repeat)`）同源 |
| E | 两个观者模组卡 id 不同（`WATCHER_X` / `WATCHER-X`） | — | `CharBossMonster.WatcherIdIs(entry, suffix)`：去掉 `-`/`_` 再比后缀，所有运行期按 id 比的地方统一改用它 |
| 6 | 「第四回合的欺瞒现实和平安即使没进神格也被打了」 | **不是多给能量**：`化拳为空` 自己写着「退出当前姿态」，而观者原版规则里**离开平静 = +2 能量**（用户已自行实机确认） | 行为本来就对，但**规划期没算这笔** ⇒ 面板先把两张画成压暗、回合里又补打出去。`BossScriptPlanner` 新增「观者姿态」记账：警惕 ⇒ 进平静、化拳为空且当前在平静 ⇒ `energy += 2`（`WatcherCards.InCalm` 双版认 `Calm|CalmStance`） |

### 实机日志证据（v0.2.680，`mod_log.txt`）

```
WATCHER_DIVINITY turn 1: hand=3 energy=2/2      ← 预算 2（定义 3 − 涅奥 1），灯笼由原版钩子另给 1
虔信：她进入神格 ⇒ +2 力量；能量**不补**（原版 Divinity.AfterApplied 自己 GainEnergy(3)）⇒ 此刻能量 5
进神格多 3 点能量 ⇒ 按当前能量 5 整块重规划第 1 回合：2 张 [WATCHER_WISH_P,WATCHER_STRIKE_P]
虔信：她回合开始时的实际能量 = 5              ← 我们的监听器比遗物钩子先跑，所以这里还是 5
done WATCHER_WISH_P: energy=6                 ← 灯笼的 +1 在这之后到账（Lantern.AfterSideTurnStart）
done WATCHER_STRIKE_P / VIGILANCE: energy=6   ← Wish3+打击1+警惕2 = 6，正好花光
WATCHER_DIVINITY turn 2/3/4/5: energy=2/2     ← 不再有 5 费
聚能成刃：生成的灭除之刃在「Draw」(1 张)，段数(Repeat)=3 ⇒ 之后剧本里的灭除之刃都按 3 段
聚能成刃：用原版预览界面展示 1 张灭除之刃（段数 3）
灭除之刃：剧本那张的段数 1 → 3（按她上次聚能成刃实付的 X）
意图 WATCHER_EXPUNGER: [LiveMultiAttackIntent]
意图伤害管道 WATCHER_EXPUNGER: 牌面 9 → 11
效果追踪 HP Ironclad −11 ×3                   ← 真实伤害 3 段（33 点），与预览 11×3 一致
观者姿态：WATCHER_EMPTY_FIST 退出「平静」⇒ 原版 +2 能量（规划期算入，之后预算 3）
剧本第 16 回合: 打 [EMPTY_FIST,WAVE_OF_THE_HAND,DECEIVE_REALITY,SAFETY] 余费=0   ← 手牌栏「4 张全部会打」
```

107 / 111 均 0 错误 · 16 语言检查通过 · `[verify] 两份实现的版本串都是 v0.2.680  OK` · 已安装。

## 第 24 轮（2026-11-16）—— 用户第二轮实测（三条 + 光辉/平安两条）

> 取证与反编译结论见 `docs/plans/watcher-divinity-fixes-2026-11-16.md` §7。

| # | 反馈（用户原话） | 根因（反编译实证） | 处理 |
|---|---|---|---|
| 1 | 「第一回合灯笼的加费没有被提前计算，导致如果不让她进神格，她会显示只打出打击，然后实际上她会打出打击和警惕（应该只打出许愿）」 | `Relics.Lantern.AfterSideTurnStart` 的 +1 是在**她回合开始之后**到账的，而计划在**玩家回合**就排死了（那时 `pcs.Energy` 只有 2）⇒ 面板只有「打击」，回合里钱到了又被补打通道打出「警惕」 | v0.2.680 我把预算里那 +1 删了（靠原版给）——那是错的。现在**预算里加**（规划期就看到 3 费 ⇒ 正确计划 = 只打许愿），并用 `BossRelicSuppressLanternForWatcherPatch` 把原版钩子**只对观者**压掉，不双份 |
| 2 | 「第二回合…她进神阁应该变成五费，但实际上显示变成了七费」 | `Calm.AfterRemoved` = `GainEnergy(2)`；进神格时 `ChangeStance` 先 `RemoveAllStances` ⇒ 2（预算）+ 2（平静）+ 3（神格）= 7 | 我们自己触发「退姿态→进神格」期间用 `WatcherRuntimePatches.SuppressCalmExitEnergy` 压掉那 2 点（她「化拳为空」正常退平静照旧 +2，用户已确认）⇒ 2 → **5** |
| 3 | 「她获得 10 层真言会先退出当前姿态再进神格…但现在再进神格的力量和加费都没有」 | 满 10 层是**观者模组自己**触发的：`Mantra.AfterPowerAmountChangedCompat` → `EnterDivinity` → `ChangeStance<Divinity>` 见到「已经在神格里」就原样返回 ⇒ 不退姿态、没有那 3 点能量、也没有力量 | 压掉模组那条自动触发（`InstallMantraTakeover`），改由 `BossDevotionPower.ResolveDivinityAsync` 按用户口径走：扣 10 层 → **先退出姿态** → +力量（普通 2 / 恐惧 3）→ **再进神格**（原版自己 +3） |
| 4 | 「光辉的伤害预览似乎没有计算虔信、手摇鼓和她特殊效果…导致的她在回合开始时的真言变化」 | `WatcherBrilliance.OnPlay` = `Damage.BaseValue + GetTotalMantraGained(owner)`，后者 = `WatcherStatePower.TotalMantraGainedThisCombat`（累计获得，扣真言不减它）；「手摇鼓」= `Damaru.AfterPlayerTurnStart` +1（我们替她广播，发生在**她回合开始**） | `BossIntentBuilder` 对 `WATCHER_BRILLIANCE` 加「累计真言 + 回合开始手摇鼓 pending 1」；玩家本回合每打牌 +1 由意图重算实时跟上；⚠ 只对 A 版加（B 版把真言做进牌面的计算型伤害里） |
| 5 | 「平安不知道为什么会被打两次（是你没有拦住原版欺瞒现实的印牌吗？）」 | 反编译 `WatcherDeceiveReality.OnPlay`：`GainBlock(4)` + `AddGeneratedCardToCombat(CreateCard<WatcherSafety>(), (PileType)2 = 手牌)` ⇒ 手牌里多一张「平安」，补打重规划按类型又匹配一次 | 把 `WATCHER_DECEIVE_REALITY` / `WATCHER-DECEIVE_REALITY` 加进 `CharBossMonster.BlocksGeneratedCardsEntries`（拦 token，它自己的 4 点格挡照旧） |

另：用户要求「不再让第一场战斗变为神格观」⇒ 已删除游戏目录里的 `charbosses_debug.txt`
（那张调试开关本来也只是把普通战斗换成角色 Boss；用户当时其实是用控制台 `fight` 进的战斗）。

107 / 111 均 0 错误 · 16 语言通过 · `[verify] 两份实现的版本串都是 v0.2.682  OK` · 已安装。

## 第 25 轮（2026-11-16 傍晚）—— 用户两条订正

| # | 用户原话 | 处理 |
|---|---|---|
| 1 | 「哦那个第二回合我忘记算退平静了，不是bug，**让那2费正常加**」 | 撤掉 v0.2.682 里「进神格时压掉离开平静 +2」的补丁（`WatcherRuntimePatches.InstallCalmEnergySuppress` / `SuppressCalmExitEnergy` 全删）。离开平静的 +2 是她的原版机制（`Calm.AfterRemoved` = `GainEnergy(2)`；B 版 `CalmStance.OnExitStance`）⇒ 我们触发的退姿态与「化拳为空」都照常给：**从平静进神格当回合共 +5（2 平静 + 3 神格），不在平静就只有 +3** |
| 2 | 「修改进阶8，不再升级摆手，而是升级**警惕、旋身和欺瞒现实**」 | `WatcherScript`：摆手恢复未升级；警惕（第 1 / 8 回合）、欺瞒现实（第 4 / 11 回合）、旋身（第 5 / 9 回合）都加 `upgradeMinAscension: 8`（`BossScriptRunner.BuildHand` 会按 `UpgradeMinAscension` 造升级版手牌） |

107 / 111 均 0 错误 · 16 语言通过 · `[verify] 两份实现的版本串都是 v0.2.683  OK` · 已安装。

---

# 三层 2 号 · 观者 Boss「Ω观」（用户 2026-11-16 设计，v0.2.684 首版已落地）

> 需求原文（用户逐字）：见本文件末尾「第 26 轮」。实现细节与踩坑：`docs/plans/watcher-omega.md`。

## 第 26 轮（2026-11-16）—— Ω观首版 + 设置顺序 + 能力面板 + 藏品

| 项 | 落地位置 |
|---|---|
| 内部 id / 编号 | `BossKeys`：`new("WATCHER_OMEGA", 6, 2, 2, typeof(WatcherOmegaBoss))`（三层 2 号，角色 6 = 观者）；`DesignNames["WATCHER_OMEGA"] = "Ω观"` |
| 怪物类 | `Monsters/CharacterBosses.cs`：`WatcherOmegaBoss : WatcherBoss`（`SpawnVariantKey` + `PinnedVariantKey` = `WATCHER_OMEGA`，`TierAct = 2`） |
| 定义 | **不新开 BossDefinition**，挂 `BossDefs.Watcher.ExtraVariants[2]`（剧本/遗物/特殊效果/`HpBonus = -31` ⇒ 72 血）；`WatcherOmegaActive()` 在 `BossLineups` 里按遭遇判定 |
| 遭遇 | `WatcherOmegaBossEncounter`（`TierAct = 2`）+ `WatcherOmegaBossNormalEncounter`；控制台 **`fight WATCHER_OMEGA_BOSS_ENCOUNTER`**；已登记 `TierEncounters` / `DebugEncounters` |
| 剧本 | `Core/WatcherOmegaScript.cs`：12 回合 + **9～12 循环**；逐回合写死能量 3/2/4/5/2/2/3/4/2/2/4/3（按用户列的牌 + 他标的「（没费）」反推，见文件头注释） |
| 进阶 | 8 = 升级**如水 / 心灵堡垒**；9 = 只升级**第 6 / 11 回合的点穴**（都写在剧本里，`UpgradeMinAscension`） |
| 遗物 | 至纯之水（观者模组）/ 涅奥的祝福 / 永冻冰晶 / 空鸟笼 / 舵盘 / 钗 / 金刚杵 |
| 特殊效果「斋戒」 | `Powers/WatcherFastingPower.cs` + `Core/WatcherOmegaMechanics.cs`：① 欧米伽隔一回合触发（压掉 `OmegaPower.AfterSideTurnEnd`，只在奇数回合结算）；② 点穴伤害可被格挡（整条接管 `PressurePoints.OnPlay`，`ValueProp` 从含 Unblockable 换成 `Unpowered`） |
| 生成牌展示 | `WatcherOmegaMechanics.NoteGeneratedAsync`：打完阿尔法/贝塔 ⇒ 从她牌堆里找出贝塔/欧米伽，走原版预览界面（与灭除之刃同一套） |
| 不惧妖邪 buff | `Powers/WatcherFearNoEvilAimPower.cs`（图标 = 1 代崩坠 `WatcherFearNoEvil84.png` → `res://CharacterBosses/images/powers/watcher_fear_no_evil.png`）；规划期挂上、被锁玩家打出攻击牌 ⇒ 她那记不惧妖邪进「平静」（`WatcherOmegaMechanics.FearNoEvilPostfix`） |
| 意图 | `BossIntentBuilder`：**点穴 = 攻势（目标印记 + 本张印记）+ 策略**；**欧米伽 = 炸弹式攻势**（只在触发回合画） |
| 设置界面顺序 | `Config/BossConfigModConfig.cs` 的 `PickColumns = { 1,2,3,4,0,5,6 }` ⇒ 猎机骨储战 → **愚者** → **观者**（同列内：神格观在上、Ω观在下） |
| 能力面板 | `BossInfoTable`：`WATCHER_OMEGA`（循环 9～12 / 单体极强 / 群攻极强 / 单体高压 5、7、8～12 / 群体高压 5、7、9、11 / 单体）与 `WATCHER_DIVINITY`（7～11 / 极强 / 强 / 2、5、7、9 / 6、10 / 单体） |
| 藏品 | `DownfallCollectibleCompat`：`case "WATCHER_OMEGA"` → **阿尔法** |
| 顺带修的缺口 | 观者遭遇的角色映射 `WATCHER` 前缀 → 6（以前 -1）；`WATCHER_BOSS` → 6；`SubstituteIfUnavailable` 认所有 `WATCHER_*`（关掉观者模组会被换成塔罗愚，不再坏档）；`DebugEncounters` 补第 7 项（以前写 `watcher` 越界 ⇒ 退化成**易伤战**，用户实测过）；`WatcherDivinity` ↔ `WatcherOmega` 互为 `ComplementVariant` |
| 本地化 | 16 语言 × 10 个新键（`WATCHER_OMEGA_BOSS.*` / `WATCHER_OMEGA_BOSS_ENCOUNTER.*` / `WATCHER_FASTING_POWER.*` / `WATCHER_FEAR_NO_EVIL_AIM_POWER.*`），`check-localization` 全绿 |

**还没做（下一步）**：
1. 全部「带『为你而死』的召唤物不会替你承受该伤害」的警告改成同时写「**瓦皮的忠诚**」（34 个键 × 16 语言；107 是否要单独一版待定 —— 文案表是两版共用的）；
2. **灾厄骨**：灾厄牌额外一条等于灾厄层数的攻势意图（无处可逃累计本回合其它灾厄、荒疫打击吃力量/虚弱、其余不吃），并确认薄荷尖塔不会双算；
3. **实机验证** 上面这一整轮（本会话只做到「107/111 编译 0 错误 + 16 语言通过 + 装机 OK」）。

## 第 27 轮（2026-11-16）—— 灾厄骨灾厄意图 + 16 语言警告文案（v0.2.685）

| 项 | 落地 |
|---|---|
| 灾厄牌额外攻势意图 | `BossIntentBuilder.IntentsFor` 里、`intents.Count == 0` 兜底**之前**新增一段：死亡使者（群体，全玩家可见）/ 鞭打（单体，只有被锁定玩家看真实数字，其他人 0）/ 无处可逃（单体，`10/15 + 5 × floor((目标已有灾厄 + 本回合其它灾厄牌) / 阈值)`，`DoomFromOtherPlannedCards` 按 `boss.Plan` 累计）；三者都走 `RawAttackIntent`（不吃力量/虚弱/易伤）。**荒疫打击跳过** —— 它的灾厄 = 它自己的伤害，已有攻势意图就是那个数，再画一条会让薄荷尖塔双算（日志会说明）。薄荷尖塔方面**不需要额外适配**：意图进了 `PlannedPlay.Intents` 后由 `PublishVanillaIntents` 写进 `NextMove.Intents`，它自然算得到；**不可**在 `PendingSourcelessDamage` 再补。 |
| 警告文案加「瓦皮的忠诚」 | 16 语言全部处理：zhs/zht/eng 先做「和」的合并写法（zhs 30 + zht 24 + eng 28 处，含 `[gold]` 标记形态），其余语言按各自已有的译名做合并，剩下的（多为**未翻译的英文条目**和措辞变体）统一追加一句「带瓦皮的忠诚的召唤物也不会替你承受」并**保持换行数不变**（`check-localization` 全绿）。107 与 111 共用同一份文案 ⇒ 107 里只是多提一件它没有的遗物，不影响机制。 |

顺带修的既有缺口（同批）：观者遭遇的角色映射 `WATCHER*` → 6、`WATCHER_BOSS` → 6、`SubstituteIfUnavailable` 认所有 `WATCHER_*`、`DebugEncounters` 补观者那一项（以前写 `watcher` 越界 ⇒ 第一场退化成一层的**易伤战**）、`WatcherDivinity ↔ WatcherOmega` 的 `ComplementVariant`、设置界面 `PickColumns = {1,2,3,4,0,5,6}`、两张能力面板、藏品 Ω观 = 阿尔法。

**仍待实机验证**（本轮只做到编译/本地化/装机）：Ω观整场（剧本 12 回合 + 9～12 循环、斋戒的欧米伽隔回合、点穴可格挡、不惧妖邪 buff、点穴/欧米伽意图、阿尔法与贝塔的展示）与灾厄骨的灾厄意图（死亡使者 21、鞭打 13、无处可逃累计、荒疫打击不重复）。

## 第 28 轮（2026-11-16）—— 姿态倍率改在「过完伤害管道之后」补差 + 欧米伽意图挪到回合结束那一排（v0.2.695）

### ① 用户：「愤怒是伤害*2 不是额外加一个基础伤害」

v0.2.691 的做法是「把**牌面基础值**乘上 `计划倍率 ÷ 此刻倍率`，再把结果交给意图」。
错在**乘的位置**：意图渲染时会走原版伤害管道 `Hook.ModifyDamage`，而姿态倍率本身就是管道里的
一个 `IModifyDamageMultiplicative`（反编译 `Watcher.Code.Stances.WrathStance.ModifyDamageMultiplicativeCompability`：
`dealer == Owner.Creature ⇒ return 2m + WatcherHook.ModifyWrathDamage(...)`，并且 `props` 带 `(ValueProp)4` 时返回 1）。

于是「牌面 6 + 力量 8、愤怒里」= `6×2 + 8 = 20`，而真实是 `(6+8)×2 = 28` —— 力量/虚弱/易伤那一段没跟着翻倍，
玩家看到的就不是「牌面 ×2」。

修法：`BossIntentBuilder` **不再改数字**，只登记「这张牌打出时的姿态倍率」
（`BossDamagePredictor.SetPlannedStance(card, stanceMul)`，键是卡实例）；
真正的补差在 `FinalDamageFor` 里、管道算完**之后**做：`结果 × 计划倍率 ÷ 此刻倍率`，
且只在 ① dealer 就是出牌的人（她本人，奥斯提 / 召唤物不吃她的姿态）、② 计划倍率 > 此刻倍率 时才动手
（她此刻已经在愤怒 ⇒ 管道已经乘过，绝不双份）。日志：`姿态倍率（渲染时补差）：… 管道值 6 × 2 ÷ 此刻 1 ⇒ 12`。

顺带删掉重复的 `StanceDamageMultiplier`（与 `StanceMultiplierOf` 完全一样）。

### ② 用户：「欧米伽的意图应当像炸弹一样在会触发的回合显示在最后面」

- 牌下那条欧米伽意图**整段删掉**（`BossIntentBuilder` 里原来那段已移除）；
- 改到 `BossHandPanel.Extra` 的**回合结束那一排**（和冰雹风暴 / 炸弹 / 球被动同一行）：
  `欧米伽（回合结束，全体）`，只在 `OmegaTriggersOn`（奇数回合 5/7/9/11…）且伤害 > 0 时画。
- `WatcherOmegaMechanics.OmegaDamage` 补了**牌面兜底**：她这一回合才要打出欧米伽（power 还没上身）时，
  退回读她本回合计划里那张欧米伽的 `MagicNumber` —— 否则第 5 回合那一下根本画不出来。
- 因为它只进「我们自己的行」、不进 `NextMove.Intents`，**薄荷尖塔不会双算**，所以没有再往 `PendingOf` 里加。

**验证**：107 / 111 均 0 错误；`[verify] 两份实现的版本串都是 v0.2.695 OK`；已安装到游戏目录
（`Impl107/111.dll.bin` 时间戳刷新）。

## 第 29 轮（2026-11-16）—— 姿态倍率改成「渲染时现推」+ 欧米伽读 `Amount` + 冥想必捞点穴（v0.2.696）

### ① 用户：「第四回合的打击和第八回合的不惧妖邪的伤害预览还是没有翻倍」

v0.2.695 把「计划倍率」登记进字典（`SetPlannedStance`），但**只要有一条重算路**在重新规划剩余手牌时
没带上 `stanceMul`（默认 1），登记就被覆盖回 1 ⇒ 玩家回合里看到的仍是 7。
日志证据：`姿态倍率（渲染时补差）：WATCHER_STRIKE_P 管道值 7 × 2 ÷ 此刻 1 ⇒ 14` 只在**她回合开始那一刻**
出现过，说明补差逻辑本身没问题、是「登记」在半路被抹掉了。

现在**不再存任何东西**：

- `BossIntentBuilder.PlannedStanceAt(她, 这张牌)` = 用「她**此刻**的真实姿态」+ 剩余计划顺序现推
  （暴怒/渐强 ⇒ ×2；安宁/冥想/警惕 ⇒ ×1；化体为空/化拳为空 ⇒ ×1 —— 与剧本规划期同一套规则）；
- `BossDamagePredictor.ApplyPlannedStance` 在管道算完之后补 `值 × 计划 ÷ 此刻`（两个倍率相同就不动手）。
- 依据：`Hook.ModifyDamageInternal` 被观者模组的 `StanceModifyDamageMultiplicativePatch` 转译过，
  stance 模型（`WrathStance : IModifyDamageMultiplicative`，`dealer == Owner.Creature ⇒ 2m`）**就是**管道里的
  一个乘算监听者 ⇒ 管道值里已经含「此刻」那一档，所以只能补差、且必须补在最终值上。

### ② 用户：「欧米伽的伤害预览没有再出现，应该在 7、9、11 等回合也出现」

反编译 `Watcher.Code.Powers.OmegaPower.AfterSideTurnEnd`：
`if (Owner.Side == side && Amount > 0) CreatureCmd.Damage(..., Amount, (ValueProp)4, Owner)`
—— 伤害就是 **power 的 `Amount`**（「欧米伽」这张牌 = `WithPower<OmegaPower>(50, 10)`）。
上一版读的是 `DynamicVars["MagicNumber"]`（那是**点穴**的变量名）⇒ 抛异常后被 `catch { break; }` 吞掉，
只剩「她这回合正好要打欧米伽」的牌面兜底 ⇒ 第 7/9/11 回合整条消失。
现在按 ① `Amount` ② power 上的 `MagicNumber` ③ 计划里那张欧米伽的 `PowerVar<OmegaPower>` / `MagicNumber` 三级取。

### ③ 用户：「更改冥想的捞牌，让它一定会捞到点穴」

`Meditate.OnPlay` 是 `CardSelectCmd.FromCombatPile(弃牌堆)` ⇒ 走我们的 `BossCardSelector`。
给「发起者是冥想」的选择加一条**最高优先**规则：候选（弃牌堆）里有「点穴」就先拿它
（日志 `冥想：按用户要求优先捞「点穴」`；没有则打一行「弃牌堆里没有点穴 ⇒ 退回普通挑牌」）。

**验证**：107 / 111 均 0 错误；`[verify] 两份实现的版本串都是 v0.2.696 OK`；已安装。

另：本机 `C:\Program Files\dotnet\sdk` 消失（只剩 9.x 运行时）⇒ 构建改用项目自带的
`tools\dotnet\dotnet.exe`（10.0.401，`install-mod.bat` 本来就用它）。

## 第 30 轮（2026-11-16）—— 姿态倍率「烤进意图对象」+ 斋戒/神格文案订正（v0.2.697）

### ① 用户截图（打击 6+金刚杵=7、打完暴怒再打它还是 7）：「你改了个寂寞」

前两版都在**渲染时**去找「计划倍率」：

- v0.2.695：按卡实例查字典（`SetPlannedStance`）—— 任何一条重算路没带上 `stanceMul`（默认 1）就把登记抹掉；
- v0.2.696：渲染时按 `boss.Plan` 现推 —— 玩家回合里 `Plan` 可能已经换过牌实例甚至被清掉 ⇒ 一律退回「此刻姿态」。

现在改成**在造意图那一刻**把倍率烤进意图对象：

- `BossIntentBuilder.IntentsFor` 入口 `StanceBake = stanceMul`（`[ThreadStatic]`，退出还原）；
- `LiveSingleAttackIntent` / `LiveMultiAttackIntent` / `LiveDelegateSingleAttackIntent` **构造时**读走存成字段；
- 渲染时只换算一次：`BossDamagePredictor.ScaleToPlannedStance(card, dealer, 管道值, 烤入值)`
  = `管道值 × 计划 ÷ 此刻`，且只在「计划 > 此刻」时动手（她已在愤怒 ⇒ 不动手，绝不双份）；
- `FinalDamageFor` 里那层渲染时补差**已删除**，避免双重。
- 兜底（烤入值为 0 时）：`PlannedStanceAt` 三级来源 —— ① 计划里按**卡实例** ② 计划里按**同一种牌**
  ③ 按**剧本**第 `TurnNumber+1` 回合的顺序推。
- 诊断日志：`剧本姿态：<牌> 造意图时 ×2（此刻她 ×1）`、`姿态倍率（意图层）：<牌> 管道值 7 × 计划 2 ÷ 此刻 1 ⇒ 14`。
- 依据（为什么必须「补在最终值上」）：`Hook.ModifyDamageInternal` 被 `StanceModifyDamageMultiplicativePatch`
  转译过，`WrathStance`（`dealer == Owner.Creature ⇒ 2m`）本身就是管道里的乘算监听者 ⇒ 管道值已含「此刻」那一档。

### ② 文案（用户要求，16 语言全改）

- 斋戒：「（只在 5、7、9、11… 这些回合结束时结算）」整段括号删掉；
- 警告上方那一个空行删掉（`\n\n` → `\n`）；
- 警告下面再加一行：「欧米伽：在你的回合结束时，对所有敌人造成50点伤害。」（各语言给译文）；
- 神格观的神格说明与上方正文之间那个空行也删掉（`BOSS_DEVOTION_POWER.description(_dread)` 与
  `WATCHER_BOSS.devotion(_dread)` 四处）；
- 顺带修掉英文里 `or Vapi Loyalty` 被重复 3 次的拼接 bug（`eng/powers.json`、`eng/monsters.json`）。
- `check-localization` 16 语言全绿。

**验证**：107 / 111 均 0 错误；`[verify] 两份实现的版本串都是 v0.2.697 OK`；已安装
（装机前先关掉了正在跑的游戏，否则 `Impl111.dll.bin` 被占用拷不进去）。

## 第 31 轮（2026-11-16）—— 真凶：`WatcherIdIs` 认不出带 `_P` 的玩家版 id（v0.2.699）

用户第三轮截图（第 4 回合打击 7、第 8 回合不惧妖邪 9）。日志两条铁证：

1. `剧本第 4 回合: 打 [WATCHER_ERUPTION_P, WATCHER_STRIKE_P, WATCHER_VIGILANCE]`
   —— 观者模组还有**第三套 id**：玩家版牌带 `_P` 后缀（`WATCHER_ERUPTION_P` = 暴怒、`WATCHER_STRIKE_P` = 打击）；
2. `意图伤害管道 WATCHER_STRIKE_P: 牌面 6 → 14` —— 她**已经在愤怒**时，原版管道自己就乘了 ×2。

也就是说：姿态补差逻辑是对的，问题在**规划期的姿态推演**从未前进过 ——
`WatcherIdIs("WATCHER_ERUPTION_P", "ERUPTION")` 规范化后是 `WATCHERERUPTIONP.EndsWith("ERUPTION")` = **false**
⇒ 「暴怒 ⇒ 进入愤怒」这一步在剧本推演里永远不命中 ⇒ 它后面的所有牌都按 ×1 烤进意图（显示 7 / 9）。

修法：`WatcherIdIs` 多比一次「去掉末尾单个字母」的形态（`_P` / `_A` / `_B`）。
顺带修的两条（同轮用户要求）：

- **欧米伽的伤害要进薄荷尖塔**：那一笔挂在 power 上、不进 `NextMove.Intents` ⇒
  `MintySpireDamagePreviewCompat.PendingOf` 补 `BossHandPanel.OmegaRowDamage`（与「本回合会不会触发」同一位）。
- **进阶 9 改成升级所有打击**：两张点穴恢复永不升级；第 4/7/9/10 回合的四张「打击」加 `upgradeMinAscension: 9`
  （进阶 8 仍是如水 / 心灵堡垒）。

**验证**：107 / 111 均 0 错误；`[verify] 两份实现的版本串都是 v0.2.699 OK`；已安装。




