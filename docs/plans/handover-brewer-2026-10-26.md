# 交接：三层「炼药猎」+ 药水栏（2026-10-26 夜）

> 写给**下一个会话**。用户已经把「炼药猎」做出来了，但药水栏还有几个视觉/刷新问题没解决，
> 用户让换人接手。**先读完这一页再动手**，别重复我已经踩过的坑。

---

## 1. 环境与必须遵守的流程

| 事项 | 值 |
|---|---|
| 工作区 | `D:\Sts2ModProject\Downfall Expansion Vanilla Characters as Bosses` |
| 构建 | `& tools\dotnet\dotnet.exe build src\CharacterBosses.csproj -c Release -v m`（必须 0 错误） |
| 安装 DLL | 复制 `src\bin\Release\CharacterBosses.dll` → `D:\SteamLibrary\steamapps\common\Slay the Spire 2\mods\CharacterBosses\`（工作区外，需要 `danger-full-access`） |
| 重建 PCK（**只改了 `content/` 才需要**） | `& tools\godot\Godot_v4.5.1-stable_mono_win64.exe --headless --path "<repo>\tools\pck_builder" --script "<repo>\tools\pck_builder\build_pck.gd" -- "<repo>\src\CharacterBosses.json" "<repo>\src\CharacterBosses.pck" "<repo>\content" "CharacterBosses" "<repo>\tools\art\pck_extras.txt"`（stderr 有 Godot 报错、退出码 1 都是正常的；看到 `Packed` 就行） |
| 反编译 | `& tools\dotnet\dotnet.exe tools\ilspy-cli\bin\Release\net10.0\ilspycli.dll {types\|type\|members\|method\|search} references\sts2.dll …` |
| 看录屏 | `docs/plans/how-to-read-video.md`（ffmpeg 在 `C:\Users\<user>\AppData\Local\Programs\xiezuo\resources\ffmpeg\ffmpeg.exe`，**只能输出 jpg**） |
| 日志 | `%APPDATA%\SlayTheSpire2\logs\mod_log.txt`（用户退出游戏后才写完） |
| 中文本地化真源 | `.recon\pck\localization\zhs\*.json`（cards/relics/potions/monsters/powers） |

**铁律**（都是这次踩出来的）：
1. **每次改 DLL 必须改 `src/Hooks/Core/BuildInfo.cs` 的 `Tag` 版本号**，否则日志里分不清装的哪版（我漏了两次，排查白费两轮）。
   **编号规则（用户 2026-10-26 定稿，别再跳段）**：它是**构建指纹**不是语义化版本 ——
   **每改一次 DLL 就 +0.0.1（0.2.12 → 0.2.13 → …），永不跳段、永不重编号**；
   `Tag` 与 `src/CharacterBosses.json` 的 `version`（打进 PCK 的 `mod_manifest.json`，游戏 mod 列表显示的就是它）**必须同时改**。
   历史教训：上一会话把 `0.1.98` 直接跳到 `0.2.0`、我又把 `0.2.9` 跳到 `0.3.0`，两次都让日志难以比对；
   **v0.3.0/v0.3.1/v0.3.2 三版已统一改标为 v0.2.10/v0.2.11/v0.2.12**（= 0.2.5 之后第 5/6/7 次构建）。
2. **不要猜 Harmony 签名**。我曾用猜的参数数组挂 `CreatureCmd.Damage`，导致整个模组 **DLL 初始化失败**（游戏卡、Boss 不动）。要挂就先用 ilspycli 看到真签名，或者只用方法名匹配。
3. **不要在每帧路径上做字符串拼接/指纹**（我做了一次，直接把游戏拖到十几帧，已回退）。
4. 单人必须零回归；改完要构建 + 装 + 让用户验。

当前装机版本：**DLL sha256 `494BA61E96A3A8273FF5EF4B9DB19852D1399247B114824FD7DF7737E142DE1F`**，
PCK `D9A81A2C3AD4F1D068CF24F941AC82B6C444C7BE679C2394C58D9334EF808F69`，构建指纹 **v0.2.27**
（2026-10-26 晚；`rowChildren` 乱跳与药水栏不刷新已按 `docs/plans/brewer-potion-row-2026-10-26b.md` 修掉，
**先读那一页**，别再按下面 §3A 的旧猜测动手）。
⚠ 每改一次 DLL 都要 bump `src/Hooks/Core/BuildInfo.cs` 的 `Tag`（+0.0.1）并同步 `src/CharacterBosses.json` 的 `version`。

---


> 本轮（意图数值 / 群蛇形态 / 追踪锁定 / 无来源警告）全部写在 `docs/plans/brewer-intent-values-2026-10-26.md`。
> **变体级 Boss 标识 + 三人联机阵容**写在 `docs/plans/boss-variants-and-multiplayer-roster.md`。

## 2. 「炼药猎」是什么、在哪

三层静默猎手的**第二套设计**（第一套是刀猎），按本局种子 **50/50** 出现；
但**战斗内以房间遭遇为准**（`SILENT_BREWER_BOSS_ENCOUNTER` ⇒ 一定炼药猎），控制台：
`fight SILENT_BREWER_BOSS_ENCOUNTER`。

| 文件 | 作用 |
|---|---|
| `src/Hooks/Core/SilentBrewerScript.cs` | 卡组 / 遗物 / 初始 8 瓶药水 / 8 回合剧本（5～8 循环） |
| `src/Hooks/Core/BossPotions.cs` | 药水底层：补栏位、塞药、喝药（按 TargetType 选目标）、`Version` 版本号、`PendingBrew` |
| `src/Hooks/Core/BossScript.cs` | `BossTurn.Potions`（`ScriptPotion`）、`ScriptCard.BrewPotion` / `DrinkBefore` |
| `src/Hooks/Core/BossDefs.cs` | `Silent.VariantsByAct[2]` = 炼药猎（`BossVariant`） |
| `src/Hooks/Core/BossDefinition.cs` | `BossVariant`、`PotionsByAct` / `HealPerPotionByAct` / `PotionSlotsByAct`；**SpecialLoc/SpecialPower 是「变体优先」**（否则会回退成刀猎的「袖里乾坤」） |
| `src/Hooks/Core/BossLineups.cs` | `SilentBrewerAt3()`（阵容用）、`BrewerActive()`（**设计**用，以房间遭遇为准） |
| `src/Hooks/Powers/BrewModPower.cs` | 「调制」的可视载体（状态栏图标 + 说明） |
| `src/Hooks/Patches/AlchemizeBrewPatch.cs` | 炼制药水改炼指定药水 |
| `src/Hooks/Patches/BossFairyInABottlePatch.cs` | 瓶中精灵（prefix on `Hook.ShouldDie`）+ 触发时清负面 |
| `src/Hooks/UI/BossPotionRow.cs` | **药水格子的渲染（问题都在这里附近）** |
| `src/Hooks/UI/BossStatusRow.cs` | 状态条：药水 → 遗物 → 能量 → 星；`Fill` 会销毁整排重建 |
| `docs/plans/boss-silent-brewer.md` | 设计稿 + 名字映射 + 待办 |

「调制」现行口径（用户 2026-10-26 定稿）：8 个药水栏位 / 每用一瓶回 5 血 / **她可以格挡「中毒」和「魂劫（崩坠·收藏家的 Doom）」造成的伤害（只对自己、不改这两者的结算时序）** / 瓶中精灵触发清全部负面。
✅ 「可格挡中毒 + 魂劫」**v0.2.10 已实现**：`src/Hooks/Patches/BossBlockableDotPatch.cs`（做法见 §3C）。

---

## 3. 现在**没解决**的三件事（按优先级）

### A. 喝药后药水栏不实时变化（**v0.2.6 已修，见 `brewer-potion-row-2026-10-26b.md`**）

> ⚠ 下面这段是当时的**错误猜测**，留着当反面教材：`rowChildren` 15/29/31 的真实原因是
> **Godot 的 `QueueFree()` 只是「排队到帧末删除」，旧格子此刻还挂在树上**，被 `GetChildCount()`
> 和同一帧的 `PlaceBesideCards` 排版一起算进去了（状态栏被算成一倍宽、x 跑到 -180）。
> 修法：`Fill` 里同步 `RemoveChild` 再 `QueueFree`；药水变化由 `BossPotions.Version` 驱动刷新。
> 另外「药水意图一个都画不出来」是 `NIntent` 没入树就 `UpdateIntent` 导致的空引用。

日志证据：

```
[12:33:06] 喝药后栏位剩 4 瓶
[12:33:06] 状态条 Fill: held=4        ← 同一秒，Fill 被调用且数据正确
held=4 rowChildren=31
held=5 rowChildren=29
held=5 rowChildren=15                 ← 同一个 held=5，子节点数 15 / 29 / 31 乱跳
```

⇒ **刷新触发和数据都是对的**；问题在 `BossStatusRow.Fill` 之后这一行的**子节点被重复挂上/旧格子没清掉**（正常应是「药水数 + 遗物数 + 能量 + 星」≈15）。
**下一步**：`Fill` 开头按名字显式清理 `BossPotion_*`（`RemoveChild` + `QueueFree`），改完再打一次 `rowChildren`，应稳定不跳；若稳定了而图标仍不更新，再查 `BossPotionRow.AddTo` 的定位。

### B. 药水意图的位置（**v0.2.9 已按用户要求压到药水上**）

用户 2026-10-26 最终口径：「第三回合出的牌太多会把药水意图挡住，所以还是把药水的意图上移**和药水本身重叠**」。
现状：`NIntent` **直接挂在药水格子下**（不套 `HBoxContainer`，容器会把子节点摆回 (0,0) 导致偏 30px），
`Position = (56/2 − 30×scale, 56 + gap + 32×scale − 31×scale)`，默认 `gap=-60`、`scale=1.0`
⇒ 箭头中心正好在药水图标中心（重叠）。两个值都在 `charbosses_ui.txt` 里可热调。

### C. 「可格挡中毒」（调制第三条）→ **v0.2.10 已实现，并扩到「魂劫」**

用户 2026-10-26：「她可以格挡中毒」改为「她可以格挡**中毒和魂劫**（崩坠收藏家的 debuff）」。

反编译结果是两者打的是**同一句**调用、参数都是 `Unblockable|Unpowered`（`(ValueProp)6`）：
- 原版 `PoisonPower.<Trigger>d__10.MoveNext` → `CreatureCmd.Damage(ctx, Owner, Amount, Unblockable|Unpowered, null, null)`
- 崩坠 `Collector.CollectorCode.Powers.CollectorDoomPower.<AfterSideTurnStart>d__2.MoveNext` → 同样形状
  （**魂劫 = `CollectorDoomPower`**，Eng 名 Doom；崩坠中文叫「魂劫」以免和原版亡灵契约师的「灾厄」撞名）

所有 `CreatureCmd.Damage` 重载最终都汇到同一个 7 参数核心
`Damage(PlayerChoiceContext, IEnumerable<Creature>, decimal, ValueProp, Creature?, CardModel?, CardPlay?)`。
所以 `BossBlockableDotPatch` 做两步（**没有猜签名，全部按 ilspycli 反编译结果**）：
1. 给上面两个 power 的**状态机 MoveNext** 挂前后夹（thread-static 计数），标出「这两种伤害正在结算」；
2. 核心 `Damage` 的 prefix：记号有效 + 目标里有 `CharBossMonster` ⇒ `props &= ~Unblockable`
   （保留 `Unpowered`：力量规则不动，只让格挡重新生效）。

为什么不用「只看 props 形状」：崩坠的「灼魂 / 活体炸弹」也是同形状的无来源不可格挡伤害，
用户只点了「中毒 + 魂劫」，别的效果必须保持原样。时序完全不变（何时结算仍由原版状态机决定）。

> 相关旧代码别混：二层「带毒刺击」那套（`Powers/PoisonStrikePower.cs` + `PoisonTriggerBlockablePatch`）
> 只在**本场有 `PoisonStrikePower`** 时接管中毒结算/立刻触发；炼药猎没有那层 power，所以走的是本补丁这条路。


---

## 4. 已排除的怀疑（别重复做）

- **卡顿不是本模组**：
  - 帧回调 `[CharBoss] 帧回调耗时(每120帧): avg=0.05ms max≈1~6ms`（预算 16.7ms/帧）；
  - 手牌栏刷新 `[CharBoss] 帧耗时(每60次): total=0.58ms fill=0.25ms`，且 **1 分半才 60 次**（不是每帧）。
  ⇒ 用户说的「第三/四回合卡、掉到十几帧」来源在**游戏本体或别的 mod**，不要再在状态条上做性能优化。
- **状态条每帧指纹节流**：做过两次（字符串版、整数版）都出问题（一次更卡、一次药水不更新），**已回退**，现在是「Fill 每次刷新都重建」。
- **手牌意图原地刷新**：`BossHandPanel.Rebuild` 里签名变化时不再整排清空，改为逐格 `BossHandCard.RefreshIntents`（卡面节点保留）。**回退开关**：`BossHandPanel.UseInPlaceIntentRefresh = false` 即回到旧行为。

---

## 5. 诊断日志现状（很有用，别删）

| 日志 | 位置 | 频率 |
|---|---|---|
| `构建：vX.Y.Z …` | `BuildInfo.LogOnce` | 每次启动一次 |
| `帧回调耗时(每120帧): avg/max` | `BossFrameTick.Tick` | 每 120 帧 |
| `帧耗时(每60次): total/fill` | `BossHandPanel.Rebuild` | 每 60 次刷新 |
| `状态条 Fill: held=N rowChildren=M` | `BossStatusRow.Fill` | 前 30 次 |
| `喝药后栏位剩 N 瓶` / `获得药水：X` | `BossPotions` | 每次 |
| `炼制药水：改炼指定药水 X` | `AlchemizeBrewPatch` | 每次 |

---

## 6. 给下一个会话的「第一句话」

> 先读 `docs/plans/handover-brewer-2026-10-26.md` 和 `docs/plans/boss-silent-brewer.md`。
> 现在要解决的问题只有一个：**炼药猎喝药后药水瓶的图标不实时变化**。
> 日志已经证明 `状态条 Fill: held=N` 的 N 是对的、且和「喝药后栏位剩」对得上，
> 但 `rowChildren` 在 15 / 29 / 31 之间乱跳 ⇒ 怀疑 `BossStatusRow.Fill` 里旧的 `BossPotion_*` 格子没被正确清掉/重复挂上。
> 请先改 `Fill`：按名字显式清理 `BossPotion_*` 再重建，然后打日志确认 `rowChildren` 稳定；
> 如果这样还不更新，再查 `BossPotionRow.AddTo` 的坐标与 `Place/LayoutChildren`。
> 别做性能优化（卡顿已证明与本模组无关），别猜 Harmony 签名（上次把模组搞崩过），
> 每次构建都要 bump `BuildInfo.Tag`，装完让用户验。
