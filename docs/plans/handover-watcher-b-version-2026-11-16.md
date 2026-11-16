# 交接：观者 B 版（lamali）「神格观」10 层真言不进神格 —— 以及其他在手事项

> 写于 2026-11-16 傍晚。接手请先读完这一页，再动手。
> 当前已装机版本：**v0.2.708**（`[verify] 两份实现的版本串都是 v0.2.708 OK`）。
> 唯一的**未解决**问题只有一条（§2），其余都是「已改完、等实机确认」。

## ★ 最重要的一条进展（v0.2.708，务必先看）

v0.2.707 的诊断日志已经把根因说清楚了：

```
[17:17:26] 进神格：StanceCmd.EnterDivinity 之后姿态模型 = NoStance
[17:17:26] 进神格（强制 SetStanceSilently<DivinityStance>）⇒ 姿态模型 = DivinityStance
```

也就是说：**姿态确实设上了**（`DivinityStance`），但 `SetStanceSilently` 是 **silent 版**，
它**跳过 `OnEnterStance`** —— 而入姿态的 **+3 能量**、入姿态钩子、演出全在 `DivinityStance.OnEnterStance` 里。
所以玩家侧看起来仍然「没进神格」（能量没涨、也没有亮起来）。

v0.2.708 的改法：强设成功后**补跑一次** `stance.OnEnterStance(ctx, owner, source)`，并打日志
`进神格：已补跑 OnEnterStance（能量 before → after，神格里=?）`。
**这一步还没实机验证** —— 新会话第一件事就是让用户打一场 B 版神格观，看这行日志：

- `能量 3 → 6` ⇒ 成了；
- `能量 3 → 3` ⇒ `OnEnterStance` 里那笔能量没落到她的 `PlayerCombatState` 上（大概率被我们自己的
  `PrepareTurn` 预算覆盖），那就得改成「把 +3 记进 `NextTurnEnergyBonus`/预算」或用 `PlayerCmd.GainEnergy` 补；
- 没有这行 ⇒ 强设失败（看前面那两行姿态模型）。

如果游戏里仍然看不到「神格」的界面提示（HUD/立绘变色），那是 silent 版跳过演出的副作用；
真正影响玩法的是「攻击三倍 + 每回合不退神格」，这两条由姿态模型 + 我们自己的倍率兜底保证。

---

## 0. 三件事先记住

1. **不要擅自关用户的游戏。** 用户明确说过「你别乱关我游戏」。装机前先 `Get-Process SlayTheSpire2`：
   有进程就**只编译不改文件**，然后告诉用户「关掉游戏后说一声我装」。游戏在跑时 `Impl111.dll.bin` 会被占用，
   `install-mod.bat` 的 copy 会被 `>nul` 吞掉（只是它自己会打 `[verify] ✗ 部署版本不一致`）。
2. **构建用项目自带的 SDK**：`.\tools\dotnet\dotnet.exe build src\CharacterBosses.csproj -c Release -p:Sts2Target=111`（107 同理）。
   本机 `C:\Program Files\dotnet\sdk` 已经不存在（只剩 9.x 运行时），直接用 `dotnet` 会报 "No .NET SDKs were found"。
3. **改版本必须改两处**：`src/CharacterBosses.json` 的 `version` **和** `src/Hooks/Core/BuildInfo.cs` 的 `Tag`
   （`Tag` 是超长单行字符串，里面的引号一律用「」，否则编译炸）。漏改 `Tag` 会让 `install-mod.bat` 报版本不一致。

装机命令（需要提权，`danger-full-access`）：

```
cmd /c 'install-mod.bat "D:\SteamLibrary\steamapps\common\Slay the Spire 2"'
```

---

## 1. 项目背景（一句话）

这是 StS2 模组 `CharacterBosses`（作者 飞猪，创意工坊 **3830639796**），把每一幕 Boss 换成原版可玩角色。
本阶段在做**第三幕的两只观者 Boss**：

| 设计 | Key | 说明 |
|---|---|---|
| 神格观 | `WATCHER_DIVINITY`（内部怪物键 `WATCHER_BOSS`） | 特殊效果「虔信」：玩家每打一张牌 → 她 +1 真言；她回合开始 −5（恐惧 −3）；**满 10 层 ⇒ 退出姿态 → +2 力量 → 进神格**，且不再在回合开始退出神格 |
| Ω观 | `WATCHER_OMEGA` | 特殊效果「斋戒」：欧米伽隔一回合（5/7/9/11…）触发、点穴伤害可被格挡；12 回合剧本 + 9～12 循环 |

