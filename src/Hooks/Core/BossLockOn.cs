using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using CharacterBosses.Monsters;

namespace CharacterBosses.Core;

/// <summary>
/// 多人模式的「**跟踪锁定**」注册表（设计见 <c>docs/plans/multiplayer-lockon.md</c> §阶段 1）。
///
/// 语义：
///   · 每只 <see cref="CharBossMonster"/> 在某一时刻**只锁定一个玩家**；一个玩家可以被多只 Boss 同时锁定；
///   · 单体攻击/随机选靶只打「锁定该 Boss 的玩家」；群体攻击仍是原版全体（自然打到所有玩家）；
///   · 玩家死亡 → 该 Boss 重新选「被锁定次数最少」的玩家（并列随机）；Boss 死亡 → 移除它的锁定；
///   · **单人模式**：唯一玩家锁定唯一 Boss —— 走同一条代码路径，行为与之前完全一致。
/// </summary>
public static class BossLockOn
{
    /// <summary>Boss 生物 → 被它锁定的玩家（用生物实例做键：每场战斗独立，天然不会跨场残留）。</summary>
    private static readonly Dictionary<Creature, Player> Locks = new();

    private static readonly Random Rng = new();

    /// <summary>已记录过的「玩家被锁」关系（避免重复挂 Power）。</summary>
    private static readonly HashSet<(int player, int boss)> Recorded = new();

    // ───────────────────────────── 查询 ─────────────────────────────

    /// <summary>
    /// **真人玩家**（排除本 mod 的 Boss 影子 Player）。
    ///
    /// ⚠ 关键事实：影子 Player 的 Creature 就是 Boss 生物本体，而原版
    /// <c>Creature.IsPlayer =&gt; Player != null</c> ⇒ **Boss 也会出现在 <c>CombatState.Players</c> 里**。
    /// 所以凡是「玩家」相关的判定都必须先把「带 Monster 的那些」滤掉，否则：
    ///   · 锁定会随到另一只 Boss 身上（单体攻击就会打自己人）；
    ///   · 「对所有玩家生效」的特殊效果会给别的 Boss 挂减益。
    /// </summary>
    public static List<Player> RealPlayers(ICombatState? combat)
    {
        var result = new List<Player>();
        try
        {
            var players = combat?.Players;
            if (players == null) return result;
            foreach (var p in players)
            {
                if (p?.Creature == null) continue;
                if (p.Creature.Monster != null) continue;      // Boss 的影子 Player 不算玩家
                result.Add(p);
            }
        }
        catch { /* 忽略 */ }
        return result;
    }

    /// <summary>场上真人玩家数（单人为 1；拿不到按 1 算）。</summary>
    public static int RealPlayerCount(ICombatState? combat)
    {
        int n = RealPlayers(combat).Count;
        return n > 0 ? n : 1;
    }

    /// <summary>
    /// 「**对应型**特殊效果」该不该对这名玩家生效（设计见 multiplayer-lockon.md §1.4 / §阶段 2）。
    ///   · 单人（真人玩家 &lt; 2）→ 恒 <c>true</c>：原样生效，零回归；
    ///   · 多人 → 只有**锁定这只 Boss** 的那名玩家才是 true。
    /// 另外：不是真人玩家（= 其它 Boss 的影子 Player）一律 false —— 他们的出牌/印牌不属于任何玩家。
    /// </summary>
    public static bool SpecialEffectAppliesTo(CharBossMonster? boss, Player? player)
    {
        try
        {
            if (boss?.Creature == null || player?.Creature == null) return false;
            if (player.Creature.Monster != null) return false;
            if (RealPlayerCount(boss.CombatState) < 2) return true;
            return ReferenceEquals(TargetFor(boss), player);
        }
        catch { return false; }
    }

    /// <summary>某个 Boss 当前锁定的玩家（**纯函数**算出来，顺手缓存进 <see cref="Locks"/> 供日志/图标用）。</summary>
    public static Player? TargetFor(CharBossMonster boss)
    {
        try
        {
            var bossCreature = boss?.Creature;
            var combat = boss?.CombatState;
            if (bossCreature == null || combat?.Players == null) return null;

            // ⚠ 多人局一律走**纯函数**（<see cref="PureVictim"/>），不再看 `Locks` 里原来的内容。
            //
            // 为什么（用户 2026-10-29：「我操控的 1 号位被储君锁定，但他的牌显示 0 伤害，实际却打了我 10」）：
            //   `Locks` 是**每个客户端各自维护**的本地状态，而「谁被谁锁」必须每个客户端算得**一模一样**，
            //   否则：我这台认为储君锁的是别人（于是我屏幕上显示 0），他那台认为锁的是我（于是真打我）。
            //   以前按「当前负荷挑人」分配 ⇒ 依赖各端已经攒下的 Locks 内容 ⇒ 各端会分叉。
            //   现在只依赖**联机同步的那点数据**（玩家 NetId 顺序 + 我们 Boss 的怪物 id 顺序）⇒ 各端必然一致。
            if (IsMultiplayerRun(boss))
            {
                var pure = PureVictim(boss, combat);
                if (pure != null)
                {
                    Locks[bossCreature] = pure;
                    return pure;
                }
                // 玩家还没到齐 ⇒ 这一帧先不锁定（各端都读同一个 RunState，所以判断也一致）
                return null;
            }

            // ── 单人（以及拿不到花名册的兜底）：沿用旧的行为 ──
            if (Locks.TryGetValue(bossCreature, out var existing))
            {
                // ⚠ 必须确认「被锁的那个玩家**还在这一场**里」：SL（退出重进）会把生物实例全换掉，
                //   老的 Player/Creature 还挂在字典里且 IsAlive 仍为真 —— 不校验的话
                //   选靶会指向一个已经不存在的生物，锁定关系也会跟着重 roll（用户实测）。
                if (existing?.Creature != null && existing.Creature.IsAlive &&
                    existing.Creature.CombatState != null &&
                    ReferenceEquals(existing.Creature.CombatState, combat))
                {
                    return existing;
                }
                Locks.Remove(bossCreature);   // 被锁的玩家死了 / 已经不是这一场 → 重新分配
            }

            var victim = PickForBoss(boss, combat);
            if (victim != null) Locks[bossCreature] = victim;
            return victim;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 取锁定玩家失败: " + e.Message);
            return null;
        }
    }

