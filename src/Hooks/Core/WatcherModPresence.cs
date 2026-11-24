#if STS2_111   // 「观者」角色只在 0.113 的观者 mod 里 ⇒ 107 不参与
using System;
using System.Reflection;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Core;

/// <summary>
/// **探测「观者（Watcher）」mod**（用户 2026-11-15）：
///
/// > 「联动观者 mod，当玩家同时装在本模组与观者模组后 boss 有可能为观者（暂时还没有设计），
/// >   但和愚者一样先设置好」
///
/// 观者是**别的 mod 里的角色**，我们编译期没有它的类型 ⇒ 只能运行期按程序集名找：
///   · `Installed`：有没有名字里含 `Watcher` 的程序集；
///   · `CharacterType()`：在所有已加载程序集里找 `CharacterModel` 的子类，
///     其 `Id.Entry` 含 `WATCHER` 的那一个（`ModelDb.GetId` 反查入口名用得上）。
///
/// 找不到就一切照旧（候选表里不会出现这只 Boss）。
/// </summary>
internal static class WatcherModPresence
{
    private static bool _probed;
    /// <summary>上一次探测的时间戳（毫秒）：未装观者 mod 时用来限流，见 `Probe()`。</summary>
    private static long _lastProbeMs;
    private static bool _installed;
    private static Type? _characterType;

    /// <summary>装了观者 mod 吗（按程序集名判断，和 BaseLibPresence 同一套思路）。</summary>
    internal static bool Installed
    {
        get
        {
            Probe();
            return _installed;
        }
    }

    /// <summary>
    /// 观者 **Boss 到底能不能用**（= 能不能放进候选表 / 能不能原样上场）。
    ///
    /// ★★ v0.2.922（用户 2026-11-24 玩家报的 bug：「在 2 层未装载观者 mod 时遇到了**保留观**，
    ///   然后变成了**不出牌占位的愚者**」）：
    ///   以前闸门只问 <see cref="Installed"/> —— 它的判据只是「有没有一个名字里含 `Watcher` 的程序集」。
    ///   可 Boss 真正需要的是三件东西：
    ///     · 卡牌/遗物引用层 `<see cref="WatcherCards.ModAssembly"/>`（认得出 A / B 哪一份实现）；
    ///     · <see cref="WatcherCards.ProviderId"/> 非空（联机时用来核对两端是不是同一份实现）；
    ///     · 观者的角色类型 <see cref="CharacterType"/>（拿不到 ⇒ 立绘退回**占位愚者** `fool.tscn`）。
    ///   三样缺一样就会变成玩家截图那样：名字是「保留观」、立绘是愚者、牌一张也出不来
    ///   （卡组全部来自观者 mod ⇒ 空）。
    ///   现在统一问这一个属性：**少了任何一样就当没装**，退回「换成同幕的普通 Boss」那条兜底。
    /// </summary>
    internal static bool Usable
    {
        get
        {
            if (!Installed) return false;
            try
            {
                if (Core.WatcherCards.ModAssembly() == null)
                {
                    LogOnce("[CharBoss] 观者 Boss：探测到名字含 Watcher 的程序集，" +
                            "但认不出它是 A（3747526116）/ B（3747492505）哪一份实现 ⇒ 按「没装」处理");
                    return false;
                }
                if (Core.WatcherCards.ProviderId.Length == 0) return false;
                if (CharacterType() == null)
                {
                    LogOnce("[CharBoss] 观者 Boss：卡牌层认出来了，但解析不到观者的角色类型 ⇒ 按「没装」处理" +
                            "（否则立绘会退化成占位愚者）");
                    return false;
                }
                return true;
            }
            catch { return false; }
        }
    }

    /// <summary>观者的 `CharacterModel` 子类（拿不到返回 null）。</summary>
    internal static Type? CharacterType()
    {
        Probe();
        return _characterType;
    }

    private static void Probe()
    {
        if (_probed) return;
        // 未装时每次调用都全扫 AppDomain 会很贵 ⇒ 2 秒最多探一次（慢车道一直在调 Installed）
        try
        {
            long nowMs = Environment.TickCount64;
            // ★ v0.2.922：**无论装没装**都限流（以前只有「没装」才限流）。
            //   因为「程序集在、但角色类型解析不到」这种情况现在不算探完（见文件末尾的 _probed），
            //   不限流的话会每帧全量扫一遍程序集类型。
            if (_lastProbeMs != 0 && nowMs - _lastProbeMs < 2000) return;
            _lastProbeMs = nowMs;
        }
        catch { /* 忽略 */ }
        // ★ v0.2.686（用户实测：整场立绘是愚者）：**只在真的探测到之后才锁**。
        //   以前第一行就 `_probed = true`，而这次探测很可能**早于**观者 mod 的程序集加载 ⇒
        //   缓存成「没装」就再也不重试（候补表/角色类型/立绘全都会错）。现在没找到就下次再来。
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name;
                try { name = asm.GetName().Name ?? string.Empty; } catch { continue; }
                if (!name.Contains("Watcher", StringComparison.OrdinalIgnoreCase)) continue;
                _installed = true;

                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types!; }
                catch { continue; }

                foreach (var t in types)
                {
                    if (t == null || t.IsAbstract) continue;
                    if (!typeof(CharacterModel).IsAssignableFrom(t)) continue;
                    string entry = string.Empty;
                    try { entry = ModelDb.GetId(t)?.Entry ?? string.Empty; } catch { continue; }
                    if (entry.IndexOf("WATCHER", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    _characterType = t;
                    LogOnce($"[CharBoss] 观者 mod 探测到：程序集 {name}，角色类型 {t.FullName}（entry={entry}）");
                    return;
                }
                LogOnce($"[CharBoss] 观者 mod 程序集 {name} 在，但没找到 WATCHER 角色类型");
            }
        }
        catch (Exception e)
        {
            LogOnce("[CharBoss] 观者 mod 探测失败: " + e.Message);
        }
        if (!_installed) LogOnce("[CharBoss] 没装观者 mod ⇒ 第三幕候选里不会有观者 Boss");
        // 只有真的探测到观者 mod 才锁死；没找到就保持未锁，下一帧再试（慢车道会反复调 Installed）
        // ★ v0.2.922：**还要真的解析到角色类型**才锁（否则「程序集在、类型还没就绪」会被永久缓存成
        //   「没角色类型」⇒ 立绘一直是占位愚者，和 v0.2.686 那个坑同一类）。
        if (_installed && _characterType != null) _probed = true;
    }

    /// <summary>同一句话只打一次（v0.2.805：以前「没装观者 / 探测到」在手机上一局能刷两千多行）。</summary>
    private static string _lastLog = string.Empty;

    private static void LogOnce(string message)
    {
        try
        {
            if (message == _lastLog) return;
            _lastLog = message;
            Logger.Log(message);
        }
        catch { /* 忽略 */ }
    }
}
#endif