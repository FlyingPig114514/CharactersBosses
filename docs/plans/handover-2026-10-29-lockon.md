# 交接报告 — 2026-10-29 晚（锁定图标问题未解，其余已修待验收）

> 给**下一个会话**：先读完这一页再动手。所有结论都来自本机实测日志
> （`%APPDATA%\SlayTheSpire2\logs\mod_log.txt`，用户允许直接读）。
> 用户在本机开 **4 个游戏实例**做四人联机测试。

---

## 0. 一句话现状

当前装进游戏的是 **v0.2.147**（0 编译错误）。本轮修好了 4 件事（播种不群攻、
奇巧牌不被丢弃、休眠不转格挡、4 人站位偏左/太挤），**只有「跟踪锁定图标数量」
没解决** —— 现在又回到「1、3、4 号位各 2 个，2 号 1 个」。用户已明确表示要换个会话接手。

---

## 1. 立刻要读的上下文（按顺序）

1. `.dshwolf/STATUS.md` —— v0.2.133 → v0.2.147 逐版本记录，包含每次实测现象与结论
2. `.dshwolf/buglog.json` —— 已修 bug（含本轮的四个）与「不要再试」的记录
3. `docs/plans/multiplayer-lockon.md` —— 跟踪锁定的**原始设计**
4. `src/Hooks/Core/BossLockOn.cs` —— 锁定注册表 + 图标对账（本轮的战场）
5. `docs/plans/config-ui-and-bgm.md` §D —— FMOD 参数是死路的结论（别重走）

---

## 2. 构建 / 安装（每次改 DLL 必做）

```powershell
# 1) 改版本号（两处必须同改，否则游戏 mod 列表显示不对）
#    src\Hooks\Core\BuildInfo.cs   → public const string Tag
#    src\CharacterBosses.json      → "version"
# 2) 编译
& tools\dotnet\dotnet.exe build src\CharacterBosses.csproj -c Release -v q
# 3) 安装（目标在工作区外，需要 danger-full-access 提权）
Copy-Item src\bin\Release\CharacterBosses.dll "D:\SteamLibrary\steamapps\common\Slay the Spire 2\mods\CharacterBosses\CharacterBosses.dll" -Force
Copy-Item src\CharacterBosses.json            "D:\SteamLibrary\steamapps\common\Slay the Spire 2\mods\CharacterBosses\CharacterBosses.json" -Force
```

- 产物路径是 `src\bin\Release\CharacterBosses.dll`（**没有** `net10.0` 这一层）
- 打包 zip（`.dist\CharacterBosses-v*.zip`）**只在用户明确说「打包」时做**；现在 `.dist` 停留在 v0.2.133
- 反编译工具：`& tools\dotnet\dotnet.exe tools\ilspy-cli\bin\Release\net10.0\ilspycli.dll <types|type|members|method|search> references\sts2.dll <名字>`
- ⚠ **绝对不要用 PowerShell 字符串替换去改 C# / json**（会毁 UTF-8、加 BOM）。用编辑工具，
  或 `[System.IO.File]::WriteAllText($p, $t, (New-Object System.Text.UTF8Encoding($false)))`

---

## 3. 未解决问题：跟踪锁定图标数量（用户唯一在意的遗留）

### 3.1 现象（用户口径，按时间）

| 版本 | 现象 |
|---|---|
| v0.2.140~144 | 开局每人 1 个（对），战斗中途 1/3/4 号位又多出 1 个 |
| v0.2.145 | 同上，开局就给 1/3/4 挂成 2 个 |
| v0.2.146 | 只给本机玩家挂 → 「除了 1 号位有 2 个，其它玩家都没有了」 |
| **v0.2.147（当前）** | 又回到「1、3、4 号各 2 个，2 号 1 个」 |

### 3.2 已经**证死**的事实（不要再重复验证）

1. **图标不跨客户端同步**：v0.2.146 只给本机玩家挂，其它三名玩家就**一个都没有**。
   ⇒ 每个客户端必须自己给**所有**玩家挂，不能指望转发。
2. **游戏会主动摘掉我们挂的 `LockOnPower`**。v0.2.145 的探针日志（10:38:29）：
   ```
   锁定图标实例 #1 创建：飞猪（applier=静默猎手）
   锁定图标被摘除：飞猪（第 1 次；这次不是我们主动摘的就会没有对应的 `跟踪锁定` 日志）
   锁定图标实例 #2/#3/#4 创建：…
   锁定图标被摘除：[unknown]（第 2/3 次）
   10:38:30 锁定图标实例 #5/#6/#7 创建      ← 屏幕上的第二个就是这么来的
   10:38:47 锁定图标被摘除 ×4
   ```
   我们自己的删除点只有三处（对账时没人锁他 / 摘重复层 / Boss 死亡），**三处都会各打一行日志**，
   而这里一条都没有 ⇒ 不是我们删的，是 power 生命周期 / 多人状态同步删的。