## 2. ★ 唯一未解决：B 版「神格观」满 10 真言不进神格

### 2.1 现象（用户按 A/B 版分别实测）

- **A 版（Boninall，工坊 3747526116）：正常。** 日志：`虔信：真言满 10 ⇒ … 能量 2 → 5，神格里=True`
- **B 版（lamali，工坊 3747492505）：不行。** 日志：
  ```
  虔信：真言满 10 ⇒ ① 退出姿态（本来在平静=False…）② 力量 +2 ③ 进入神格（原版 Divinity 自己 +3 能量）⇒ 能量 3 → 3，神格里=False
  ```
  ⇒ 力量给了、退姿态跑了，**只有「进神格」这一步没生效**（能量没 +3）。

### 2.2 已经试过、都失败的三种写法

都在 `src/Hooks/Core/WatcherCards.cs` 的 `EnterDivinityCoreAsync` / `ForceDivinitySilentlyAsync` 里，按顺序：

| # | 写法 | 结果 |
|---|---|---|
| 1 | `StanceCmd.EnterDivinity(new ThrowingPlayerChoiceContext(), player, null)` | 静默无效（v0.2.702 之前） |
| 2 | `StanceCmd.EnterDivinity(真实 ctx, player, null)`（ctx 从 `AfterPowerAmountChanged` 的 `__args` 里捞） | 仍然无效（v0.2.702 之后） |
| 3 | `WatcherModel.SetStanceSilently<DivinityStance>(player)`（反射泛型实例化，v0.2.707） | **姿态设上了**（日志 `姿态模型 = DivinityStance`），但 silent 版跳过 `OnEnterStance` ⇒ 那 +3 能量没给 ⇒ 玩家看着仍「没进神格」 |
| 4 | 强设后补跑 `OnEnterStance(ctx, owner, source)`（v0.2.708） | **待实机验证**（见开头「最重要的一条进展」） |

### 2.3 反编译已知事实（B 版，`.dll` 路径见 §5）

```csharp
// Watcher.Code.Core.WatcherModel
private static readonly SpireField<PlayerCombatState, WatcherStanceModel> ActiveStance
    = new SpireField<PlayerCombatState, WatcherStanceModel>(() => WatcherModelDb.WatcherStance<NoStance>());

public static WatcherStanceModel GetStanceModel(Player player)
{
    PlayerCombatState pcs = player.PlayerCombatState;
    if (pcs == null) return WatcherModelDb.WatcherStance<NoStance>();     // ← ① 这里
    return ActiveStance[pcs] ?? WatcherModelDb.WatcherStance<NoStance>();
}
public static bool IsInStance<T>(Player player) => GetStanceModel(player) is T;
public static Task SetStanceSilently<T>(Player player) where T : WatcherStanceModel
    => SetStance(null, player, WatcherModelDb.WatcherStance<T>(), null, silent: true);

private static Task SetStance(PlayerChoiceContext? ctx, Player player, WatcherStanceModel newCanonical,
                              CardModel? source, bool silent = false)
{
    PlayerCombatState state = player.PlayerCombatState;     // ← ② 这里被拿走用
    WatcherStanceModel current = ...;
    if (current?.GetType() == newCanonical.GetType()) return;   // ← ③ 「已经是这个姿态」就直接 return
    ... 后面才是 OnExitStance / OnEnterStance / VFX / 力量 / 能量
}

// Watcher.Code.Stances.DivinityStance : WatcherStanceModel
//   OnEnterStance(ctx, owner, source) 里给 +3 能量（A 版对应 `Divinity.AfterApplied`）
```

⇒ **最可疑的三条**（按可能性排序）：

1. **`player.PlayerCombatState` 在她身上是 null**。`GetStanceModel` 见 null 就返回 `NoStance`，
   `SetStance` 拿到的 `state` 也是 null ⇒ 写不进 `ActiveStance`、`OnEnterStance` 那条链也可能提前 return。
   我们的影子玩家 `PlayerCombatState` **平时是可用的**（手牌/能量/球都靠它），但要确认**那一刻**是不是 null
   （她被打断/重规划期间 pcs 可能被换掉）。
