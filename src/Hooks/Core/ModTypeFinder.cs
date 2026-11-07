using System;
using System.Collections.Generic;
using System.Reflection;

namespace CharacterBosses.Core;

/// <summary>
/// **按「类型简单名」在已加载的程序集里找类型** —— 给崩坠（Downfall）兼容用。
///
/// 为什么不能写死全名（用户 2026-10-24）：
/// 崩坠有两套发行：早先 `mods\Downfall\Downfall.dll` 那一份是**改版**（所有角色塞在一个 dll 里），
/// 而**创意工坊官方版**把它拆成了 `Downfall.dll` + `Awakened.dll` / `Hexaghost.dll` / `SlimeBoss.dll` …
/// 命名空间与拆分方式都可能变。写死全名的兼容会**静默失效**（FindType 返回 null，什么都不挂）。
/// 这里先按全名找（快、精确），找不到再退化成「简单名 + 命名空间包含关键字」，并缓存结果。
/// </summary>
internal static class ModTypeFinder
{
    private static readonly Dictionary<string, Type?> Cache = new(StringComparer.Ordinal);

    /// <summary>
    /// 找类型：先按 <paramref name="fullName"/>，再退化成「简单名 == <paramref name="fullName"/> 的最后一段」。
    /// <paramref name="namespaceHint"/> 用于在多个同名类型里挑（例如都叫 <c>Ceremony</c> 时认 `Awakened`）。
    /// </summary>
    internal static Type? Find(string? fullName, string? namespaceHint = null)
    {
        if (string.IsNullOrEmpty(fullName)) return null;
        string key = fullName + "|" + (namespaceHint ?? string.Empty);
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
        }

        var found = FindCore(fullName, namespaceHint);
        lock (Cache) { Cache[key] = found; }
        return found;
    }

    private static Type? FindCore(string fullName, string? namespaceHint)
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        // ① 全名精确命中
        foreach (var assembly in assemblies)
        {
            try
            {
                var type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            catch { /* 忽略 */ }
        }

        // ② 退化成简单名（+ 命名空间关键字）
        string simple = fullName;
        int dot = simple.LastIndexOf('.');
        if (dot >= 0 && dot + 1 < simple.Length) simple = simple[(dot + 1)..];

        foreach (var assembly in assemblies)
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = Array.FindAll(e.Types, t => t != null)!; }
            catch { continue; }

            foreach (var type in types)
            {
                try
                {
                    if (type == null || type.Name != simple) continue;
                    if (namespaceHint != null)
                    {
                        var ns = type.Namespace ?? string.Empty;
                        if (ns.IndexOf(namespaceHint, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    }
                    Logger.Log($"[CharBoss] 崩坠兼容：{fullName} 按简单名找到 {type.FullName}（{assembly.GetName().Name}）");
                    return type;
                }
                catch { /* 忽略 */ }
            }
        }
        return null;
    }

    /// <summary>
    /// 把所有程序集里**简单名**命中的类型都找出来（同一个类可能在多个 dll 里各有一份，
    /// 例如官方版崩坠把角色拆开打包）。
    /// </summary>
    internal static List<Type> FindAll(string simpleName)
    {
        var result = new List<Type>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = Array.FindAll(e.Types, t => t != null)!; }
            catch { continue; }

            foreach (var type in types)
            {
                try
                {
                    if (type != null && type.Name == simpleName && !result.Contains(type)) result.Add(type);
                }
                catch { /* 忽略 */ }
            }
        }
        return result;
    }
}