3. **摘掉的图标节点不会立刻从状态栏消失**，所以我们「看到 0 个 → 再补一份」就等于
   在屏幕上多画一个。这条是「2 个图标」的直接成因。
4. `LockOnPower` 是 `StackType.Single` + `InstanceType.None`，
   **同一个生物上不可能存在两份实例**（`Creature.ApplyPowerInternal` 会抛
   `Trying to add multiple instances of a non-instanced power`）。
   ⇒ 屏幕上多出来的那个**不是第二份 power**，是**残留的 UI 图标节点**。
5. `Creature.Powers` 就是内部 `_powers`（无过滤），所以「巡检显示 1 个但屏幕有 2 个」
   只有一种解释：UI 节点残留。
6. `PowerModel.IsVisible` 里有游戏自己的 `LocalContext.IsMe(Target)` 判据 ——
   想继续走 UI 路线可以顺这条线查 `NPowerIcon`/状态栏的增删逻辑。

### 3.3 已经试过并且**无效**的办法（不要再试）

- 每帧/每 15 帧对账 + 摘掉多余的 `LockOnPower` 实例（实例本来就不可能有两份）
- `Recorded` 去重键 / `_applying` 帧预算 / `_createdCount` 上限 2
- 只给本机玩家挂（别人直接没有图标）
- 在 `Apply` 返回后立刻查重复（`Apply` 返回时那份还没进 `creature.Powers`）
- `CleanupStaleForNewFight` 里清记账（它每只 Boss 生成都跑，4 只 Boss 一秒跑 4 次，反而把上限作废）

### 3.4 建议的下一步（给接手者的方案，按推荐顺序）

**方案 A（推荐，最省事且一定干净）：干掉这层玩家图标，换成 Boss 侧显示。**
战斗逻辑完全不依赖图标，真相在 `BossLockOn.Locks`。在 Boss 自己的面板
（`src/Hooks/UI/BossStatusRow.cs` / `BossHandUi.cs`）上加一行「锁定：<玩家名>」，
我们自己渲染，绝不会有同步/残留问题。同时删掉
`src/Hooks/Powers/LockOnPower.cs`、`src/Hooks/Patches/LockOnPowerRemoveProbe.cs`，
以及 `BossLockOn` 里所有 Reconcile/Apply/Remove 图标的代码
（**保留** `Locks`、`TargetFor`、`IsLocked`、`Rebalance` 的分配逻辑 —— 选靶靠它们）。
⚠ 注意：`powers.json` 里的 `LOCK_ON_POWER.*` 与 `BossSpecialPowerIconPatch` 里的
`LockOnPower` 分支要一起清理。

**方案 B（保留图标，改渲染）**：查清状态栏图标节点的创建位置
（`NCombatUi` / `NCreatureStateDisplay` / `NPowerIcon` 之类，`docs/plans/_recon/`
下已有部分反编译稿），找到「power 被移除时节点销毁」的那段，把残留节点强制回收。
工作量大、且是原版 UI 行为，风险高。

**方案 C（摆烂）**：保持现状。用户说过「不影响游玩」，只是看着别扭。

### 3.5 复现/取证用的现有工具

- `src/Hooks/Patches/LockOnPowerRemoveProbe.cs`（v0.2.145 加的探针，**只打日志**）
  → 打 `锁定图标被摘除：<玩家>（第 N 次）`
- `BossLockOn.ReconcilePowers` 里的「巡检」日志（数量变化时打一行）
- `LockOnPower.AfterApplied` 里的「锁定图标实例 #N 创建」

---

## 4. 本轮已修、**待用户验收**的四件事

| 问题 | 修法 | 验收看点 |
|---|---|---|
| **播种（SOW）不群攻** | 真凶是 `ShadowPetNotAnOpponentPatch` 对 `CombatState.GetOpponentsOf` 做的「只留锁定玩家」收窄 —— 而 `AttackCommand.GetPossibleTargets()` 在**多目标**时返回的就是它 ⇒ 群攻被打成单体。新增 `HittableEnemiesSidePatch.MultiTargetCardDepth`，`CharBossMonster` 在 `CardCmd.AutoPlay` 前后按 `TargetType.AllEnemies/AllAllies` 成对加减，收窄处 `depth>0` 直接跳过 | 播种打出 **4 个受击数字**；日志不再出现结算期的 `选靶收窄` |
| **奇巧牌没被丢弃** | 自动选择器（替 Boss 回答「弃 N 张」）原来按「价值→费用」挑，把 0 费防御挑走了。奇巧（`CardKeyword.Sly` / `IsSlyThisTurn`）牌被丢弃会**自动打出**，所以排序里插一档「奇巧优先」 | 日志 `自动选择 … → 选 [SNEAKY,FLICK_FLACK]` |
| **休眠不给其它 Boss 格挡** | 原版 `FrostOrb.Passive/Evoke` 的 `foreach (玩家 in CombatState.Players)` **到不了影子 Player**（`BossHookBridge` 类注释第 21 行：故意排除）。改成拦下发给真人那一笔后，用 `GrantToOthersAsync` **主动转给其它 Boss** | 日志 `休眠：冰球格挡 2 … ⇒ 转给其它 Boss [...]`；其它 Boss 掉格挡前能看到数值 |
| **4 人站位偏左/太挤** | 不再参考原版位置：最右那只贴右缘（`视口宽 - 0.75*SpreadX`），相邻中心距 `1.5*SpreadX`（≈300 = 一个意图面板宽），最左不越过 `0.17*视口宽`。1920 视口下落在 x≈870/1170/1470/1770 | 四只不再压意图面板，且右侧空间用上了 |