    /// <summary>
    /// **锁定对象的纯函数**（多人局唯一权威）—— 用户 2026-10-29 定稿的两条要求同时满足：
    /// 「已经成立的锁不要搬家」+「优先锁身上『跟踪锁定』份数少的玩家」。
    ///
    /// ```text
    /// 花名册 = 本局真人玩家，按 NetId 升序（**包含已阵亡的** ⇒ 顺序不随死亡变化）
    /// 锚点   = 花名册[rank % 花名册长度]（rank = 这只 Boss 在「场上我们的 Boss 按怪物 id 升序」里的下标）
    ///
    /// ① 锚点玩家还活着 ⇒ 就锁他（**永不搬家**，玩家死亡不会引起别人的锁平移）
    /// ② 锚点玩家已阵亡 ⇒ 这只 Boss 是「孤儿」，按**当前被锁份数最少**的活人里挑一个；
    ///    并列时用「本局种子 + 幕数」算出的固定轮转来挑（看起来随机，但 SL 前后 / 各客户端完全一致）
    /// ```
    ///
    /// 于是：
    ///   · 4 人 4 Boss：A→1、B→2、C→3、D→4；死一个 3 号位 ⇒ 只有 C 变成孤儿并被分给「份数最少」的活人，
    ///     A/B/D 一个都不动（用户口径）；
    ///   · **死掉两个队友、只剩 2 人**（用户 2026-10-29 实测的坏情况）⇒ 两个孤儿依次分给当前份数最少的人，
    ///     结果 **2 : 2**，不会再出现「一个被 3 只锁、另一个只有 1 只」。
    ///
    /// 三条性质：**各端一致**（只依赖联机同步的 NetId / 怪物 id / 种子）、**死亡不平移**、**确定性**。
    /// </summary>
    internal static Player? PureVictim(CharBossMonster? boss, ICombatState? combat)
    {
        try
        {
            if (boss?.Creature == null) return null;
            var map = AssignmentOf(combat ?? boss.CombatState);
            if (map == null) return null;
            return map.TryGetValue(boss.Creature, out var p) ? p : null;
        }
        catch { return null; }
    }

    /// <summary>整张「Boss → 玩家」分配表（纯函数；同一帧内缓存，避免每张牌都重算）。</summary>
    internal static Dictionary<Creature, Player>? AssignmentOf(ICombatState? combat)
    {
        try
        {
            if (combat == null) return null;

            int frame = Now();
            if (_assignCache.TryGetValue(combat, out var cached) && cached.Frame == frame)
                return cached.Map;

            var map = ComputeAssignment(combat);
            _assignCache[combat] = (frame, map);
            return map;
        }
        catch { return null; }
    }

    private static readonly Dictionary<ICombatState, (int Frame, Dictionary<Creature, Player>? Map)> _assignCache = new();

    private static Dictionary<Creature, Player>? ComputeAssignment(ICombatState combat)
    {
        var roster = RosterOf(combat);
        if (roster.Count == 0) return null;

        // 玩家还没到齐（开局几帧 `combat.Players` 逐个填满）⇒ 先不锁定。
        // 各端读的是同一个 `RunState.Players`，所以这道闸门也是各端一致的。
        int declared = -1;
        try { declared = combat.RunState.Players.Count; } catch { declared = -1; }
        if (declared > roster.Count) return null;

        var aliveList = roster.Where(IsAlivePlayer).ToList();
        if (aliveList.Count == 0) return null;

        var bosses = OurBossesSorted(combat);
        if (bosses.Count == 0) return null;

        int rotate = StableRotate(combat, aliveList.Count);
        var load = new Dictionary<Player, int>();
        foreach (var p in aliveList) load[p] = 0;

        var map = new Dictionary<Creature, Player>();
        var orphans = new List<CharBossMonster>();

        // ① 锚点：活着就占住（永不搬家）
        //
        // ⚠ 锚点序号必须取**本场固定的「Boss 花名册」**（`BossRank`），不能用「当前 Enemies 里的下标」——
        //   用户 2026-10-29：**一只 Boss 死亡后所有玩家的锁定关系都变了**。
        //   原因就是死掉那只从 `Enemies` 里消失 ⇒ 其余 Boss 的下标整体前移 ⇒ 所有人的锚点都换人。
        //   现在花名册只增不减（死掉的 Boss 仍占着它那一格），于是「只有死亡那只不再锁定任何人」，
        //   其它 Boss ↔ 玩家的关系一个字都不动。
        for (int i = 0; i < bosses.Count; i++)
        {
            var b = bosses[i];
            if (b?.Creature == null) continue;
            if (!b.Creature.IsAlive) continue;      // 死掉的 Boss 不锁定任何人

            var anchor = roster[BossRank(combat, b) % roster.Count];
            if (IsAlivePlayer(anchor))
            {
                map[b.Creature] = anchor;
                load[anchor] = load.TryGetValue(anchor, out int n) ? n + 1 : 1;
            }
            else
            {
                orphans.Add(b);
            }
        }

        // ② 孤儿：分给「当前份数最少」的活人；并列时按固定轮转挑（各端一致、SL 不变）
        int k = 0;
        foreach (var b in orphans)
        {
            if (b?.Creature == null) continue;

            int min = load.Values.Min();
            var candidates = aliveList.Where(p => load.TryGetValue(p, out int n) && n == min).ToList();
            if (candidates.Count == 0) continue;

            var pick = candidates[(((k + rotate) % candidates.Count) + candidates.Count) % candidates.Count];
            map[b.Creature] = pick;
            load[pick] = load.TryGetValue(pick, out int m) ? m + 1 : 1;
            k++;
        }

        return map;
    }

    /// <summary>
    /// 「本局种子 + 幕数」算出的固定轮转量（0..n-1）。
    /// 用途：并列时的**等概率**挑人 —— 但同一局同一幕永远算出同一个值，
    /// 所以 SL 前后不变、四个客户端也一致（用户要求：「相同就等概率，但保证 SL 不能随便变」）。
    /// </summary>
    private static int StableRotate(ICombatState combat, int n)
    {
        try
        {
            if (n <= 1) return 0;
            ulong seed = 0;
            try { seed = unchecked((ulong)combat.RunState.Rng.Seed); } catch { seed = 0; }
            int act = 0;
            try { act = combat.RunState.CurrentActIndex; } catch { act = 0; }

            ulong h = unchecked(seed * 6364136223846793005UL + (ulong)(act + 7) * 1442695040888963407UL);
            return (int)(h % (ulong)n);
        }
        catch { return 0; }
    }

    /// <summary>本局花名册：真人玩家按 NetId 升序（**包含已阵亡的** ⇒ 顺序不随死亡变化）。</summary>
    private static List<Player> RosterOf(ICombatState? combat)
        => RealPlayers(combat).OrderBy(p => p.NetId).ToList();

