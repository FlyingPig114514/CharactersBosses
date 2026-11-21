using System;
using System.Collections.Generic;
using System.Text;
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace CharacterBosses.Core;

/// <summary>
/// **双向的「以房主为准」同步消息**。
///
/// | 方向 | 带的内容 |
/// |---|---|
/// | 房主 → 客人 | ① 每张地图的 Boss 勾选（A2）② 玩法设置（共用匹配池 / 进阶10一场原本 / 每张地图替换开关）③ **本局每个 (幕, 场) 的最终遭遇**（v0.2.711）④ 观者能力结论 |
/// | 客人 → 房主 | 只有一项：**本机的观者实现身份**（`boninall` / `lamali` / 没装），房主据此判断「观者能不能出现」（v0.2.711） |
///
/// ## 为什么要有 ③「本局每个 (幕, 场) 的最终遭遇」
/// 玩家反馈（2026-11-16，工坊评论，无日志）：「新版多人联机 n10 第三幕第二个 boss 疑似不同步，
/// 房主看到的是俩玩家 boss，客人看到的是一个原来的 boss」。
///
/// 自检结论（`docs/plans/desync-2026-11-16-mp-act3-second-boss.md`）：第三幕**第二场**在客人端
/// 只有一条「跟随房主」的路 —— `act.SecondBossEncounter` 的原值还是原版时就只能自己按
/// 「确定性规则」重算一遍。而那套规则的输入里有三处**每台机器可能不同**的东西：
/// 候补表（观者模组装没装）、房主勾选（广播没到就没有）、地图替换开关（模组图默认**不替换**）
/// ⇒ 只要一处不同，客人就会「保持原来的 Boss」，而房主换成了角色 Boss。
///
/// 现在房主把**自己算出来的结论**（每个 (幕, 场) 到底是哪一只，或者「这一场保持原版」）
/// 直接广播过去，客人**无条件采用** ⇒ 配置 / 模组 / 地图默认值这一整类分叉被一次关掉。
///
/// ## 协议
/// 走 BaseLib 的 `ICustomMessage` + `CustomMessageWrapper.Send`（类名排序分配消息 id）。
/// ⚠ **协议变过 ⇒ 多人两端必须装同一版本的模组**。
/// BaseLib 缺失时这条通道整个不可用（`BaseLibPresence`），此时退回确定性规则（行为同 v0.2.710）。
/// </summary>
public sealed class BossPicksMessage : ICustomMessage, IPacketSerializable
{
    /// <summary>房主：序列化后的勾选 `地图id|Key,Key,Key;地图id|Key,…`（没出现的地图 = 全选）。</summary>
    public string Payload = string.Empty;

    /// <summary>房主：玩法设置 `sp=1;asc10=0;wok=1;maps=OVERGROWTH:1,…`。</summary>
    public string Settings = string.Empty;

    /// <summary>
    /// 房主：**本局每个 (幕, 场) 的最终遭遇** —— `幕:场|KEY;幕:场|VANILLA;…`
    /// （幕 / 场都从 0 起；`VANILLA` = 这一场房主决定保持原版 Boss 不替换）。
    /// </summary>
    public string Bosses = string.Empty;

    /// <summary>客人 → 房主：本机的观者实现身份（`boninall` / `lamali` / 空 = 没装）。</summary>
    public string Caps = string.Empty;

    public bool ShouldBroadcast => true;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteString(Payload ?? string.Empty);
        writer.WriteString(Settings ?? string.Empty);
        writer.WriteString(Bosses ?? string.Empty);
        writer.WriteString(Caps ?? string.Empty);
    }

    public void Deserialize(PacketReader reader)
    {
        Payload = reader.ReadString() ?? string.Empty;
        Settings = reader.ReadString() ?? string.Empty;
        Bosses = reader.ReadString() ?? string.Empty;
        Caps = reader.ReadString() ?? string.Empty;
    }

    public void HandleMessage(ulong senderId)
    {
        try { BossPicksSync.Apply(Payload, Settings, Bosses, Caps, senderId); }
        catch (Exception e) { Logger.Log("[CharBoss] 勾选同步：处理消息失败（忽略）: " + e.Message); }
    }
}

/// <summary>
/// 「以房主为准」的同步：勾选 / 玩法设置 / **每个 (幕, 场) 的最终遭遇** / 观者能力结论。
/// </summary>
internal static class BossPicksSync
{
    /// <summary>已收到的房主勾选：地图 id → Key 列表。空字典 + <see cref="Received"/> 表示「房主全选」。</summary>
    private static readonly Dictionary<string, string[]> FromHost = new(StringComparer.Ordinal);

    /// <summary>是否已经收到过房主的 pickset（含「全选」这种空集）。</summary>
    internal static bool Received { get; private set; }

    /// <summary>上一次广播出去的载荷（一样就不再重发）。</summary>
    private static string _lastSent = string.Empty;

    // ── v0.2.586：房主的玩法设置（客人只读，null = 还没收到）───────────────
    /// <summary>房主的 A4「与原本 Boss 共用匹配池」。</summary>
    internal static bool? HostSharePool { get; private set; }

    /// <summary>
    /// **房主那一场用了几个人**（v0.2.795）。阵容（Boss 数 = 玩家数）是按这个数确定性推导的 ——
    /// 玩家掉线/重连时两端 `RunState.Players.Count` 会短暂不一致（本机日志实证：
    /// 第 3 层第 2 场 房主 `玩家=2 阵容=[FOOL_TAROT,REGENT_CHOICE]`，客人 `玩家=3
    /// 阵容=[SILENT_SHIV,IRONCLAD_BOULDER,REGENT_STAR]`）⇒ 整场 Boss 都不一样。
    /// 现在客人端**只认房主广播的这个数**，与「勾选/玩法设置以房主为准」同一原则。
    /// </summary>
    internal static int? HostPlayersPerFight { get; private set; }

    /// <summary>每个对端最后说话的时刻（活着吗）。v0.2.797。</summary>
    private static readonly Dictionary<ulong, DateTime> _lastHeard = new();

    /// <summary>记一笔「这个对端刚说过话」（任何一条 BossPicksMessage 都算）。</summary>
    internal static void NoteHeard(ulong senderId)
    {
        try { if (senderId != 0) _lastHeard[senderId] = DateTime.UtcNow; } catch { /* 忽略 */ }
    }

