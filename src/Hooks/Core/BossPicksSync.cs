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

    /// <summary>客人端：房主对某个 (幕, 场) 的结论（null = 还没收到 / 房主还没算）。</summary>
    internal static string? HostBossOf(int act, int fight)
    {
        try { return FromHostBosses.TryGetValue((act, fight), out var v) ? v : null; }
        catch { return null; }
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
                sb.Append(kv.Key.Act).Append(':').Append(kv.Key.Fight).Append('|').Append(kv.Value);
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
                if (Received) return;
                if (now - _lastAskAt < 3000) return;
                _lastAskAt = now;
                _lastCapsSent = string.Empty;           // 允许重发
                _forceHello = true;
                BroadcastIfHost();                      // 客人这条路上会走 SendCaps
                _forceHello = false;
            }
            else if (role == MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Host)
            {
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

            BossLineups.InvalidateCache();

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
