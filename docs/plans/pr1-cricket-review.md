# PR #1 评审：`feat(cricket)` 斗蛐蛐适配（作者 eternitylarva1）

> **状态：已合入本地 main（`779e819` / `v0.2.512`），未推送、未打包。** 落地前做了 4 项修正，见文末「九、实际落地情况」。
> 基线 `522d3c4 (v0.2.503)`，合入时已自动 rebase 到 `b0d2e1d (v0.2.511)`（唯一冲突是 3 个 monsters.json，按两侧键并集解决）。
> 规模：25 个源码/配置文件 +1679/−223，另 +16 语言本地化与 1 份计划文档。

> ⚠ **勘误（2026-11-08 复核）**：下面第三节原先把 `BossOsty.cs` 里 96 行 U+FFFD 乱码算在 PR 头上，**这是错的** ——
> 逐行比对确认 main（`v0.2.511`）里**本来就有这 96 行乱码**，PR 只是原样带着它们。
> 也就是说这是本仓库自己的历史遗留（某次编辑器用 GBK 往返写过这个文件），不是 PR 引入的，
> 但**仍然建议修**（注释全是问号，读代码时会被误导）。乱码只落在注释里，3 行「代码+行尾注释」里的代码部分是好的。

## 结论（先看这一句）

**逻辑上对原版（不玩斗蛐蛐时的正常单人/联机）基本零侵入** —— 没有新增任何 Harmony 补丁，
所有新增开关默认关闭或默认走老路径。但有 **3 处「正常局也会变」的观感/日志改动**，
其中「特殊效果说明框」这一项等于**把我们之前主动删掉的自绘面板又加回来了**，需要你拍板。

## 一、确认不影响原版的证据

| 项 | 证据 |
| --- | --- |
| 没有新补丁 | `git diff main...pr-cricket -- src` 里 `[HarmonyPatch]` 新增 **0 条** ⇒ 原版方法的调用点一个都没动 |
| 档位钉死默认关闭 | `BossDefinition.PinnedVariantKey` 默认 `null`；`ActiveVariant()` 有 pin 才走新分支，否则仍是「遭遇→种子」老逻辑 |
| 幕数钉死默认关闭 | `CharBossMonster.TierAct` 默认 `null`；`ActIndex` 里 `TierAct` 判空后才用，正常局仍是「调试→遭遇→真实幕数」 |
| 翻侧抑制只在斗蛐蛐触发 | `PlayerSideBossDepth` 只在 `SafeSide(Creature) == "Player"` 时 ++（正常局 Boss 恒为 Enemy ⇒ 恒不触发） |
| HUD 层选择向后兼容 | `BossHudLayer.Resolve()`：`%PlayContainer` 可见就用它（正常局永远可见）⇒ 与改动前一致；只有整层被隐藏（斗蛐蛐）才自建 `CanvasLayer(80)` |
| 30 个新怪物类不进任何池子 | 反编译确认 `ModelDb.Monsters = Acts.AllMonsters ∪ EventEncounters.AllPossibleMonsters`；这 30 个类**不挂 Act/Encounter** ⇒ 不进怪物库、不进 Boss 池、不进图鉴（作者实测 `ModelDb.Monsters` 改动前后都是 107） |
| 选靶逻辑 | `ChooseTarget` 只是加了「锁定目标与 Boss 同阵营就不采纳」的过滤，正常局锁定目标恒为玩家（异阵营）⇒ 结果不变，只多一行日志 |
| 奥斯提朝向 | `FacingDir` 改成按阵营定（`Side==Player ? +1 : -1`），正常局她是 Enemy ⇒ −1，与原几何算法同向 |
| 设置界面 | 斗蛐蛐那一段 `if (CricketCompat.Installed)` 才构建（按程序集名 `CricketFightMod` 探测） |

## 二、正常局**会变**的地方（需要你决定要不要）

1. **特殊效果说明框整体换了实现**（`BossSpecialUi` 重写 + 新增 `BossSpecialPanel` 295 行）
   - 现状（main）：用原版 `NHoverTipSet.CreateAndShow(anchor, tips, Right).SetFollowOwner()`，字体/配色/换行全是原版，钉在立绘右侧。
     **当初正是因为「自绘 PanelContainer 定位算错、跑到左上角」才整段删掉的**（见 `BossSpecialUi` 顶部注释）。
   - PR：又改回自绘小面板，可拖动，默认贴**左上/右上 y=212**，位置与字号存 `%AppData%\SlayTheSpire2\CharacterBosses\hud_layout.json`。
   - 影响：所有战斗的说明框外观/位置都会变，且不再跟随 Boss 位置。
   - 建议：要么这一段受 `CricketCompat.Installed`（或单开一个「用自绘说明框」开关）控制，要么你确认要全局换成这一版。
2. **出牌飞牌动画默认开启**（`CricketFxEnabled = true`，200ms 飞 + 180ms 停 + 200ms 淡出）
   - 正常局也会看到 Boss 手牌「飞向出牌区」——观感变化（逻辑无影响）。
