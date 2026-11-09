# 华丽猎（SILENT_GRAND_FINALE）六条 bug 修复 — v0.2.525（2026-11-09）

> 承接 `docs/plans/handover-grand-finale-2026-11-09.md`。
> 本轮**一次改完六条、两个目标都编译通过、一次性部署**（用户明确要求：不要一条一发布）。
> 部署目标：`D:\SteamLibrary\steamapps\common\Slay the Spire 2\mods\CharacterBosses`，
> 107 / 111 两份 Impl 均为 **v0.2.525**（字节层 UTF-16 查过：`0.2.525=True`、`0.2.524=False`）。

---

## 零、动手前用户拍板的三件事（唯一权威，别再改）

| 问题 | 用户回答（原话） |
| --- | --- |
| 第 6 回合「必备工具」弃哪张？ | 「我好像漏算了必备工具的弃牌。不过没关系一样。改成这样：**尖啸、打击、生存者（手牌是空的，不弃牌）、防御（回合开始时被必备工具丢弃）**」 |
| 钢铁风暴 / 隐秘匕首的 `x*x`？ | 「确认：**丢几张打几刀，低进阶每刀 4、进阶 10 每刀 6**」 |
| 谋杀伤害？ | 「你就直接按**第四回合打 20，第八回合打 42，之后每回合成长 7**，也就是每个循环成长 21，**第十一回合打 63，第 14 回合打 84**……这个样子」 |

---

## 一、六条修复逐条

### 1. 弹珠袋（`BAG_OF_MARBLES`）

**做了什么**：压掉原版 + 战斗开始补一次 + **清掉 `SkipNextDurationTick`**。

**本次用日志 + 反编译钉死的根因**（这条上过去踩了三种错做法，这次是证据链）：

1. 实测日志（同一次战斗）：
   ```
   弹珠袋：施加后 观者 的易伤层数 = 1（遗物=BAG_OF_MARBLES）
   玩家侧快照（她第 1 回合出手前）：… powers=[VULNERABLE_POWER:2]   ← 原版又加了一层
   玩家侧快照（她第 2 回合出手前）：… powers=[VULNERABLE_POWER:2]   ← 完全没衰减
   ```
2. 为什么是 2 层：反编译 `BagOfMarbles.BeforeSideTurnStart`：
   `participants.Contains(Owner.Creature) && Owner.PlayerCombatState.TurnNumber <= 1`
   → 对 `HittableEnemies` 施加易伤。判据是「可命中敌人」而不是阵营，
   她把影子玩家的遗物也算监听者 ⇒ 我们自己补的那一次之后，她第 1 回合开始又触发一次。
3. 为什么「持续三回合」：反编译 `PowerCmd.Apply`（`<Apply>d__2`）里有**这一句**：
   ```csharp
   if (target.Side == CombatSide.Player && power.Type == PowerType.Debuff)
       power.SkipNextDurationTick = true;
   ```
   而 `PowerCmd.TickDownDuration` 见到这个标记就 `SkipNextDurationTick = false;` **跳过这一次衰减**。
   易伤正是「挂在玩家身上的 Debuff」⇒ 她第 1 回合结束那次本该把它 tick 成 0 的机会被跳过
   ⇒ 一路挂到玩家第 3 回合。
   （`VulnerablePower.AfterSideTurnEnd` 只在 `side == CombatSide.Enemy` 时 tick —— 也就是
   **她的回合结束**才是玩家身上易伤该衰减的时点；这一条与上面那句配合起来正好是「三回合」的来源。）

**落点**：
- `src\Hooks\Patches\BossRelicHookBridge.cs` → `BossRelicSuppressBagOfMarblesPatch` 改回**压掉**
  （`IsOwnedByOurBoss` 时才压；`__result = Task.CompletedTask`）。
- `src\Hooks\Core\BossHookBridge.SideTurn.cs` →
  `GrantBagOfMarblesVulnerableAtCombatStart` 改成 `var applied = await PowerCmd.Apply<VulnerablePower>(…)`
  然后逐个 `power.SkipNextDurationTick = false`，并打日志
  `弹珠袋：清掉 N 个「跳过下一次衰减」标记 ⇒ 这层易伤在她第 1 回合结束就掉光`。

**验收点**：`玩家侧快照（她第 2 回合出手前）` 里**不应再有** `VULNERABLE_POWER`。