    /// <summary>
    /// **真正还在场的大厅玩家**（NetId 集合）。拿不到返回 null。
    ///
    /// ★★ v0.2.804（用户 2026-11-19「3 玩家 2 boss」+ `mod_log (42)/(43)`）：
    ///   `RunManager.RunLobby.Players` 是游戏**自己维护**的大厅玩家列表 —— 有人离开 / 重连它都会更新
    ///   （`RunLobby.HandlePlayerLeftMessage` / `PlayerRejoined`），比我们猜心跳可靠得多。
    /// </summary>
    private static HashSet<ulong>? TryLobbyPlayerIds()
    {
        try
        {
            var lobby = MegaCrit.Sts2.Core.Runs.RunManager.Instance?.RunLobby;
            var list = lobby?.Players;
            if (list == null || list.Count == 0) return null;
            var ids = new HashSet<ulong>();
            foreach (var entry in list)
            {
                try { if (entry.id != 0) ids.Add(entry.id); } catch { /* 忽略 */ }
            }
            return ids.Count > 0 ? ids : null;
        }
        catch { return null; }
    }

    // ─────────────────────────────────────────────────────────────────────
    // ★★ v0.2.816（用户 2026-11-19「有玩家联机不同步了」，`mod_log (44)` 最后一局 v0.2.804）：
    //
    //   `RunLobby.Players` **会来回抖**。日志实证同一局里跳了五次：
    //     `[16:35:18] 阵容人数：RunState 有 2 人，但大厅里只剩 1 人 ⇒ 按 1 只 Boss`
    //     `[16:35:18] 阵容人数变成 1 人 ⇒ 房主立即重播…（含 pc=1）`
    //     `[16:35:31] 阵容人数变成 2 人 ⇒ …（含 pc=2）`
    //     `[16:37:40] 变成 1 / [16:38:05] 变成 2 / [16:39:59] 变成 1`
    //   而 `RunState.Players` 一直就是 2 人（那 1 人从没真的离开）。
    //   直接把这个数当权威 ⇒ `PlayersPerFight()` 跟着抖 ⇒ 房主反复重播 `pc=1/pc=2`、
    //   客人反复重排阵容 ⇒ **战斗中两端阵容条数对不上**：
    //     `场上我方 Boss=2 只` 紧跟着 `阵容人数变成 1 人`，下一次快照就报不同步。
    //
    //   两道闸门，只加在这里（`PlayersPerFight()` 和重播循环都会经过它）：
    //     ① **战斗中一律锁定人数**：这一场的阵容早造好了，中途改人数只可能两端分叉；
    //        真要有人退了，下 **一场** 再按新人数排（那一场还没开始，改了不会分叉）。
    //     ② **去抖**：新数值要连续稳定 ≥ {@link LiveStableSeconds} 秒才认，读取瞬间拿到了
    //        不完整的大厅列表不会被当真。
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>已经确认过的稳定人数（-1 = 还没算过）。</summary>
    private static int _stableLive = -1;

    /// <summary>正在观察的候选人数。</summary>
    private static int _pendingLive = -1;

    /// <summary>候选人数从什么时候开始保持不变的。</summary>
    private static DateTime _pendingSince = DateTime.MinValue;

    /// <summary>候选人数要连续稳定多少秒才认。</summary>
    private const double LiveStableSeconds = 5.0;

    /// <summary>「新的分数才认得」这条闸门（见上面那段注释）。</summary>
    private static int GateLive(int raw)
    {
        try
        {
            if (raw <= 0) return _stableLive > 0 ? _stableLive : raw;

            // 第一次拿到数 ⇒ 直接认（不拖延开局）
            if (_stableLive <= 0)
            {
                _stableLive = raw;
                _pendingLive = raw;
                _pendingSince = DateTime.UtcNow;
                return raw;
            }

            if (raw == _stableLive)
            {
                _pendingLive = raw;
                _pendingSince = DateTime.UtcNow;
                return _stableLive;
            }

            // 和稳定值不一样 ⇒ 先记候选，等它稳够 5 秒
            if (raw != _pendingLive)
            {
                _pendingLive = raw;
                _pendingSince = DateTime.UtcNow;
                return _stableLive;
            }
            if ((DateTime.UtcNow - _pendingSince).TotalSeconds < LiveStableSeconds) return _stableLive;

            // ★ 战斗中不改（阵容已经造好，改人数只会两端分叉）
            bool inCombat = false;
            try { inCombat = MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.IsInProgress ?? false; }
            catch { inCombat = false; }
            if (inCombat) return _stableLive;

            int before = _stableLive;
            _stableLive = raw;
            Logger.Log($"[CharBoss] 阵容人数：{raw} 人已连续稳定 {LiveStableSeconds:0} 秒 ⇒ 确认为 {raw} 人" +
                       $"（此前 {before} 人；战斗中锁定的那几次不算）");
            return _stableLive;
        }
        catch { return raw; }
    }

    /// <summary>
    /// 对外入口：原始判定 + 上面那两道闸门（**战斗中锁定 / 稳定 5 秒才认**）。
    /// </summary>
    internal static int LivePlayerCount(int fallback) => GateLive(LivePlayerCountRaw(fallback));

