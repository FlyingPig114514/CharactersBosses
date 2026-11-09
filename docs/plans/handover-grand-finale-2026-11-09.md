# 交接报告：华丽猎（SILENT_GRAND_FINALE）与联机/打包现场（2026-11-09）

> **写给下一个会话。先读完这一页再动手。**
> 用户 2026-11-09 明确要求：「你别干了，你这bug越修越多。写交接报告吧，我让下一个回合干」。
> 所以**当前版本处于「用户不满意、几处修错」的中间态**，动手前请先把下面「四、已知踩错的修法」读完，别再走回头路。

---

## 一、现场状态（务必先核对）

| 项 | 值 |
| --- | --- |
| 版本 | **v0.2.524**（`src/CharacterBosses.json` 的 `version` + `src/Hooks/Core/BuildInfo.cs` 的 `Tag` 必须同步改） |
| HEAD | `55d8471`（本地） |
| origin/main | `387d04f`（**落后 12 个提交，全部未推送**） |
| 部署 | 已部署到 `D:\SteamLibrary\steamapps\common\Slay the Spire 2\mods\CharacterBosses`（107/111 均为 v0.2.524，pck 已重打） |
| 创意工坊 | 线上是 **v0.2.513**（item `3830639796`），之后**没有**再上传 |
| 打包 | `.dist\CharacterBosses-v0.2.513.zip` 是最后一次打包产物；0.2.514+ 未打包 |

构建（**两个目标都要编**，编不过不许部署）：

```powershell
.\tools\dotnet\dotnet.exe build src\CharacterBosses.csproj -c Release -p:Sts2Target=107 -t:Rebuild
.\tools\dotnet\dotnet.exe build src\CharacterBosses.csproj -c Release -p:Sts2Target=111 -t:Rebuild
cmd /c 'install-mod.bat "D:\SteamLibrary\steamapps\common\Slay the Spire 2"'   # 需要 danger-full-access 沙箱
```

**验证 DLL 版本必须用「字节层 UTF-16 查找」**，不能把整份文件 `Unicode.GetString` 一次再正则（`#US` 堆偏移不一定是偶数，会读出「无版本串」，我在这上面误判过两次，`tools\pack-dist.ps1` 里的 `Test-BytesContain` 是正确写法，可照抄）。

---

## 二、用户此刻的 6 条未解决 bug（原话，别改写）

1. **弹珠袋又触发两次了**。「应该是**你拦掉原版的，然后仿照原版的在战斗开始时触发一次**。而且你这易伤又持续三回合了」。
2. 「谋杀的伤害偏低」。
3. 「第六回合**生存者应该弃防御**啊，你弃打击干什么？」（我上一版把 T6 的「打击」点名给必备工具丢，用户不认这个做法）
4. 「隐秘匕首和钢铁风暴还是**一个都没有修好**」（预览仍是 `4×2`，钢铁风暴仍只有「强化」意图、没有 `x*x`）。
5. 「第七回合必备工具弃的牌也不对，提前把要上奇巧的防御给弃掉了」→ 我上一版用 `ToolsDiscard` 点名了钢铁风暴，**用户未确认是否生效**。
6. 「第 10 回合手上技法添加的牌也不对，为什么是隐秘匕首？」

另外两条本轮已修但**未经验证**：第三回合手上技法给奇巧、第 9 回合华丽收场不再被画红杠。

---

## 三、华丽猎是什么（用户设计原文，改动必须对得上）

- Key：`SILENT_GRAND_FINALE`（用户：「内部名字用「华丽收场」的英文」），遭遇类 `SilentGrandFinaleBossEncounter` → `SILENT_GRAND_FINALE_BOSS_ENCOUNTER`，**三层静默猎手第 3 套设计**（前两套 = 刀猎 `SILENT_SHIV` 主设计、炼药猎 `SILENT_BREWER`）。
- 特殊效果「**压制**」：每当她打出**华丽收场**时，将一张**虚空**放入**所有玩家**抽牌堆**顶部**（用原版塞牌动画）。
- 遗物：蛇之戒指、涅奥的祝福、**弹珠袋**、佩尔之肉、绷带、布质果实、水银沙漏。
- 进阶 8：升级中和；进阶 9：升级背刺、钢铁风暴、隐秘匕首。藏品（崩坠联动）：**计划妥当**。
- 剧本 10 回合，之后 **8~10 循环**；「拦掉原版的抽牌」「每张弃牌卡的丢弃哪张牌都要按剧本上来」。
- 逐回合剧本（**这是唯一权威**，实现文件 `src/Hooks/Core/GrandFinaleSilentScript.cs`）：