另外本轮还做了：

- **站位顺序 = 行动顺序**：`PlacementRank` 改成取「这只 Boss 在 `CombatState.Enemies` 里的下标」。
  ⚠ 副作用：「猎人永远最左」这条**不再保证**，取决于遭遇里的顺序。用户如果还要猎人最左，
  需要「按行动次序排，但把 SILENT_BOSS 强制提到第一」这种折中。
- **每帧回调同帧去重**（`Engine.GetFramesDrawn()`）：之前 3 个驱动各跑一遍，4 Boss 时 fps 掉到 2~10。
- **锁定分配改成一次算全表**（`Rebalance` 里 `bosses[i] → players[(i+offset)%n]`）：
  以前每只 Boss 各算 `(自己rank+offset)%玩家数`，而 `combat.Players` 在开场几帧是**逐个填满**的 ⇒
  两只 Boss 撞同一名玩家（日志里 NECROBINDER 与 REGENT 都指向 Ironclad）。
  现在有 `跟踪锁定分配表（4 人 / 4 只）：…` 一行可核对。

---

## 5. 这个模组的几个「反直觉」事实（踩过坑，别再踩）

1. **我们 Boss 的影子 `Player` 不在 `CombatState.Players` 里**（故意排除，避免它参与玩家回合）。
   所以任何「对所有玩家」的原版逻辑（`foreach (玩家 in CombatState.Players)`）**都不会作用于 Boss**。
   要影响 Boss 必须自己写。相关：`src/Hooks/Core/BossHookBridge.cs` 类注释。
2. 反过来，`CombatState.Players` 里出现的带 `Monster != null` 的玩家是**别的 Boss**，
   `BossLockOn.RealPlayers()` 就是滤掉它们的。
3. **群攻牌与随机单体牌共用同一份候选列表**（`AttackCommand.GetPossibleTargets()`），
   所以在 `GetOpponentsOf` 上做「只打锁定玩家」的收窄会把群攻也变成单体。
4. FMOD 的 `<boss>_progress` 参数对**自定义音乐无效**（只有切歌有效），已记在
   `docs/plans/config-ui-and-bgm.md` §D，不要再拿它做低血量变化。
5. 多人局里**我们自己的 power 不跨客户端同步**（见 §3.2 事实 1）。

---

## 6. 本轮改动文件清单

```
src/Hooks/Patches/CombatSidePatches.cs        MultiTargetCardDepth（群攻豁免收窄）+ 日志数量修正
src/Hooks/Monsters/CharBossMonster.cs         出牌期按 TargetType 加减 MultiTargetCardDepth
src/Hooks/Patches/SerpentFormLockOnPatch.cs   收窄改成一次性 RequestNarrowOnce（保留）
src/Hooks/Core/BossCardSelector.cs            奇巧牌优先丢弃
src/Hooks/Patches/HibernateFrostBlockPatch.cs 冰球格挡转给其它 Boss
src/Hooks/Patches/BossEnemySpreadPatch.cs     站位按行动次序 + 右移贴右缘 + 间距 1.5×
src/Hooks/Patches/BossFrameTickPatch.cs       同帧去重（v0.2.140 已加）
src/Hooks/Core/BossLockOn.cs                  一次算全表 + 图标对账/巡检/上限
src/Hooks/Powers/LockOnPower.cs               SetBosses + 实例创建日志
src/Hooks/Patches/LockOnPowerRemoveProbe.cs   新增（只打日志的摘除探针）
src/Hooks/Core/BuildInfo.cs, src/CharacterBosses.json   版本 v0.2.147
```

---

## 7. 建议开场先问用户的三个问题

1. 锁定图标要不要按**方案 A** 直接去掉、改成挂在 Boss 面板上？（推荐）
2. 站位还要不要「猎人永远最左」？（现在按行动次序，猎人位置取决于遭遇顺序）
3. 其余四件修好的（播种/奇巧/休眠格挡/站位间距）要不要先打一版 zip 验收？
