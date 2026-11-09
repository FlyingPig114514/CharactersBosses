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

    public bool ShouldBroadcast => true;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteString(Payload ?? string.Empty);
    }

    public void Deserialize(PacketReader reader)
    {
        Payload = reader.ReadString() ?? string.Empty;
    }

    public void HandleMessage(ulong senderId)
    {
        try { BossPicksSync.ApplyFromHost(Payload, senderId); }
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
        try
        {
            if (NetRole.Type != MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Host) return;

            string payload = BuildPayload();
            if (string.IsNullOrEmpty(payload) || string.Equals(payload, _lastSent, StringComparison.Ordinal))
                return;

            var net = MegaCrit.Sts2.Core.Runs.RunManager.Instance?.NetService;
            if (net == null) return;

            CustomMessageWrapper.Send(new BossPicksMessage { Payload = payload }, net);
            _lastSent = payload;
            Logger.Log($"[CharBoss] 勾选同步：房主已广播本局勾选（{payload}）⇒ 客人的阵容候选也按它来");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 勾选同步：广播失败（客人将退回整张候补表）: " + e.Message);
        }
    }

    /// <summary>客人端：收到房主的勾选 ⇒ 解析、落库、清阵容缓存（下次抽签用新房主勾选重算）。</summary>
    internal static void ApplyFromHost(string payload, ulong senderId)
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
            BossLineups.InvalidateCache();

            if (!_appliedLogged)
            {
                _appliedLogged = true;
                Logger.Log($"[CharBoss] 勾选同步：已收到房主（NetId={senderId}）的勾选 " +
                           $"（{FromHost.Count} 张地图）⇒ 本机阵容候选改成房主那一套，并清空阵容缓存重算");
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
