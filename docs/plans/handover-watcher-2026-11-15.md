# 交接报告 · 三层 1 号「神格观」（观者 Boss）· 2026-11-15 深夜

> 写给**下一个会话**。用户已经明确「别修了，让新会话修」，所以这份文件的目标是：
> 让接手者**不用重新摸一遍**，直接从「当前坏在哪、为什么坏、下一步怎么改」开始。
>
> 相关文档（都要先读一遍）：
> - `docs/plans/boss-watcher-divinity.md` —— 需求全文（用户原话）+ 第 1~22 轮进度记录
> - `rules.md` / `AGENTS.md` —— 项目规范（禁止编译期引用别的模组、只改 `src/Hooks/`、版本号两处一起改…）

---

## 0. 一句话现状

**Boss 已经能在游戏里打**（`fight WATCHER_DIVINITY_BOSS_ENCOUNTER`，需装观者模组），剧本/遗物/特殊效果/16 语言都在，
但**还有 5 个 bug**（见 §2）。当前版本 **v0.2.679**，107 / 111 双目标 0 错误，已安装，16 语言检查通过。

---

## 1. 已完成的部分（别重做）

| 模块 | 文件 | 说明 |
|---|---|---|
| 需求全文 + 22 轮进度 | `docs/plans/boss-watcher-divinity.md` | **一切口径以它为准** |
| 两个观者模组的兼容层 | `src/Hooks/Core/WatcherCards.cs` | provider 结构：优先 3747526116（Boninall），次选 3747492505（lamali）。卡牌按「A 类名 → B 类名 → 模型 id」三级解析；姿态/真言/神格接口抹平 |
| 运行期补丁（神格不退、许愿固定） | `src/Hooks/Core/WatcherRuntimePatches.cs` | 运行期 Harmony（不能编译期引用观者类型） |
| lamali 版许愿固定 | `src/Hooks/Patches/WatcherWishLowLevelChoicePatch.cs` | 补本体的 `CardSelectCmd.FromChooseACardScreen` |
| 11 回合剧本 | `src/Hooks/Core/WatcherScript.cs` | 7~11 循环；进阶 8 升级摆手 |
| 特殊效果「虔信」 | `src/Hooks/Powers/BossDevotionPower.cs` | 真言走**观者自己的 Mantra power**；她回合开始 −5（恐惧 −3）；满 10 层给力量（**普通 2 / 恐惧 3**，用户 2026-11-15 削弱过） |
| 虔信 buff（斗蛐蛐 / 恐惧支配用） | `src/Hooks/Powers/WatcherFaithPower.cs` | 持有者回合开始给观者真言 |
| 观者支配 | `src/Hooks/Core/CharDominions.cs` | `WatcherDevotionDominion`（所有玩家 2 层虔信） |
| 斗蛐蛐两条规则 | `src/Hooks/Core/WatcherCricketRule.cs` | 角色 Boss 对手 ⇒ 他出牌 +1 真言；否则开局给她 3 层虔信 |
| 观者小工具 | `src/Hooks/Monsters/CharBossMonster.Watcher.cs` | 聚能成刃 → 灭除之刃的展示辅助 |
| 定义 / 排期 / 名字 / 图标 | `BossDefs.cs`（`Watcher`）、`BossKeys.cs`（`WATCHER_DIVINITY`）、`Encounters/CharBossEncounters.cs`（`WatcherDivinityBossEncounter`，地图图标 `watcher_divinity`） | 控制台 id = `WATCHER_DIVINITY_BOSS_ENCOUNTER` |
| 本地化 | `content/localization/*/monsters|powers|encounters|ascension.json` | 16 语言，键：`WATCHER_BOSS.name/.title/.devotion/.devotion_dread`、`BOSS_DEVOTION_POWER.*`、`WATCHER_DIVINITY_BOSS_ENCOUNTER.*`、`DREAD.WATCHER.description` |

---

## 2. 现在坏在哪（用户 2026-11-15 最后一次实测的 5 条）

### ① 能量：第 2 / 第 4 回合「不进神格也有 5 / 4 费」，应为 2

用户口径（最新、最权威）：

> 「你别给她补，用她原版的加费就行。」