```
1. 计划妥当、背刺、华丽收场(保留不出)、防御(保留不出)、笨拙(诅咒)
2. 肾上腺素、夜魇+(夜魇杂技)、杂技(丢弃战术大师)、战术大师(奇巧)、隐秘匕首(丢弃2张防御)、
   铭记死亡、防御*2(被丢弃)、防御(保留不出)、华丽收场(保留不出)
3. 中和、手上技法(为防御添加奇巧)、钢铁风暴(丢弃剩下所有手牌共5张→打出等量小刀)、
   防御(奇巧)、杂技*3(被丢弃)、华丽收场(被丢弃)
4. 谋杀、必备工具+、偏折
5. 打击、打击、防御、打击(被必备工具丢弃)
6. 生存者、打击、尖啸、防御(被生存者丢弃)
7. 洗牌。杂技、手上技法(为防御添加奇巧)、打击、隐秘匕首(丢弃防御，只丢一张)、防御(奇巧)、
   钢铁风暴(被必备工具丢弃)、打击(被杂技丢弃)
8. 杂技(弃战术大师)、战术大师(奇巧)、谋杀、偏折、生存者(被必备工具丢弃)、防御(保留不出)、打击(保留不出)
9. 杂技、铭记死亡、中和、防御、华丽收场、打击(被必备工具丢弃)、防御(被杂技丢弃)、
   防御(保留不出)、打击(保留不出)
10. 洗牌。杂技、手上技法(为防御添加奇巧)、钢铁风暴(丢弃剩下所有牌共4张)、防御(奇巧)、
    打击(被必备工具丢弃)、防御(被杂技丢弃)、隐秘匕首(被钢铁风暴丢弃)、打击*2(被钢铁风暴丢弃)
```

- **用户给的谋杀期望值**（判断对不对就看这个）：每回合新进手牌数 = `5, 8, 3, 3` ⇒ **第 4 回合伤害 = 1+19 = 20**；再 `4, 4, 7, 7` ⇒ **第 8 回合 = 42**。
  - 口径：**夜魇印出来的牌不算**（那是剧本里写的复制品）；**上一回合被计划妥当留在手上的同类型牌不算**（哪怕这回合它换角色去被丢弃）。

---

## 四、已知踩错的修法（**别再重复**）

1. **弹珠袋**：我先后试过三种，全被用户否掉：
   - ① 手动补一次 + 压制原版 → 用户看到 **3 层**（当时原版还触发两次：`FireCombatStart` 替遗物补触发一次 + 她第 1 回合 `BeforeSideTurnStart` 一次，两者都满足 `TurnNumber <= 1`）；
   - ② 干脆全交给原版（v0.2.524 现在的状态）→ 用户看到 **又触发两次**，且**易伤持续三回合**；
   - ③ 手动补一次 + 在她第 2 回合开始手动 tick → 用户仍说「第二回合没消失」。
   - **用户要的做法**：**压掉原版那次**，然后**在战斗开始时仿照原版手动触发一次**，并且那层易伤必须**在他第 2 回合开始前消失**。
   - **关键线索（本次反编译到的，下个会话直接用）**：
     - `VulnerablePower.AfterSideTurnEnd(choiceContext, side, participants)` 里**只有 `side == CombatSide.Enemy`** 才 `PowerCmd.TickDownDuration(power)`；
     - `PowerCmd.TickDownDuration` 里会检查 **`power.SkipNextDurationTick`**，为真就跳过这一次 tick。
     - ⇒ 极可能就是因为「手动施加发生在战斗开始/她的回合附近 ⇒ `SkipNextDurationTick` 为真 ⇒ 她第 1 回合结束那次 tick 被跳过 ⇒ 一直挂到第 3 回合」。
       **下一步**：手动施加后立刻把 `power.SkipNextDurationTick = false`（该字段可读写，反编译确认），或者改为在**玩家第 1 回合开始前**施加。这次**只需要改这一处**，不要再加别的补丁。
     - 现成的「拦原版 + 手动补一次」样板：`src\Hooks\Patches\ShadowRedMaskPatch.cs`（红面具的虚弱）、`src\Hooks\Patches\BossRelicHookBridge.cs` 里的 `BossRelicSuppressBagOfMarblesPatch`（现在被我改成 `return true` 放行了，要改成压掉）。