2. **`current?.GetType() == newCanonical.GetType()` 已经成立** —— 即她其实已经是 `DivinityStance`，
   `SetStance` 直接 return。这种情况下 `InDivinity` 应该为 true（v0.2.707 已改成同时看姿态模型），
   但我们**还没拿到 v0.2.707 的日志**，所以目前无法排除。
3. `WatcherModelDb.WatcherStance<T>()` 里 `ModelDb.GetById<T>(ModelDb.GetId<T>())` 对**模组的角色/模型注册表**
   可能有额外条件（她不是"玩家侧的观者"，某些模型没被注册到 combat 的 model 集合里）。

### 2.4 下一步该做的（建议顺序，每一步都先加日志）

1. **先要日志**：让用户用 v0.2.707 打一场（B 版），把这几行发来：
   ```
   Select-String -Path "$env:APPDATA\SlayTheSpire2\logs\mod_log.txt" -Pattern "进神格|虔信：真言满|真言接管"
   ```
   v0.2.707 会在 `EnterDivinityCoreAsync` 里打 `进神格：StanceCmd.EnterDivinity 之后姿态模型 = <类型名>`，
   以及强设后的 `进神格（强制 SetStanceSilently<DivinityStance>）⇒ 姿态模型 = <类型名>`。
   **这两行会立刻区分 §2.3 的三条可能。**
2. 若两条都拿到 `NoStance` 或空 ⇒ 打日志确认 `player.PlayerCombatState == null`：
   ```csharp
   Logger.Log($"pcs={(shadow.PlayerCombatState == null ? "null" : "ok")} stance={WatcherCards.StanceModelName(shadow)}");
   ```
3. **绕过 `SetStance` 整条链**（最稳的兜底，还没试）：
   - 反射拿到私有静态字段 `WatcherModel.ActiveStance`（`SpireField<PlayerCombatState, WatcherStanceModel>`）；
   - 直接写 `ActiveStance[pcs] = WatcherModelDb.WatcherStance<DivinityStance>()`；
   - 再手动调 `divinityStance.OnEnterStance(ctx, player, source)` 拿那 +3 能量（并打日志核对能 3→6）。
   - 注意 `SpireField<TKey,TValue>` 的索引器用法要用反射 `get_Item/set_Item`，键是 `PlayerCombatState` 实例。
4. 如果连 `ActiveStance[pcs]` 都写不进去（pcs 为 null），就改成**先补 pcs**：影子玩家的 pcs 由
   `Core/ShadowPlayerFactory` + `CharBossMonster` 的 `PrepareTurn` 维护，查它什么时候会被置空。
5. 兜底方案（不优雅但一定能"看起来进了神格"）：给她挂一个自制的「神格」power（攻击三倍 + 回合开始不退），
   代价是能量 +3 要我们自己给 —— 用户 2026-11-16 明确说过「**别给她补，用她原版的加费**」，
   所以这只能当最后手段，且要先跟用户确认。

### 2.5 相关代码位置

| 作用 | 文件 |
|---|---|
| 满 10 层结算（扣 10 → 退姿态 → 力量 → 进神格） | `src/Hooks/Powers/BossDevotionPower.cs` → `ResolveDivinityAsync` |
| 观者接口抹平（真言 / 姿态 / 进神格 / 点穴层数） | `src/Hooks/Core/WatcherCards.cs`（`MantraOf` / `SetMantra` / `ExitStanceAsync` / `EnterDivinityCoreAsync` / `ForceDivinitySilentlyAsync` / `StanceModelName` / `StanceMultiplierFromModel` / `PressurePointsMarks`） |
| 满 10 层的自动触发（压掉模组那条 + 自己结算） | `src/Hooks/Core/WatcherRuntimePatches.cs` → `MantraTakeoverPrefix` |
| 神格不退 | `WatcherRuntimePatches.InstallDivinityKeep` |
| 每回合开始扣真言 / 玩家出牌加真言 | `BossDevotionPower.cs`（`AfterSideTurnStart` / `AfterCardPlayed`） |

---

## 3. 本次会话改完、**等实机确认**的其它条目