**当前实现里我们一共在补三笔能量，全部要去掉或核实：**

1. `CharBossMonster.Turn.cs` 里那块 `#if STS2_111` 的「观者加费」：
   - `int lantern = 0;`（已经是 0，但代码还在，可整块删）
   - `Powers.BossDevotionPower.TakePendingDivinityEnergy(this)` ⇒ **这就是「进神格重复加费」的来源**
2. `BossDevotionPower.WatchDivinity()` 里 `pcs.Energy += 3;` + `_pendingEnergy[key] += 3;` ⇒ **双份**（一份立刻给、一份下回合给）
3. `BossDefs.Watcher.Energy = {3,3,3,3}` —— **涅奥的祝福每回合 −1** ⇒ 实际 2。
   ⚠ 但这套算法显然还有别的来源在加能量（第 2 回合 5 费 = 2 + 3？）。**接手的第一个动作：读日志对账**：

   ```
   grep "[CharBoss] 观者加费" mod_log.txt
   grep "[CharBoss] 佩尔之泪"  mod_log.txt
   grep "[CharBoss] 天人形态"  mod_log.txt
   grep "[CharBoss] 回合能量"  mod_log.txt      # 若有
   ```

**建议改法（下次一定这样做）**：
- 把 §2① 里 1、2、3 三处**全部删掉**，让观者模组自己的 `EnterDivinity`（它内部会 +3 能量给影子玩家）**唯一来源**；
- 若 `PrepareTurn` 的 `pcs.Energy = 预算` 会把原版加的那 3 点覆盖掉，正确做法是**只在这一处**做「保留原版已到账的能量」：先把 `pcs.Energy` 读出来与预算取 `max`，而不是我们自己造一笔 +3；
- 佩尔之泪（+2）与我们的预算是并存的（那条已压掉原版钩子，见 `BossRelicHookBridge.cs` 的 `BossRelicSuppressPaelsTearsPatch`），**别再动它**。

### ② 进神格后意图预览仍不重算

已经做过两步（都不够）：
1. `BossDevotionPower.WatchDivinity()` 里调 `boss.RefreshPlannedIntents()` + `BossHandUi.RefreshForce(boss)`；
2. `CharBossMonster` 的「回合中又拿到资源 ⇒ 按剧本补打」通道给观者放开（`|| Creature?.GetPower<Powers.BossDevotionPower>() != null`），并在补打前把 `extra` **并进 `Plan`**。

**还没解决**。接手时先确认这两件事：
- 补打通道到底有没有进（日志：`回合中又拿到资源 → 按剧本补打：[...]`）；
- 意图是从 `Plan`（`PlannedPlay.Intents`）画的，还是从**手牌栏重建**时的另一次 `Plan(...)` 画的 —— 后者的话，光「并进 Plan」不够，得触发**整块面板重建**（`BossHandUi.Refresh(boss)`，而不是 `RefreshForce`）。
- 还有一个已知坑：面板在**玩家回合**画的是「下一回合的计划」，而进神格发生在玩家回合 ⇒ 计划已经算完，**必须整块重算**（走 `PrepareTurn` 那条规划路径），只在原计划上打补丁不够。

### ③ 聚能成刃打出后不展示灭除之刃；④ 灭除之刃段数恒为 1

- 展示：`CharBossMonster.PlayOne` 打完牌后调用 `SurpriseWatcherConjureShown` + `Core.BossCardPreview.ShowAsync`（同「羽化」那套原版预览）。
- 段数：`BossScriptPlanner` 里新增 `conjuredX`（打聚能成刃时记 `cost`）→ 遇到灭除之刃时 `xHits = conjuredX - 1`。