    /// <summary>
    /// **真正还在场的玩家数**（v0.2.797，用户 2026-11-19：中途有人离开游戏后，Boss 房变成 4v4，
    /// 多出来那只面对一个没人操控的角色 ⇒ 剩下的人结束不了回合）。
    ///
    /// ★★ v0.2.804（用户 2026-11-19「3 玩家却只有 2 只 Boss」，`mod_log (43)` 房主侧）：
    ///   以前这里靠「最近 N 秒说过话」猜活人，实测**会把活人判死**（日志同一局里来回跳）：
    ///     `[23:42:23] 阵容人数：RunState 有 3 人，其中 1 人已经 30 秒没说话 ⇒ 这一场按 2 只 Boss 出场`
    ///     `[23:42:41] 阵容人数：RunState 有 3 人，其中 0 人已经 30 秒没说话 ⇒ 这一场按 3 只 Boss 出场`
    ///   而那位「30 秒没说话」的玩家 23:44 才因为别的原因掉线，当时明明在打。
    ///   人数一塌 ⇒ 阵容条数一塌 ⇒ 3 人局场上只剩 2 只 Boss。
    ///
    ///   现在**优先用权威数据**：大厅自己的玩家列表（`RunManager.RunLobby.Players`）。
    ///   · 拿到大厅列表 ⇒ 按它数（本机自己恒算 1）；
    ///   · 拿不到（单人 / 还没建大厅 / 反射失败）⇒ 退回心跳判活，阈值放宽到 **120 秒**
    ///     （以前 30 秒太短：客人每 5 秒才发一次心跳，中间断一会儿就误判）。
    /// </summary>
    private static int LivePlayerCountRaw(int fallback)
    {
        try
        {
            var run = MegaCrit.Sts2.Core.Runs.RunManager.Instance?.DebugOnlyGetState();
            var players = run?.Players;
            if (players == null || players.Count == 0) return fallback;

            MegaCrit.Sts2.Core.Entities.Players.Player? me = null;
            try { me = MegaCrit.Sts2.Core.Context.LocalContext.GetMe((MegaCrit.Sts2.Core.Runs.IPlayerCollection?)run); } catch { me = null; }

            // ── ① 权威：大厅玩家列表 ────────────────────────────────────────────
            var lobbyIds = TryLobbyPlayerIds();
            if (lobbyIds != null)
            {
                int live = 0, gone = 0;
                foreach (var p in players)
                {
                    if (p == null) continue;

                    bool self = false;
                    try { self = me != null && ReferenceEquals(p, me); } catch { self = false; }
                    if (self) { live++; continue; }

                    ulong id = 0;
                    try { id = p.NetId; } catch { id = 0; }
                    if (id == 0) { live++; continue; }        // 拿不到 NetId ⇒ 不敢剔除
                    if (lobbyIds.Contains(id)) { live++; continue; }
                    gone++;
                }

                if (live > 0)
                {
                    if (gone > 0 && live != _lastLive && _logSparse++ < 30)
                    {
                        _lastLive = live;
                        Logger.Log($"[CharBoss] 阵容人数：RunState 有 {players.Count} 人，但大厅里只剩 {live} 人" +
                                   $"（{gone} 人已不在大厅）⇒ 这一场按 {live} 只 Boss 出场" +
                                   "（判据 = RunManager.RunLobby.Players，不再靠心跳猜）");
                    }
                    return live;
                }
            }

            // ── ② 兜底：心跳判活（阈值 120 秒）────────────────────────────────────
            if (_lastHeard.Count == 0) return fallback;      // 还什么消息都没收到 ⇒ 不猜

            const double SilentSeconds = 120;
            var now = DateTime.UtcNow;
            int liveH = 0;
            int silent = 0;
            foreach (var p in players)
            {
                if (p == null) continue;

                bool self = false;
                try { self = me != null && ReferenceEquals(p, me); } catch { self = false; }
                if (self) { liveH++; continue; }

                ulong id = 0;
                try { id = p.NetId; } catch { id = 0; }
                if (id == 0) { liveH++; continue; }

                if (!_lastHeard.TryGetValue(id, out var when)) { liveH++; continue; }   // 从没听过 ⇒ 按活着算
                if ((now - when).TotalSeconds <= SilentSeconds) { liveH++; continue; }
                silent++;
            }

            if (liveH <= 0) return fallback;
            if (liveH != _lastLive && _logSparse++ < 30)
            {
                _lastLive = liveH;
                Logger.Log($"[CharBoss] 阵容人数：RunState 有 {players.Count} 人，其中 {silent} 人已经 " +
                           $"{(int)SilentSeconds} 秒没说话（其余已离开 / 掉线）⇒ 这一场按 {liveH} 只 Boss 出场" +
                           "（拿不到大厅玩家列表，退回心跳判活）");
            }
            return liveH;
        }
        catch { return fallback; }
    }

    private static int _logSparse;
    private static int _lastLive = -1;

    /// <summary>房主的 A4 子项「进阶 10 第三幕一场原本一场角色」。</summary>
    internal static bool? HostAsc10Act3OneEach { get; private set; }

    /// <summary>房主的「每张地图替不替换」（只记显式设过的；不在表里 = 按原版默认）。</summary>
    private static readonly Dictionary<string, bool> HostMaps = new(StringComparer.Ordinal);

    /// <summary>客人端：房主对某张地图的替换开关（null = 房主没显式设过 / 还没收到）。</summary>
    internal static bool? HostMapEnabled(string? mapEntry)
    {
        try
        {
            if (string.IsNullOrEmpty(mapEntry)) return null;
            return HostMaps.TryGetValue(mapEntry!, out bool on) ? on : (bool?)null;
        }
        catch { return null; }
    }

    /// <summary>房主设置是否已经收到（客人端用来决定「按房主算」还是「按默认算」）。</summary>
    internal static bool HostSettingsReceived { get; private set; }

    private static bool _appliedLogged;

    // ── v0.2.711：每个 (幕, 场) 的最终遭遇 ──────────────────────────────
    /// <summary>`VANILLA` = 这一场房主决定保持原版 Boss（不替换）。</summary>
    internal const string VanillaMark = "VANILLA";

    /// <summary>房主：本机算出来的每个 (幕, 场) 结论（广播用）。</summary>
    private static readonly Dictionary<(int Act, int Fight), string> HostResolved = new();

    /// <summary>客人：从房主消息里收到的每个 (幕, 场) 结论。</summary>
    private static readonly Dictionary<(int Act, int Fight), string> FromHostBosses = new();

    /// <summary>
    /// 客人端：房主对某个 (幕, 场) 的**主 Boss** 结论（null = 还没收到 / 房主还没算）。
    /// ★ v0.2.802：广播值现在可能是「主 Boss,伙伴1,伙伴2…」整条阵容 ⇒ 这里只取逗号前那一段
    ///   （`DreadDominionSync` / 遭遇槽位等老调用方要的是**单个 Key**）。
    /// </summary>
    internal static string? HostBossOf(int act, int fight)
    {
        try
        {
            if (!FromHostBosses.TryGetValue((act, fight), out var v) || string.IsNullOrEmpty(v)) return null;
            int comma = v.IndexOf(',');
            return comma > 0 ? v.Substring(0, comma) : v;
        }
        catch { return null; }
    }

    /// <summary>
    /// 客人端：房主这一场的**整条阵容**（主 Boss 在 `[0]`，其余是伙伴）。
    /// null = 还没收到 / 房主决定保持原版 / 里面有认不出的 Key（不敢照抄）。
    ///
    /// ★★ v0.2.802（用户 2026-11-19「这怎么就一个boss了」，`mod_log (41)`）：
    ///   以前房主只广播**主 Boss**，客人得按**自己**的玩家数重算阵容 —— 而客人那一刻读到的玩家数
    ///   可能是**过时的 1**（`HostPlayersPerFight` 来自房主某次早先的广播）：
    ///   日志 23:19:44 `※阵容输入※ … 玩家=1`、阵容 `[储君 3 号]`，23:21 结算 `REGENT_BOSS 为主，共 1 只 Boss`，
    ///   而同一张地图在 23:21:47 重算时是 `3 人局` 3 条 —— 3 人局场上只有 1 只 Boss。
    ///   现在房主把整条阵容一起广播 ⇒ 客人直接照抄，条数不可能再和房主不一致。
    /// </summary>
    internal static string[]? HostLineupOf(int act, int fight)
    {
        try
        {
            if (!FromHostBosses.TryGetValue((act, fight), out var v) || string.IsNullOrEmpty(v)) return null;
            if (v == VanillaMark) return null;                     // 房主决定这一场保持原版 Boss
            var parts = v.Split(',');
            var keys = new System.Collections.Generic.List<string>(parts.Length);
            foreach (var p in parts)
            {
                string k = p.Trim();
                if (k.Length == 0) continue;
                if (k == VanillaMark) return null;
                if (Core.BossKeys.MonsterTypeOf(k) == null) return null;   // 认不出的 Key ⇒ 不敢照抄
                keys.Add(k);
            }
            return keys.Count > 0 ? keys.ToArray() : null;
        }
        catch { return null; }
    }

