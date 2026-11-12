using System;
using System.Collections.Generic;
using System.Text;
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace CharacterBosses.Core;

/// <summary>
/// **房主 → 所有客人**：本局「每张地图允许遇到哪些 Boss」的那套勾选（A2）。
///
/// ★ 2026-11-09（用户要求）：「多人时**所有** Boss（主 Boss 与搭子）都只在**房主勾选**的 Boss 里选」。
///   以前做不到，因为 `charbosses_config.cfg` 是**每台机器自己的文件** —— 客人根本读不到房主的勾选，
///   所以 v0.2.503 只好让多人局一律用「整张花名册」⇒ 勾选在多人里等于完全没用。
///   现在用房主广播一次 pickset，两端算阵容时用**同一套**候选：
///     阵容 = f(种子, 幕, 场, 勾选) ⇒ 仍然是确定性纯函数，不会因此不同步。
///
/// 走 BaseLib 的**自定义网络消息**通道（`ICustomMessage` + `CustomMessageWrapper.Send`）：
/// 类名排序分配消息 id（只要两端装同一套模组就一致），不需要我们自己碰底层的包格式。
///
/// ⚠ 时序：客人可能在收到消息**之前**就把三层阵容预告算过了。
///   ⇒ 收到消息时由 <see cref="BossPicksSync.ApplyFromHost"/> 调 `BossLineups.InvalidateCache()`，
///     之后真正的抽签（进 Boss 房那一刻）会用房主的勾选重算；主 Boss 本来就跟**同步遭遇**走，
///     所以最终两端的结果一致。
/// </summary>
public sealed class BossPicksMessage : ICustomMessage, IPacketSerializable
{
    /// <summary>序列化后的勾选：`地图id|Key,Key,Key;地图id|Key,…`（没出现的地图 = 全选）。</summary>
    public string Payload = string.Empty;

    /// <summary>
    /// ★ v0.2.586：**房主的玩法设置**（`sp=1;asc10=0;maps=OVERGROWTH:1,UNDERDOCKS:0`）。
    ///
    /// 为什么必须一起同步（玩家反馈「联机时开和原版 boss 混合匹配会不同步」）：
    ///   客人端 `SharePoolForGameplay` 恒为 false ⇒ 走到「共用匹配池」那段时**整段被跳过**
    ///   ⇒ 房主按种子抽到「这一场保留原版 Boss」时，**客人却照样把它换成了角色 Boss** ⇒ 进 Boss 房立刻分叉。
    ///   同一形状的问题还有「地图替换开关」：房主关掉某张原版图的替换时，客人按默认规则仍然替换。
    ///   所以现在把「决定上哪个 Boss」的那几项玩法设置也随这条消息广播，客人按房主的值算。
    /// </summary>
    public string Settings = string.Empty;

    public bool ShouldBroadcast => true;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteString(Payload ?? string.Empty);
        writer.WriteString(Settings ?? string.Empty);
    }

    public void Deserialize(PacketReader reader)
    {
        Payload = reader.ReadString() ?? string.Empty;
        Settings = reader.ReadString() ?? string.Empty;
    }

    public void HandleMessage(ulong senderId)
    {
        try { BossPicksSync.ApplyFromHost(Payload, Settings, senderId); }
        catch (Exception e) { Logger.Log("[CharBoss] 勾选同步：处理房主消息失败（忽略）: " + e.Message); }
    }
}

/// <summary>本机保存的「房主勾选」（只对客人有意义），以及房主端的广播（见 <see cref="BossPicksMessage"/>）。</summary>
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

    /// <summary>
    /// 客人端：房主这套勾选里，这张地图有哪些 Boss。
    /// 返回 null = **还没收到**或这张地图房主没配（= 全选）⇒ 调用方按整张候补表处理。
    /// </summary>
    internal static string[]? HostPicksOf(string? mapEntry)
    {        try
        {
            if (!Received || string.IsNullOrEmpty(mapEntry)) return null;
            return FromHost.TryGetValue(mapEntry!, out var keys) && keys != null && keys.Length > 0
                ? keys
                : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// 房主端：把本机勾选广播出去（内容没变就不重复发）。单人不发。
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
            Logger.Log("[CharBoss] 勾选同步：本机没有 BaseLib ⇒ 关闭「房主勾选/玩法设置广播」" +
                       "（多人时退回整张候补表，其它照常）");
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
            if (NetRole.Type != MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Host) return;

            string payload = BuildPayload();
            string settings = BuildSettings();
            string combined = payload + "#" + settings;
            if (string.Equals(combined, _lastSent, StringComparison.Ordinal)) return;

            var net = MegaCrit.Sts2.Core.Runs.RunManager.Instance?.NetService;
            if (net == null) return;

            CustomMessageWrapper.Send(new BossPicksMessage { Payload = payload, Settings = settings }, net);
            _lastSent = combined;
            Logger.Log($"[CharBoss] 勾选同步：房主已广播本局勾选 + 玩法设置（勾选={payload}；设置={settings}）" +
                       "⇒ 客人的阵容候选 / 共用匹配池 / 地图开关都按房主来");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 勾选同步：广播失败（客人将退回整张候补表）: " + e.Message);
        }
    }

    /// <summary>把「决定上哪个 Boss」的那几项玩法设置拼成一个字符串（房主端）。</summary>
    private static string BuildSettings()
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append("sp=").Append(BossConfig.SharePool ? '1' : '0');
            sb.Append(";asc10=").Append(BossConfig.Asc10Act3OneEach ? '1' : '0');
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

    /// <summary>客人端：收到房主的勾选 + 玩法设置 ⇒ 解析、落库、清阵容缓存（下次抽签用新房主勾选重算）。</summary>
    internal static void ApplyFromHost(string payload, string settings, ulong senderId)
    {
        try
        {
            if (NetRole.Type != MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Client) return;   // 只客人吃这条

            FromHost.Clear();
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
            HostSharePool = null;
            HostAsc10Act3OneEach = null;
            HostMaps.Clear();
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
            BossLineups.InvalidateCache();

            if (!_appliedLogged)
            {
                _appliedLogged = true;
                Logger.Log($"[CharBoss] 勾选同步：已收到房主（NetId={senderId}）的勾选 " +
                           $"（{FromHost.Count} 张地图）与玩法设置" +
                           $"（共用匹配池={(HostSharePool == true ? "开" : "关")}、" +
                           $"进阶10一场原本={(HostAsc10Act3OneEach == true ? "开" : "关")}、" +
                           $"显式地图开关 {HostMaps.Count} 条）⇒ 本机阵容候选 / 共用匹配池 / 地图开关都改成房主那一套");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 勾选同步：应用房主勾选失败（继续用整张候补表）: " + e.Message);
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
