# 计划：状态栏 Power 图标节点对账（跟踪锁定图标变 2 个的根治）

> 状态：**代码已实现（v0.2.148）、已编译 0 错误、已装进游戏；实机未验证**。
> 用户 2026-10-29 拍板走**方案 B**（保留玩家侧图标，改渲染层回收残留节点）。

## 1. 问题

四人联机时，1、3、4 号位玩家的状态栏上会出现**两个**「跟踪锁定」图标。交接文档
`handover-2026-10-29-lockon.md` §3.2 已经证死的事实：

1. 图标不跨客户端同步（只给本机玩家挂 ⇒ 其它三名玩家一个都没有）；
2. 游戏会自己摘掉我们挂的 `LockOnPower`（我们三处删除点都会打日志，实测一条都没有）；
3. 被摘掉的图标**节点**不会立刻从状态栏消失 ⇒ 我们再补一份 = 屏幕上一个变两个；
4. `LockOnPower` 是 `StackType.Single` + `InstanceType.None` ⇒ **同一生物上不可能有两份实例**；
5. `Creature.Powers` 就是内部 `_powers`（无过滤）⇒「巡检 1 个、屏幕 2 个」只能是 **UI 节点残留**。

## 2. 根因（反编译 `NPowerContainer` 得到的，不再是推断）

见 `docs/plans/_recon/NPowerContainer.txt`：

| 原版实现 | 后果 |
|---|---|
| `Add(power)`：`if (power.IsVisible) { var n = NPower.Create(power); _powerNodes.Add(n); AddChildSafely(n); }` | **没有去重** ⇒ 同一个模型被加两次就是两个节点 |
| `Remove(power)`：`if (CombatManager.Instance.IsInProgress) { … }` | 战斗不在进行中时**整段跳过**，`_powerNodes` 里那项也不摘 |
| `Remove` 里 `_powerNodes.FirstOrDefault(x => x.Model == power)` | 只摘**第一个**匹配项 ⇒ 一旦有两个节点，Power 消失时只回收一个，另一个**永久残留** |

⇒ 屏幕上多出来的从来不是第二份 Power，而是**没被回收的 UI 节点**。

## 3. 方案（`src/Hooks/Patches/PowerIconNodeReconcilePatch.cs`）

不改原版语义、只回收「我们自己也不该有第二个」的那部分：

1. **`Add` 前置去重**（`[HarmonyPrefix] NPowerContainer.Add`）
   - 只对 `InstanceType.None` 的 Power 生效 —— 同类型**不可能**有两份活实例
     （`Creature.ApplyPowerInternal` 会抛 `Trying to add multiple instances of a non-instanced power`），
     所以第二个节点一定是重复；实例化 Power（`Instanced` / `InstancedPerApplier`）一律放行。
   - 已有**同类型且模型还活着**的节点 ⇒ 返回 `false`（不新建）。
   - 已有**同类型但模型已不在清单里**的节点（残留）⇒ `residual.Model = power` **改绑**，返回 `false`
     —— 屏幕上从头到尾只有一个图标，连一帧闪烁都没有。
2. **`Remove` 兜底**（`[HarmonyPostfix] NPowerContainer.Remove`）
   - 原版因 `IsInProgress == false` 整段跳过时：若 `power` 已不在 `creature.Powers` 而节点还在，自己摘掉。
3. **5Hz 权威对账**（`ReconcileAll` ← `BossFrameTick` 每帧驱动、200ms 节流）
   - 容器登记表：`[HarmonyPostfix] NPowerContainer.SetCreature` 填 `Containers`。
   - 判据：节点的 `NPower.Model` 必须**引用出现在 `creature.Powers` 里**（唯一真相，`_powers` 无过滤），
     且同一份模型只允许**一个**节点。
   - 摘除顺序：先 `RemoveChild`（这一帧就消失）再 `QueueFree`，摘完调一次 `UpdatePositions`。
   - **不碰 `IsVisible`**：队友身上图标该不该显示仍由原版 `LocalContext.IsMe(Target)` 决定 ⇒ 零回归。

## 4. 配套改动

- `src/Hooks/Core/BossLockOn.cs`：`_createdCount` 上限 2 → **8**（`MaxIconAppliesPerPlayer`）。
  那个上限是给残留节点打的补丁；现在残留会被回收、新图标复用残留节点，不必再靠「少挂」掩盖问题。
  耗尽时打一行日志，便于判断是不是「每帧都被摘」。
- `src/Hooks/Patches/BossFrameTickPatch.cs`：接上 `PowerIconNodeReconcile.Tick(...)`（200ms）
  与 `Reset()`（`ResetForNewFight`）。
- 版本：`BuildInfo.Tag` / `src/CharacterBosses.json` → v0.2.148。

## 5. 验收看什么

`%APPDATA%\SlayTheSpire2\logs\mod_log.txt`：

- `[CharBoss] 状态栏图标：<POWER> 已有节点，拦下重复创建（原版 Add 不去重；模型 …）`
- `[CharBoss] 状态栏图标：<POWER> 复用残留节点（改绑新模型，避免屏幕上出现第二个图标）`
- `[CharBoss] 状态栏图标：<POWER> 已被摘除但原版没回收节点（战斗不在进行中时 Remove 会整段跳过）⇒ 我们自己摘掉`
- `[CharBoss] 状态栏图标对账：回收 N 个残留/重复图标节点（场上 M 个状态栏）`
- `[CharBoss] 状态栏图标对账（<生物名>）：<POWER>：背后已经没有这份 Power（残留）`

屏幕期望：**每人正好 1 个锁定图标**，开局到战斗结束都不变。

## 6. 风险与回退

- Harmony 目标名（`Add` / `Remove` / `SetCreature` / `UpdatePositions` / `_powerNodes` / `_creature`）
  全部照反编译核对过；若仍对不上，`mod_log.txt` 会有补丁失败红字。
- 若对账误删了本该显示的图标（例如某个 `PowerModel` 的 `Model` 引用与 `creature.Powers` 里的不是同一个），
  最省事的回退是：把 `PowerIconAddDedupePatch` 的 Prefix 改成恒 `return true`（只留 5Hz 对账），
  再不行就整文件停用（删 `[HarmonyPatch]` 三处），行为退回 v0.2.147。