    /// <summary>房主端：本机算出的每个 (幕, 场) 的整条阵容（随「每场最终遭遇」一起广播）。</summary>
    private static readonly Dictionary<(int Act, int Fight), string> HostLineups = new();

    /// <summary>
    /// 房主端：`BossLineups.For` 算完一条阵容就记下来。
    /// ⚠ 这里**只记不发**（把 `_lastSent` 清空即可，真正的发送交给下一次 `BroadcastIfHost`）——
    ///   否则会在 `BossLineups.For` 的锁里递归广播。
    /// </summary>
    internal static void NoteHostLineup(int act, int fight, string[]? lineup)
    {
        try
        {
            if (!NetRole.IsHostOrSolo) return;
            if (lineup == null || lineup.Length == 0) return;
            string value = string.Join(",", lineup);
            var slot = (act, Math.Clamp(fight, 0, 1));
            if (HostLineups.TryGetValue(slot, out var old) && old == value) return;
            HostLineups[slot] = value;
            _lastSent = string.Empty;                  // 让下一次 BroadcastIfHost 真的发出去
            Logger.Log($"[CharBoss] 勾选同步：房主记下第 {act + 1} 层第 {fight + 1} 场的整条阵容 = [{value}]" +
                       "（客人直接照抄，不再自己按人数重算）");
        }
        catch { /* 忽略 */ }
    }

    /// <summary>
    /// 客人端：房主对某个 (幕, 场) 的结论**是不是「保持原版」**。
    /// `null` = 这一场房主还没算出来（**不要**当成「保持原版」）。
    ///
    /// ★ v0.2.780：`Core.DreadDominionSync` 用它决定「本幕支配」用哪只 Boss 解析 ——
    ///   以前客人是拿**本机槽位**解析的，而本机槽位这时往往还是原版 Boss ⇒
    ///   拿到的是**原版 Boss 的支配**，与房主（按我们替换后的 Boss 解析）不同 ⇒ 整幕普通战斗不同步。
    /// </summary>
    internal static bool? HostSaysVanilla(int act, int fight)
    {
        try
        {
            var v = HostBossOf(act, fight);
            if (v == null) return null;
            return v == VanillaMark;
        }
        catch { return null; }
    }

    /// <summary>
    /// 房主端：记下「本幕这一场最终是哪一只」（由 `CharBossEncounters.ForVanillaBoss` 每次算完调）。
    /// `resolved == null` 记成 <see cref="VanillaMark"/>（= 这一场保持原版）。
    /// 结论变了就重播一次给客人。
    /// </summary>
    internal static void NoteHostBoss(MegaCrit.Sts2.Core.Models.ActModel? act, int fight,
                                      MegaCrit.Sts2.Core.Models.EncounterModel? resolved)
    {
        try
        {
            if (!NetRole.IsHostOrSolo) return;
            if (act == null) return;
            int actIndex = act.Index;
            if (actIndex < 0) return;

            string value;
            if (resolved == null) value = VanillaMark;
            else
            {
                string? key = BossKeys.KeyOfEncounter(resolved);
                // 不是我们的遭遇（真·原版）⇒ 和「决定不替换」同义
                if (string.IsNullOrEmpty(key)) return;
                value = key!;
            }

            var slot = (actIndex, Math.Clamp(fight, 0, 1));
            if (HostResolved.TryGetValue(slot, out var old) && old == value) return;
            HostResolved[slot] = value;
            Logger.Log($"[CharBoss] 勾选同步：房主记下第 {actIndex + 1} 层第 {slot.Item2 + 1} 场的最终遭遇 = " +
                       $"{(value == VanillaMark ? "保持原版" : BossKeys.LabelOf(value))} ⇒ 准备广播给客人");
            BroadcastIfHost();
        }
        catch (Exception e) { Logger.Log("[CharBoss] 勾选同步：记录房主结论失败（忽略）: " + e.Message); }
    }