    /// <summary>
    /// 「Boss 花名册」：本场出现过的**我们的 Boss（按怪物 id 排）**，**只增不减**。
    ///
    /// 为什么必须只增不减（用户 2026-10-29：「boss 死亡会刷新锁定，所有玩家对应的 boss 都发生了变化，
    /// 应该只有死亡 boss 对应的玩家失去跟踪锁定」）：
    /// 死掉的 Boss 会从 `CombatState.Enemies` 里消失，若拿「当前 Enemies 的下标」当锚点序号，
    /// 其余 Boss 的下标会整体前移 ⇒ 每个玩家的锁定对象都换人。
    /// 记成「只增不减的花名册」之后，死掉那只仍然占着它那一格（只是不再锁定任何人）。
    /// </summary>
    private static readonly Dictionary<ICombatState, List<string>> BossRosterCache = new();

    /// <summary>这只 Boss 在「本场 Boss 花名册」里的下标（稳定、不随死亡变化）。</summary>
    private static int BossRank(ICombatState combat, CharBossMonster boss)
    {
        try
        {
            var roster = BossRosterOf(combat);
            string entry = SafeEntry(boss);

            int idx = roster.IndexOf(entry);
            if (idx >= 0) return idx;

            roster.Add(entry);
            roster.Sort(StringComparer.Ordinal);
            return Math.Max(0, roster.IndexOf(entry));
        }
        catch { return 0; }
    }

    /// <summary>本场 Boss 花名册（按怪物 id 升序、只增不减）。</summary>
    private static List<string> BossRosterOf(ICombatState combat)
    {
        if (!BossRosterCache.TryGetValue(combat, out var list))
        {
            list = new List<string>();
            BossRosterCache[combat] = list;
        }

        // 把「现在场上还没有记录过的」补进去（第一帧时一次性补齐）
        try
        {
            var enemies = combat.Enemies;
            if (enemies != null)
            {
                bool added = false;
                foreach (var c in enemies)
                {
                    if (c?.Monster is not CharBossMonster b) continue;
                    string entry = SafeEntry(b);
                    if (entry.Length == 0 || list.Contains(entry)) continue;
                    list.Add(entry);
                    added = true;
                }
                if (added) list.Sort(StringComparer.Ordinal);
            }
        }
        catch { /* 忽略 */ }

        if (BossRosterCache.Count > 32) BossRosterCache.Clear();   // 长期同进程反复开局别把字典撑爆
        return list;
    }

    /// <summary>场上我们的 Boss，按**怪物 id** 升序（`rank` 的来源，各端一致）。</summary>
    private static List<CharBossMonster> OurBossesSorted(ICombatState? combat)
    {
        var list = new List<CharBossMonster>();
        try
        {
            var enemies = combat?.Enemies;
            if (enemies != null)
            {
                foreach (var c in enemies)
                {
                    if (c?.Monster is CharBossMonster b) list.Add(b);
                }
            }
            list.Sort((a, b) => string.CompareOrdinal(SafeEntry(a), SafeEntry(b)));
        }
        catch { /* 忽略 */ }
        return list;
    }

    private static bool IsAlivePlayer(Player? p)
    {
        try { return p?.Creature != null && p.Creature.IsAlive; }
        catch { return false; }
    }

    /// <summary>
    /// **只读**地看一眼「这只 Boss 此刻锁着谁」（不分配、不修正、无副作用）。
    ///
    /// 用途：意图显示要按「实时锁定」判断「这一手打不打得到本地玩家」。
    /// 多人局直接返回纯函数的结果（和 `TargetFor` 同一个答案，但不会写 `Locks`）。
    /// </summary>
    internal static Player? PeekLock(CharBossMonster? boss)
    {
        try
        {
            if (boss?.Creature == null) return null;
            if (IsMultiplayerRun(boss)) return PureVictim(boss, boss.CombatState);

            if (!Locks.TryGetValue(boss.Creature, out var p)) return null;
            if (p?.Creature == null || !p.Creature.IsAlive) return null;
            return p;
        }
        catch { return null; }
    }

    /// <summary>某个 Boss 是否「专门针对这个玩家」（特殊效果按人分流时用）。</summary>
    public static bool IsLocked(CharBossMonster boss, Player? player)
    {
        try
        {
            if (boss?.Creature == null || player == null) return false;
            if (player.Creature?.Monster != null) return false;      // Boss 影子 Player 不算玩家
            if (Locks.TryGetValue(boss.Creature, out var p) && ReferenceEquals(p, player)) return true;
            // 单人：没有注册过也认为锁定他（保证单人不依赖注册顺序）
            return RealPlayerCount(boss.CombatState) == 1 &&
                   ReferenceEquals(RealPlayers(boss.CombatState).FirstOrDefault(), player);
        }
        catch { return false; }
    }

    /// <summary>
    /// 当前这场里**锁定这名玩家**的所有我们的 Boss（按怪物 id 稳定排序）。
    ///
    /// 用途：<see cref="Powers.LockOnPower"/> 的说明文字。以前名字只来自那层 Power 自己的
    /// 「谁挂过我」名单，一旦那份名单为空就退化成「一只角色 Boss」（用户 2026-10-29 四人联机截图：
    /// 一个人身上两个锁定图标、悬浮下去都没有 Boss 名）。真相在 `Locks` 里，这里直接按它算。
    /// </summary>
    public static List<CharBossMonster> BossesLocking(Player? player, ICombatState? combat)
    {
        var result = new List<CharBossMonster>();
        try
        {
            if (player?.Creature == null || combat == null) return result;
            foreach (var creature in combat.Enemies)
            {
                if (creature?.Monster is not CharBossMonster boss) continue;
                if (!creature.IsAlive) continue;
                if (IsLocked(boss, player)) result.Add(boss);
            }
            result.Sort((a, b) => string.CompareOrdinal(SafeEntry(a), SafeEntry(b)));
        }
        catch { /* 忽略 */ }
        return result;
    }

    /// <summary>
    /// 给这只 Boss 挑一个锁定对象。规则（用户 2026-10-29 第二次订正后**只有一条**）：**按当前负荷挑人** ——
    /// 活着的玩家按 NetId 排，给「被锁次数最少」的那个，并列取 NetId 最小的。
    /// 既均匀（N 人 M Boss 时天然 round-robin），又是确定的（SL 前后一致），
    /// 而且**不会动已经成立的锁**（那只归 `Rebalance` 管）。
    ///
    /// ⚠ 以前这里算的是 `boss[rank] → players[(rank + offset) % n]`（offset 来自本局种子）。
    /// 那个写法的致命问题：`offset` 依赖**玩家数**，所以只要有人死掉（4→3）整张表就整体平移 ——
    /// 明明没死的锁也被换人（用户 2026-10-29 实测：「3 号位死后 1 号位的 Boss 跑到了 2 号位」）。
    /// </summary>
    private static Player? PickForBoss(CharBossMonster boss, ICombatState combat)
    {
        try
        {
            var alive = RealPlayers(combat)
                        .Where(p => p?.Creature != null && p.Creature.IsAlive)
                        .OrderBy(p => p.NetId)
                        .ToList();
            if (alive.Count == 0) return null;

            // ⚠ 开局前几帧 `combat.Players` 是**逐个填满**的：还没到齐就先不锁定。
            //   否则先被问到的那几只 Boss 会按「当时只有 1 个人」全都挑中他（旧 bug：两只 Boss 撞同一人）。
            //   返回 null 的这段时间里，选靶/图标都退回原版行为，等人都到齐再由 `Rebalance` 统一分配。
            try
            {
                int declared = combat.RunState.Players.Count;
                if (declared > alive.Count) return null;
            }
            catch { /* 读不到人数就不管 */ }

            return PickLeastLockedAmong(alive);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 选锁定玩家失败: " + e.Message);
            return null;
        }
    }

