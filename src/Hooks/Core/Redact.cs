using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CharacterBosses.Core;

/// <summary>
/// **日志隐私脱敏**（v0.2.892，用户 2026-11-22：「能不能让 modlog 在保证能继续用于修 bug 的同时尽可能去掉隐私信息？
/// 比如把玩家 id 求哈希值（保证仍然能识别多人游戏中各个玩家『是谁』）」）。
///
/// ## 目标
///   · 日志里**不再出现**完整 SteamID / NetId、Windows 用户名、玩家昵称；
///   · 但仍然能区分「玩家 A / 玩家 B / 玩家 C」，而且**两端（房主/客人）同一份日志里的代号必须一致**，
///     否则不同步排查就废了。
///
/// ## 做法
///   代号 = `P#` + SHA-256(**本局种子** ‖ NetId) 的前 6 位十六进制。
///   · 同一局里，同一 SteamID 永远得到同一个代号（房主与客人读到的 `RunState.Rng.Seed` 相同 ⇒ 两端一致）；
///   · 不同局的代号不同 ⇒ 换一局之后**无法把两份日志里的同一个人关联起来**（也不利于拿 SteamID 反推）；
///   · 6 位 hex 在同一局（最多 6 名玩家）里碰撞概率可以忽略。
///
/// ## 覆盖范围
///   · `Logger.Log` 的**唯一出口**：通过 Harmony 前缀改写写入内容（`ModEntry.cs` 本身不动）；
///   · 明确格式化的地方（`DescribePlayer` / 不同步摘要 / 数据上传的 diag 行）另做一次，双保险；
///   · 具体替换项：17 位 SteamID、行内 `NetId=` 数字、`C:\Users\<名字>` 路径、已知真人玩家昵称。
///
/// ## 保留的东西（修 bug 必须）
///   怪物/卡牌/能力名、血量、意图、回合数、种子（种子本身就是公开的排布依据，且不含个人信息）、
///   `P#xxxxxx` 代号、以及"本机=房主/客人"这类角色信息。
/// </summary>
internal static class Redact
{
    private static ulong _seedCache;
    private static bool _seedResolved;

    /// <summary>本局种子（两端一致）；读不到时退回常量，保证仍然确定。</summary>
    private static ulong Seed()
    {
        if (_seedResolved) return _seedCache;
        _seedResolved = true;
        _seedCache = 1UL;
        try
        {
            var run = MultiBossPlan.RunStateOrNull();
            if (run != null) _seedCache = unchecked((ulong)run.Rng.Seed);
        }
        catch { /* 用常量 */ }
        return _seedCache;
    }

    /// <summary>把一个 NetId / SteamID 变成局内稳定代号：`P#a1b2c3`。</summary>
    internal static string Id(ulong netId)
    {
        try
        {
            if (netId == 0UL) return "P#000000";
            var key = new byte[16];
            ulong s = Seed();
            for (int i = 0; i < 8; i++) key[i] = (byte)(s >> (8 * i));
            for (int i = 0; i < 8; i++) key[8 + i] = (byte)(netId >> (8 * i));
            byte[] hash = SHA256.HashData(key);
            return "P#" + Convert.ToHexString(hash, 0, 3).ToLowerInvariant();
        }
        catch { return "P#??????"; }
    }

    private static readonly Regex SteamId = new(@"\b7656\d{13}\b", RegexOptions.Compiled);
    private static readonly Regex NetIdEq = new(@"NetId=(\d{5,})", RegexOptions.Compiled);

    /// <summary>
    /// 路径里的 Windows 用户名。
    ///
    /// ★★ v0.2.980（用户 2026-11-27：「检查一下，目前本模组有没有什么其它地方写了隐私信息？」）：
    /// 原来的写法是 `[A-Za-z]:\\Users\\[^\\\s]+` —— **只认反斜杠**。而 Godot 的
    /// `OS.GetUserDataDir()` / `ProjectSettings.GlobalizePath("user://")` 返回的是**正斜杠**形式
    /// （`C:/Users/<名字>/AppData/Roaming/...`），于是这类行**整条漏网**：
    /// 实测 `characterbosses_recent.txt`（就是让玩家发出来的那份）里躺着 3 条
    /// `C:/Users/<用户名>/AppData/Roaming/SlayTheSpire2\...`。
    /// 现在两个方向的分隔符都认（也认混写），把 `C:/Users/<名字>` / `C:\Users\<名字>` 统一替换成 `&lt;userdir&gt;`。
    /// </summary>
    private static readonly Regex UserPath = new(@"[A-Za-z]:[\\/]+Users[\\/]+[^\\/\s]+", RegexOptions.Compiled);

    /// <summary>
    /// 这行**有没有可能**含隐私信息（便宜的预筛）。给「批量导出」用：
    /// 6 万行日志逐行走正则太浪费，先看有没有特征串，可疑的才真正脱敏。
    /// </summary>
    internal static bool LooksSensitive(string? line)
    {
        if (string.IsNullOrEmpty(line)) return false;
        try
        {
            return line!.Contains("7656", StringComparison.Ordinal)
                || line.Contains("NetId=", StringComparison.Ordinal)
                || line.Contains(":\\Users\\", StringComparison.OrdinalIgnoreCase)
                || line.Contains(":/Users/", StringComparison.OrdinalIgnoreCase)
                || line.Contains("\\Users\\", StringComparison.OrdinalIgnoreCase)
                || line.Contains("/Users/", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>对一行日志做脱敏（幂等，失败原样返回）。</summary>
    internal static string Text(string? message)
    {
        if (string.IsNullOrEmpty(message)) return message ?? string.Empty;
        try
        {
            string s = message!;

            // ① 17 位 SteamID → 局内代号
            s = SteamId.Replace(s, m => ulong.TryParse(m.Value, out var id) ? Id(id) : "P#??????");

            // ② 行内 `NetId=123...` → `NetId=P#xxxxxx`
            s = NetIdEq.Replace(s, m => "NetId=" + (ulong.TryParse(m.Groups[1].Value, out var id) ? Id(id) : "P#??????"));

            // ③ 路径里的 Windows 用户名
            s = UserPath.Replace(s, "<userdir>");

            // ④ 已知真人玩家的昵称 → 同一个代号（昵称也是个人信息）
            try
            {
                var combat = MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.DebugOnlyGetState();
                if (combat != null)
                {
                    foreach (var p in BossLockOn.RealPlayers(combat))
                    {
                        var name = p?.Creature?.Name;
                        if (string.IsNullOrEmpty(name) || name.Length < 2) continue;
                        string token = Id(p!.NetId);
                        if (s.Contains(name!, StringComparison.Ordinal)) s = s.Replace(name!, token);
                    }
                }
            }
            catch { /* 拿不到名单就不替换昵称 */ }

            return s;
        }
        catch { return message!; }
    }
}