    private static string BuildBosses()
    {
        try
        {
            if (HostResolved.Count == 0) return string.Empty;
            var sb = new StringBuilder();
            foreach (var kv in HostResolved)
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(kv.Key.Act).Append(':').Append(kv.Key.Fight).Append('|');
                // ★ v0.2.802：「保持原版」照旧；换了角色 Boss 的话，若整条阵容已知就一条不落地发出去
                //   （`主Boss,伙伴1,伙伴2…`）。老调用方读 `HostBossOf`（取逗号前那段）→ 完全兼容。
                if (kv.Value == VanillaMark) sb.Append(VanillaMark);
                else if (HostLineups.TryGetValue(kv.Key, out var lineup) && !string.IsNullOrEmpty(lineup))
                    sb.Append(lineup);
                else sb.Append(kv.Value);
            }
            return sb.ToString();
        }
        catch { return string.Empty; }
    }

    // ── v0.2.711：观者能不能出现（全端同一实现才允许）─────────────────────
    /// <summary>房主：各客人上报的观者身份（NetId → `boninall`/`lamali`/空）。</summary>
    private static readonly Dictionary<ulong, string> PeerCaps = new();

    /// <summary>房主算出来的「观者可以出现吗」。</summary>
    private static bool _watcherOk;

    /// <summary>房主是否已经算过一次结论。</summary>
    private static bool _watcherDecided;

    /// <summary>客人：房主广播下来的「观者可以出现吗」（null = 还没收到）。</summary>
    private static bool? HostWatcherOk { get; set; }

    /// <summary>
    /// **本局能不能把观者放进三层候补表**（用户 2026-11-16：
    /// 「只有当所有玩家都装了相同的观者 mod（指统一为 A 或 B 版）时才让观者有可能作为 boss 出现」）。
    ///
    /// · 单人 / 房主：本机装了观者，并且**已经收到「玩家数 − 1」份客人上报**、且每一份的身份串都跟本机一致；
    /// · 客人：用房主广播下来的结论（还没收到 ⇒ **保守地不放观者**）。
    ///
    /// 为什么必须这样做：三层候补表的**条数**会随观者进出一变，而阵容是
    /// `(种子, 幕, 场, 候补表)` 的纯函数 ⇒ 一端有观者一端没有，第三幕两场的阵容都可能不同（不同步）。
    /// 而且客人那边如果解不出观者，`BossKeys.SubstituteIfUnavailable` 只会换成塔罗愚，
    /// 两端仍然打不同的 Boss。
    /// </summary>
    internal static bool WatcherAllowedHere()
    {
#if STS2_111
        try
        {
            // ⚠ 房主**每次都重算**（不是「算一次就锁」）：开局早期 `RunState` 可能还读不到，
            //   那时会算出「单人 ⇒ 可以放观者」；等人数真的读到 2、或客人上报到齐之后必须纠正。
            //   重算很便宜（几个字典查表），而且结论没变时立刻 return，不会反复刷日志/清缓存。
            if (NetRole.IsHostOrSolo) return RecomputeWatcherAllowed("询问");
            return HostWatcherOk == true;
        }
        catch { return false; }
#else
        return false;
#endif
    }

    /// <summary>房主：重算「观者可以出现吗」，结论变了就清阵容缓存并重播。返回新结论。</summary>
    private static bool RecomputeWatcherAllowed(string why)
    {
#if STS2_111
        try
        {
            if (!NetRole.IsHostOrSolo) return false;
            string mine = WatcherCards.ProviderId;
            int expected = Math.Max(0, BossLineups.PlayerCount() - 1);

            bool ok;
            if (expected == 0) ok = true;                       // 单人：只看本机
            else if (string.IsNullOrEmpty(mine)) ok = false;    // 房主自己没装 ⇒ 不放
            else if (PeerCaps.Count < expected) ok = false;      // 还有客人没上报 ⇒ 先不放（保守）
            else
            {
                ok = true;
                foreach (var kv in PeerCaps)
                {
                    if (!string.Equals(kv.Value, mine, StringComparison.Ordinal)) { ok = false; break; }
                }
            }

            if (_watcherDecided && ok == _watcherOk) return _watcherOk;
            _watcherOk = ok;
            _watcherDecided = true;
            Logger.Log($"[CharBoss] 观者 Boss：房主判定「可以出现」={ok}（{why}）—— 本机实现={(string.IsNullOrEmpty(mine) ? "未装" : mine)}、" +
                       $"期待客人上报 {expected} 份、已收到 {PeerCaps.Count} 份" +
                       (ok ? "；全端一致 ⇒ 观者进三层候补表" : "；不全一致 ⇒ 观者**不进**三层候补表（避免两端阵容不同步）"));
            BossLineups.InvalidateCache();
            BroadcastIfHost();
            return _watcherOk;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 观者 Boss：判定失败（按不可以出现处理）: " + e.Message);
            return false;
        }
#else
        return false;
#endif
    }

    /// <summary>
    /// **换局时把同步状态清干净**（由 `BossLineups` 在种子变化时调）。
    /// 不清的话：上一局收到的客人能力位 / 每场结论会漏到新一局，
    /// 出现「新一局一开始就判定观者可以出现」「客人拿上一局的 Boss」这类幽灵。
    /// </summary>
    internal static void ResetForNewRun()
    {
        try
        {
            PeerCaps.Clear();
            FromHostBosses.Clear();
            HostResolved.Clear();
            FromHost.Clear();
            HostMaps.Clear();
            Received = false;
            HostSettingsReceived = false;
            HostSharePool = null;
            HostAsc10Act3OneEach = null;
            HostWatcherOk = null;
            _watcherDecided = false;
            _watcherOk = false;
            _lastSent = string.Empty;
            _lastCapsSent = string.Empty;
            _appliedLogged = false;
            // ★ v0.2.816：新的一局 ⇒ 「稳定人数」重新算（否则会拿上一局的数去锁这一局的阵容）
            _stableLive = -1;
            _pendingLive = -1;
            _pendingSince = DateTime.MinValue;
        }
        catch { /* 忽略 */ }
    }

    /// <summary>客人端：房主广播下来的观者结论（解析 `Settings` 里的 `wok=`）。</summary>
    private static void ApplyHostWatcherOk(string settings)
    {
        foreach (var part in (settings ?? string.Empty).Split(';'))
        {
            if (!part.StartsWith("wok=", StringComparison.Ordinal)) continue;
            bool val = part.Substring(4).Trim() == "1";
            if (HostWatcherOk == val) return;
            HostWatcherOk = val;
            Logger.Log($"[CharBoss] 观者 Boss：房主判定「可以出现」={val} ⇒ 本机三层候补表" +
                       (val ? "加入观者" : "**排除**观者") + "（阵容缓存已清，下次抽签按新候补表重算）");
            try { BossLineups.InvalidateCache(); } catch { /* 忽略 */ }
            return;
        }
    }

    // ── 收发 ───────────────────────────────────────────────────────────
    /// <summary>客人端：房主这套勾选里，这张地图有哪些 Boss（null = 还没收到 / 全选）。</summary>
    internal static string[]? HostPicksOf(string? mapEntry)
    {
        try
        {
            if (!Received || string.IsNullOrEmpty(mapEntry)) return null;
            return FromHost.TryGetValue(mapEntry!, out var keys) && keys != null && keys.Length > 0
                ? keys
                : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// 房主端：把「勾选 + 玩法设置 + 每个 (幕, 场) 的最终遭遇 + 观者结论」广播出去（内容没变就不重发）。
    /// 单人不发；客人端在这里改成「上报自己的观者身份」（内容没变就不重发）。
    /// 由 `BossLineups.For` 在每次算阵容时顺手调一次 —— 那时必然已经有 RunManager/NetService。
    /// </summary>
    internal static void BroadcastIfHost()
    {
        // ★ v0.2.587：**先确认 BaseLib 在不在，再决定要不要碰它的类型**。
        //   `BossPicksMessage` 实现了 BaseLib 的 `ICustomMessage`，所以「碰它」的那段必须放在
        //   标了 NoInlining 的独立方法里 —— 否则 JIT 编译本方法时就会因缺类型抛 TypeLoadException，
        //   这一行判断根本执行不到（就是玩家「没装 BaseLib 进战斗就崩 / 主界面按钮消失」的根因）。
        if (!BaseLibPresence.Available)
        {
            if (_missingLogged) return;
            _missingLogged = true;
            Logger.Log("[CharBoss] 勾选同步：本机没有 BaseLib ⇒ 关闭「房主勾选/玩法设置/最终遭遇广播」" +
                       "（多人时退回整张候补表 + 确定性规则，其它照常）");
            return;
        }
        BroadcastCore();
    }

    private static bool _missingLogged;

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void BroadcastCore()
    {
        try
        {
            var net = MegaCrit.Sts2.Core.Runs.RunManager.Instance?.NetService;
            if (net == null) return;

            var role = NetRole.Type;
            if (role == MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Client)
            {
                SendCaps(net);          // 客人：只上报自己的观者身份
                return;
            }
            if (role != MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Host) return;

            string payload = BuildPayload();
            string settings = BuildSettings();
            string bosses = BuildBosses();
            string combined = payload + "#" + settings + "#" + bosses;
            if (string.Equals(combined, _lastSent, StringComparison.Ordinal)) return;

            // ★ 2026-11-15：先确认 BaseLib 已经认识 BossPicksMessage（否则它会抛 KeyNotFound ⇒ 广播失败）
            try { BaseLibMessageRegistration.TryFix(); } catch { /* 忽略 */ }
            CustomMessageWrapper.Send(new BossPicksMessage
            {
                Payload = payload,
                Settings = settings,
                Bosses = bosses,
                Caps = "",
            }, net);
            _lastSent = combined;
            Logger.Log($"[CharBoss] 勾选同步：房主已广播（勾选={payload}；设置={settings}；" +
                       $"每场最终遭遇={bosses}）⇒ 客人的阵容候选 / 共用匹配池 / 地图开关 / **每场实际 Boss** 都按房主来");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 勾选同步：广播失败（客人将退回整张候补表）: " + e.Message);
        }
    }

    /// <summary>
    /// **心跳 / 重发**（v0.2.747，用户 2026-11-17）。由帧驱动每秒调一次（很便宜）：
    /// · 客人：还没收到房主结论 ⇒ 每 3 秒带 `hello` 重发一次上报，房主收到就重播；
    /// · 房主：多人局里还没收齐客人上报 ⇒ 每 5 秒强制重播一次（晚进的客人也能拿到）。
    /// 目的：把「第一幕必须重开房间才同步」这种**开局丢包一次就永久错**的情况收紧成「几秒内自愈」。
    /// </summary>
    internal static void Tick()
    {
        try
        {
            // ★ v0.2.780：恐惧模式下「本幕支配」要对齐到房主结论 —— 客人收到结论可能晚于进幕，
            //   这里每秒顺带重试一次（`EnsureCurrentAct` 自己幂等、只在真的不一致时才动手）。
#if STS2_113
            try { Core.DreadDominionSync.EnsureCurrentAct("心跳重试"); } catch { /* 忽略 */ }
#endif
            if (!BaseLibPresence.Available) return;
            long now = Environment.TickCount64;
            var role = NetRole.Type;
            if (role == MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Client)
            {
                if (!Received)
                {
                    if (now - _lastAskAt < 3000) return;
                    _lastAskAt = now;
                    _lastCapsSent = string.Empty;           // 允许重发
                    _forceHello = true;
                    BroadcastIfHost();                      // 客人这条路上会走 SendCaps
                    _forceHello = false;
                    return;
                }

                // ★★ v0.2.800（用户 2026-11-19 的两份 `mod_log (38)/(39)`）：**收到结论之后也要继续说话**。
                //   以前这里一句 `if (Received) return;` ⇒ 客人拿到房主结论就彻底闭嘴 ⇒ 房主的
                //   「最近 30 秒说过话」判活（`LivePlayerCount`）把还在玩的客人判成「已离开」
                //   ⇒ 房主 22:36:25 数出 2 人、22:36:55 数出 1 人（3 人局）⇒ 阵容条数跟着塌 ⇒ 两端 Boss 对不上。
                //   现在每 5 秒发一次**无副作用的心跳**（内容照旧是本机观者实现，`hello` 不带⇒房主不会重播）。
                if (now - _lastHeartbeatAt < 5000) return;
                _lastHeartbeatAt = now;
                _lastCapsSent = string.Empty;               // 绕过「内容没变就不重发」
                _heartbeatQuiet = true;                     // 心跳不刷日志（每 5 秒一条会淹掉真日志）
                try { BroadcastIfHost(); } finally { _heartbeatQuiet = false; }
            }
            else if (role == MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Host)
            {
                // ★★ v0.2.800：房主这边「本场几个人」一旦变了（有人离开 / 回来）⇒ 立刻把结论重播一次，
                //   客人拿到的 `pc=` 才不会停在旧值（阵容条数 = 这个数，差一条就是两只 Boss 对不上）。
                try
                {
                    int pcNow = BossLineups.PlayersPerFight();
                    if (pcNow != _lastBroadcastPc)
                    {
                        bool first = _lastBroadcastPc < 0;
                        _lastBroadcastPc = pcNow;
                        _lastSent = string.Empty;
                        if (!first)
                            Logger.Log($"[CharBoss] 阵容人数变成 {pcNow} 人 ⇒ 房主立即重播勾选 / 玩法设置（含 pc={pcNow}）" +
                                       "，客人按同一个数排阵容");
                    }
                }
                catch { /* 忽略 */ }

                // ★★ v0.2.802：**每 5 秒无条件重播一次**（这条消息很小）。以前只在「有客人上报没到齐 /
                //   去重标记空了」时才重播 ⇒ 客人只要**一次**没收到、或收到的是**早先的过时内容**
                //   （例如玩家数还是开局那会儿的 1），它就会一直按那个数排阵容 ——
                //   `mod_log (41)` 23:19:44 `玩家=1` 排出的 1 条阵容，就是 3 人局只出 1 只 Boss 的来源。
                if (now - _lastHostRefreshAt >= 5000)
                {
                    _lastHostRefreshAt = now;
                    _lastSent = string.Empty;
                    BroadcastIfHost();
                    return;
                }

                int expected = Math.Max(0, BossLineups.PlayerCount() - 1);
                if (expected <= 0) return;
                // 两种「还没发到位」的状态都要重播：
                //   ① 客人上报没到齐（说明还有端没连上 / 没收到）；
                //   ② 去重标记是空的（有新客人刚上报、或上次广播失败 ⇒ 内容等着发出去）。
                bool pending = PeerCaps.Count < expected || string.IsNullOrEmpty(_lastSent);
                if (!pending) return;
                if (now - _lastHostResendAt < 5000) return;
                _lastHostResendAt = now;
                _lastSent = string.Empty;
                BroadcastIfHost();
            }
        }
        catch { /* 绝不影响战斗 */ }
    }

    private static long _lastAskAt;
    private static long _lastHostResendAt;
    private static long _lastHeartbeatAt;
    private static long _lastHostRefreshAt;
    private static int _lastBroadcastPc = -1;
    private static bool _heartbeatQuiet;
    private static bool _forceHello;
    /// <summary>客人 → 房主：上报本机观者身份（内容没变就不重发）。</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void SendCaps(MegaCrit.Sts2.Core.Multiplayer.Game.INetGameService net)
    {
        try
        {
            string caps;
#if STS2_111
            caps = WatcherCards.ProviderId;
#else
            caps = "";
#endif
            caps = "w=" + caps;                 // 前缀让它以后好扩展（w= 观者实现）
            if (_forceHello) caps += ";hello";  // v0.2.747：客人请求房主重播结论
            if (string.Equals(caps, _lastCapsSent, StringComparison.Ordinal)) return;
            try { BaseLibMessageRegistration.TryFix(); } catch { /* 忽略 */ }
            CustomMessageWrapper.Send(new BossPicksMessage
            {
                Payload = "",
                Settings = "",
                Bosses = "",
                Caps = caps,
            }, net);
            _lastCapsSent = caps;
            if (!_heartbeatQuiet)
                Logger.Log($"[CharBoss] 观者 Boss：客人已上报本机观者实现（{caps}）⇒ 房主据此判断观者能否出现");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 观者 Boss：上报实现失败（房主会按「不能出现」处理）: " + e.Message);
        }
    }

    private static string _lastCapsSent = string.Empty;

    /// <summary>把「决定上哪个 Boss」的那几项玩法设置拼成一个字符串（房主端）。</summary>
    private static string BuildSettings()
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append("sp=").Append(BossConfig.SharePool ? '1' : '0');
            sb.Append(";asc10=").Append(BossConfig.Asc10Act3OneEach ? '1' : '0');
            sb.Append(";wok=").Append(WatcherAllowedHere() ? '1' : '0');
            // ★ v0.2.849：把房主用的**阵容随机种子**一起广播（客人据此排阵容，避免两端拿到不同种子）
            try { sb.Append(";seed=").Append(Core.BossLineups.RunSeed); } catch { /* 取不到就不发 */ }
            // ★ v0.2.795：把「本场用几个人」一起广播（阵容条数 = 这个数）
            try { sb.Append(";pc=").Append(Core.BossLineups.PlayersPerFight()); } catch { /* 取不到就不发 */ }
            sb.Append(";maps=");
            bool first = true;
            foreach (var kv in BossConfig.AllMapSwitches())
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append(kv.Key).Append(':').Append(kv.Value ? '1' : '0');
            }
            return sb.ToString();
        }
        catch { return string.Empty; }
    }

    /// <summary>收到一条消息（房主收客人的能力位 / 客人收房主的整包）。</summary>
    internal static void Apply(string payload, string settings, string bosses, string caps, ulong senderId)
    {
        try
        {
            NoteHeard(senderId);        // ★ v0.2.797：记活着的对端（阵容人数要用）
            if (NetRole.Type == MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Client)
            {
                ApplyFromHost(payload, settings, bosses, senderId);
                return;
            }
            if (NetRole.Type == MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Host)
            {
                ApplyPeerCaps(senderId, caps);
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 勾选同步：分流消息失败（忽略）: " + e.Message); }
    }

    /// <summary>房主：记下某个客人上报的观者身份，可能改变「观者能否出现」。</summary>
    private static void ApplyPeerCaps(ulong senderId, string caps)
    {
        try
        {
            string value = caps ?? string.Empty;
            int eq = value.IndexOf('=');
            if (eq >= 0) value = value.Substring(eq + 1);      // 去掉 `w=` 前缀
            bool isNewPeer = !PeerCaps.ContainsKey(senderId);
            if (!isNewPeer && PeersEqual(senderId, value)) return;
            PeerCaps[senderId] = value;

            // ★ v0.2.747（用户 2026-11-17：工坊玩家「第一幕得重开房间才能同步，好像和联机大厅 mod 兼容性不好」）：
            //   那个联机 mod 会自己重建大厅 / 提前拉房间，我们这条 BaseLib 自定义消息有可能**在它连上之前就发过一次**，
            //   而 `_lastSent` / `_lastCapsSent` 的「内容没变就不重发」会把它**永久钉死在丢包状态**。
            //   客人上报里带 `hello` 就是「我没收到结论，请重发」；房主收到就把去重标记清掉、立刻重播一遍。
            try
            {
                if (value != null && value.IndexOf("hello", StringComparison.Ordinal) >= 0)
                {
                    _lastSent = string.Empty;
                    Logger.Log("[CharBoss] 勾选同步：客人请求重发 ⇒ 房主立即重播（勾选/设置/每场最终遭遇）");
                    BroadcastIfHost();
                }
            }
            catch { /* 忽略 */ }
            Logger.Log($"[CharBoss] 观者 Boss：收到客人（NetId={senderId}）的观者实现 = " +
                       $"{(string.IsNullOrEmpty(value) ? "未装" : value)}（共 {PeerCaps.Count} 份上报）");

            // ★ 新客人一上线就**强制重播一次**（清掉去重缓存）：
            //   否则「房主早就发过了、内容一直没变」⇒ 迟到入场的客人永远收不到
            //   「每场最终遭遇」和「观者结论」，只能退回确定性规则（正是会不同步的那条路）。
            if (isNewPeer) _lastSent = string.Empty;

            RecomputeWatcherAllowed(isNewPeer ? "有新客人上报" : "收到客人上报");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 观者 Boss：处理客人上报失败: " + e.Message); }
    }

    private static bool PeersEqual(ulong senderId, string value)
        => PeerCaps.TryGetValue(senderId, out var old) && string.Equals(old, value, StringComparison.Ordinal);

    /// <summary>客人端：收到房主的勾选 + 玩法设置 + 每场最终遭遇 ⇒ 解析、落库、清阵容缓存。</summary>
    internal static void ApplyFromHost(string payload, string settings, string bosses, ulong senderId)
    {
        try
        {
            if (NetRole.Type != MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Client) return;   // 只客人吃这条

            // ★ v0.2.780（玩家 2026-11-18 三机联机日志）：**只认带结论的消息**。
            //   客人给客人发的上报里这三样都是空的，而以前它照样走一遍完整流程 ⇒
            //   `FromHost.Clear()` / `FromHostBosses.Clear()` 把房主刚发来的列表清空，
            //   并把「0 条」当成房主的结论（日志实证：`勾选同步：已收到房主(NetId=76561198830124068)`
            //   —— 那个 NetId 其实是**另一位客人**，房主是 …246781）。
            bool hostish = !string.IsNullOrEmpty(payload) || !string.IsNullOrEmpty(settings)
                           || !string.IsNullOrEmpty(bosses);
            if (!hostish)
            {
                Logger.Log($"[CharBoss] 勾选同步：忽略来自 NetId={senderId} 的空消息" +
                           "（房主结论里勾选/设置/每场遭遇不可能同时为空 ⇒ 这是另一位客人的上报，不能当成房主结论）");
                return;
            }

            // ⚠ 三个字段各自「有内容才覆盖」：房主是**逐步**广播的（每场最终遭遇 0 条 → 1 条 → 2 条 → 3 条），
            //   以前一次空载荷就把已知结论清掉 ⇒ 客人永远解析不到 ⇒ 退回原版 Boss 的支配/阵容。
            if (!string.IsNullOrEmpty(payload)) FromHost.Clear();
            foreach (var entry in (payload ?? string.Empty).Split(';'))
            {
                if (entry.Length == 0) continue;
                int bar = entry.IndexOf('|');
                if (bar <= 0) continue;
                string map = entry.Substring(0, bar);
                string keysPart = entry.Substring(bar + 1);
                var keys = keysPart.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (map.Length > 0 && keys.Length > 0) FromHost[map] = keys;
            }
            Received = true;

            // ── 玩法设置（v0.2.586）──
            if (!string.IsNullOrEmpty(settings))
            {
                HostSharePool = null;
                HostAsc10Act3OneEach = null;
                HostPlayersPerFight = null;
                HostMaps.Clear();
            }
            foreach (var part in (settings ?? string.Empty).Split(';'))
            {
                if (part.Length == 0) continue;
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string name = part.Substring(0, eq).Trim();
                string value = part.Substring(eq + 1).Trim();
                switch (name)
                {
                    case "sp": HostSharePool = value == "1"; break;
                    case "seed":
                        // ★ v0.2.849（玩家反馈 2026-11-21：「联机时只有我看到的是猎手，别人看到的是骨头」
                        //   + 日志里出现过 `阵容随机种子 = 1（**固定值**：读不到本局种子，人数=4）`）：
                        //   **阵容随机种子由房主广播**。以前客人自己读 `RunState.Rng.Seed`，读不到就退到常量 1
                        //   ⇒ 两端按不同种子排阵容 ⇒ 场上的 Boss 都不一祥 ⇒ 立刻不同步。
                        //   现在房主把自己的种子放进来，客人直接照用（只认第一次，之后同值忽略）。
                        if (ulong.TryParse(value, out ulong hostSeed) && hostSeed != 0)
                        {
                            try { Core.BossLineups.AdoptHostSeed(hostSeed); } catch { /* 忽略 */ }
                        }
                        break;
                    case "pc":
                        // ★ v0.2.795：房主的人数（阵容按它推导；玩家掉线/重连时两端可能不一致）
                        if (int.TryParse(value, out int hostPc) && hostPc > 0) HostPlayersPerFight = hostPc;
                        break;
                    case "asc10": HostAsc10Act3OneEach = value == "1"; break;
                    case "maps":
                        foreach (var kv in value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            int colon = kv.LastIndexOf(':');
                            if (colon <= 0) continue;
                            string map = kv.Substring(0, colon).Trim();
                            string flag = kv.Substring(colon + 1).Trim();
                            if (map.Length > 0) HostMaps[map] = flag == "1";
                        }
                        break;
                }
            }
            HostSettingsReceived = true;

            // ── 观者结论（v0.2.711）──
            ApplyHostWatcherOk(settings ?? string.Empty);

            // ── 每个 (幕, 场) 的最终遭遇（v0.2.711）──
            // ⚠ v0.2.780：**合并，不再 Clear**。房主是逐步广播的（0 条 → 1 条 → 2 条 → 3 条），
            //   以前每来一条就先 `Clear()` 再填 ⇒ 只要收到过一条空/不全的，已知结论就被抹掉
            //   （玩家日志：`每场最终遭遇 0 条` 之后支配按**原版 Boss** 解析 ⇒ RITUAL_POWER vs MONARCHS_GAZE_POWER）。
            int before = FromHostBosses.Count;
            foreach (var entry in (bosses ?? string.Empty).Split(';'))
            {
                if (entry.Length == 0) continue;
                int bar = entry.IndexOf('|');
                if (bar <= 0) continue;
                string slot = entry.Substring(0, bar);
                string value = entry.Substring(bar + 1).Trim();
                int colon = slot.IndexOf(':');
                if (colon <= 0) continue;
                if (!int.TryParse(slot.Substring(0, colon), out int act)) continue;
                if (!int.TryParse(slot.Substring(colon + 1), out int fight)) continue;
                if (value.Length > 0) FromHostBosses[(act, fight)] = value;
            }

            // ★★ v0.2.802：房主现在**每 5 秒重播一次**（防止客人拿到过时结论）⇒ 这里不能每收到一条就清阵容缓存，
            //   否则客人整局都在反复重算阵容、日志刷屏。只在「内容真的变了」时才清。
            string bossesRaw = bosses ?? string.Empty;
            bool bossesChanged = !string.Equals(bossesRaw, _lastBossesRaw, StringComparison.Ordinal);
            _lastBossesRaw = bossesRaw;
            if (bossesChanged) BossLineups.InvalidateCache();

            // ★ v0.2.776（玩家 2026-11-19 联机日志）：恐惧模式里「本幕支配」是游戏按
            //   **本幕 Boss 遭遇**算的（`DreadRunModel.ApplyCurrentDominion`），而遭遇是各端本地替换的
            //   ⇒ 房主结论晚到时客人这一幕拿到的是**原版** Boss 的支配，整幕普通战斗里敌人身上
            //   就会多/少一层 power（日志实证：客人那只 SHRINKER_BEETLE 只有 RITUAL，房主那只还有
            //   MONARCHS_GAZE）⇒ 第一个回合开始校验和就分叉。收到结论立刻对齐一次。
#if STS2_113
            try { DreadDominionSync.EnsureCurrentAct("收到房主的每场最终遭遇"); } catch { /* 忽略 */ }
#endif

            if (!_appliedLogged || before != FromHostBosses.Count)
            {
                _appliedLogged = true;
                Logger.Log($"[CharBoss] 勾选同步：已收到房主（NetId={senderId}）的勾选 " +
                           $"（{FromHost.Count} 张地图）、玩法设置" +
                           $"（共用匹配池={(HostSharePool == true ? "开" : "关")}、" +
                           $"进阶10一场原本={(HostAsc10Act3OneEach == true ? "开" : "关")}、" +
                           $"显式地图开关 {HostMaps.Count} 条）、**每场最终遭遇 {FromHostBosses.Count} 条**" +
                           $"（{string.Join("；", Describe(FromHostBosses))}）" +
                           "⇒ 本机阵容候选 / 共用匹配池 / 地图开关 / 每场实际 Boss 都改成房主那一套");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 勾选同步：应用房主勾选失败（继续用整张候补表）: " + e.Message);
        }
    }

    /// <summary>客人端：上一次应用过的「每场最终遭遇」原文（v0.2.802，只在内容变了时才清阵容缓存）。</summary>
    private static string _lastBossesRaw = string.Empty;

    private static IEnumerable<string> Describe(Dictionary<(int Act, int Fight), string> map)
    {
        foreach (var kv in map)
        {
            yield return $"第{kv.Key.Act + 1}层第{kv.Key.Fight + 1}场=" +
                         (kv.Value == VanillaMark ? "保持原版" : kv.Value);
        }
    }

    /// <summary>把本机配置里的所有地图勾选拼成一个字符串（没配的地图不出现 = 全选）。</summary>
    private static string BuildPayload()
    {
        try
        {
            var picks = BossConfig.AllBossPicks();
            if (picks == null || picks.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            foreach (var kv in picks)
            {
                if (string.IsNullOrEmpty(kv.Key) || kv.Value == null || kv.Value.Count == 0) continue;
                if (sb.Length > 0) sb.Append(';');
                sb.Append(kv.Key).Append('|').Append(string.Join(",", kv.Value));
            }
            return sb.ToString();
        }
        catch { return string.Empty; }
    }
}