| # | 用户要求 | 实现 | 日志确认点 |
|---|---|---|---|
| 1 | 历史记录里观者应是**头像**不是地图图标 | `Patches/EncounterIconPathPatch.cs` 的 `OurBadge`：优先 `CharacterModel.IconTexture.ResourcePath`，其次猜路径（`top_panel` / `res://Watcher/images/watcher` / `res://Watcher/_imported`），最后地图徽章 | `结算/历史图标诊断：… IconTexture=…` |
| 2 | （上一版的 bug）历史里变成**愚者**头像 | `WatcherCharacterFallback` 改用 `WatcherModPresence.CharacterType()`，且等于 `typeof(Fool)` 时直接放弃 | 同上 |
| 3 | 观者冥想之后**玩家的结束回合按钮消失** | 新增 `Patches/ShadowPlayerEndTurnSkipPatch.cs`：`PlayerCmd.EndTurn` 目标是影子玩家时整条吞掉（`Meditate.OnPlay` 最后一句会替出牌者结束回合）；另在 `BossFrameTickPatch` 的按钮保险里加 `CombatManager.IsPlayerReadyToEndTurn(me)` 兜底 | `影子玩家的 PlayerCmd.EndTurn 被吞掉` |
| 4 | 冥想捞牌**一定捞点穴** | `BossCardSelector`：发起者是冥想时，候选（弃牌堆）里有「点穴」就先拿 | `冥想：按用户要求优先捞「点穴」` |
| 5 | 许愿**固定永生不死** | `BossCardSelector`：候选里出现 `LIVE_FOREVER` 就优先选它（A 版另有 `WatcherRuntimePatches.InstallWishChoice` 改 `OnPlay` 返回值，双保险） | `许愿：按用户要求固定挑「永生不死」` |
| 6 | **B 版**点穴：只有策略/不算伤害/不算前一张叠的印记 | `WatcherCards.PressurePointsMarks`（先读 `MarkPower`，再 `MagicNumber`），意图与规划期都用它；并置 `suppressGenericDebuff` 防止出现两个策略箭头 | `点穴意图：目标已有印记 … + 这张施加 …` |
| 7 | **B 版**愤怒时攻击不翻倍（实伤） | 只对 B 版给 `Hook.ModifyDamage` 挂 postfix（dealer 是她、props 不含 `(ValueProp)4`、`modifyDamageHookType == All` 时按姿态倍率乘一次）；`StanceMultiplierOf` 增加 `WatcherCards.StanceMultiplierFromModel`（调 `WatcherModel.GetStanceModel`） | `姿态倍率兜底：… 出手 10 ×2 ⇒ 20`、`效果追踪 HP … −20` |
| 8 | 第 3 回合「贝塔 / 如水」调序 | `WatcherOmegaScript` 第 3 回合改为 化体为空 → 点穴（0费）→ **如水** → **贝塔** → 奇迹（不出） | — |
| 9 | 进阶 9 不再升级点穴，改升级**所有打击** | 剧本里两张点穴去掉 `upgradeMinAscension: 9`；第 4/7/9/10 回合的四张打击加上 | — |
| 10 | 欧米伽伤害要进薄荷尖塔统计 | `MintySpireDamagePreviewCompat.PendingOf` 增 `BossHandPanel.OmegaRowDamage`（与本回合是否触发同一位） | — |
| 11 | 神格观遗物：护喉甲 → **奥利哈刚** | `WatcherScript.Relics()`：`Relics.Gorget` → `Relics.Orichalcum` | — |
| 12 | 斋戒 / 神格 的文案（16 语言） | 括号整段删、警告上方空行删、警告下加「欧米伽：在你的回合结束时，对所有敌人造成50点伤害。」、神格说明上方空行删 | `check-localization` 全绿 |

---

## 4. A 版 / B 版差异速查（踩过的坑都在这里）