**两个都没生效**，怀疑点（按优先级）：
1. **灭除之刃不在她手牌里**：观者 A 版可能把生成的 Expunger 放进**抽牌堆/手牌**之一。`GeneratedExpungers()` 只扫 `pcs.Hand`。**先加一行日志把「A 版聚能成刃打完以后，这张 token 牌到底在哪个 pile」打出来**（`CardPileType` / `pcs.Hand/ DrawPile / DiscardPile` 全扫一遍）。
2. **`PlayOne` 里那段代码没被执行到**：注意它插在 `Patches.StrayBossCardQueuePatch.Reap()` 之前，而那段在 `try {} finally` 里、且外面还有 `if (extra.Count > 0)` 之类的嵌套 —— **先去日志里确认有没有 `灭除之刃展示失败` / 展示成功那行**。
3. **`conjuredX` 记不下**：规划器 `entryNow` 的取值依赖 `SafeEntry(card)`，而 A 版这张牌的 `Id.Entry` 是 `WATCHER_CONJURE_BLADE`（已兼容）—— 但**如果这张牌根本不在剧本手牌里被规划**（例如她打的是 relic/卡池里的另一张），就永远记不上。**打一行 `聚能成刃：本回合 X=…` 的日志确认有没有走到。**
4. 段数的真实来源可能是**牌自己的 `DynamicVars`（Expunger 生成时带 HitCount）**，那样根本不需要我们算 —— 先打印那张牌的 `Type`/`DynamicVars` 看看。

---

## 3. 曾经踩过、千万别重踩的坑（血的教训）

### 3.1 观者模组的结构（两份实现完全不同）

| | A = 3747526116（Boninall，**优先**） | B = 3747492505（lamali） |
|---|---|---|
| 卡牌 | `WatcherMod.WatcherStrike_P` | `Watcher.Code.Cards.Basic.StrikeWatcher` |
| 模型 id | `WATCHER_STRIKE_P` | `WATCHER-STRIKE_WATCHER` |
| 姿态 API | `WatcherMod.WatcherCombatHelper.{EnterDivinity,ExitStance,GainMantra}`（只收 `Player`） | `Watcher.Code.Commands.StanceCmd.{EnterDivinity,ExitStance}`（收 `ctx, Player, card`） |
| 真言 power | `Mantra` | `MantraPower` |
| 神格 power | `Divinity`（**回合结束**自动退） | `DivinityStance`（**回合开始**自动退，`ModifyDamageMultiplicativeCompability` ×3） |
| 许愿三选一 | `WatcherCombatHelper.ChooseOne` | 本体 `CardSelectCmd.FromChooseACardScreen`（选项 token = `LiveForever`） |
| 至纯之水印牌 | `BeforeCombatStart` | `BeforeHandDraw` |

**两个都装时优先 A**（`WatcherCards.Probe()` 按 A 的招牌类型判定），日志会打
`观者引用层：使用 3747526116（Boninall）（两个观者模组都在 ⇒ 优先 3747526116）`。

### 3.2 能量相关的三个「重复源」

- **涅奥的祝福每回合 −1 能量**（`Relics/NeowBlessing.cs`：拾起 +120、每幕 +180、**每回合少抽 2 + 少 1 能量**）；
- **灯笼（Lantern）遗物钩子自己会 +1**（第 1 回合），别再手动加；
- **观者 `EnterDivinity` 自己会 +3 能量**给影子玩家，别再手动加（用户明确要求「用她原版的加费」）。

### 3.3 `PrepareTurn` 会硬写 `pcs.Energy = 预算`

所以任何「原版钩子加的能量」都会被覆盖。要么把原版那笔算进预算（读 power 层数），要么**别再加**。

### 3.4 意图刷新

`RefreshPlannedIntents()` 只拿**当前 `Plan`** 重算意图 ⇒ 新打的牌必须先并进 `Plan`；
而「玩家回合里进神格」那种情况，计划本来就过时了 ⇒ 需要**整块重规划**。

### 3.5 特殊效果说明框（`NHoverTipSet`）挂在**全局**提示层

离场必须主动收：`BossSpecialTip.Hide()` 里要对**框本身** `Visible=false` + `QueueFree`，并加了
`BossSpecialTipRoomGuardPatch`（postfix `NHoverTipSet._Process`，不在战斗房就收）。别再走「按锚点 Remove」那条老路。

### 3.6 联机

- 血肉骨「吞噬生命」以前在 Power 钩子里**每端各改一次状态** ⇒ 不同步；现在**只让房主结算**（`NetRole.IsHostOrSolo`）。
- `BossPicksMessage` 没在 BaseLib 注册 ⇒ 房主勾选广播失败；已加 `Core/BaseLibMessageRegistration`（反射清 BaseLib 的 `_customMessages` 再 `Initialize`）。