3. **手牌栏宽度只增不减**（`BossHandPanel.Layout.cs`：`Math.Max(_turnCards, _widthCards)`），牌打完后不再收窄。
4. **日志量上升**：`选靶(锁定/玩家侧/对手侧)` 每张牌一行、`PLAY … self=…(side=…)`、
   而 `BossOsty.FacingDir` 里的去重写错了 —— `if (n > 0 && !_facingLogged.Contains(0UL))`
   那个 `0UL` 永远不在集合里 ⇒ **每次调用都打一行**（正常局她自己那只奥斯提也会刷）。

## 三、合入前必须处理的 4 件事

1. **`src/Hooks/Core/BossOsty.cs` 有 96 行乱码（U+FFFD）**，还有文件头多出一个 BOM 字符（`+﻿using System;`）。
   作者那边的编辑器把中文往返写坏了（`翻回来）�?` 这种）。这不是小瑕疵 —— 本仓库的 `.ps1` 就因为这个静默挂过，
   中文注释损坏会让后续 review/diff 全部失效。**必须让他用 UTF-8 重新存一次**（或我手工重写该文件）。
2. **`src/Hooks/Patches/CombatSidePatches.cs` 有一行注释被吞掉后拼接**（`…一律走原版，不翻。            //     （玩家被打…`）。
3. **rebase 到当前 main**：PR 不含 `v0.2.504~0.2.509`（蛇咬猎 4 个修正、手牌顺序、**杂交修复**）。
   其中 v0.2.509 修的是「亡灵契约师变体表里误粘打击战」，与他 `NecrobinderDoomBoss` 的 pinned 方案不冲突（那套走 key 精确匹配）。
4. **版本号没 bump**：`src/CharacterBosses.json` 的 `version` 与 `BuildInfo.Tag` 都要动（否则玩家日志分不清版本）。

## 四、另外两点要留意（不是阻塞项）

- `MonsterModel.Def` 被改名为 `DefSource`、`Def` 变成非虚属性。本仓库目前只有 5 个基类 override，能编过；
  但这是**对外语义变更**：以后任何按类 override `Def` 的代码（含第三方扩展）都会编译失败。
- 新增 30 个 `AbstractModel` 子类 ⇒ mod 的内容清单变长。联机时两端**必须同一版本**（本来就是这个要求），
  但升级后老录像/斗蛐蛐 `last_teams.json` 的类名要能对上（作者在 PR 第七节自己也列了这个坑）。
- 他自己在 PR 里列的 2 个**斗蛐蛐侧** bug（`SavedMonsterData.ToConfig()` 只遍历 `ModelDb.Monsters`、
  `CricketRedirectPower` 让格挡对转运伤害无效）不在本仓库范围内，属于那个模组要修。

## 五、我的建议

**方案本身可以接受**（「一个档位 = 一个怪物类 + 只钉 pin」确实是侵入最小的做法），但**不要原样 merge**：

1. 让作者 rebase 到最新 main、修掉 `BossOsty.cs` 乱码、bump 版本号；
2. 让他把「自绘说明框」和「飞牌动画」收进开关（默认关 / 或至少默认只在斗蛐蛐下生效）——
   正常局保持现在的原版说明框，你先看习惯的那个别动；
3. 剩下的（HUD 层回退、阵营朝向、翻侧抑制、UI 会话复位、日志去重）都是净收益，可以收。

## 九、实际落地情况（v0.2.512，提交 `779e819`）

| 项 | 处理 |
| --- | --- |
| 3 个 `monsters.json` 冲突 | 取两侧键**并集**：HEAD 的 `SILENT_BOSS.snake_form` + PR 的 90 个档位键；三个文件都过 JSON 校验（139 键） |
| 特殊效果说明框 | **分流**：正常局仍用原版 `NHoverTipSet`（新文件 `UI/BossSpecialTip.cs`，逐字搬自 v0.2.511）；只有「装了斗蛐蛐」或「原版战斗 UI 被整层隐藏」才走 PR 的自绘面板（`BossSpecialRichUi`）。分发在 `UI/BossSpecialUi.cs`，判据 `BossHudLayer.CombatUiVisible()` |
| 出牌飞牌动画 | `BossHandPlayFx.Fly` 先要求 `CricketCompat.Installed` ⇒ 正常局观感不变，斗蛐蛐里默认开（配置项 `cricketFxEnabled` 仍可控） |
| `FacingDir` 日志刷屏 | 原写法 `!_facingLogged.Contains(0UL)` 恒为真 ⇒ 每次调用都打一行；改成 `_facingLogged.Add(boss.Creature?.CombatId ?? 0)`，每只 Boss 只打一次 |
| `CombatSidePatches` 注释被吞 | 已拆回两行（PR 引入的，不是历史遗留） |
| 版本号 | `0.2.512` + `BuildInfo.Tag` 已更新 |

**未动 / 仍待办**
- `BossOsty.cs` 的 96 行乱码：**历史遗留**，建议单独一次清理（按上下文重写中文注释，风险低但量大）。
- `BossPlanner.ChooseTarget` 里每张牌一行的 `选靶(...)` 日志：斗蛐蛐调试用，正常局也打；量不大，先留着。
- PR 作者 fork 上的分支没动（我们只在本地合入）；要不要在他 PR 下回帖说明处理方式，等你定。
