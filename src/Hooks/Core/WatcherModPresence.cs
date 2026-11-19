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
            if (!_installed && nowMs - _lastProbeMs < 2000) return;
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
        if (_installed) _probed = true;
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