---

### 2. 谋杀（`MURDER`）：改成**按回合查表**

**做了什么**：华丽猎不再记「抽到过多少张」，直接查表。

`src\Hooks\Core\GrandFinaleSilentScript.cs` 新增：

```csharp
public static int MurderDamageForTurn(int turn)
{
    if (turn <= 3) return 1;
    if (turn == 4) return 20;
    if (turn == 5) return 24;
    if (turn == 6) return 28;
    if (turn == 7) return 35;
    return 42 + 7 * (turn - 8);      // 第 8 回合 = 42，之后每回合 +7
}
```

第 5/6/7 回合的 24/28/35 来自用户原来那串「每回合新进手牌 4、4、7、7」；
第 9/10/11 = 49/56/63 ⇒ 正好是「8~10 每循环 +21」，第 14 回合 = 84。

`src\Hooks\Core\BossScript.cs` 的谋杀块里，`SpawnVariantKey == "SILENT_GRAND_FINALE"` 时用查表值
覆盖记账值，并打日志 `谋杀：华丽猎按回合查表 ⇒ 第 N 回合 = X（记账口径本来会算出 Y，已忽略）`。
蛇眼猎等其它设计**一点不变**（仍走原来的记账口径）。

---

### 3. 小刀（隐秘匕首 / 钢铁风暴）

**3a. 每刀伤害 4 → 6**（真根因，一行）

`src\Hooks\Core\BossIntentBuilder.cs` 的 `shivs > 0` 分支里，`shivBase`（上部已算成
`4 + (GeneratesUpgradedShivs(card) ? 2 : 0)`）**只传给了「幻影之刃」那一支**，
另外两条（多段 / 单段）调 `ShivDamagePerHit(shivBoss, rise, victim)` 时**漏传**，
于是走默认值 4 ⇒ 升级版生成者（隐秘匕首+ / 钢铁风暴+，给的是升级小刀 6 点）照样画成 4。

实测证据：`小刀预览: 牌面 4 → 4（…目标易伤=0…）`（第 7 回合，`HIDDEN_DAGGERS+` 在场），
而同一场她身上有易伤时打真小刀是 `意图伤害管道 SHIV: 牌面 4 → 6`。
现在两处都显式传 `shivBase`。

反编译确认（`HiddenDaggers`）：`CardsVar(2)`、`DynamicVar("Shivs", 2)`，**没有 `OnUpgrade`**
⇒ 升级不改张数，只让 `OnPlay` 里 `if (IsUpgraded) CardCmd.Upgrade(current)` 把生成的刀升级
（`Shiv` 4 → 6）。所以**隐秘匕首+ = 2 刀 × 6**、低进阶 = **2 刀 × 4** —— 用户看到的 `4×2`
就是这条路径的 4。

**3b. 钢铁风暴的段数 = 「她出手那一刻手上剩下的张数」**

`src\Hooks\Core\BossScriptPlanner.cs` 的 `stormDiscards` 原来数的是
「本回合**所有**标了被丢弃的条目」（实测第 10 回合 = 6），而用户口径是 **4**
（防御 + 隐秘匕首 + 打击×2）。改成：

- 只数**排在 `STORM_OF_STEEL` 之后**的丢弃条目（在它之前丢的，出手时已不在手上）；
- **排除 `ToolsDiscard`**（必备工具在**回合开始**就先丢 1 张，那张也不在手上了）。

于是第 3 回合 = 5（防御 + 杂技×3 + 华丽收场，与用户写的「共 5 张」一致）、
第 10 回合 = 4。新增小工具 `EntryOfScriptType(Type)` 用来在不造卡实例的情况下取剧本条目 entry。

---

### 4. 「手上技法」把奇巧给错牌 / 弃错牌 → 点名名单**按发起者分桶**

**真根因（日志实证）**：以前「点名要丢的牌」只有一份**平铺名单**（`PreferByTurn`），第 2 回合的日志是

```
自动选择: 候选 7 个（min=1 max=1）→ 选 [DEFEND_SILENT]      ← 这是「杂技」，本该弃战术大师
自动选择: 候选 5 个（min=2 max=2）→ 选 [DEFEND_SILENT,TACTICIAN]  ← 这是「隐秘匕首」，本该弃 2 张防御
```