| 项目 | A 版 Boninall（3747526116） | B 版 lamali（3747492505） |
|---|---|---|
| 牌 id | 下划线 `WATCHER_ERUPTION_P`（玩家版带 `_P`） | 连字符 `WATCHER-ERUPTION`；**基础牌/衍生牌带 `_WATCHER` 尾巴**：`WATCHER-STRIKE_WATCHER`、`WATCHER-DEFEND_WATCHER`、`WATCHER-WISH_WATCHER` |
| id 判定 | 统一走 `CharBossMonster.WatcherIdIs(entry, suffix)`：规范化（去 `-`/`_`/空格、转大写）后比后缀，**再比一次"去掉末尾单个字母"**（`_P`），**再比一次"去掉末尾 WATCHER"** | 同上 |
| 点穴层数变量 | `DynamicVar("MagicNumber")` | `PowerVar<MarkPower>`（键 `MarkPower`） |
| 姿态的存放 | 生物身上的 power（`WRATH` / `CALM` 一类） | **玩家身上的模型**：`SpireField<PlayerCombatState, WatcherStanceModel>`，入口 `WatcherModel.GetStanceModel(player)` |
| 进神格 | `WatcherMod.WatcherCombatHelper.EnterDivinity(player, source)`（可用） | `StanceCmd.EnterDivinity(ctx, player, source)` → `WatcherModel.SetStance<DivinityStance>`（**在影子玩家身上无效，未解决**） |
| 角色头像资源 | 自带 `res://images/ui/top_panel/character_icon_watcher.png` | 只有 `res://Watcher/images/watcher/character_icon_watcher.png`（`res://` 前缀由模组自己定） |
| 角色类型 | `WatcherMod.Watcher` | `Watcher.Code.Character.Watcher`（entry `WATCHER-WATCHER`） |
| 真言 power | `Mantra` | `MantraPower`（entry `WATCHER-MANTRA_POWER`） |
| 欧米伽 | `OmegaPower.AfterSideTurnEnd`，伤害取 **`Amount`**（牌是 `WithPower<OmegaPower>(50,10)`） | 同名类同位置 |

其它既有约定（别改坏）：

- `BossDefs.Watcher.CharacterType` 的**初值是愚者占位** `typeof(Characters.Fool)`，运行期由
  `BossDefs.RefreshWatcherCharacterType()` / `WatcherModPresence.CharacterType()` 换成真观者 —— **代码里任何地方读它都要考虑"可能还是愚者"**。
- 意图/实伤的姿态倍率：真实伤害管道里**只有 A 版**会自己乘（B 版不会），所以我们只在 B 版挂 `Hook.ModifyDamage` postfix。
  预览的补差在 `BossDamagePredictor.ScaleToPlannedStance`（`计划 ÷ 此刻`，双向），倍率烤在 `Live*AttackIntent` 构造时（`BossIntentBuilder.StanceBake`）。
- 剧本规划期会按出牌顺序推演姿态：`BossScriptPlanner.plannedStanceMul`；通用规划路 `BossPlanner.Plan` 也必须传 `stanceMul`。

## 5. 常用排查信息

- 日志：`%APPDATA%\SlayTheSpire2\logs\mod_log.txt`（另有 `godot.log`）
- 关键 grep：`剧本姿态`、`姿态倍率`、`点穴意图`、`虔信：`、`进神格`、`真言接管`、`斋戒：`、`结算/历史图标`、`效果追踪 HP`、`自动选择`
- 反编译（B 版）：`D:\SteamLibrary\steamapps\workshop\content\2868840\3747492505\Watcher.dll`
  A 版：`...\3747526116\Watcher.dll`；基础游戏：`.refs\sts2_111.dll` / `.refs\sts2_107.dll`
- 反编译命令：`.\tools\dotnet\dotnet.exe .\tools\ilspy-cli\bin\Release\net10.0\ilspycli.dll <types|type|method|members|search> <dll> [pattern]`
- 双目标必须都 0 错误：`-p:Sts2Target=107` 与 `111`（107 没有 `STS2_111` 宏 ⇒ 所有 `WatcherCards` / `BossDefs.Watcher` 相关代码都要 `#if STS2_111`）
- 本地化检查：`powershell -ExecutionPolicy Bypass -File tools\check-localization.ps1`（16 语言，`\n` 数 / 占位符 / 标签平衡必须一致）

## 6. 用户的沟通习惯（重要）

- 用中文回复，给**日志证据**；不要长篇解释，说清「根因 / 改法 / 怎么验证」。
- 用户自己会实机测，测完反馈；不要擅自替他做「削弱/加强」的决定，改动要按他给的原文。
- 他讨厌反复改不动 —— 每次动手前先看日志，别猜。
- **不要关他的游戏**（重申一次）。
