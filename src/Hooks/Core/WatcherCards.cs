#if STS2_111   // 观者 Boss 只在 0.113 构建里（107 不加）
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Core;

/// <summary>
/// **观者模组的牌 / 姿态 API 引用层**（用户 2026-11-15：三层 1 号「神格观」）。
///
/// 铁律：**绝不编译期引用别的模组**（见 `WatcherModPresence` / `WatcherVfx` 的注释）——
/// 观者模组没装时我们的 DLL 也必须能加载。所以这里全部走**运行期反射**：
///   · 牌：按 `Type` 名去观者程序集里取（`WatcherMod.WatcherStrike_P` …），取不到返回 null；
///   · 姿态 / 真言：观者把机制放在 `WatcherMod.WatcherCombatHelper`（反编译 0.113 的 Watcher.dll）：
///       `Task EnterDivinity(Player, CardModel?)`
///       `Task ExitStance(Player)`
///       `Task GainMantra(Player, int, CardModel?)`
///       `int  GetTotalMantraGained(Player)`
///       `bool IsInStance<T>(Creature)`
///     它们的参数都是 `Player` —— 我们这边传 **Boss 的影子玩家**（`boss.Shadow`），
///     也就是「观者的状态挂在影子玩家身上」。
///
/// 牌的内部 id 与中文名的对照（从观者模组 `res://Watcher/localization/zhs/cards.json` 反查）：
///   许愿 WATCHER_WISH_P / 打击 WATCHER_STRIKE_P / 警惕 WATCHER_VIGILANCE / 光辉 WATCHER_BRILLIANCE
///   防御 WATCHER_DEFEND_P / 敬拜 WATCHER_WORSHIP / 奇迹 WATCHER_MIRACLE / 聚能成刃 WATCHER_CONJURE_BLADE
///   供奉 WATCHER_CONSECRATE / 化拳为空 WATCHER_EMPTY_FIST / 摆手 WATCHER_WAVE_OF_THE_HAND
///   欺瞒现实 WATCHER_DECEIVE_REALITY / 平安 WATCHER_SAFETY / 灭除之刃 WATCHER_EXPUNGER
///   虔信 WATCHER_DEVOTION / 旋身 WATCHER_SWIVEL / 圣洁 WATCHER_SANCTITY / 结末 WATCHER_CONCLUDE
///   （贪婪 GREED 是**本体**的牌，直接 typeof；「洗牌」在观者与本体两份本地化里都查不到 ⇒ 待用户确认）
/// </summary>
internal static class WatcherCards
{
    private static Assembly? _asm;
    private static bool _probed;
    private static Type? _helper;
    private static bool _helperProbed;

    private static readonly Dictionary<string, Type?> _types = new(StringComparer.Ordinal);