    /// <summary>
    /// <summary>
    /// **玩家阵亡 → 立刻把锁定关系同步到「纯函数」算出来的结果**（用户 2026-10-29）。
    ///
    /// 现在「谁锁谁」是 <see cref="PureVictim"/> 的纯函数结果：花名册（含死者）按 NetId 取模，
    /// 分到的人若已阵亡就交给「还活着的、NetId 最小」的那位。
    /// ⇒ 3 号位死后只有「原本分给 3 号位的那只 Boss」换到 1 号位，A/B/D 一个都不动
    /// （用户口径：「应该是 1 号位同时对自己本来对的和 3 号位对的」）。
    /// 这里只负责**立刻**把 `Locks` 与图标同步过去，不等 4Hz 的慢车道。
    /// </summary>
    public static void ReassignDeadLocks(ICombatState? combat)
    {
        try
        {
            if (combat?.Enemies == null) return;
            if (!IsMultiplayerRun(null)) return;

            foreach (var c in combat.Enemies)
            {
                if (c?.Monster is not CharBossMonster boss) continue;
                if (!c.IsAlive) continue;

                var want = PureVictim(boss, combat);
                if (want == null) continue;

                Locks.TryGetValue(c, out var cur);
                if (ReferenceEquals(cur, want)) continue;

                Locks[c] = want;
                Logger.Log($"[CharBoss] 跟踪锁定同步：{SafeEntry(boss)} → {DescribeTarget(want)}" +
                           (cur == null ? "（本场分配）" : $"（原来锁的 {DescribeTarget(cur)} 已阵亡/离场）"));

                // 图标：死者身上那份摘掉、新目标那份补上 —— 立刻做一次，别等下一帧对账。
                if (cur != null) { try { DropLockIcon(cur, c, "锁定对象已阵亡"); } catch { /* 忽略 */ } }
                try { ReconcilePowersOnce(combat); } catch { /* 忽略 */ }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 跟踪锁定同步失败（忽略）: " + e.Message);
        }
    }

    /// <summary>在给定活人里挑「被锁次数最少」的那个（并列按 NetId 升序）。**只给单人/兜底用**。</summary>
    private static Player? PickLeastLockedAmong(List<Player> alive)
    {
        try
        {
            if (alive.Count == 0) return null;
            var counts = new Dictionary<Player, int>();
            foreach (var p in alive) counts[p] = 0;
            foreach (var kv in Locks)
            {
                if (kv.Value == null) continue;
                if (counts.ContainsKey(kv.Value)) counts[kv.Value]++;
            }
            int min = counts.Values.Min();
            return alive.FirstOrDefault(p => counts.TryGetValue(p, out int n) && n == min);
        }
        catch { return alive.FirstOrDefault(); }
    }


    /// <summary>
    /// 日志里描述一名玩家：`<角色类名>/NetId=N`。
    /// **必须带 NetId** —— 远端玩家的昵称在本机经常是 `[unknown]`，而四个实例又写同一个 `mod_log.txt`，
    /// 不带 NetId 根本分不清「打的是 1 号位还是 2 号位」（用户 2026-10-29 实测要核对这个）。
    /// </summary>
    internal static string DescribeTarget(Player? player)
    {
        try
        {
            if (player == null) return "？";
            string who = player.Character?.GetType().Name ?? "?";
            string netId = "?";
            try { netId = player.NetId.ToString(); } catch { netId = "?"; }
            return $"{who}/NetId={netId}";
        }
        catch { return "？"; }
    }
    /// <summary>把「对账」包一层节流外的直调（改派时立刻用一次）。</summary>
    private static void ReconcilePowersOnce(ICombatState? combat)
    {
        try { ReconcilePowers(combat); } catch { /* 忽略 */ }
    }

    /// <summary>被锁定次数最少的活着的玩家（并列按 NetId 升序）。保留给「按负荷挑人」的旧路径。</summary>
    private static Player? PickLeastLocked(Creature bossCreature, IReadOnlyList<Player> players)
    {
        var alive = players.Where(p => p?.Creature != null && p.Creature.IsAlive)
                           .Where(p => p.Creature.Monster == null)       // ⚠ 排除 Boss 影子 Player
                           .OrderBy(p => p.NetId)                        // 稳定顺序（SL 后也一致）
                           .ToList();
        if (alive.Count == 0) return null;

        // 每只 Boss 各占一个锁定；统计每个玩家被几只 Boss 锁着
        var counts = new Dictionary<Player, int>();
        foreach (var p in alive) counts[p] = 0;
        foreach (var kv in Locks)
        {
            if (ReferenceEquals(kv.Key, bossCreature)) continue;             // 自己那只不算
            if (kv.Value == null || !counts.ContainsKey(kv.Value)) continue;
            counts[kv.Value]++;
        }

        int min = counts.Values.Min();
        return alive.FirstOrDefault(p => counts.TryGetValue(p, out int n) && n == min);
    }

    // ───────────────────────────── 记录 / 清理 ─────────────────────────────

    /// <summary>已经为哪一场战斗做过「清理旧跟踪锁定」（一场只做一次，见方法注释）。</summary>
    private static object? _cleanedFight;

    /// <summary>
    /// 新战斗开始（每只 Boss 生成时调一次，双方都会各跑一遍）：
    /// 把**跨战斗残留**的「跟踪锁定」清干净 —— 名单清空、层数归零、图标摘掉。
    ///
    /// 为什么必须做：这层 Power 会留在玩家身上跨战斗，而它的**层数＝锁定者数量**是联机校验的一部分。
    /// 两边的历史场次不同 ⇒ 层数不同（实机诊断包：local LOCK_ON_POWER:6 / remote:4）
    /// ⇒ 直接报「多人数据不同步」并掉线（2026-10-22 实测）。
    /// </summary>
    public static void CleanupStaleForNewFight(CharBossMonster boss)
    {
        try
        {
            // ⚠ 一场战斗只清一次（用户 2026-10-29 日志里的浪费：`实例 #1 创建` → 立刻
            //   `新战斗：把 … 旧「跟踪锁定」摘掉了` → 又 `实例 #4 创建`）。
            //   它原本每只 Boss 生成时都跑一次（4 只 Boss = 同一秒跑 4 次），
            //   而那时**本场刚挂上的图标**也会被当成「旧图标」摘掉 ⇒ 加一份、删一份地来回折腾。
            //   现在用「当前战斗状态对象」做门闩：一场只清一次，而且清完由 `ReconcilePowers` 统一重建。
            object? fightKey = null;
            try { fightKey = MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.DebugOnlyGetState(); }
            catch { fightKey = null; }
            if (fightKey != null && ReferenceEquals(fightKey, _cleanedFight)) return;
            _cleanedFight = fightKey;

            // 注册表本来就该是干净的（生物实例每场都是新的），顺手清一次防残留
            Locks.Clear();
            Recorded.Clear();
            // ⚠ 这里**不清**图标的本地记账：`CleanupStaleForNewFight` 每只 Boss 生成时都会跑
            //   （4 只 Boss = 同一秒跑 4 次），清掉「本场已经挂过几次」就会把上限作废 ⇒
            //   v0.2.145 里 `#1~#4` 之后又冒出 `#5~#7` 就是这么来的。
            //   只有 `Reset()`（战斗结束）才清。

            foreach (var p in RealPlayers(boss?.CombatState))
            {
                if (p?.Creature == null) continue;

                // 一 Boss 一份 ⇒ 一个人身上可能有好几份，全部摘掉（每场重新挂）
                var olds = new List<Powers.LockOnPower>();
                foreach (var slot in p.Creature.Powers)
                {
                    if (slot is Powers.LockOnPower lp) olds.Add(lp);
                }
                if (olds.Count == 0) continue;

                foreach (var power in olds) RemovePower(power);
                Logger.Log($"[CharBoss] 新战斗：把 {p.Character?.GetType().Name} 身上的旧「跟踪锁定」摘掉了" +
                           $"（{olds.Count} 份）");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 清理旧跟踪锁定失败: " + e.Message);
        }
    }

    /// <summary><see cref="Powers.LockOnPower"/> 挂上时回报的锁定关系。</summary>
    internal static void NoteLocked(Creature playerCreature, Creature? bossCreature)
    {
        try
        {
            if (playerCreature == null || bossCreature == null) return;
            Locks[bossCreature] = playerCreature.Player!;
        }
        catch { /* 忽略 */ }
    }

    /// <summary>给「锁定这个 Boss 的玩家」挂上 <see cref="Powers.LockOnPower"/>（幂等）。</summary>
    public static void EnsurePower(CharBossMonster boss)
    {
        try
        {
            var bossCreature = boss?.Creature;
            if (bossCreature == null) return;

            var victim = TargetFor(boss);
            if (victim?.Creature == null) return;

            // ① 先把历史/并发留下的重复锁定层扫掉（幂等）。
            SweepDuplicateLockPowers(boss.CombatState);

            // 用「玩家引用 + Boss 引用」做去重键（Creature 没有 Id 属性）
            var key = (System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(victim),
                       System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(boss));
            if (!Recorded.Add(key)) return;

            // ② 图标的挂载**只走权威对账**（ReconcilePowers）—— 以前这里直接
            //    `_ = ApplyLockPowerAsync(...)`，多只 Boss 并发进来会各建一份 ⇒
            //    同一名玩家身上两个锁定图标（用户 2026-10-29 四人局）。
            bool multiplayerRun = IsMultiplayerRun(boss);
            int aliveCount = RealPlayerCount(boss.CombatState);
            if (multiplayerRun) ReconcilePowers(boss.CombatState);
            SubscribeDeath(victim);

            Logger.Log($"[CharBoss] 跟踪锁定：{boss.Def.Key} → {victim.Character?.GetType().Name}" +
                       $"（多人局={multiplayerRun}，存活 {aliveCount} 人" +
                       $"{(multiplayerRun ? string.Empty : "，单人仅内部选靶")}）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 跟踪锁定挂载失败: " + e.Message);
        }
    }

    /// <summary>
    /// <summary>
    /// **权威对账**（用户 2026-10-29 定稿：一个 Boss 一份图标，要像「坚韧之环 / 炸弹」那样能共存）。
    ///
    /// 数据模型：玩家的状态栏上，**每只锁定他的 Boss 各一份 `LockOnPower`**
    /// （`PowerInstanceType.InstancedPerApplier`，见 `Powers/LockOnPower.cs` 类注释）。
    /// 所以这里做的是**集合对账**：
    ///   · `wanted` = 注册表里此刻锁定他的、还活着的我们的 Boss；
    ///   · `byBoss` = 他身上现有实例，按**施加者**（= 哪只 Boss）分组；
    ///   · 不在 `wanted` 里的、施加者丢失的、以及同一只 Boss 的多余副本 → 摘掉；
    ///   · `wanted` 里有但没有实例的 → 补挂一份（同一只 Boss 的 Apply 只会合并到它自己那份，
    ///     因为 `InstancedPerApplier` 的 `FindExistingInstanceForStacking` 是按 applier 找的）。
    ///
    /// 为什么每帧（4Hz）对账而不是只在 Apply 时挂：`PowerCmd.Apply` 是异步走动作队列的，
    /// 两只 Boss 同一帧进来会各自建一份（对不上账），而且游戏自己还会摘掉我们挂的 power
    /// （v0.2.145 探针实证）。对账把它拉回「注册表说该有几个就有几个」。
    /// </summary>
    public static void ReconcilePowers(ICombatState? combat)
    {
        try
        {
            if (combat == null) return;
            if (!IsMultiplayerRun(null)) return;          // 单人：不挂图标（零回归）

            var state = new List<string>();

            Player? me = null;
            try { me = MegaCrit.Sts2.Core.Context.LocalContext.GetMe(combat); }
            catch { me = null; }

            foreach (var player in RealPlayers(combat))
            {
                var creature = player?.Creature;
                if (creature == null) continue;

                // ① 注册表说「现在该有哪几只 Boss 锁着他」
                var wanted = new List<Creature>();
                foreach (var c in combat.Enemies)
                {
                    if (c?.Monster is not CharBossMonster b) continue;
                    if (!c.IsAlive) continue;
                    if (IsLocked(b, player)) wanted.Add(c);
                }
                wanted.Sort((a, b) => string.CompareOrdinal(SafeCreatureEntry(a), SafeCreatureEntry(b)));

                // ② 他身上的实例，按施加者分组
                var byBoss = new Dictionary<Creature, List<Powers.LockOnPower>>();
                var stray = new List<Powers.LockOnPower>();
                foreach (var slot in creature.Powers)
                {
                    if (slot is not Powers.LockOnPower lp) continue;
                    Creature? applier = null;
                    try { applier = lp.Applier; } catch { applier = null; }
                    if (applier?.Monster is CharBossMonster)
                    {
                        if (!byBoss.TryGetValue(applier, out var list))
                        {
                            list = new List<Powers.LockOnPower>();
                            byBoss[applier] = list;
                        }
                        list.Add(lp);
                    }
                    else
                    {
                        stray.Add(lp);      // 施加者不是我们的 Boss（旧存档 / 跨场残留）
                    }
                }

                // 巡检日志：`<玩家>=实例数/应有数`
                state.Add($"{player!.Character?.GetType().Name}{(ReferenceEquals(player, me) ? "(本机)" : string.Empty)}" +
                          $"={byBoss.Count}/{wanted.Count}");

                // ③ 摘掉不该有的
                foreach (var kv in byBoss)
                {
                    if (!wanted.Contains(kv.Key))
                    {
                        foreach (var p in kv.Value)
                        {
                            Logger.Log($"[CharBoss] 跟踪锁定图标：{player.Character?.GetType().Name} 身上的 " +
                                       $"{SafeCreatureEntry(kv.Key)} 已经不再锁定他 → 摘掉这一个图标");
                            RemovePower(p);
                        }
                        continue;
                    }
                    for (int i = 1; i < kv.Value.Count; i++)
                    {
                        Logger.Log($"[CharBoss] 跟踪锁定图标：{player.Character?.GetType().Name} 身上 " +
                                   $"{SafeCreatureEntry(kv.Key)} 的图标有 {kv.Value.Count} 份（并发/历史遗留）" +
                                   "⇒ 摘掉多余的，每只 Boss 只留一份");
                        RemovePower(kv.Value[i]);
                    }
                }
                foreach (var p in stray)
                {
                    Logger.Log($"[CharBoss] 跟踪锁定图标：{player.Character?.GetType().Name} 身上有一份" +
                               "没有施加者的旧锁定（跨场残留）→ 摘掉");
                    RemovePower(p);
                }

                // ④ 补上没有的：每只该锁他的 Boss 各一份
                // ⚠ 阵亡的玩家不再补图标（锁本身应该已经被 `ReassignDeadLocks` 改派走了；
                //   万一还没改派，也不要往尸体上挂新图标）。
                bool victimAlive = creature.IsAlive;
                foreach (var bossCreature in wanted)
                {
                    if (byBoss.TryGetValue(bossCreature, out var have) && have.Count > 0) continue;
                    if (!victimAlive) continue;
                    TryApplyLockIcon(player!, bossCreature);
                }
            }

            var text = string.Join(" | ", state);
            if (text != _lastIconState)
            {
                _lastIconState = text;
                Logger.Log($"[CharBoss] 锁定图标巡检（每名玩家：实例数/应有数）：{text}");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 锁定图标对账失败（忽略）: " + e.Message);
        }
    }

    private static void RemovePower(Powers.LockOnPower? power)
    {
        try { if (power != null) _ = MegaCrit.Sts2.Core.Commands.PowerCmd.Remove(power); }
        catch { /* 忽略 */ }
    }

    /// <summary>正在挂图标的 (玩家, Boss) 组合（→ 发起挂载的帧号；600 帧后过期重试）。</summary>
    private static readonly Dictionary<(int player, int boss), int> _applying = new();

    /// <summary>
    /// 同一名玩家身上、同一只 Boss 的图标最多补挂几次。
    ///
    /// v0.2.148 起「屏幕上多出一个图标」的主要成因（残留 UI 节点）已经在渲染层根治
    /// （`Patches/PowerIconNodeReconcile`），所以这里不再需要靠「少挂」来掩盖问题；
    /// 留一个宽松上限只是防「游戏每帧都摘」时变成死循环。
    /// </summary>
    private const int MaxIconAppliesPerPlayer = 8;

    /// <summary>本场每个 (玩家, Boss) 已经挂过几次图标。</summary>
    private static readonly Dictionary<(int player, int boss), int> _createdCount = new();

    private static int Now()
    {
        try { return (int)Godot.Engine.GetFramesDrawn(); }
        catch { return 0; }
    }

    /// <summary>上次对账的帧号（节流用）。</summary>
    private static int _lastReconcileFrame = -100;

    /// <summary>上次印出来的「每人几个锁定图标」（变化时才打日志）。</summary>
    private static string _lastIconState = string.Empty;

    /// <summary>给这名玩家补挂一张「某只 Boss 锁定他」的图标（幂等：同一 (玩家,Boss) 600 帧内只发起一次）。</summary>
    private static void TryApplyLockIcon(Player victim, Creature bossCreature)
    {
        try
        {
            var creature = victim?.Creature;
            if (creature == null || bossCreature == null) return;

            var key = (System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(creature),
                       System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(bossCreature));

            _createdCount.TryGetValue(key, out int made);
            if (made >= MaxIconAppliesPerPlayer) return;

            int frame = Now();
            if (_applying.TryGetValue(key, out int startedAt) && frame - startedAt <= 600) return;

            _applying[key] = frame;
            _createdCount[key] = made + 1;
            _ = ApplyLockIconAsync(victim, bossCreature, key);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 跟踪锁定：发起挂图标失败: " + e.Message);
        }
    }

    /// <summary>
    /// 挂一份图标。**applier 必须传这只 Boss 的生物** —— `InstancedPerApplier` 就是靠它
    /// 判断「同一只 Boss 是否已经有一份」，也是悬浮文案里「谁盯上了你」的来源。
    /// </summary>
    private static async System.Threading.Tasks.Task ApplyLockIconAsync(
        Player victim, Creature bossCreature, (int player, int boss) key)
    {
        try
        {
            var creature = victim?.Creature;
            if (creature == null) return;
            var ctx = new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext();
            var power = await MegaCrit.Sts2.Core.Commands.PowerCmd.Apply<Powers.LockOnPower>(
                ctx, creature, 1m, bossCreature, null, false);
            _applying.Remove(key);
            if (power == null) return;
            Logger.Log($"[CharBoss] 跟踪锁定图标：{victim!.Character?.GetType().Name} 挂了 1 个锁定图标" +
                       $"（{SafeCreatureEntry(bossCreature)} 锁定他；同类图标可以共存）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 跟踪锁定：挂图标失败: " + e.Message);
            _applying.Remove(key);      // 失败就立刻放开，下一帧重试
        }
    }

    private static string SafeCreatureEntry(Creature? creature)
    {
        try { return creature?.Monster?.Id.Entry ?? creature?.Name ?? "?"; } catch { return "?"; }
    }

    /// <summary>
    /// 这一局**本身**是不是多人局（与「现在还剩几个活人」无关）—— 读 `RunState.Players`，
    /// 一个玩家死了它仍然是 2。单人局恒为 false ⇒ 场上不加锁定图标（用户要求零回归）。
    /// </summary>
    private static bool IsMultiplayerRun(CharBossMonster? boss)
    {
        try
        {
            return MultiBossPlan.WantsSecondBoss(out int players) && players >= 2;
        }
        catch { return false; }
    }

    /// <summary>
    /// 玩家死亡 → 手动把他的锁全部作废（设计 §阶段 1 第 5 条）。
    /// 每个玩家生物只订阅一次；`TargetFor` 里还有一道「锁的人已死就重分」的兜底，
    /// 所以就算这里没赶上（例如读档），下次选靶也会自愈。
    /// </summary>
    private static readonly HashSet<int> DeathSubscribed = new();

    private static void SubscribeDeath(Player? player)
    {
        try
        {
            var creature = player?.Creature;
            if (creature == null) return;
            if (!DeathSubscribed.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(creature))) return;
            creature.Died += _ => OnPlayerDied(player);
        }
        catch { /* 忽略 */ }
    }


    /// <summary>
    /// **扫掉同一只 Boss 的多余图标**（一 Boss 一份是设计，多出来的才是并发/历史遗留）。
    ///
    /// 为什么需要单独扫一遍：`PowerCmd.Apply` 是走动作队列的，**返回时那份 Power 还不一定已经进
    /// `creature.Powers`**，所以「Apply 完立刻查重复」会漏；而同一个 (玩家, Boss) 组合被并发发起两次时
    /// `InstancedPerApplier` 的合并判定也可能扑空 ⇒ 同一只 Boss 出现两份图标。
    /// 这里在每个 Boss 每回合的 <see cref="EnsurePower"/> 开头扫一次（幂等）。
    ///
    /// ⚠ 只摘「同一只 Boss 的副本」—— **不同 Boss 的图标要共存**（用户 2026-10-29 定稿）。
    /// </summary>
    private static void SweepDuplicateLockPowers(ICombatState? combat)
    {
        try
        {
            if (combat == null) return;
            foreach (var player in RealPlayers(combat))
            {
                var creature = player?.Creature;
                if (creature == null) continue;

                var byBoss = new Dictionary<Creature, List<Powers.LockOnPower>>();
                foreach (var slot in creature.Powers)
                {
                    if (slot is not Powers.LockOnPower lp) continue;
                    Creature? applier = null;
                    try { applier = lp.Applier; } catch { applier = null; }
                    if (applier == null) continue;         // 无施加者的交给 ReconcilePowers 处理
                    if (!byBoss.TryGetValue(applier, out var list))
                    {
                        list = new List<Powers.LockOnPower>();
                        byBoss[applier] = list;
                    }
                    list.Add(lp);
                }

                foreach (var kv in byBoss)
                {
                    if (kv.Value.Count <= 1) continue;
                    for (int i = 1; i < kv.Value.Count; i++)
                    {
                        Logger.Log($"[CharBoss] 跟踪锁定：{player.Character?.GetType().Name} 身上 " +
                                   $"{SafeCreatureEntry(kv.Key)} 的图标有 {kv.Value.Count} 份" +
                                   "（并发/历史遗留）⇒ 摘掉多余的，每只 Boss 只留一份");
                        RemovePower(kv.Value[i]);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 清理重复锁定层失败（忽略）: " + e.Message);
        }
    }

    /// <summary>
    /// 把「N 只 Boss 分给 N 个玩家」做一次均衡（用户设计：每只 Boss 锁一个玩家、尽量各锁一个）。
    ///
    /// 为什么需要单独一步：每只 Boss 是在自己回合里各自 <see cref="TargetFor"/> 选人的，
    /// 若两只都在「还没别人锁」的时刻选，就可能都挑中同一个玩家（另一人没人打）。
    /// 这里在每帧便宜地检查一次：只处理「没锁 / 锁的人已死」的 Boss；若负荷不均匀（max−min &gt; 1）
    /// 再把它们按稳定顺序重新轮着分。单人（1 只 Boss / 1 个玩家）时什么都不做。
    /// </summary>
    public static void Rebalance(System.Collections.Generic.IReadOnlyList<CharBossMonster> bosses)
    {
        try
        {
            if (bosses == null || bosses.Count < 2) return;

            var combat = bosses[0]?.CombatState;
            if (combat == null) return;
            // ⚠ 「是不是多人局」用 RunState 的人数判，**不能用活着的真人玩家数** ——
            //   一个玩家死掉之后存活只剩 1 人，按它判就会直接早退，
            //   于是「玩家死 → 那只 Boss 改锁存活玩家」这一步永远不发生（用户实测）。
            if (!IsMultiplayerRun(bosses[0])) return;

            // ① 分配：**纯函数**（见 PureVictim）—— 各端算出来的答案必然一致。
            //    这里只把结果同步进 `Locks`（缓存的用途仅限日志/图标；选靶与显示都走纯函数）。
            //    ⚠ 不要在这里「按当前负荷挑人」：那依赖各端已经攒下的 `Locks` 内容，
            //      各端一分叉就会出现「我这台显示 0 伤害、他那台真打我 10」（用户 2026-10-29 实测）。
            var sorted = StableBossOrder(bosses);
            if (sorted.Count == 0) return;

            bool changed = false;
            foreach (var boss in sorted)
            {
                if (boss?.Creature == null) continue;

                var want = PureVictim(boss, combat);
                if (want == null) continue;                 // 玩家还没到齐 ⇒ 各端都先不锁

                bool hasLock = Locks.TryGetValue(boss.Creature, out var current);
                if (hasLock && ReferenceEquals(current, want)) continue;

                Locks[boss.Creature] = want;
                changed = true;
                Logger.Log($"[CharBoss] 跟踪锁定：{SafeEntry(boss)} → {DescribeTarget(want)}" +
                           (hasLock
                               ? $"（原来锁的 {DescribeTarget(current)} 已阵亡/离场）"
                               : "（本场分配）") +
                           $"（花名册 {RosterOf(combat).Count} 人 / {sorted.Count} 只 Boss，NetId 顺序取模）");
            }

            if (changed)
            {
                // 把整张表打一遍 —— 站位/行动次序对不上时用它核对「谁被谁盯上」
                var table = new List<string>();
                foreach (var boss in sorted)
                {
                    if (boss?.Creature == null) continue;
                    table.Add($"{SafeEntry(boss)}→{DescribeTarget(PureVictim(boss, combat))}");
                }
                Logger.Log($"[CharBoss] 跟踪锁定分配表（{RosterOf(combat).Count} 人 / {sorted.Count} 只）：" +
                           string.Join(" | ", table));
            }

            // 校准完立即按注册表把图标重建一遍（唯一入口，见 ReconcilePowers）
            ReconcilePowers(combat);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 跟踪锁定再平衡失败: " + e.Message);
        }
    }

    /// <summary>
    /// Boss 的**稳定**顺序（按怪物 id 排）—— 不能用 `RuntimeHelpers.GetHashCode`：
    /// 那个值每次运行/SL 后都可能变，锁定分配就会跟着变（用户实测：SL 后锁定关系变了）。
    /// </summary>
    private static List<CharBossMonster> StableBossOrder(System.Collections.Generic.IReadOnlyList<CharBossMonster> bosses)
    {
        var list = new List<CharBossMonster>();
        try
        {
            foreach (var b in bosses)
            {
                if (b?.Creature != null) list.Add(b);
            }
            list.Sort((a, b) => string.CompareOrdinal(SafeEntry(a), SafeEntry(b)));
        }
        catch { /* 忽略 */ }
        return list;
    }

    private static string SafeEntry(CharBossMonster? boss)
    {
        try { return boss?.Id.Entry ?? string.Empty; } catch { return string.Empty; }
    }

    /// <summary>
    /// 每帧兜底：扫一遍注册表，**主人已经死了的锁**直接清掉（并把玩家身上那层状态摘掉）。
    ///
    /// 为什么需要：<see cref="OnBossDied"/> 只对「当前遍历到的」Boss 生效，而遍历用的
    /// `AllBosses` 是从面板 Boss + `CombatState.Enemies` 来的 —— 死掉的 Boss 可能已经从
    /// Enemies 里消失、面板又显示的是另一只，于是「被打死的那只的锁」永远清不掉
    /// （用户实测：秒杀储君后身上的跟踪锁定还在）。
    /// </summary>
    public static void PurgeDeadBosses()
    {
        try
        {
            if (Locks.Count == 0) return;
            foreach (var key in Locks.Keys.ToList())
            {
                // 死掉的、或者**已经不属于任何一场战斗**（SL / 换房间后残留的旧实例）都清掉
                bool stale;
                try
                {
                    stale = key == null || key.IsDead || !key.IsAlive || key.CombatState == null;
                }
                catch { stale = true; }
                if (!stale) continue;

                if (Locks.TryGetValue(key, out var victim))
                {
                    Locks.Remove(key);
                    DropLockIcon(victim, key, "锁定它的 Boss 已死亡");
                }
                else
                {
                    Locks.Remove(key);
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 清理已死 Boss 的锁定失败: " + e.Message);
        }
    }

    /// <summary>Boss 死亡 → 移除它的锁定，并把玩家身上那层「跟踪锁定」状态摘掉（否则图标会残留）。</summary>
    public static void OnBossDied(CharBossMonster? boss)
    {
        try
        {
            var bossCreature = boss?.Creature;
            if (bossCreature == null) return;
            if (!Locks.TryGetValue(bossCreature, out var victim))
            {
                Locks.Remove(bossCreature);
                return;
            }
            Locks.Remove(bossCreature);

            // 摘掉残留的状态图标（**只摘这一只**；同一名玩家身上可能还挂着别的 Boss 的锁定）
            DropLockIcon(victim, bossCreature, $"{boss.Def.Key} 已倒下");
        }
        catch { /* 忽略 */ }
    }

    /// <summary>
    /// 摘掉这名玩家身上**属于这只 Boss** 的那一份 <see cref="Powers.LockOnPower"/>
    /// （一 Boss 一份 ⇒ 别人那份不动，图标继续共存）。
    /// </summary>
    private static void DropLockIcon(Player? victim, Creature? bossCreature, string why)
    {
        try
        {
            var creature = victim?.Creature;
            if (creature == null || bossCreature == null) return;

            int dropped = 0, others = 0;
            var targets = new List<Powers.LockOnPower>();
            foreach (var slot in creature.Powers)
            {
                if (slot is not Powers.LockOnPower lp) continue;
                Creature? applier = null;
                try { applier = lp.Applier; } catch { applier = null; }
                if (ReferenceEquals(applier, bossCreature)) targets.Add(lp);
                else others++;
            }

            foreach (var power in targets)
            {
                RemovePower(power);
                dropped++;
            }

            if (dropped > 0)
            {
                Logger.Log($"[CharBoss] 跟踪锁定解除（{why}）：摘掉 {SafeCreatureEntry(bossCreature)} 那份图标，" +
                           $"玩家身上还剩 {others} 只 Boss 锁定");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 摘除锁定图标失败: " + e.Message);
        }
    }

    /// <summary>玩家死亡 → 他身上的锁全部作废，并**立刻**改派给还活着的玩家（不等下一帧）。</summary>
    public static void OnPlayerDied(Player? player)
    {
        try
        {
            if (player == null) return;
            var stale = Locks.Where(kv => ReferenceEquals(kv.Value, player)).Select(kv => kv.Key).ToList();
            foreach (var key in stale) Locks.Remove(key);

            // ⚠ 只删不补 = 「死人还挂着锁、活人没人管」（用户 2026-10-29 实测就是这个现象）。
            //   这里立刻按「被锁次数最少的活人」补齐，并顺手把图标搬过去。
            var combat = player.Creature?.CombatState;
            if (combat != null) ReassignDeadLocks(combat);
        }
        catch { /* 忽略 */ }
    }

    /// <summary>新一场战斗开始：清空注册表（生物实例换了，旧键无意义）。</summary>
    public static void Reset()
    {
        Locks.Clear();
        Recorded.Clear();
        _cleanedFight = null;      // 下一场重新允许「清理旧图标」一次
        // 本场「Boss 花名册」也要清（它按 CombatState 缓存，下一场是新的 CombatState；
        // 这里顺手清掉，避免长期同进程反复开局时越攒越多）
        try { BossRosterCache.Clear(); } catch { /* 忽略 */ }
        ClearIconBookkeeping();
    }

    /// <summary>
    /// 清掉「图标的本地记账」（本场已建过 / 正在建 / 上次巡检文本）。
    /// 跨场必须清：`RuntimeHelpers.GetHashCode` 会被新生物复用，
    /// 不清的话上一场的「已建过」会挡住这一场的创建。
    /// </summary>
    private static void ClearIconBookkeeping()
    {
        try
        {
            _createdCount.Clear();
            _applying.Clear();
            _lastIconState = string.Empty;
        }
        catch { /* 忽略 */ }
    }
}
