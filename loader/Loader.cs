using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using MegaCrit.Sts2.Core.Modding;

namespace CharacterBosses;

/// <summary>
/// **双版本外壳（loader）** —— 游戏只会加载这一个 dll（文件名 = 清单里的模组 id），
/// 真正的实现放在同目录的 `CharacterBosses.Impl107.dll` / `CharacterBosses.Impl111.dll`：
///
/// <code>
/// mods/CharacterBosses/
///   CharacterBosses.dll            ← 本文件（外壳：版本判断 + 转发）
///   CharacterBosses.Impl107.dll    ← 正式版 v0.107 的实现
///   CharacterBosses.Impl111.dll    ← beta v0.111 的实现
///   CharacterBosses.pck / .json
/// </code>
///
/// ⚠ 关键坑（2026-11-05 实测踩过）：**不能**用 `Assembly.LoadFrom` ——
///   那会把实现装进**默认 ALC**，而游戏是在它自己的 ALC 里加载模组的，
///   于是实现对 `sts2` / `0Harmony` 的引用解析不到 ⇒ `GetType("CharacterBosses.ModEntry")` 返回 null
///   （日志：`Impl111.dll 里没有 ModEntry.Initialize ⇒ 放弃`），
///   表现就是**模组整个没生效**：控制台 `fight XXX_BOSS_ENCOUNTER` 全部 not found。
///   ⇒ 必须装进**外壳所在的同一个 ALC**（`AssemblyLoadContext.GetLoadContext`），
///     并给那个 ALC 挂一个 Resolving 兜底（从 mod 目录 / mods 各子目录 / 游戏目录找依赖）。
/// </summary>
[ModInitializer("Initialize")]
public static class LoaderEntry
{
    private const string ImplBeta = "CharacterBosses.Impl111.dll";
    private const string ImplRelease = "CharacterBosses.Impl107.dll";

    public static void Initialize()
    {
        try
        {
            string dir = Path.GetDirectoryName(typeof(LoaderEntry).Assembly.Location) ?? ".";
            bool isBeta = DetectBeta();

            string want = isBeta ? ImplBeta : ImplRelease;
            string other = isBeta ? ImplRelease : ImplBeta;
            string path = Path.Combine(dir, want);

            if (!File.Exists(path))
            {
                Log($"双版本外壳：找不到 {want}，尝试退回另一份");
                string fallback = Path.Combine(dir, other);
                if (!File.Exists(fallback)) { Log($"双版本外壳：{want} 与 {other} 都不在 {dir} ⇒ 放弃"); return; }
                path = fallback; want = other;
            }

            Log($"双版本外壳：游戏={VersionText()}（探测 beta={isBeta}）⇒ 加载实现 {want}");

            var alc = AssemblyLoadContext.GetLoadContext(typeof(LoaderEntry).Assembly)
                      ?? AssemblyLoadContext.Default;
            alc.Resolving += (ctx, name) => ResolveFromDisk(ctx, name, dir);

            Assembly asm;
            try { asm = alc.LoadFromAssemblyPath(path); }
            catch (Exception e)
            {
                Log("双版本外壳：LoadFromAssemblyPath 失败（改用 LoadFrom 兜底）: " + e.GetType().Name + ": " + e.Message);
                asm = Assembly.LoadFrom(path);
            }
            Log($"双版本外壳：已载入 {asm.GetName().Name} {asm.GetName().Version}");

            var entry = asm.GetType("CharacterBosses.ModEntry", throwOnError: false);
            if (entry == null)
            {
                Log("双版本外壳：拿不到 CharacterBosses.ModEntry ⇒ 打印加载诊断");
                DumpLoadDiagnostics(asm);
                // 再兜一层：按「名字就叫 ModEntry、命名空间是 CharacterBosses」扫一遍
                try
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (t.Name != "ModEntry") continue;
                        entry = t;
                        Log($"双版本外壳：改用扫描到的 {t.FullName}");
                        break;
                    }
                }
                catch (Exception e) { Log("双版本外壳：扫描类型也失败: " + e.GetType().Name + ": " + e.Message); }
                if (entry == null) return;
            }

            // ★★ 2026-11-05（实测）：游戏只在「清单声明的那个程序集」里自动登记模组模型
            //   （`ModelDb` 扫描）。我们的实现是**第二个程序集** ⇒ 它的 Encounter/怪物模型
            //   **没有被登记**，于是设置 Boss 槽时会报
            //   `The given key 'ENCOUNTER.XXX_BOSS_ENCOUNTER' was not present in the dictionary`。
            //   这里手动补登记：对实现里每个具体的 `AbstractModel` 子类调 `ModelDb.Inject`，
            //   再 `InitIds()` 让每个模型拿到自己的 id。
            RegisterModels(asm);