2. **必备工具（ToolsOfTheTrade）弃牌**：它从第 4 回合起每回合开始「抽 1 弃 1」。我加了 `ScriptCard.ToolsDiscard` 标记 + `BossCardSelector.TakeToolsDiscard`（每回合消费一次，优先挑标记那张）。
   - 用户只认「第 7 回合弃钢铁风暴」「第 8/9/10 回合弃对应的牌」，但**第 6 回合他要求「生存者弃防御」而不是「必备工具弃打击」**——也就是说第 6 回合的「必备工具那一次弃牌」到底弃哪张，得**回去问用户**（或者让他确认「第 6 回合允许必备工具弃掉打击、从而打不出打击」）。
3. **谋杀**：我试过两种口径（`CardsDrawnThisCombat`、`MurderDrawnCounter`），用户仍说偏低。现在的实现是「每回合新进手牌」累加（`CharBossMonster.MurderDrawnCounter` + `LastKeptTypes`）。**先让他给一次实际数字**：日志里的 `谋杀记账：本回合新进手牌 N 张 ⇒ 累计 M` 与 `谋杀：… ⇒ 伤害 1 + 1×M = X`，对照他的 20/42。
4. **小刀预览 / 钢铁风暴**：**两条不同的路径**，别搞混：
   - `BossIntentBuilder.IntentsFor` 里的 `shivs > 0` 分支 → 生成 `LiveMultiAttackIntent`，每刀伤害走 `CleanShivDamage → ShivDamagePerHit(..., shivBase)`，`shivBase = 4 + (GeneratesUpgradedShivs(card) ? 2 : 0)`，`GeneratesUpgradedShivs` 现在已认 `HIDDEN_DAGGERS`/`STORM_OF_STEEL` 的升级版（诊断日志实测打印「升级刀基础=6」）；
   - `SilentBlades.FrontloadRealShivs` / `ShivPlays` → 用**真的 Shiv 卡实例**渲染每把小刀，日志是 `小刀预览: 牌面 4 → …`（用户看到的 4×2 极可能就是这条路，因为**我们造的 Shiv 卡是未升级的**）。
   - ⇒ 下一步：让「我们造的 Shiv 卡实例」在生成者为升级版时也 `UpgradeInternal()`（改 `SilentBlades`/`ShivCardFor` 一带），并把 `StormOfSteel` 补进 `BossIntentBuilder.ShivGeneratorCards` 的等价逻辑（现在靠 `shivOverride` 传段数，来自 `BossScriptPlanner.stormDiscards`）。
   - 钢铁风暴「只有一个强化意图」：诊断日志 `小刀段数诊断：… 剧本丢弃=N ⇒ 意图 M 条` 已经加上，等他下一次日志确认是「段数 0」还是「意图条数 0」。
5. **手上技法（HandTrick）把奇巧给错牌**：剧本用 `BossTurn.PickOrder = [DefendSilent]` 点名，但 `BossCardSelector` 的「选择队列」会被同一回合里**别的选择**（杂技的弃牌、必备工具的弃牌）抢先消费。我这版改成「带弃牌点名的选择不吃队列 + 兜底阶段再用一次点名」，用户报的第 10 回合问题仍未解。
   - ⇒ 更稳的做法：**给 `PickOrder` 加「用途」维度**（`PickOrderFor = { 弃牌 / 加奇巧 / 夜魇 }`），或者干脆在剧本里给「要上奇巧的那张防御」一个独立标记，由我们自己在她打出手上技法时 `CardCmd.ApplyKeyword(Sly)` 到那张牌上，并把原版的「选一张技能牌」拦掉。

