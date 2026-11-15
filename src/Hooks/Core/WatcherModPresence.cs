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
        _probed = true;
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
                    Logger.Log($"[CharBoss] 观者 mod 探测到：程序集 {name}，角色类型 {t.FullName}（entry={entry}）");
                    return;
                }
                Logger.Log($"[CharBoss] 观者 mod 程序集 {name} 在，但没找到 WATCHER 角色类型");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 观者 mod 探测失败: " + e.Message);
        }
        if (!_installed) Logger.Log("[CharBoss] 没装观者 mod ⇒ 第三幕候选里不会有观者 Boss");
    }
}
#endif