            var init = entry.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static);
            if (init == null) { Log("双版本外壳：ModEntry 里没有静态 Initialize ⇒ 放弃"); return; }

            init.Invoke(null, null);
            Log($"双版本外壳：{want} 已初始化");
        }
        catch (Exception e)
        {
            Log("双版本外壳：加载实现失败: " + e);   // 外壳不能抛：抛了游戏会判「模组初始化失败」
        }
    }

    /// <summary>
    /// 把实现程序集里的**所有具体 `AbstractModel` 子类**登记进 `ModelDb`
    /// （游戏只自动扫描清单声明的那个程序集，第二个程序集得我们自己来）。
    /// 不登记的症状：设 Boss 槽时报 `ENCOUNTER.XXX not present in the dictionary`、
    /// `ModelDb.Encounter<...>()` 拿不到已注册实例。
    /// </summary>
    private static void RegisterModels(Assembly asm)
    {
        try
        {
            var modelBase = typeof(MegaCrit.Sts2.Core.Models.AbstractModel);
            int injected = 0, total = 0;
            foreach (var t in asm.GetTypes())
            {
                try
                {
                    if (t == null || t.IsAbstract || t.IsInterface || t.ContainsGenericParameters) continue;
                    if (!modelBase.IsAssignableFrom(t)) continue;
                    total++;
                    if (MegaCrit.Sts2.Core.Models.ModelDb.Contains(t)) continue;
                    MegaCrit.Sts2.Core.Models.ModelDb.Inject(t);
                    injected++;
                }
                catch (Exception e)
                {
                    Log($"双版本外壳：登记模型 {t?.FullName} 失败: {e.GetType().Name}: {e.Message}");
                }
            }
            if (injected > 0)
            {
                try { MegaCrit.Sts2.Core.Models.ModelDb.InitIds(); }
                catch (Exception e) { Log("双版本外壳：InitIds 失败: " + e.GetType().Name + ": " + e.Message); }
            }
            Log($"双版本外壳：模型登记完成（{injected} 个新登记 / 共 {total} 个 AbstractModel 实现）");
        }
        catch (Exception e)
        {
            Log("双版本外壳：模型登记整体失败: " + e.GetType().Name + ": " + e.Message);
        }
    }

    private static Assembly? ResolveFromDisk(AssemblyLoadContext ctx, AssemblyName name, string modDir)
    {
        try
        {
            var candidates = new List<string> { modDir };
            try
            {
                var parent = Directory.GetParent(modDir)?.FullName;
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                    candidates.AddRange(Directory.GetDirectories(parent));
            }
            catch { /* 忽略 */ }
            try
            {
                var exeDir = AppContext.BaseDirectory;
                if (!string.IsNullOrEmpty(exeDir)) candidates.Add(exeDir);
            }
            catch { /* 忽略 */ }

            foreach (var dir in candidates)
            {
                try
                {
                    var p = Path.Combine(dir, name.Name + ".dll");
                    if (!File.Exists(p)) continue;
                    var a = ctx.LoadFromAssemblyPath(p);
                    Log($"双版本外壳：依赖 {name.Name} ← {p}");
                    return a;
                }
                catch { /* 换下一个 */ }
            }

            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { if (a.GetName().Name == name.Name) return a; } catch { /* 忽略 */ }
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    private static void DumpLoadDiagnostics(Assembly asm)
    {
        try
        {
            var types = asm.GetTypes();
            Log($"双版本外壳：该实现里共 {types.Length} 个类型，其中名字含 ModEntry 的：");
            foreach (var t in types)
            {
                if (t.Name.Contains("ModEntry")) Log("   · " + t.FullName);
            }
        }
        catch (ReflectionTypeLoadException e)
        {
            foreach (var le in e.LoaderExceptions)
            {
                if (le == null) continue;
                Log("双版本外壳：类型加载错误: " + le.GetType().Name + ": " + le.Message);
            }
        }
        catch (Exception e)
        {
            Log("双版本外壳：枚举类型失败: " + e.GetType().Name + ": " + e.Message);
        }
    }

    /// <summary>beta 才有 `OneForAllPower`；正式版（v0.107）没有。</summary>
    private static bool DetectBeta()
    {
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (asm.GetName().Name != "sts2") continue;
                    if (asm.GetType("MegaCrit.Sts2.Core.Models.Powers.OneForAllPower", false) != null) return true;
                }
                catch { /* 忽略 */ }
            }
        }
        catch { /* 忽略 */ }
        return false;
    }

    private static string VersionText()
    {
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (asm.GetName().Name != "sts2") continue;
                    var attr = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                    return attr?.InformationalVersion ?? asm.GetName().Version?.ToString() ?? "?";
                }
                catch { /* 忽略 */ }
            }
        }
        catch { /* 忽略 */ }
        return "?";
    }

    private static void Log(string message)
    {
        try
        {
            string logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SlayTheSpire2", "logs", "mod_log.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] [Loader] {message}{Environment.NewLine}");
        }
        catch { /* 忽略 */ }
    }
}
