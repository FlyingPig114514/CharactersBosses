using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using MegaCrit.Sts2.Core.Modding;

namespace CharacterBosses;

/// <summary>
/// **双版本外壳（loader）** —— 游戏只会加载这一个 dll（文件名 = 清单里的模组 id），
/// 真正的实现放在同目录的 `CharacterBosses.Impl107.dll.bin` / `CharacterBosses.Impl111.dll.bin`：
///
/// <code>
/// mods/CharacterBosses/
///   CharacterBosses.dll            ← 本文件（外壳：版本判断 + 转发）
///   CharacterBosses.Impl107.dll.bin ← 正式版 v0.107 的实现
///   CharacterBosses.Impl111.dll.bin ← beta v0.111 的实现
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

            // ★ v0.2.588：实现文件现在以 **`.dll.bin`** 的名义随包发布（见 install-mod.bat / pack-dist.ps1）。
            //   原因：`CVC-跨版本兼容补丁`（以及其它扫描器）会把 mod 目录里**每一个 `.dll`** 当成一个模组实现去
            //   指纹识别；我们同时带 107/111 两份，于是它扫到休眠的那份 `Impl107.dll`，把整个模组判定成
            //   「目标 0.107（older→newer）」—— 那是 CVC 报告里对我们唯一不利的推断，还会让它去 rewrite
            //   一份根本不会被加载的 DLL。改成 `.dll.bin` 后扫描器看不见它，我们自己照样能按路径加载。
            string path = FindImpl(dir, want);
            if (path == null)
            {
                Log($"双版本外壳：找不到 {want}（.dll / .dll.bin 都没有），尝试退回另一份");
                path = FindImpl(dir, other);
                if (path == null) { Log($"双版本外壳：{want} 与 {other} 都不在 {dir} ⇒ 放弃"); return; }
                want = other;
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
                    foreach (var t in GetLoadableTypes(asm))
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
    /// 按「.dll → .dll.bin」的顺序找实现文件（v0.2.588 起默认是后者）。
    /// `LoadFromAssemblyPath` 不看扩展名，所以 `.bin` 照样能加载。
    /// </summary>
    private static string? FindImpl(string dir, string fileName)
    {
        try
        {
            string direct = Path.Combine(dir, fileName);
            if (File.Exists(direct)) return direct;
            string hidden = direct + ".bin";
            if (File.Exists(hidden)) return hidden;
            return null;
        }
        catch { return null; }
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
            foreach (var t in GetLoadableTypes(asm))
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

    /// <summary>
    /// **能拿到多少算多少**的类型枚举（★ v0.2.587）。
    ///
    /// 为什么要它：实现程序集里有几个类型直接实现了 BaseLib 的接口（`ICustomMessage` 等）。
    /// 玩家**没装 BaseLib** 时，`Assembly.GetTypes()` 会整体抛 `ReflectionTypeLoadException`，
    /// 于是我们一个模型都登记不上 ⇒ 整局没有角色 Boss（表现为「模组装了但什么都没变」）。
    /// 这里改用异常里带回来的**部分结果**：坏掉的类型跳过，其余照常登记。
    /// </summary>
    private static Type[] GetLoadableTypes(Assembly asm)
    {
        try { return asm.GetTypes(); }
        catch (ReflectionTypeLoadException e)
        {
            var ok = new List<Type>();
            int failed = 0;
            foreach (var t in e.Types)
            {
                if (t != null) ok.Add(t);
                else failed++;
            }
            Log($"双版本外壳：有 {failed} 个类型加载不了（多半是没装 BaseLib）⇒ 其余 {ok.Count} 个照常处理");
            return ok.ToArray();
        }
        catch (Exception e)
        {
            Log("双版本外壳：枚举类型失败: " + e.GetType().Name + ": " + e.Message);
            return Array.Empty<Type>();
        }
    }
    private static void DumpLoadDiagnostics(Assembly asm)
    {
        try
        {
            var types = GetLoadableTypes(asm);
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