---

## 五、华丽猎的实现落点（改代码从这几处进）

| 内容 | 文件 |
| --- | --- |
| 剧本（10 回合 + 8~10 循环、弃牌点名、`PickOrder`、`Kept/Discard/SlyDiscard/NotDrawn/ToolsDiscard` 小工具） | `src\Hooks\Core\GrandFinaleSilentScript.cs` |
| 特殊效果「压制」 | `src\Hooks\Powers\BossSuppressionPower.cs`（`AfterCardPlayed` 里认 `GRAND_FINALE`，`CardPileCmd.AddGeneratedCardToCombat(..., PileType.Draw, player, CardPilePosition.Top)` + `CardCmd.PreviewCardPileAdd`） |
| 变体登记（Deck/Relics/Script/SpecialLoc/SpecialPower/进阶/各开关） | `src\Hooks\Core\BossDefs.cs` 里 `Silent` 的 **`ExtraVariants[2]`**（`VariantsByAct[2]` 已被炼药猎占用） |
| Key / 花名册 / 设计名 / 遭遇映射 / 补位 | `src\Hooks\Core\BossKeys.cs`（`SILENT_GRAND_FINALE`，Number=3） |
| 遭遇类（`TierAct => 2`、地图图标 `silent_shiv`） | `src\Hooks\Encounters\TierBossEncounters.cs` 的 `SilentGrandFinaleBossEncounter` |
| 变体判定（必须**显式**排除另外两套遭遇，否则炼药猎会按种子抢场） | `src\Hooks\Core\BossLineups.cs` 的 `GrandFinaleActive()` / `ComputeGrandFinale()` / `ComputeBrewer()` |
| 藏品（计划妥当 `WellLaidPlans`） | `src\Hooks\Core\DownfallCollectibleCompat.cs`（`SILENT_GRAND_FINALE` 与 `SILENT_GRAND_FINALE_BOSS_ENCOUNTER` 两个 switch 分支都加了） |
| 本地化 | `content\localization\*\monsters.json`（`SILENT_GRAND_FINALE_BOSS.name/title/moves.CHARBOSS_PLAY.title` + `SILENT_BOSS.suppression`）与 `content\localization\*\powers.json`（`BOSS_SUPPRESSION_POWER.title/description`），16 语言均已补 |

### 我新加的剧本/变体开关（都在 `BossScript.cs` / `BossDefinition.cs`）

- `ScriptCard.EmptyDrawPileFirst`：打这张牌**之前**先清空她的抽牌堆（华丽收场要求抽牌堆为空）。现在在**造手牌时**就清（`BossScript.ClearDrawPileFor`），所以第 9 回合它一进手就可打出。
- `ScriptCard.NotDrawn`：这张不算「她抽到的牌」（夜魇复制的杂技），只影响谋杀记账。
- `ScriptCard.ToolsDiscard`：被「必备工具」在回合开始丢掉的那张（`BossCardSelector.TakeToolsDiscard` 优先挑它）。
- `BossVariant.BlocksVanillaDraw` + `BossDefinition.BlocksVanillaDrawFor(act)`：拦原版抽牌（只对华丽猎开）。
- `BossVariant.BlocksNightmareClones` + `...For(act)`：拦夜魇复制印牌（`CharBossMonster.SettleNightmareAsync` 只移除 power、不复制）。
- `BossVariant.MurderCountsDrawnCards` + `...For(act)`：谋杀按「本场新进手牌」起步（走 `MurderDrawnCounter`）。

---

## 六、其它本轮改动（**未经验证**，下一个会话可以先放一放）