两个丢弃者互相抢牌；而「手上技法 / 夜魇」这种**不丢牌**的选择也会把名单里最靠后的那张抢去
（第 3 回合：`选 [ACROBATICS]` ⇒ 奇巧加到了杂技上，用户第 10 回合报的同一件事）。

**做法**（`src\Hooks\Core\BossCardSelector.cs`）：
- 新增 `ActingCard`（`CharBossMonster.PlayOne` 里设），配合已有的 `ActingBoss/ActingTurn`
  就能知道「这次选牌是**哪张牌**发起的」；
- `MarksOfDiscarder(boss, turn, options, actingCard)`：按剧本把「被丢弃」的条目挂到
  **它前面最近一张真的会打出的牌**上，只取这一次发起者的那一份；
  发起的牌没有条目 ⇒ **空名单** ⇒ 自然落到 `PickOrder`（手上技法 / 夜魇靠它点名）。
- `ToolsDiscard` / `Burned`（回合开始那一次）不进任何牌的桶，由 `TakeToolsDiscard` /
  旧逻辑单独处理；`ScriptBindsDiscardsToCards()` 为假时**完全走旧路径**（零回归）。

效果（按剧本推演，等实机日志核对）：
- 第 2 回合：夜魇 → 队列点名 `ACROBATICS`；杂技 → 战术大师；隐秘匕首 → 2 张防御；
- 第 3/7/10 回合：手上技法 → 队列点名 `DEFEND_SILENT`（奇巧给防御）；
  杂技只丢它自己那张、隐秘匕首只丢防御；
- 第 7 回合：必备工具仍丢钢铁风暴（`TakeToolsDiscard` 不变）；
- 第 8/9 回合：杂技分别丢战术大师 / 防御，不受影响。

日志里每次选牌现在都会带 `发起者=<牌名>（按丢弃者分桶）`，一眼能核对。

---

### 5. 第 6 回合剧本按用户新口径重写

`src\Hooks\Core\GrandFinaleSilentScript.cs` 第 6 回合改成：

```
ToolsDiscard(Discard<DefendSilent>())   // 防御：回合开始被必备工具丢掉
C<PiercingWail>()                       // 尖啸
C<StrikeSilent>()                       // 打击
C<Survivor>()                           // 生存者（此时手牌已空 ⇒ 不弃牌）
Energy = 3                              // 尖啸1 + 打击1 + 生存者1
```

---

### 6. 第 9 回合「华丽收场」不该被画成不可打出

**本轮没有改代码** —— 上一版已经改好，本次日志实证通过：

```
华丽收场：抽牌堆已清空（61 张进弃牌堆）⇒ 这一手它可打出
剧本第 9 回合: 打 [ACROBATICS,MEMENTO_MORI,NEUTRALIZE,DEFEND_SILENT,GRAND_FINALE] …
PLAY GRAND_FINALE (cost 0) …
```

`手牌栏：9 张里有 4 张本回合不会打 [DEFEND_SILENT,STRIKE_SILENT,…]` 里**不含** `GRAND_FINALE`。
只需下一轮实机确认面板上它不再是暗的/红杠。

---

## 二、下一轮实机要看的日志行

| 关注点 | 日志行 |
| --- | --- |
| 弹珠袋只 1 层且会掉 | `弹珠袋：施加后 … 易伤层数 = 1` / `弹珠袋：清掉 N 个「跳过下一次衰减」标记` / `玩家侧快照（她第 2 回合出手前）` **不应**有 `VULNERABLE_POWER` |
| 谋杀 | `谋杀：华丽猎按回合查表 ⇒ 第 4 回合 = 20`（第 8 回合 = 42、第 11 回合 = 63） |
| 小刀 | `小刀预览: 牌面 6 → …`（升级版生成者）；`小刀段数诊断：STORM_OF_STEEL（升级=True） 剧本丢弃=5/4 张 ⇒ 意图 1 条（升级刀基础=6）` |
| 选牌归属 | `自动选择: … 发起者=<牌名>（按丢弃者分桶）` |
| 第 6 回合 | `剧本第 6 回合: 打 [PIERCING_WAIL,STRIKE_SILENT,SURVIVOR] 跳过 [DEFEND_SILENT(被丢弃)]` |
| 第 9 回合 | `华丽收场：抽牌堆已清空…` + 面板不压暗 |