    /// <summary>观者模组的程序集（按名字含 "Watcher" 且不是我们自己的程序集找）。</summary>
    internal static Assembly? ModAssembly()
    {
        if (_probed) return _asm;
        _probed = true;
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string? name = null;
                try { name = asm.GetName().Name; } catch { continue; }
                if (string.IsNullOrEmpty(name)) continue;
                if (name.StartsWith("CharacterBosses", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.IndexOf("Watcher", StringComparison.OrdinalIgnoreCase) < 0) continue;
                _asm = asm;
                Logger.Log("[CharBoss] 观者引用层：找到程序集 " + name);
                break;
            }
        }
        catch { _asm = null; }
        if (_asm == null) Logger.Log("[CharBoss] 观者引用层：没找到观者模组的程序集（相关功能全部跳过）");
        return _asm;
    }

    /// <summary>按短名取观者程序集里的类型（`WatcherStrike_P` → `WatcherMod.WatcherStrike_P`）。</summary>
    internal static Type? T(string shortName)
    {
        if (string.IsNullOrEmpty(shortName)) return null;
        if (_types.TryGetValue(shortName, out var cached)) return cached;

        Type? found = null;
        try
        {
            var asm = ModAssembly();
            if (asm != null)
            {
                found = asm.GetType("WatcherMod." + shortName, false)
                        ?? asm.GetType(shortName, false);
                if (found == null)
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (string.Equals(t.Name, shortName, StringComparison.Ordinal)) { found = t; break; }
                    }
                }
            }
        }
        catch { found = null; }

        _types[shortName] = found;
        return found;
    }

    // ── 剧本用到的牌（一次取好，取不到就是 null ⇒ 剧本那一格会被跳过） ──────────────
    internal static Type? Wish => T("WatcherWish_P");                 // 许愿（选「永生不死」）
    internal static Type? Strike => T("WatcherStrike_P");             // 打击
    internal static Type? Defend => T("WatcherDefend_P");             // 防御
    internal static Type? Vigilance => T("WatcherVigilance");         // 警惕
    internal static Type? Brilliance => T("WatcherBrilliance");       // 光辉
    internal static Type? Worship => T("WatcherWorship");             // 敬拜
    internal static Type? Miracle => T("WatcherMiracle");             // 奇迹
    internal static Type? ConjureBlade => T("WatcherConjureBlade");   // 聚能成刃（X 费）
    internal static Type? Consecrate => T("WatcherConsecrate");       // 供奉
    internal static Type? EmptyFist => T("WatcherEmptyFist");         // 化拳为空
    internal static Type? WaveOfTheHand => T("WatcherWaveOfTheHand"); // 摆手
    internal static Type? DeceiveReality => T("WatcherDeceiveReality"); // 欺瞒现实
    internal static Type? Safety => T("WatcherSafety");               // 平安
    internal static Type? Expunger => T("WatcherExpunger");           // 灭除之刃
    internal static Type? Devotion => T("WatcherDevotion");           // 虔信
    internal static Type? Swivel => T("WatcherSwivel");               // 旋身
    internal static Type? Sanctity => T("WatcherSanctity");           // 圣洁
    internal static Type? Conclude => T("WatcherConclude");           // 结末
    internal static Type? Greed => typeof(MegaCrit.Sts2.Core.Models.Cards.Greed);   // 贪婪（本体牌）

    // ── 姿态 / 真言（`WatcherMod.WatcherCombatHelper`）─────────────────────────
    private static Type? Helper()
    {
        if (_helperProbed) return _helper;
        _helperProbed = true;
        _helper = T("WatcherCombatHelper");
        if (_helper == null) Logger.Log("[CharBoss] 观者引用层：找不到 WatcherCombatHelper（姿态/真言功能会跳过）");
        return _helper;
    }

    private static MethodInfo? M(string name, params Type[] args)
    {
        try
        {
            var t = Helper();
            if (t == null) return null;
            return t.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                               null, args, null);
        }
        catch { return null; }
    }

    /// <summary>进入神格（观者自己的实现：给 3 点能量 + 攻击三倍）。</summary>
    internal static Task EnterDivinityAsync(Player? owner, CardModel? source)
    {
        try
        {
            var m = M("EnterDivinity", typeof(Player), typeof(CardModel));
            return m?.Invoke(null, new object?[] { owner, source }) as Task ?? Task.CompletedTask;
        }
        catch (Exception e) { Logger.Log("[CharBoss] 进神格失败: " + e.Message); return Task.CompletedTask; }
    }

    /// <summary>退出当前姿态（平静/愤怒/神格都会退）。**进神格前必须先退**，否则拿不到入姿态的 3 点能量。</summary>
    internal static Task ExitStanceAsync(Player? owner)
    {
        try
        {
            var m = M("ExitStance", typeof(Player));
            return m?.Invoke(null, new object?[] { owner }) as Task ?? Task.CompletedTask;
        }
        catch (Exception e) { Logger.Log("[CharBoss] 退出姿态失败: " + e.Message); return Task.CompletedTask; }
    }

    /// <summary>获得真言（观者的实现里满 10 层会自己结算神格；我们只在需要时自己再补一层逻辑）。</summary>
    internal static Task GainMantraAsync(Player? owner, int amount, CardModel? source)
    {
        try
        {
            if (amount <= 0) return Task.CompletedTask;
            var m = M("GainMantra", typeof(Player), typeof(int), typeof(CardModel));
            return m?.Invoke(null, new object?[] { owner, amount, source }) as Task ?? Task.CompletedTask;
        }
        catch (Exception e) { Logger.Log("[CharBoss] 获得真言失败: " + e.Message); return Task.CompletedTask; }
    }

    /// <summary>本场战斗累计获得过多少真言（观者自己的账本）。</summary>
    internal static int TotalMantraGained(Player? owner)
    {
        try
        {
            var m = M("GetTotalMantraGained", typeof(Player));
            if (m == null || owner == null) return 0;
            return m.Invoke(null, new object?[] { owner }) is int n ? n : 0;
        }
        catch { return 0; }
    }
}
#endif
