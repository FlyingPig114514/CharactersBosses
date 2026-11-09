# 贴给新对话的开场白（复制 `---` 之间的整段）

---

先读 `docs\plans\handover-grand-finale-2026-11-09.md`（华丽猎交接报告），再动手。

**任务**：修华丽猎（`SILENT_GRAND_FINALE`，三层静默猎手第 3 套设计）剩下的 6 条 bug。

**优先级 1 · 弹珠袋**：要的做法是「**压掉原版那次触发**，然后**在战斗开始时仿照原版手动触发一次**，带原版的飘字与音效」，并且那 1 层易伤**必须在玩家第 2 回合开始前消失**。
- 前一个会话踩错的三种做法（别再重复）：① 手动补 + 压制原版 → 用户看到 3 层；② 干脆全交原版 → 又触发两次、且持续三回合；③ 手动补 + 第 2 回合手动 tick → 用户仍说没消失。
- 反编译线索：`VulnerablePower.AfterSideTurnEnd` **只在 `side == CombatSide.Enemy` 时才 `PowerCmd.TickDownDuration`**，而 `TickDownDuration` 会跳过 `power.SkipNextDurationTick == true` 的那一次 ⇒ 我们手动施加后就一直挂着、拖到第三回合。下一步：手动施加后把这个标志清掉（或改成在玩家第 1 回合开始前施加）。
- 「压原版 + 开局补一次」的样板：`src\Hooks\Patches\ShadowRedMaskPatch.cs`（红面具的虚弱）；`BossRelicHookBridge.cs` 里的 `BossRelicSuppressBagOfMarblesPatch` 现在是 `return true`（放行原版）——要改回压掉。

**优先级 2 · 谋杀伤害偏低**：期望第 4 回合 = **20**（1 + 5+8+3+3）、第 8 回合 = **42**（再 +4+4+7+7）。口径：**夜魇印出来的牌不算**、**上回合被计划妥当留在手上的同类型牌不算**。日志里有 `谋杀记账：本回合新进手牌 N 张 ⇒ 累计 M` 与 `谋杀：… ⇒ 伤害 1 + 1×M = X` 可直接对。

**优先级 3 · 小刀（隐秘匕首 / 钢铁风暴）**：要「丢几张打几刀」的 `x*x`，进阶 10（升级版）每刀 **6**、低进阶 4。现在面板仍是 `4×2`、钢铁风暴只有「强化」没有伤害。⚠ 这是**两条渲染路径**，别搞混：
- `BossIntentBuilder.IntentsFor` 的 `shivs > 0` 分支 → `LiveMultiAttackIntent`，每刀伤害 `CleanShivDamage → ShivDamagePerHit(..., shivBase)`（`shivBase` 已经会算成 6）；
- `SilentBlades.FrontloadRealShivs` → 用**真 Shiv 卡实例**渲染（日志 `小刀预览: 牌面 4 → …`），我们造的 Shiv 卡是**未升级的** ⇒ 极可能就是这里显示 4。
- 钢铁风暴的段数来自 `BossScriptPlanner.stormDiscards`（诊断日志 `小刀段数诊断：… 剧本丢弃=N ⇒ 意图 M 条`）。

**优先级 4 · 手上技法把奇巧给错牌**（第 3 / 7 / 10 回合应该给「防御」）：`BossTurn.PickOrder` 会被同回合其它选择（杂技的弃牌、必备工具的弃牌）抢先消费。建议给 `PickOrder` 加「用途」维度，或直接拦掉原版「选一张技能牌」由我们自己给那张牌加奇巧。

**优先级 5 · 必备工具弃错牌**：第 7 回合要弃「钢铁风暴」、第 6 回合按报告里的说明处理（见下面要问我的问题）。

**优先级 6**：第 9 回合「华丽收场」不该被画成不可打出（1~3 回合的确实应该不可打出）——上一版改过但未验证。

**必须遵守的口径**：
- 「**你就不能用原版的直接搞吗？别乱加东西**」——能走原版 API / 原版演出的一律走原版；必须接管的，就**先压掉原版、再照原版的样子补一次**，不要留「两边都跑」的中间态。
- **不要一次只改一条就发布**。攒一批、两个目标都编译通过、再一次性部署让我验一轮。
- 版本号两处一起改：`src\CharacterBosses.json` 的 `version` 与 `src\Hooks\Core\BuildInfo.cs` 的 `Tag`。
- 编译 `-p:Sts2Target=107` 与 `111` 各一次（都通过才部署）；部署 `cmd /c 'install-mod.bat "D:\SteamLibrary\steamapps\common\Slay the Spire 2"'`（需要 danger-full-access；游戏开着会导致 pck 写不进去）。
- 验证 DLL 版本用**字节层 UTF-16 查找**（`tools\pack-dist.ps1` 里的 `Test-BytesContain` 可照抄），不要把整份文件按 Unicode 解一次再正则。
- 中文 `.ps1` 一律 UTF-8 **带 BOM**（`edit` 会吃掉 BOM，改完补回并 `Parser::ParseFile` 校验）。
- 我不能启动游戏，实机验证靠我；本机 `%APPDATA%\SlayTheSpire2\logs\mod_log.txt` 你可以直接读。

**动手前先问我这三个问题**（不能猜）：
1. 第 6 回合「必备工具」那一次弃牌应该弃哪一张？（手牌只有 生存者 / 打击 / 尖啸 / 防御，而我要求生存者弃防御）
2. 钢铁风暴的 `x*x` = 「丢几张打几刀」对吧？低进阶 4 点、进阶 10 是 6 点？
3. 谋杀的伤害就按 20 / 42 算、之后继续按「每回合新进手牌」累加，对吧？

---
