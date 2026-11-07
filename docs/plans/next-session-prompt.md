# 贴给新对话的开场白（复制下面整段）

---

继续这个项目：`D:\Sts2ModProject\Downfall Expansion Vanilla Characters as Bosses`（杀戮尖塔2 模组，当前版本 **v0.2.382**，已装机 + 工坊两处目录）。

**先读这两份（按顺序，别跳）：**
1. `docs/plans/handover-2026-11-04-late.md` —— 上一场超长会话的压缩交接（当前状态 / 工作流 / 8 条踩过的坑）
2. `docs/plans/todo-2026-11-04-stun-and-echo-slash.md` —— 现在要做的两条需求

**这次只做第 1 条：Boss 被击晕时的整套行为**（第 2 条「回响斩击显示斩杀段数」先别碰）：
> 当 boss 被吹哨等牌击晕时，他应该立刻使意图中的所有牌变暗，撤掉所有意图，并且本回合不行动。
> 在下回合，他应该尝试使用上回合没有成功完成的意图（不一定是上回合，因为他有可能被连续击晕好几回合，具体要看他之前执行过哪些回合的行动）

**已经查清的事实（直接用，别再重复查）：**
- 「吹哨」`WHISTLE` 反编译 = 攻击 33 后调 **`CreatureCmd.Stun(cardPlay.Target)`**；
- 通用入口 `CreatureCmd.Stun(Creature, nextMoveId)` → `Creature.StunInternal(...)`；
- **检测面是 `Creature.IsStunned`（public bool）**；
- 原版没有 `StunPower`：击晕 = 把移动状态机切到「被晕」的 `MoveState`，播 `NStunnedVfx`，之后自动转到 `nextMoveId`。

**所以第一件事是读 `Creature.StunInternal` / `IsStunned` 的实现**，搞清楚 `IsStunned` 什么时候被清掉。⚠ 我们的 Boss **不走原版 `PerformMove`**（出牌由 `Plan` 驱动）⇒ 直接检测 `IsStunned` 很可能永久为 true、把她永久定住。要不要「让她被晕时真的跑一次原版 stun move」还是「跳过后自己还原」，是这个任务的第一个决策点，先想清楚再动手。

**改的时候注意（都写进 cerebrum 了）：**
- 计划结构：`CharBossMonster.Plan`（`List<PlannedPlay>`，打一张 `Plan.Remove(play)`）；回合流程在 `PlayPlannedCards`；
- 被晕时**不能重建计划**（要保留「待执行的那一套」），否则「下回合补打」就没了；
- UI 压暗/撤意图：`src/Hooks/UI/BossHandPanel.cs`、`BossHandPanel.Extra.cs`（已经有「本回合不会打 → 压暗」的能力可复用）；
- Harmony 前缀跳过返回 `Task` 的方法，**必须 `ref Task __result` 赋 `Task.CompletedTask`**，否则 null Task → 下游 NRE → 整条钩子链静默断掉；
- 做完 build 要 0 错误 → 版本 +0.0.1（两处）→ 重打 PCK（`content/` 变了才需要）→ 拷 dll/json/pck 到游戏与工坊两个目录（工作区外，需 `danger-full-access`）。

**实机验证**：让我（用户）打一场，或你自己起游戏——**用 `tools\launch_sts2.ps1`（`steam://run/2868840`），别直接跑 exe**；关游戏用 `Stop-Process -Name SlayTheSpire2 -Force`。
验收：吃一次击晕 → 手牌立刻全暗、意图全没、该回合零 `PLAY` 日志；下一回合她照**被打断的那个回合 N** 的计划出牌（日志 `剧本第 N 回合`，不是 N+1）；连续晕两回合也一样。

**另外两件挂着的事**（别主动做，等我开口）：
- 工坊 v0.2.375 之后都没上传（命令：`powershell -ExecutionPolicy Bypass -File tools\upload-workshop.ps1`，Steam 要开着）；
- `.dist` 本地包停在 **v0.2.375**（我说「打包」才重打）。

---