### 3.7 工具/编辑坑（我自己吃过好几次）

- PowerShell 里 `"…`r`n…"` 用**单引号**写会变字面量 `` `r`n `` ⇒ 编译报 `CS1056 意外的字符``` ；
- 用**行号插入**改 `CharBossMonster.cs` 这种大文件极易插错位置（我弄坏过两次括号、一次把方法声明行覆盖掉）；
  **改之前先打印上下文**，改完立刻编译；
- `git` 会报一堆 CRLF 警告，属正常。

---

## 4. 构建 / 安装 / 验证流程（照抄即可）

```powershell
.\tools\dotnet\dotnet.exe build src\CharacterBosses.csproj -c Release -p:Sts2Target=111 -v q --nologo
.\tools\dotnet\dotnet.exe build src\CharacterBosses.csproj -c Release -p:Sts2Target=107 -v q --nologo
powershell -ExecutionPolicy Bypass -File tools\check-localization.ps1
powershell -ExecutionPolicy Bypass -File tools\close_sts2.ps1
cmd /c 'install-mod.bat "D:\SteamLibrary\steamapps\common\Slay the Spire 2"'   # 需要 danger-full-access
powershell -ExecutionPolicy Bypass -File tools\launch_sts2.ps1                # 需要 danger-full-access
```

- 版本号**两处一起改**：`src/CharacterBosses.json` 的 `version` + `src/Hooks/Core/BuildInfo.cs` 的 `Tag = "vX.Y.Z / …"` 前缀。
- 装完必须看到 `[verify] 两份实现的版本串都是 vX.Y.Z  OK`；**游戏开着会导致 copy 失败**（`[verify] ✗ 部署版本不一致`）⇒ 先关游戏。
- 进游戏测试：一局进行中按 <kbd>'</kbd> 或 <kbd>*</kbd> 开控制台 → `fight WATCHER_DIVINITY_BOSS_ENCOUNTER`（需要观者模组）。
- 日志：`%APPDATA%\SlayTheSpire2\logs\mod_log.txt`。有用的行：
  `观者引用层：使用 …`、`虔信：她的回合开始，真言 N → N-5`、`虔信：她进入神格 ⇒ +N 力量，本回合 +3 能量`、
  `观者加费：…`、`回合中又拿到资源 → 按剧本补打：[…]`、`聚能成刃：本回合 X=…`。

---

## 5. 给接手者的第一小时计划（建议顺序）

1. 读 `docs/plans/boss-watcher-divinity.md`（§0 需求 + 第 15~22 轮进度，那里有每次用户反馈的原话）；
2. **先做能量对账**（用户最恼的就是这个）：把所有「我们补能量」的代码删干净（§2①），然后在游戏里跑一次，
   用日志确认：第 1 回合 3、之后 2、进神格当回合 +3（且只加一次）；
3. 再做意图刷新（§2②）：确认补打通道是否真的进了，必要时改成**整块重规划**；
4. 最后做灭除之刃的展示与段数（§2③④）：**先加日志把 token 牌的位置和 `DynamicVars` 打出来**，再决定是「我们算」还是「读牌自己带的段数」。

---

## 6. 当前 git 状态

```
184003b fix(watcher): 能量口径(3,涅奥-1)/灯笼去重/补打进Plan再刷意图/卡id双版兼容, 去第1回合奇迹
115e44d balance(watcher): 至纯之水两版都不拦+第1回合奇迹(不出) / B版许愿固定永生不死 / 神格力量 3->2(恐惧 4->3)
6aa23e7 feat(watcher): 神格观同时兼容两个观者模组(3747526116 优先) — provider 化引用层
13397b5 fix(mp): 血肉骨吞噬生命只由房主结算(修联机不同步) + BaseLib 自定义消息注册兜底
```

未提交的改动应该为零（最后一条 commit 之后只做了文档追加）。工作区还有两份玩家日志：`godot (11).log`、`mod_log (26).txt`（联机不同步那次，可删）。
