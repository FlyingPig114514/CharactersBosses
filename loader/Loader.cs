using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using MegaCrit.Sts2.Core.Modding;

namespace CharacterBosses;

/// <summary>
/// **双版本外壳（loader）** —— 游戏只会加载这一个 dll（文件名 = 清单里的模组 id），
/// 真正的实现放在同目录的 `CharacterBosses.Impl111.dll.bin`（正式 0.111）/ `CharacterBosses.Impl113.dll.bin`（测试 0.113）：
///
/// <code>
/// mods/CharacterBosses/
///   CharacterBosses.dll            ← 本文件（外壳：版本判断 + 转发）
///   CharacterBosses.Impl111.dll.bin ← **正式版 v0.111** 的实现（无愚者/恐惧）
///   CharacterBosses.Impl113.dll.bin ← **测试分支 v0.113** 的实现（含愚者/恐惧）
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
    /// <summary>清单里的模组 id（登记程序集归属要用它）。</summary>
    private const string ModId = "CharacterBosses";

    private const string ImplBeta = "CharacterBosses.Impl113.dll";
    private const string ImplRelease = "CharacterBosses.Impl111.dll";

    public static void Initialize()
    {
        try
        {
            BootTrace.Mark("Initialize 开始");
            string dir = Path.GetDirectoryName(typeof(LoaderEntry).Assembly.Location) ?? ".";
            bool isBeta = DetectBeta();
            BootTrace.Mark("游戏版本=" + VersionText() + "，探测 beta=" + isBeta);

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

            // ★★ v0.2.920（用户 2026-11-23：玩家装了「MLM-模组启动管理器」后启动崩「致命错误」）：
            //   MLM（`ModLaunchManager`，工坊 3788556097）会把每个模组的 DLL 装进它自己那个
            //   **collectible（可卸载）** 的 `AssemblyLoadContext` 里（反编译
            //   `ModLaunchManager.CollectibleModAssemblyLoader` 实证：`base(name, isCollectible: true)`），
            //   以便运行期热开关模组。我们跟着它进同一个 ALC（v0.2.588 那条「必须同一个 ALC」的规矩
            //   仍然成立）⇒ 它一旦卸载这个 ALC，我们挂上去的**上百个 Harmony 补丁就全成了悬空引用**，
            //   之后游戏一走到被补的方法就是
            //     `InvalidOperationException: Operation is not valid due to the current state of the object.
            //      at System.Reflection.MemberInfo.get_MetadataToken()`
            //   这类崩溃（玩家 trace 里 5 次「致命错误」前都是它）。
            //   对策：只要发现自己在**可卸载的 ALC** 里，就注册一个 `Unloading` 回调 ——
            //   在 MLM 真正卸载之前把本模组的补丁**全部撤掉**（`Harmony.UnpatchAll(ModId)`），
            //   卸载之后就只是「模组没生效」，而不是把游戏带崩。
            if (alc.IsCollectible)
            {
                Log("双版本外壳：检测到**可卸载的加载上下文**（多半是 MLM「模组启动管理器」）" +
                    "⇒ 注册卸载回调：卸载前先撤掉本模组的全部 Harmony 补丁，避免悬空补丁把游戏带崩");
                BootTrace.Mark("宿主持有可卸载 ALC（MLM？）⇒ 已注册卸载清补丁回调");
                try { alc.Unloading += OnHostUnloading; }
                catch (Exception e) { Log("双版本外壳：注册卸载回调失败: " + e.Message); }
            }

            Assembly asm;
            try { asm = alc.LoadFromAssemblyPath(path); }
            catch (Exception e)
            {
                Log("双版本外壳：LoadFromAssemblyPath 失败（改用 LoadFrom 兜底）: " + e.GetType().Name + ": " + e.Message);
                asm = Assembly.LoadFrom(path);
            }
            Log($"双版本外壳：已载入 {asm.GetName().Name} {asm.GetName().Version}");
            BootTrace.Mark($"已载入实现 {asm.GetName().Name} {asm.GetName().Version}");

            // ★★ v0.2.817（玩家 2026-11-19「联机进不去房间」实证）：
            //   实现程序集是我们自己的 ALC 载入的，游戏**不知道它属于本模组** ⇒ 多人大厅初始化时
            //   把每个模型都判成「不属于任何 mod」：
            //     [ERROR] Attempting to register type CharacterBosses.Powers.X in assembly CharacterBosses.Impl113,
            //             ... but it is not associated with any mod
            //     at Multiplayer.Serialization.ContentSorter`1.Sort
            //     at Multiplayer.Serialization.ModelIdSerializationCache.Init
            //     at Multiplayer.Transport.Steam.SteamCallResult`1.OnCallResult
            //   这段正好跑在**大厅回调里** ⇒ 建房/加入直接失败（k_EChatRoomEnterResponseError、
            //   或卡在「正在加载…」）。官方接口 AssociatAssemblyWithMod(modId, assembly) 一行就能补上。
            // ⚠ v0.2.817 试过在这里登记归属，但游戏的 ModelDb **会因此自己再注册一遍我们的模型**
            //   ⇒ DuplicateModelException（RELIC.NEOW_BLESSING 重复），启动直接致命错误。
            //   所以先关掉；正确做法见 Loader.cs 里 AssociateImplAssembly 的注释（要配合「放弃手动注册」）。
            bool associated = AssociateImplAssembly(asm);
            BootTrace.Mark("归属登记=" + associated);

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
            // ★★ v0.2.819：**二选一** ——
            //   登记归属成功 ⇒ 游戏自己会扫描并注册实现里的模型（ModelDb 把它当本模组的程序集），
            //   这时再走外壳的手动 `ModelDb.Inject + InitIds` 就会 DuplicateModelException（v0.2.817 的崩溃）；
            //   没有这个 API（老版本）⇒ 仍走外壳的手动注册。
            if (associated)
            {
                Log("双版本外壳：归属登记已生效 ⇒ 跳过外壳的手动模型注册（交给游戏自己扫）");
                BootTrace.Mark("跳过手动模型注册（交给游戏扫）");
            }
            else
            {
                RegisterModels(asm);
            }

            var init = entry.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static);
            if (init == null) { Log("双版本外壳：ModEntry 里没有静态 Initialize ⇒ 放弃"); return; }

            BootTrace.Mark("调用 ModEntry.Initialize：" + want);
            init.Invoke(null, null);
            Log($"双版本外壳：{want} 已初始化");
            BootTrace.Mark("ModEntry.Initialize 已返回：" + want);
        }
        catch (Exception e)
        {
            Log("双版本外壳：加载实现失败: " + e);   // 外壳不能抛：抛了游戏会判「模组初始化失败」
            BootTrace.Problem("加载实现失败", e);
        }
    }

    /// <summary>
    /// 宿主（MLM 之类的启动管理器）要卸载我们这个可卸载 ALC 时的收尾：
    /// **把本模组的 Harmony 补丁全部撤掉**，再让它卸载。
    ///
    /// 不动手的话，补丁里的方法引用会指向一个正在被卸载的程序集 —— 之后游戏一调到那个方法，
    /// 就是 `MemberInfo.get_MetadataToken` 那类 `InvalidOperationException`，
    /// 玩家那边看到的就是启动或进战斗时弹「致命错误」。
    /// 撤销之后最坏的结果是「本模组这一局没生效」，不会连累原版和其它模组。
    /// </summary>
    private static void OnHostUnloading(AssemblyLoadContext ctx)
    {
        try
        {
            Log($"双版本外壳：宿主正在卸载 ALC「{ctx.Name}」⇒ 先撤掉本模组的全部 Harmony 补丁");
            BootTrace.Mark("宿主卸载 ALC ⇒ 撤销全部 Harmony 补丁");
        }
        catch { /* 忽略 */ }

        try
        {
            // ⚠ 外壳**不能**在编译期引用 0Harmony（它只引用两版都有的 sts2 API，手机端也不带 Harmony）
            //   ⇒ 反射拿 `HarmonyLib.Harmony`，再反射调 `UnpatchAll(string)`。
            var harmonyType = Type.GetType("HarmonyLib.Harmony, 0Harmony")
                              ?? FindTypeInLoadedAssemblies("HarmonyLib.Harmony");
            if (harmonyType == null)
            {
                Log("双版本外壳：找不到 HarmonyLib.Harmony ⇒ 无法撤销补丁（卸载时可能留悬空补丁）");
                return;
            }
            var instance = Activator.CreateInstance(harmonyType, new object[] { ModId });
            var unpatchAll = harmonyType.GetMethod("UnpatchAll", new[] { typeof(string) });
            if (instance == null || unpatchAll == null)
            {
                Log("双版本外壳：Harmony.UnpatchAll(string) 取不到 ⇒ 无法撤销补丁");
                return;
            }
            unpatchAll.Invoke(instance, new object[] { ModId });
            Log("双版本外壳：本模组的 Harmony 补丁已全部撤销（卸载安全）");
        }
        catch (Exception e)
        {
            try { Log("双版本外壳：撤销 Harmony 补丁失败: " + e.GetType().Name + ": " + e.Message); } catch { }
        }
    }

    /// <summary>在已加载的程序集里按全名找类型（反射兜底，找不到返回 null）。</summary>
    private static Type? FindTypeInLoadedAssemblies(string fullName)
    {
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(fullName, throwOnError: false);
                    if (t != null) return t;
                }
                catch { /* 这个程序集不能扫就跳过 */ }
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    /// <summary>
    /// 把「实现程序集」登记到本模组名下 —— 走官方的 `ModManager.AssociateAssemblyWithMod(modId, assembly)`。
    /// 用反射调用：107 那份引用里未必有这个 API，缺了也只是跳过（单机不受影响）。
    /// </summary>
    private static bool AssociateImplAssembly(Assembly asm)
    {
        try
        {
            var m = typeof(ModManager).GetMethod(
                "AssociateAssemblyWithMod",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(string), typeof(Assembly) },
                modifiers: null);
            if (m == null)
            {
                Log("双版本外壳：本游戏版本没有 ModManager.AssociateAssemblyWithMod ⇒ 跳过归属登记（联机可能不正常）");
                return false;
            }
            m.Invoke(null, new object[] { ModId, asm });
            Log($"双版本外壳：已把 {asm.GetName().Name} 登记到模组 {ModId} 名下（联机序列化需要）");
            return true;
        }
        catch (Exception e)
        {
            Log("双版本外壳：登记实现程序集归属失败: " + e.GetType().Name + ": " + e.Message);
            return false;
        }
    }
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

    /// <summary>
    /// 是不是 **0.113+ 测试分支**（有「愚者」与整套塔罗牌 / 恐惧模式）。
    ///
    /// ★ v0.2.732（用户 2026-11-17）：原来用 `OneForAllPower` 当判据 —— 那是「0.107 正式版没有、
    ///   0.111 beta 有」时代的写法。官方把 v0.108–v0.111 合进主线之后 **0.111 正式版也有
    ///   `OneForAllPower`**（本更新新增的联机牌「一心化万」）⇒ 旧判据会把新正式版误判成 beta，
    ///   于是加载针对 0.113 编译的实现，进游戏就炸（类型加载期 TypeLoadException）。
    ///   现在改认**只在 0.113 存在**的类型：塔罗牌 `TheFool`（0.111 = 无 / 0.113 = 有，实测过）。
    /// </summary>
    private static bool DetectBeta()
    {
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (asm.GetName().Name != "sts2") continue;
                    if (asm.GetType("MegaCrit.Sts2.Core.Models.Cards.TheFool", false) != null) return true;
                    if (asm.GetType("MegaCrit.Sts2.Core.Models.Cards.Judgement", false) != null) return true;
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