## 二·补、v0.2.527：三条「每帧重算盖掉规划值」（用户 2026-11-09 第二轮回报）

实机日志（v0.2.525，17:26–17:29 那一场，10 回合 + 循环）把三条都定位到了**同一类**根因：
**规划期算对了，面板被「每帧重算」那条路盖掉了**。

| 症状 | 日志证据 | 根因 | 落点 |
|---|---|---|---|
| 钢铁风暴只有「强化」蓝箭头 | 规划期明明是 `意图 STORM_OF_STEEL: [LiveMultiAttackIntent]`、`剧本丢弃=5 张 ⇒ 意图 1 条（升级刀基础=6）` | 段数牌面上**没有变量**，靠 `shivOverride` 从剧本传；而 `BladesGrowth.RecomputeAll` 与 `CharBossMonster.RefreshPlannedIntents` 两条重算路**都没传** ⇒ `ShivsFrom(card)`=0（STORM_OF_STEEL 不在小刀生成白名单里）⇒ 一条小刀意图都没有 | 两条路都补 `shivOverride: play.GeneratedShivs` |
| 第 9 回合铭记死亡预览低 4 | 面板 13、真实 17（9 + 4×2） | `BladesGrowth.discardsSoFar` 从 **0** 起算，漏了「必备工具在她回合开始丢的那 1 张」（规划期 `BossScriptPlanner` 早就算进去了） | 起算值改成 `HasToolsOfTrade ? 1 : 0` |
| 第 5 回合只打出 1 张打击 | 规划 `打 [STRIKE,STRIKE,DEFEND]`、`余费=0`；执行时 `成长修正（出牌期）: … \| STRIKE(补=0) DEFEND(补=0)` 只剩 2 张，随后 `手牌栏：2 张里有 1 张本回合不会打 [STRIKE_SILENT]` | 「必备工具」弃牌只按**牌名**挑第一张，不看计划保留了哪几张实例 ⇒ 丢掉了计划要打的那张 ⇒ 出牌循环「这张已不在手牌」静默跳过 | `TakeToolsDiscard` 改成**优先挑不在 `boss.Plan` 里的那张**，并打日志说明避开了几张 |

**排查口诀（已写进 `cerebrum.md`）**：本模组凡是「牌面上没有、只能由剧本算出来」的意图数值
（`shivOverride` / `extraHits` / `discardsThisTurn` / `simBlock` / 烟囱 / 开信刀…），
**三条路都要传**：`BossScriptPlanner` 规划、`BladesGrowth.RecomputeAll`、有球 Boss 的
`RefreshPlannedIntents`。判据：日志里 `剧本第 N 回合: 打 [...]` + `意图 X: [...]` 与面板不一致
⇒ 就是重算路漏参。

**本轮 v0.2.525 已验证通过的部分**（同一份日志）：

* 弹珠袋：`清掉 1 个「跳过下一次衰减」标记`，`玩家侧快照（她第 1 回合出手前）powers=[VULNERABLE_POWER:1]` —— 只有 1 层；
* 谋杀：`第 4 回合 = 20`、`第 8 回合 = 42`、`第 11 回合 = 63`、`第 14 回合 = 84` —— 与用户口径逐回合吻合；
* 选牌分桶：第 2 回合 夜魇→杂技 / 杂技→战术大师 / 隐秘匕首→2×防御；第 3、7、10 回合 手上技法→防御；
  第 7/8/9/10 回合 必备工具分别丢 钢铁风暴 / 生存者 / 打击 / 打击 —— **全部与剧本一致**；
* 第 6 回合：`打 [PIERCING_WAIL,STRIKE_SILENT,SURVIVOR] 跳过 [DEFEND_SILENT(被丢弃)]`。

---

## 三、仍然待验证（本轮没动）

- 蛇眼猎整场（v0.2.356 的抽牌炸弹修复后必测）、养手骨的无界命匣召唤与「不死」两条 hook 顺序、
  事件机的谋杀加伤与情感芯片球被动翻倍显示。
- 创意工坊：线上仍是 **v0.2.513**，`.dist` 与 `workshop\` 都没更新到 0.2.525；
  需要时跑 `tools\upload-workshop.ps1`（Steam 要开着并登录）。