- **力量/集中不再以 `silent: true` 施加**（用户抱怨「能力机获得集中/力量时头上没有绿色飘字、也没有音效」）：我用正则把 8 个文件的 `PowerCmd.Apply<X>(…, null, true)` 改成 `false`（`BossHookBridge.cs` / `BossHookBridge.SideTurn.cs` / `BossOsty.cs` / `BossCallToArmsPower.cs` / `BossLightningRodPower.cs` / `DeviationPower.cs` / `DeviationPower.Mirror.cs` / `NotYetPower.cs`）。**没有实测**，若飘字/音效仍然没有，说明还有别的静默点或演出被我们的面板抑制。
- 用户反复强调的一条口径：**「你就不能用原版的直接搞吗？别乱加东西」** —— 凡是能走原版 API / 原版演出的，一律走原版；需要接管的地方，**先压掉原版、再照原版的样子补一次**（红面具就是样板），不要留「两边都跑」的中间态。

---

## 七、其它线程（与华丽猎无关，别顺手改）

- **联机阵容**：`v0.2.503` 起多人局一律用整张花名册（房主勾选只影响单人）；`v0.2.515` 修了「三层第二场阵容无限递归崩溃」（`BossLineups.For` 加了重入保护 + 读存档原值）。
- **斗蛐蛐 PR #1 已并入**（`779e819`，正常局零变化：说明框仍走原版 `NHoverTipSet`，飞牌动画只在装了斗蛐蛐时启用）；评审细节见 `docs\plans\pr1-cricket-review.md`。
- **打包护栏**：`tools\pack-dist.ps1` 会校验两份 Impl DLL 的版本串必须等于清单版本，否则拒绝打包。
- **`.ps1` 一律 UTF-8 带 BOM**（`edit` 工具会吃掉 BOM，改完必须补回并用 `[System.Management.Automation.Language.Parser]::ParseFile` 校验）。

---

## 八、验证方式

沙箱不能启动游戏，**一切实机验证靠用户**。用户会贴 `%APPDATA%\SlayTheSpire2\logs\mod_log.txt`（本机可**直接读**，不要让他手动粘贴）或截图。
日志里我已经埋好的诊断行（下一个会话直接用）：

- `谋杀记账：本回合新进手牌 N 张 ⇒ 累计 M` / `谋杀：她**保留在手**期间抽到 M 张 ⇒ 伤害 1 + 1×M = X`
- `小刀段数诊断：<牌>（升级=…） 剧本丢弃=N 张 ⇒ 意图 M 条（升级刀基础=…）`
- `小刀预览: 牌面 4 → X（Shiv卡实例=…，目标易伤=…）`
- `自动选择：按剧本**点名**挑中 X` / `自动选择：兜底阶段按剧本**点名**挑中 X` / `自动选择：必备工具按剧本点名丢掉 X` / `自动选择: 候选 N 个 → 选 [...]（第 T 回合的点名：…）`
- `弹珠袋：施加后 <玩家> 的易伤层数 = N`（这行在 v0.2.524 已被我删掉，需要的话加回来）
- `玩家侧快照（她第 N 回合出手前）：… powers=[VULNERABLE_POWER:N]`

---

## 九、建议的下一步（按优先级）

1. **先问用户三个问题**（别的都能猜，这三个不能）：
   - 第 6 回合「必备工具」那一张到底弃什么？（他要求生存者弃防御，那必备工具弃的必须是打击以外的牌，但手牌只有 生存者/打击/尖啸/防御）
   - 钢铁风暴的 `x*x` 他期望是「丢几张就打几刀」对吧？低进阶（未升级钢铁风暴）小刀 4 点、进阶 10（升级）6 点，确认这条。
   - 谋杀他希望的**最终数字**用他那条公式即可（20 / 42），是否允许「第 4 回合之后不再变」。
2. **弹珠袋**：压掉原版 + 战斗开始手动补一次 + `SkipNextDurationTick = false`（只改这一处）。
3. **小刀**：让我们造的 Shiv 卡实例跟随生成者的升级状态（`SilentBlades` 一路），并把 `StormOfSteel` 的段数接到同一条渲染路径上。
4. **手上技法**：给 `PickOrder` 加用途维度，或直接拦掉「选一张技能牌」自己加奇巧。
5. 全部改完再一起编译/部署/让用户验一轮；**不要再一次只改一条就发布**（用户已经因此炸了）。
