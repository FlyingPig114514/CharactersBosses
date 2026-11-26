// wiki-dump：从编译好的模组 DLL 里把「每个 Boss 的真实数据」导成百科用的 JSON。
//
// 为什么要这样：BossDefs.All / BossVariant / BossScript 都是运行时对象，剧本每一回合的
// Note（作者手写的说明）+ ScriptCard 的 Card/Upgraded/Burned/… 全在对象里 —— 反射读出来
// 就是游戏实际用的那一份，不会和代码脱节。
//
// 用法：
//   dotnet run --project tools/wiki-dump -c Release -- <impl.dll> <gameManagedDir> <out.json> [<projectRoot>]
// 例：
//   dotnet run --project tools/wiki-dump -c Release -- src\bin\Release-113\CharacterBosses.Impl113.dll ^
//       "D:\SteamLibrary\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64" tools\wiki\data.json

using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class WikiDump
{
    private static string[] _probeDirs = Array.Empty<string>();
    private static readonly Dictionary<string, string> Cards = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Relics = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Powers = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Monsters = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> TypeToEntry = new(StringComparer.Ordinal);
    private static readonly List<string> Missed = new();

    // 塔罗牌/衍生牌的中文名：这些牌不在游戏本体的中文表里（是塔罗 mod / 本模组自己的），手动补。
    private static readonly Dictionary<string, string> NameOverride = new(StringComparer.Ordinal)
    {
        ["TheFool"] = "愚者", ["TheMagician"] = "魔术师", ["TheHighPriestess"] = "女祭司",
        ["TheEmpress"] = "皇后", ["TheEmperor"] = "皇帝", ["TheHierophant"] = "教皇",
        ["TheLovers"] = "恋人", ["TheChariot"] = "战车", ["Strength"] = "力量",
        ["TheHermit"] = "隐者", ["WheelOfFortune"] = "命运之轮", ["Justice"] = "正义",
        ["TheHangedMan"] = "倒吊人", ["Death"] = "死神", ["Temperance"] = "节制",
        ["TheDevil"] = "恶魔", ["TheTower"] = "高塔", ["TheStar"] = "星星",
        ["TheMoon"] = "月亮", ["TheSun"] = "太阳", ["Judgement"] = "审判", ["TheWorld"] = "世界",
        ["MythicForm"] = "神话形态", ["SongBow"] = "歌曲之弓",
    };

    // 设计名与遭遇 id：主设计的 key（IRONCLAD_BOSS）**不是**遭遇 id（IRONCLAD_ANGER），
    // 变体的 key 恰好就是遭遇 id 前缀。这张表是用户 Excel 表里那 35 个名字/控制台指令的权威来源。
    private static readonly Dictionary<string, (string Name, string Encounter)> MainDesign = new(StringComparer.Ordinal)
    {
        ["IRONCLAD_BOSS|1"] = ("愤怒战", "IRONCLAD_ANGER"),
        ["IRONCLAD_BOSS|2"] = ("放血战", "IRONCLAD_BLOOD"),
        ["IRONCLAD_BOSS|3"] = ("易伤战", "IRONCLAD_VULN"),
        ["SILENT_BOSS|1"] = ("奇巧猎", "SILENT_SLY"),
        ["SILENT_BOSS|2"] = ("涂毒猎", "SILENT_ENVENOM"),
        ["SILENT_BOSS|3"] = ("刀猎", "SILENT_SHIV"),
        ["DEFECT_BOSS|1"] = ("黑球机", "DEFECT_DARK"),
        ["DEFECT_BOSS|2"] = ("物理机", "DEFECT_CLAW"),
        ["DEFECT_BOSS|3"] = ("能力机", "DEFECT_AI"),
        ["NECROBINDER_BOSS|1"] = ("灾厄骨", "NECROBINDER_DOOM"),
        ["NECROBINDER_BOSS|2"] = ("灵魂骨", "NECROBINDER_SOUL"),
        ["NECROBINDER_BOSS|3"] = ("根除骨", "NECROBINDER_ERADICATE"),
        ["REGENT_BOSS|1"] = ("碎屑储", "REGENT_DEBRIS"),
        ["REGENT_BOSS|2"] = ("铸剑储", "REGENT_FORGE"),
        ["REGENT_BOSS|3"] = ("无色储", "REGENT_COLORLESS"),
        ["FOOL_BOSS|3"] = ("塔罗愚", "FOOL_TAROT"),
        ["WATCHER_DIVINITY|3"] = ("神格观", "WATCHER_DIVINITY"),
    };

    private static readonly Dictionary<string, string> VariantName = new(StringComparer.Ordinal)
    {
        ["IRONCLAD_STRIKE"] = "打击战", ["IRONCLAD_JUGGLER"] = "杂耍战", ["IRONCLAD_BOULDER"] = "巨石战",
        ["SILENT_SNAKEBITE"] = "蛇咬猎", ["SILENT_SNECKO"] = "蛇眼猎",
        ["SILENT_BREWER"] = "炼药猎", ["SILENT_GRAND_FINALE"] = "华丽猎",
        ["DEFECT_THUNDER"] = "电球机", ["DEFECT_RAINBOW"] = "彩虹机",
        ["DEFECT_EVENT"] = "事件机", ["DEFECT_STATUS"] = "状态机",
        ["NECROBINDER_FLESH"] = "血肉骨", ["NECROBINDER_ETHEREAL"] = "虚无骨", ["NECROBINDER_OSTY"] = "养手骨",
        ["REGENT_BOMBARD"] = "轰击储", ["REGENT_KING"] = "王储", ["REGENT_MINION"] = "仆从储",
        ["REGENT_STAR"] = "辉星储", ["REGENT_CHOICE"] = "抉择储",
        ["WATCHER_FORESIGHT"] = "预见观", ["WATCHER_CREATE"] = "印牌观", ["WATCHER_RETAIN"] = "保留观",
        ["WATCHER_OMEGA"] = "欧米伽观", ["WATCHER_RUSHDOWN"] = "红蓝观",
        ["WATCHER_FLURRY"] = "疾风观",   // 一层 2 号（用户 2026-11-23）
    };
    private static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: wiki-dump <impl.dll> <gameManagedDir> <out.json> [<projectRoot>]");
            return 2;
        }

        string implPath = Path.GetFullPath(args[0]);
        string gameDir = Path.GetFullPath(args[1]);
        string outPath = Path.GetFullPath(args[2]);
        string root = args.Length > 3 ? Path.GetFullPath(args[3]) : Directory.GetCurrentDirectory();

        _probeDirs = new[] { Path.GetDirectoryName(implPath), gameDir, AppContext.BaseDirectory };
        AppDomain.CurrentDomain.AssemblyResolve += Resolve;

        // ★ 2026-11-23：**可选地把观者模组预加载进来**（第 6 个参数 = 那个 mod 的目录）。
        //   我们的观者 Boss 全是「运行期反射」取牌/遗物的（`WatcherCards.Probe()` 只扫
        //   **已加载**的程序集），不预加载时 `至纯之水`（PureWater）/ `美琅脂`（Melange）这类
        //   **观者 mod 自己的遗物**会在导出的 data.json 里整个消失 —— 旧版表里那几行就少了它们。
        //   不传这个参数时行为与以前完全一致。
        //   用法：在 4 个位置参数之后再跟一个目录（例如观者 mod 的工坊目录），不是 `--` 开头的都算候选。
        string? preloadDir = null;
        for (int i = 4; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal)) continue;
            if (Directory.Exists(args[i])) preloadDir = args[i];
        }
        if (preloadDir != null)
        {
            foreach (var dll in Directory.GetFiles(preloadDir, "*.dll"))
            {
                try
                {
                    Assembly.LoadFrom(dll);
                    Console.WriteLine("[INFO] 预加载 " + Path.GetFileName(dll));
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine("[WARN] 预加载失败 " + Path.GetFileName(dll) + ": " + e.GetBaseException().Message);
                }
            }
        }

        LoadLocalization(root);

        Assembly impl;
        try { impl = Assembly.LoadFrom(implPath); }
        catch (Exception e) { Console.Error.WriteLine("load impl failed: " + e); return 3; }

        if (args.Length > 4 && args[4] == "--probe")
        {
            try
            {
                var asm = Assembly.LoadFrom(implPath);
                foreach (var n in new[] { "MegaCrit.Sts2.Core.Models.Cards.StrikeIronclad", "MegaCrit.Sts2.Core.Models.Cards.Glacier", "MegaCrit.Sts2.Core.Models.Cards.BootSequence" })
                {
                    var ty = asm.GetType(n) ?? AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(n)).FirstOrDefault(x => x != null);
                    if (ty == null) { Console.WriteLine($"{n}: TYPE NOT FOUND"); continue; }
                    try
                    {
                        var inst = Activator.CreateInstance(ty);
                        var pc = ty.GetProperty("CanonicalEnergyCost", BindingFlags.Public | BindingFlags.Instance);
                        var px = ty.GetProperty("HasEnergyCostX", BindingFlags.Public | BindingFlags.Instance);
                        Console.WriteLine($"{ty.Name}: cost={(pc?.GetValue(inst) ?? "?")} x={(px?.GetValue(inst) ?? "?")}");
                    }
                    catch (Exception e) { Console.WriteLine($"{ty.Name}: INSTANTIATE FAIL {e.GetBaseException().GetType().Name}: {e.GetBaseException().Message}"); }
                }
                var defs = asm.GetType("CharacterBosses.Core.BossDefs");
                var probeAll = defs.GetField("All", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null);
                foreach (var kv in EnumerateDictionary(probeAll))
                {
                    if (kv.Key != "DEFECT_BOSS") continue;
                    var e = GetField(kv.Value, "Energy") as Array;
                    Console.WriteLine($"DEFECT energy array = [{string.Join(",", e.Cast<object>())}]");
                    var sba = GetField(kv.Value, "ScriptByAct") as Array;
                    var sc = ArrayAt(sba, 0);
                    foreach (var turn in AsEnumerable(Prop<object>(sc, "Turns")).Take(3))
                    {
                        Console.WriteLine($"  turn Energy={Prop<int>(turn, "Energy")} ForceZero={Prop<bool>(turn, "ForceZeroEnergy")}");
                        foreach (var c in AsEnumerable(Prop<object>(turn, "Cards")))
                        {
                            var ty = c?.GetType().GetProperty("Card")?.GetValue(c) as Type;
                            if (ty == null) continue;
                            Console.Write($"     {ty.Name}: ");
                            try
                            {
                                var inst = Activator.CreateInstance(ty);
                                foreach (var mem in ty.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                                {
                                    if (mem.Name.IndexOf("Cost", StringComparison.OrdinalIgnoreCase) < 0) continue;
                                    string kind = mem.MemberType.ToString();
                                    object val = null;
                                    try
                                    {
                                        if (mem is PropertyInfo pi && pi.GetIndexParameters().Length == 0) val = pi.GetValue(inst);
                                        else if (mem is FieldInfo fi) val = fi.GetValue(inst);
                                    }
                                    catch (Exception ex) { val = "<" + ex.GetBaseException().GetType().Name + ">"; }
                                    Console.WriteLine($"       {kind} {mem.Name} = {val}");
                                }
                            }
                            catch (Exception ex) { Console.WriteLine($"FAIL {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}"); }
                            break;
                        }
                    }
                }
            }
            catch (Exception e) { Console.WriteLine("probe failed: " + e); }
            return 0;
        }
        var bossDefs = impl.GetType("CharacterBosses.Core.BossDefs", true);

        object all;
        try { all = bossDefs.GetField("All", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null); }
        catch (Exception e)
        {
            Console.Error.WriteLine("读取 BossDefs.All 失败: " + e.GetBaseException().Message);
            return 5;
        }

        // 配置界面里那张「能力统计」表（BossInfoTable.Info，私有静态字典）
        var infoTable = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var infoType = impl.GetType("CharacterBosses.Core.BossInfoTable", false);
            var field = infoType?.GetField("Info", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (field?.GetValue(null) is IDictionary dict)
            {
                foreach (DictionaryEntry e in dict) infoTable[Convert.ToString(e.Key)] = Convert.ToString(e.Value);
            }
        }
        catch (Exception e) { Console.Error.WriteLine("BossInfoTable 读取失败（忽略）: " + e.GetBaseException().Message); }

        var bosses = new List<Dictionary<string, object>>();
        foreach (var kv in EnumerateDictionary(all))
        {
            try { bosses.Add(DumpDefinition(kv.Key, kv.Value, infoTable)); }
            catch (Exception e) { Console.Error.WriteLine($"dump {kv.Key} 失败: {e.GetBaseException().Message}"); }
        }
        bosses.Sort((a, b) => string.CompareOrdinal((string)a["key"], (string)b["key"]));

        var rootObj = new Dictionary<string, object>
        {
            ["generatedFrom"] = Path.GetFileName(implPath),
            ["generatedAt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            ["cardNameMisses"] = Missed,
            ["bosses"] = bosses,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        File.WriteAllText(outPath, JsonSerializer.Serialize(rootObj, opts), new UTF8Encoding(false));
        Console.WriteLine($"OK: {bosses.Count} 个角色定义 -> {outPath}");
        if (Missed.Count > 0) Console.WriteLine($"注意：有 {Missed.Count} 个类型没查到中文名（回退成类名），见 cardNameMisses");
        return 0;
    }

    private static Assembly Resolve(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name + ".dll";
        foreach (var dir in _probeDirs)
        {
            if (string.IsNullOrEmpty(dir)) continue;
            string p = Path.Combine(dir, name);
            if (File.Exists(p))
            {
                try { return Assembly.LoadFrom(p); } catch { /* 继续找 */ }
            }
        }
        return null;
    }

    // ── 本地化 ─────────────────────────────────────────────
    private static void LoadLocalization(string root)
    {
        // ★ 2026-11-23：**观者模组**（3747526116）的 zhs 表也读进来 —— 我们预加载它之后，
        //   牌/遗物会按真实 Type 出现（`WatcherNirvana` / `PureWater`），而它们的中文名只在
        //   观者自己的 pck 里（`tools\tmp\loc-w2\zhs_*.json` 就是那份 dump，key 是下划线 id，
        //   正好对上 `Snake("WatcherNirvana")` = `WATCHER_NIRVANA`）。不读的话会回退成类名。
        //   没预加载观者 mod 时这些 key 也用不上，纯属多读三个文件。
        string watcherLoc = Path.Combine(root, "tools", "tmp", "loc-w2");
        if (File.Exists(Path.Combine(watcherLoc, "zhs_cards.json")))
        {
            Load(Path.Combine(watcherLoc, "zhs_cards.json"), Cards);
            Load(Path.Combine(watcherLoc, "zhs_relics.json"), Relics);
            Load(Path.Combine(watcherLoc, "zhs_powers.json"), Powers);
        }

        // 先读**游戏本体**的中文表（tools\tmp\zhs_*.json 是从游戏 pck 里 dump 出来的全量表），
        // 再用本模组自己的 content\localization\zhs\* 覆盖/补充（本模组的牌、遗物、能力只在后者里）。
        string gameLoc = Path.Combine(root, "tools", "tmp");
        Load(Path.Combine(gameLoc, "zhs_cards.json"), Cards);
        Load(Path.Combine(gameLoc, "zhs_relics.json"), Relics);
        Load(Path.Combine(gameLoc, "zhs_powers.json"), Powers);
        Load(Path.Combine(gameLoc, "zhs_monsters.json"), Monsters);

        string ours = Path.Combine(root, "content", "localization", "zhs");
        Load(Path.Combine(ours, "cards.json"), Cards);
        Load(Path.Combine(ours, "relics.json"), Relics);
        Load(Path.Combine(ours, "powers.json"), Powers);
        Load(Path.Combine(ours, "monsters.json"), Monsters);
    }

    private static void Load(string path, Dictionary<string, string> dict)
    {
        if (!File.Exists(path)) { Console.Error.WriteLine("缺本地化文件: " + path); return; }
        using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        foreach (var p in doc.RootElement.EnumerateObject()) dict[p.Name] = p.Value.GetString() ?? "";
    }

    private static string Snake(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return "";
        var sb = new StringBuilder();
        for (int i = 0; i < typeName.Length; i++)
        {
            char c = typeName[i];
            // ★ 2026-11-23：类名里本来就带下划线的（观者 mod 的 `WatcherStrike_P` / `WatcherDefend_P`）
            //   以前会变成 `WATCHER_STRIKE__P`（双下划线）⇒ 对不上本地化 key `WATCHER_STRIKE_P`。
            //   紧跟在 `_` 后面的大写字母不再补分隔符。
            if (char.IsUpper(c) && i > 0 && typeName[i - 1] != '_') sb.Append('_');
            sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }

    private static string CardName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return "";
        if (NameOverride.TryGetValue(typeName, out var ov)) return ov;
        string entry = TypeToEntry.TryGetValue(typeName, out var mapped) ? mapped : Snake(typeName);
        if (Cards.TryGetValue(entry + ".title", out var name) && name.Length > 0) return name;
        if (!Missed.Contains(typeName)) Missed.Add(typeName);
        return typeName;
    }

    private static string RelicName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return "";
        if (NameOverride.TryGetValue(typeName, out var ov)) return ov;
        string entry = TypeToEntry.TryGetValue(typeName, out var mapped) ? mapped : Snake(typeName);
        if (Relics.TryGetValue(entry + ".title", out var name) && name.Length > 0) return name;
        if (!Missed.Contains(typeName)) Missed.Add(typeName);
        return typeName;
    }

    /// <summary>把 SpecialLoc 的键（如 IRONCLAD_BOSS.anger）解析成中文正文。</summary>
    private static string LocText(string key)
    {
        if (string.IsNullOrEmpty(key)) return "";
        foreach (var dict in new[] { Monsters, Powers, Cards })
        {
            if (dict.TryGetValue(key, out var v) && v.Length > 0) return v;
        }
        return key;
    }

    // ── 多人换牌 / 恐惧换牌（2026-11-26 用户：「有一些通用的多人替换没有被写到 wiki 里」）──
    //
    // 数据源有两处，全部**反射**读取（不复制一份表，避免和游戏里真正生效的那张表写叉）：
    //   · `CharacterBosses.Core.MultiplayerCardSwap` 的私有静态表
    //       `Table`（多人换牌：(幕0起, Boss 基类型, 原牌, 换上)）、
    //       `UpgradeRows`（换上去顺便升级：(幕, Boss, 原牌, 起效进阶)）、
    //       `DreadTable`（恐惧模式换牌，同上形状）；
    //   · `CharacterBosses.Core.BossKeys.All`：设计 Key → (幕, 怪物基类型)。
    //
    // 匹配条件与运行时 `MultiplayerCardSwap.Resolve` 完全一致：**幕 + 怪物基类型 + 剧本里出现了那张原牌**。
    // 所以同幕同角色的另一套设计只有它自己真的带那张牌时才会命中（例：一层亡灵里只有血肉骨的剧本有「吸引仇恨」）。
    private static List<(int Act, string Boss, string From, string To, int MinAsc, bool Dread)>? _swapRows;
    private static Dictionary<string, (int Act, string Monster)>? _designMap;

    private static void EnsureSwapData(Assembly asm)
    {
        if (_swapRows != null && _designMap != null) return;

        var swapType = FindType(asm, "CharacterBosses.Core.MultiplayerCardSwap");

        // ① 先把「换牌 + 同时升级到几进阶」收成一张表
        var minAsc = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in StaticArray(swapType, "UpgradeRows"))
        {
            if (item == null) continue;
            var t = item.GetType();
            int act = ToInt(t.GetField("Item1")?.GetValue(item));
            string boss = (t.GetField("Item2")?.GetValue(item) as Type)?.Name ?? "";
            string from = (t.GetField("Item3")?.GetValue(item) as Type)?.Name ?? "";
            int asc = ToInt(t.GetField("Item4")?.GetValue(item));
            if (boss.Length == 0 || from.Length == 0) continue;
            minAsc[act + "|" + boss + "|" + from] = asc;
        }

        // ② 两张换牌表
        var rows = new List<(int, string, string, string, int, bool)>();
        foreach (var pair in new[] { ("Table", false), ("DreadTable", true) })
        {
            foreach (var item in StaticArray(swapType, pair.Item1))
            {
                if (item == null) continue;
                var t = item.GetType();
                int act = ToInt(t.GetField("Item1")?.GetValue(item));
                string boss = (t.GetField("Item2")?.GetValue(item) as Type)?.Name ?? "";
                string from = (t.GetField("Item3")?.GetValue(item) as Type)?.Name ?? "";
                string to = (t.GetField("Item4")?.GetValue(item) as Type)?.Name ?? "";
                if (from.Length == 0 || to.Length == 0) continue;
                int asc = minAsc.TryGetValue(act + "|" + boss + "|" + from, out int a) ? a : 0;
                rows.Add((act, boss, from, to, asc, pair.Item2));
            }
        }
        _swapRows = rows;

        // ③ 设计 Key → (幕, 怪物基类型)
        var map = new Dictionary<string, (int, string)>(StringComparer.Ordinal);
        foreach (var item in StaticArray(FindType(asm, "CharacterBosses.Core.BossKeys"), "All"))
        {
            if (item == null) continue;
            var t = item.GetType();
            string k = t.GetProperty("Key")?.GetValue(item) as string ?? "";
            int act = ToInt(t.GetProperty("Act")?.GetValue(item));
            string monster = (t.GetProperty("MonsterType")?.GetValue(item) as Type)?.Name ?? "";
            if (k.Length == 0 || monster.Length == 0) continue;
            map[k] = (act, monster);
        }
        _designMap = map;
    }

    private static Type? FindType(Assembly asm, string fullName)
    {
        try
        {
            var t = asm.GetType(fullName, false);
            if (t != null) return t;
            foreach (var candidate in asm.GetTypes())
            {
                if (string.Equals(candidate.FullName, fullName, StringComparison.Ordinal)) return candidate;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    private static IEnumerable<object?> StaticArray(Type? t, string fieldName)
    {
        object? v = null;
        try { v = t?.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)?.GetValue(null); }
        catch { v = null; }
        if (v is Array arr)
        {
            foreach (var item in arr) yield return item;
        }
    }

    private static int ToInt(object? v) => v is int i ? i : 0;

    /// <summary>主设计的「设计 Key」（`REGENT_BOSS|1` → `REGENT_DEBRIS`）；查不到就原样返回。</summary>
    private static string DesignKeyOf(string key, int actNo)
    {
        return MainDesign.TryGetValue(key + "|" + actNo, out var info) ? info.Encounter : key;
    }

    /// <summary>这套设计用的怪物**基类型名**（换牌表按它匹配）；查不到就按角色名推（`Necrobinder` → `NecrobinderBoss`）。</summary>
    private static string MonsterOf(string designKey, string characterName)
    {
        if (!string.IsNullOrEmpty(designKey) && _designMap != null
            && _designMap.TryGetValue(designKey, out var info) && info.Monster.Length > 0)
        {
            return info.Monster;
        }
        return string.IsNullOrEmpty(characterName) ? "" : characterName + "Boss";
    }

    /// <summary>剧本里出现的卡**类型名**（用于和换牌表的「原牌」比对）。</summary>
    private static IEnumerable<string> ScriptCardTypes(object script)
    {
        if (script == null) yield break;    // `Prop<T>` 会直接 GetType() ⇒ 空剧本必须先挡掉
        foreach (var turn in AsEnumerable(Prop<object>(script, "Turns")))
        {
            if (turn == null) continue;
            foreach (var c in AsEnumerable(Prop<object>(turn, "Cards")))
            {
                if (c == null) continue;
                var ct = c.GetType().GetProperty("Card")?.GetValue(c) as Type;
                if (ct != null) yield return ct.Name;
            }
        }
    }

    /// <summary>某个设计在多人 / 恐惧模式下会换掉哪些牌（形如 `吸引仇恨 → 骸骨军团`）。</summary>
    private static List<string> SwapsFor(string designKey, int actIndex, string characterName, IEnumerable<string> cardTypes, bool dread)
    {
        var result = new List<string>();
        if (_swapRows == null || _designMap == null) return result;

        string monster = MonsterOf(designKey, characterName);
        if (monster.Length == 0) return result;
        var set = new HashSet<string>(cardTypes, StringComparer.Ordinal);

        foreach (var row in _swapRows)
        {
            if (row.Item6 != dread) continue;
            if (row.Item1 != actIndex) continue;
            if (!string.Equals(row.Item2, monster, StringComparison.Ordinal)) continue;
            if (!set.Contains(row.Item3)) continue;

            string line = CardName(row.Item3) + " → " + CardName(row.Item4);
            if (row.Item6) line += "（恐惧模式）";
            if (row.Item5 > 0) line += "（进阶 " + row.Item5 + " 起升级）";
            if (!result.Contains(line)) result.Add(line);
        }
        return result;
    }

    // ── 定义 ───────────────────────────────────────────────
    private static Dictionary<string, object> DumpDefinition(string key, object def, Dictionary<string, string> infoTable)
    {
        var hp = GetField(def, "Hp") as Array;
        var energy = GetField(def, "Energy") as Array;
        var handSize = GetField(def, "HandSize") as Array;
        var deckByAct = GetField(def, "DeckByAct") as Array;
        var relicsByAct = GetField(def, "RelicsByAct") as Array;
        var scriptByAct = GetField(def, "ScriptByAct") as Array;
        var specialByAct = GetField(def, "SpecialLocByAct") as Array;
        var specialDreadByAct = GetField(def, "SpecialLocDreadByAct") as Array;
        var dreadUpByAct = GetField(def, "DreadUpgradesByAct") as Array;
        var potionsByAct = GetField(def, "PotionsByAct") as Array;

        int acts = hp?.Length ?? 3;
        var actsOut = new List<object>();

        for (int act = 0; act < acts; act++)
        {
            // 先算出代码登记的卡组：剧本没标洗牌时要用「卡组张数 ÷ 每回合抽牌」推算首次洗牌回合
            var deck = TypeNames(ArrayAt(deckByAct, act));
            // ★ v0.2.939：塔罗愚的遗物登记在 `RelicsByAct[2]`（不是 `Relics`）⇒ 两处都要看
            bool hasIceCream = false;
            foreach (var rn in TypeNames(Prop<object>(def, "Relics"))) if (rn.IndexOf("IceCream", StringComparison.Ordinal) >= 0) hasIceCream = true;
            foreach (var rn in TypeNames(ArrayAt(relicsByAct, act))) if (rn.IndexOf("IceCream", StringComparison.Ordinal) >= 0) hasIceCream = true;
            var script = DumpScript(ArrayAt(scriptByAct, act), IntAt(energy, act), IntAt(handSize, act), deck.Count, hasIceCream);

            // 2026-11-26：多人 / 恐惧换牌（数据源 = MultiplayerCardSwap 的两张私有表）
            EnsureSwapData(def.GetType().Assembly);
            string designKey = DesignKeyOf(key, act + 1);
            string characterTypeName = (GetField(def, "CharacterType") as Type)?.Name ?? "";
            var swapTypes = new List<string>(TypeNames(ArrayAt(deckByAct, act)));
            swapTypes.AddRange(ScriptCardTypes(ArrayAt(scriptByAct, act)));

            var variants = new List<object>();
            foreach (var v in CallEnumerable(def, "VariantsForAct", act))
            {
                if (v == null) continue;
                var dv = DumpVariant(v, act, IntAt(energy, act), IntAt(handSize, act));
                if (dv["script"] != null) variants.Add(dv);
            }

            if (script == null && deck.Count == 0 && variants.Count == 0) continue;

            actsOut.Add(new Dictionary<string, object>
            {
                ["act"] = act + 1,
                ["name"] = DesignName(key, act + 1),
                ["encounter"] = DesignEncounter(key, act + 1),
                ["baseHp"] = IntAt(hp, act),
                ["energy"] = IntAt(energy, act),
                ["handSize"] = IntAt(handSize, act),
                ["specialKey"] = StrAt(specialByAct, act),
                ["special"] = LocText(StrAt(specialByAct, act)),
                ["specialDreadKey"] = StrAt(specialDreadByAct, act),
                ["specialDread"] = LocText(StrAt(specialDreadByAct, act)),
                ["deck"] = DeckFromScriptOrRegistered(script, deck),
                ["deckRegistered"] = deck.Select(CardName).Where(s => s.Length > 0).ToList(),
                ["deckNote"] = DeckNote(script),
                ["relics"] = TypeNames(ArrayAt(relicsByAct, act)).Select(RelicName).ToList(),
                ["potions"] = TypeNames(ArrayAt(potionsByAct, act)).Select(RelicName).ToList(),
                ["dreadUpgrades"] = TypeNames(ArrayAt(dreadUpByAct, act)).Select(CardName).ToList(),
                // ★ v0.2.874（用户 2026-11-22：「github 的 wiki 上好像没有写进阶增强」）：
                //   以前**只**导出了变体（`BossVariant`）的进阶表 ⇒ 主设计（黑球机 / 愤怒战 / 灾厄骨 /
                //   碎屑储 …）的进阶升级与额外遗物在 wiki 上一个字都没有。这里补上按幕的两张表。
                ["ascensionUpgrades"] = DumpAscensionForAct(def, act, relic: false),
                ["ascensionRelics"] = DumpAscensionForAct(def, act, relic: true),
                // v0.2.875：主设计（如神格观）的**纯展示用**进阶名单
                ["ascensionDisplay"] = StrList(GetField(def, "AscensionDisplay")),
                // 2026-11-26：多人 / 恐惧模式下的换牌（用户：「通用的多人替换没写进 wiki」）
                ["multiplayerSwaps"] = SwapsFor(designKey, act, characterTypeName, swapTypes, dread: false),
                ["dreadSwaps"] = SwapsFor(designKey, act, characterTypeName, swapTypes, dread: true),
                // v0.2.883：主设计的能力面板（key 是设计 key，如 IRONCLAD_ANGER ⇒ 由遭遇 id 去掉后缀得到）
                ["info"] = InfoOf(infoTable, DesignEncounter(key, act + 1), null, def?.GetType().Assembly),
                ["script"] = script,
                ["variants"] = variants,
            });
        }

        return new Dictionary<string, object>
        {
            ["key"] = key,
            ["character"] = CharacterName(GetField(def, "CharacterType") as Type, key),
            ["info"] = InfoOf(infoTable, key, null, def?.GetType().Assembly),
            ["acts"] = actsOut,
        };
    }

    /// <summary>
    /// 卡组 = **首次洗牌之前的回合**里出现过的牌（用户 2026-11-20 口径：剧本跨了至少两次洗牌，
    /// 直接统计「出现过的所有牌」会把大部分牌重复计入）。剧本里没牌时退回代码登记的卡组。
    /// </summary>
    private static List<string> DeckFromScriptOrRegistered(Dictionary<string, object> script, object registered)
    {
        var fromScript = new List<string>();
        if (script != null && script.TryGetValue("deckFromScript", out var ds) && ds is List<string> list)
        {
            fromScript.AddRange(list);
        }
        if (fromScript.Count > 0) return fromScript;
        return TypeNames(registered).Select(CardName).Where(s => s.Length > 0).ToList();
    }

    /// <summary>卡组是怎么数出来的（写在表里让人知道口径）。</summary>
    private static string DeckNote(Dictionary<string, object> script)
    {
        if (script == null) return "代码登记卡组（该设计没有剧本）";
        int shuffleTurn = script.TryGetValue("shuffleTurn", out var st) && st is int n ? n : 0;
        bool guessed = script.TryGetValue("shuffleGuessed", out var g) && g is bool gb && gb;
        bool fromScript = script.TryGetValue("deckFromScript", out var ds) && ds is List<string> l && l.Count > 0;
        if (!fromScript) return "代码登记卡组（首次洗牌前剧本里没有出牌，例如塔罗牌都是生成的）";
        if (shuffleTurn > 0 && guessed) return "按首次洗牌之前的回合统计（剧本没标洗牌，按「卡组÷每回合抽牌」推算首次洗牌在第 " + shuffleTurn + " 回合）";
        if (shuffleTurn > 0) return "按首次洗牌之前的回合统计（剧本标注：首次洗牌在第 " + shuffleTurn + " 回合）";
        return "按全部回合统计（剧本既没标洗牌、也没法推算）";
    }
    private static Dictionary<string, object> DumpVariant(object v, int actIndex, int actEnergy, int actHandSize)
    {
        bool hasIceCream = false;
        foreach (var rn in TypeNames(Prop<object>(v, "Relics"))) if (rn.IndexOf("IceCream", StringComparison.Ordinal) >= 0) hasIceCream = true;
        var script = DumpScript(Prop<object>(v, "Script"), actEnergy, actHandSize, TypeNames(Prop<object>(v, "Deck")).Count, hasIceCream);

        // 2026-11-26：多人 / 恐惧换牌（变体的 Key 就是设计 Key ⇒ 幕与怪物基类型查 BossKeys）
        string vKey = Prop<string>(v, "Key") ?? "";
        EnsureSwapData(v.GetType().Assembly);
        var swapTypes = new List<string>(TypeNames(Prop<object>(v, "Deck")));
        swapTypes.AddRange(ScriptCardTypes(Prop<object>(v, "Script")));

        return new Dictionary<string, object>
        {
            ["key"] = Prop<string>(v, "Key"),
            ["name"] = VariantName.TryGetValue(Prop<string>(v, "Key") ?? "", out var vn) ? vn : (Prop<string>(v, "Key") ?? ""),
            ["encounter"] = Prop<string>(v, "Key") ?? "",
            ["specialKey"] = Prop<string>(v, "SpecialLoc"),
            ["special"] = LocText(Prop<string>(v, "SpecialLoc")),
            ["specialDreadKey"] = Prop<string>(v, "SpecialLocDread"),
            ["specialDread"] = LocText(Prop<string>(v, "SpecialLocDread")),
            ["hpBonus"] = Prop<int>(v, "HpBonus"),
            ["multiplayerSwaps"] = SwapsFor(vKey, actIndex, "", swapTypes, dread: false),
            ["dreadSwaps"] = SwapsFor(vKey, actIndex, "", swapTypes, dread: true),
            ["deck"] = DeckFromScriptOrRegistered(script, Prop<object>(v, "Deck")),
            ["deckRegistered"] = TypeNames(Prop<object>(v, "Deck")).Select(CardName).ToList(),
            ["deckNote"] = DeckNote(script),
            ["relics"] = TypeNames(Prop<object>(v, "Relics")).Select(RelicName).ToList(),
            ["ascensionUpgrades"] = DumpPairs(Prop<object>(v, "AscensionUpgrades")).Select(p => $"{p["ascension"]}: {CardName((string)p["type"])}").ToList(),
            ["ascensionRelics"] = DumpPairs(Prop<object>(v, "AscensionRelics")).Select(p => $"{p["ascension"]}: {RelicName((string)p["type"])}").ToList(),
            // v0.2.875：纯展示用进阶名单（观者那几只卡类型是 null ⇒ 名字读不出来，改用作者写好的中文名）
            ["ascensionDisplay"] = StrList(Prop<object>(v, "AscensionDisplay")),
            // v0.2.883：变体自己的能力面板（key = 设计 key，如 SILENT_BREWER）
            ["info"] = InfoOf(null, (string?)Prop<string>(v, "Key") ?? "", null, v.GetType().Assembly),            ["dreadUpgrades"] = TypeNames(Prop<object>(v, "DreadUpgrades")).Select(CardName).ToList(),
            ["script"] = script,
        };
    }

    private static Dictionary<string, object> DumpScript(object script, int actEnergy, int handSize, int registeredCount, bool iceCream = false)
    {
        if (script == null) return null;
        var turns = new List<object>();
        int shuffleTurn = 0;      // 首次洗牌发生在第几回合（0 = 剧本里没标）
        var subroutine = new bool[1];   // 整份剧本共享：「子程序」是否已经打出来过
        int carry = 0;            // 上一回合结转到这一回合的能量（冰激凌 / 星星）
        foreach (var turn in AsEnumerable(Prop<object>(script, "Turns")))
        {
            if (turn == null) continue;

            var cards = new List<object>();
            foreach (var c in AsEnumerable(Prop<object>(turn, "Cards")))
            {
                if (c == null) continue;
                // ★ 2026-11-26：单张牌导出失败**不要**把整回合的卡列清空（以前会静默变成空表，
                //   于是那一回合退化成「从 Note 文字反解」⇒ wiki 里出现「失去 3 集中）」这种假牌名）。
                try { cards.Add(DumpCard(c)); }
                catch (Exception cardEx)
                {
                    Console.Error.WriteLine("[WARN] 导出卡牌失败（已跳过这张）: "
                        + (c.GetType().GetProperty("Card")?.GetValue(c) as Type)?.Name
                        + " : " + cardEx.GetBaseException().Message);
                }
            }
            // 观者那几套剧本的牌是用运行期反射拿的 Type（没装观者模组/离线都是 null）⇒ Cards 是空的，
            // 只能从作者手写的 Note 里反解出来（格式固定：`1. 许愿+（永生不死）、打击（有费就打）、…`）。
            {
                string noteForParse = Prop<string>(turn, "Note") ?? "";
                if (noteForParse.Length > 0)
                {
                    var fromNote = new List<object>();
                    foreach (var nc in ParseNoteCards(noteForParse)) fromNote.Add(nc);
                    // 观者那几套的牌只有一部分能离线解析出来（其余是运行期反射的 Type）⇒
                    // Note 里列得更全时以 Note 为准。
                    // ⚠ 2026-11-26：以前是「Note 里列的比真卡多就以 Note 为准」⇒ 只要备注里多写一个词
                    //   （例如物理机第 5 回合「超能光束（24 群攻、失去 3 集中）」被按「、」切出假牌名），
                    //   真卡就被整段顶掉。现在只在**真卡一张都解析不出来**时才用 Note 兜底
                    //   （观者模组没预加载时才是这种情况）。
                    if (cards.Count == 0 && fromNote.Count > 0)
                    {
                        cards.Clear();
                        cards.AddRange(fromNote);
                    }
                }
            }

            // 这一回合到底有多少能量：剧本里逐回合写死的（BossTurn.Energy）优先，0 = 用该幕默认；
            // ForceZeroEnergy = 那一回合故意 0 费（只打得起 0 费牌）。
            int turnEnergy = Prop<int>(turn, "Energy");
            bool forceZero = Prop<bool>(turn, "ForceZeroEnergy");
            int energy = forceZero ? 0 : (turnEnergy > 0 ? turnEnergy : actEnergy);
            int carriedIn = carry;
            energy += carriedIn;

            // 按「队列顺序 + 费用」走一遍，标出每张牌到底打不打得出
            carry = 0;
            AnnotatePlayed(cards, energy, subroutine, ref carry, iceCream, iceCream && turns.Count > 0);

            string noteText = Prop<string>(turn, "Note") ?? "";
            bool shuffles = Prop<bool>(turn, "ShuffleDrawPile")
                            || noteText.IndexOf("洗牌", StringComparison.Ordinal) >= 0
                            || noteText.IndexOf("重新洗", StringComparison.Ordinal) >= 0;

            turns.Add(new Dictionary<string, object>
            {
                ["note"] = noteText,
                ["energy"] = energy,
                ["shuffle"] = shuffles,
                ["cards"] = cards,
            });

            if (shuffles && shuffleTurn == 0) shuffleTurn = turns.Count;   // 1 起
        }

        // 剧本里没标洗牌时自己推算：每回合抽 handSize 张 ⇒ 大约第（卡组张数 ÷ 每回合抽牌 + 1）回合开始洗牌。
        // 例：黑球机 21 张 ÷ 每回合 5 张 ⇒ 第 5 回合洗牌 ⇒ 只统计前 4 回合（用户 2026-11-20 口径）；
        //     塔罗愚 登记卡组只有 3 张、每回合抽 6 张 ⇒ 第 1 回合就洗 ⇒ 洗牌前没有牌 ⇒ 退回代码登记卡组。
        bool guessed = false;
        if (shuffleTurn == 0 && registeredCount > 0 && handSize > 0)
        {
            int guess = registeredCount / handSize + 1;
            if (guess < turns.Count)
            {
                shuffleTurn = guess;
                guessed = true;
            }
        }

        // 卡组：**首次洗牌之前的回合**里出现过的牌（用户 2026-11-20 口径）。
        // 剧本跨了至少两次洗牌，直接统计「所有出现过的牌」会把大部分牌重复计入 ⇒ 只取首次洗牌之前。
        var deck = new List<string>();
        int upto = shuffleTurn > 0 ? shuffleTurn - 1 : turns.Count;
        for (int ti = 0; ti < upto && ti < turns.Count; ti++)
        {
            var turnDict = turns[ti] as Dictionary<string, object>;
            if (turnDict == null) continue;
            foreach (var co in AsEnumerable(turnDict["cards"]))
            {
                var cd = co as Dictionary<string, object>;
                if (cd == null) continue;
                if (cd.ContainsKey("DisplayOnly")) continue;          // 只显示意图、不是真牌
                if (cd.ContainsKey("from")) continue;                 // 不是从卡组来的（生成/回收）
                string nm = cd["name"] as string;
                if (string.IsNullOrEmpty(nm)) continue;
                deck.Add(DeckLabel(cd, nm));
            }
        }
        if (turns.Count == 0) return null;
        return new Dictionary<string, object>
        {
            ["loopFrom"] = Prop<int>(script, "LoopFrom"),
            ["loopLength"] = Prop<int>(script, "LoopLength"),
            // 2026-11-26：循环段标签偏移（轰击储 = 1 ⇒ 循环第一回合写成 x+1，因为它的 x 指「最后一次被击晕的回合」）
            ["loopLabelOffset"] = Prop<int>(script, "LoopLabelOffset"),
            ["shuffleTurn"] = shuffleTurn,
            ["shuffleGuessed"] = guessed,
            ["deckFromScript"] = deck,
            ["turns"] = turns,
        };
    }

    private static Dictionary<string, object> DumpCard(object c)
    {
        var t = c.GetType();
        string typeName = ShortName(t.GetProperty("Card")?.GetValue(c) as Type);
        var card = new Dictionary<string, object>
        {
            ["name"] = CardName(typeName),
            ["type"] = typeName,
        };
        {
            var cardType = t.GetProperty("Card")?.GetValue(c) as Type;
            // ★ 2026-11-26（用户：「放血战第一回合的意图有误，全身撞击+ 是 0 费可以打出」）：
            //   这里以前**恒按未升级**取费用 ⇒ 升级后降费的牌（全身撞击 1→0、热修复 2→0…）被算成没费。
            //   现在按这张 ScriptCard 自己的 `Upgraded` 取。
            bool upCost = t.GetProperty("Upgraded")?.GetValue(c) is bool ub && ub;
            var (baseCost, xCost) = CardCost(cardType, upCost);
            card["cost"] = baseCost;
            if (xCost) card["xCost"] = true;
            try
            {
                var inst = Activator.CreateInstance(cardType);
                var mType = cardType.GetMethod("get_Type", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var vType = mType?.Invoke(inst, null);
                if (vType != null) card["cardType"] = vType.ToString();
            }
            catch { /* 取不到就算了 */ }
        }
        foreach (var flag in new[]
        {
            "Upgraded", "Burned", "DiscardInstead", "DiscardInsteadWhenMultiplayer", "DiscardInsteadSingleOnly",
            "Sly", "Retain", "HoldInstead", "DisplayOnly", "NotDrawn", "AutoPlayed", "FreeThisTurn",
            "ToolsDiscard", "HellraiserFree", "EnchantSpiral", "EnchantSouls", "EnchantRoyallyApproved",
            "ShowsDrillGain", "SpendsAllEnergy", "ForceDebuffIntent", "JugglingCopy", "StampedePlay",
            "EmptyDrawPileFirst", "ScriedFromDraw", "WouldPlayIfEnergy",
        })
        {
            var p = t.GetProperty(flag);
            if (p == null || p.PropertyType != typeof(bool)) continue;
            if ((bool)p.GetValue(c)) card[flag] = true;
        }
        foreach (var num in new[] { "CostOverride", "CostThisTurn", "DamageOverride", "BlockOverride", "ExtraAoe", "UpgradeMinAscension" })
        {
            var p = t.GetProperty(num);
            if (p == null) continue;
            if (p.GetValue(c) is int i && i >= 0) card[num] = i;
        }
        var reasonOvr = t.GetProperty("ReasonOverride")?.GetValue(c) as string;
        if (!string.IsNullOrEmpty(reasonOvr)) card["ReasonOverride"] = reasonOvr;
        var from = t.GetProperty("From")?.GetValue(c) as string;
        if (!string.IsNullOrEmpty(from) && from != "deck") card["from"] = from;

        foreach (var arrName in new[] { "ExhaustWith", "ReturnsToHand", "PrintsToDrawPile" })
        {
            var names = TypeNames(t.GetProperty(arrName)?.GetValue(c)).Select(CardName).Where(s => s.Length > 0).ToList();
            if (names.Count > 0) card[arrName] = names;
        }
        return card;
    }

    /// <summary>
    /// 按「剧本队列顺序 + 每张牌实际费用」判定这一回合到底哪些牌打得出来。
    ///
    /// 剧本给的是**排队顺序**（和游戏里一样：从前往后打，没费就停），所以：
    ///   · 0 费牌 / 自动打出（奇巧·被丢弃触发·地狱狂徒免费）不消耗能量，直接算「打出」；
    ///   · X 费牌（消耗全部能量）算出「打出」并把能量清零；
    ///   · 其余按费用扣，扣不下就归「不出（没费）」，后面的牌也一律「不出（没费）」；
    ///   · 带 Burned/HoldInstead/NotDrawn/DiscardInstead/DisplayOnly 标记的牌本来就不打，也不消耗能量。
    /// </summary>
    /// <summary>卡组里一张牌怎么写：名字 + 升级「+」 + 附魔（漩涡 / 灵魂 / 王室认证）。</summary>
    private static string DeckLabel(Dictionary<string, object> card, string name)
    {
        string label = name;
        if (card.ContainsKey("Upgraded")) label += "+";
        if (card.ContainsKey("EnchantSpiral")) label += "（漩涡）";
        if (card.ContainsKey("EnchantSouls")) label += "（灵魂）";
        if (card.ContainsKey("EnchantRoyallyApproved")) label += "（王室认证）";
        return label;
    }

    /// <summary>
    /// 从作者手写的 Note 反解牌表（只在 `Cards` 为空时用 —— 观者那几套的牌是运行期反射拿的 Type）。
    /// 格式：`1. 许愿+（永生不死）、打击（有费就打）、警惕（有费就打）`
    /// </summary>
    private static IEnumerable<Dictionary<string, object>> ParseNoteCards(string note)
    {
        string text = Regex.Replace(note ?? "", @"^\d+\.\s*", "").Trim();
        if (text.Length == 0) yield break;
        // 备注里常带「单人：…；多人：…」两套口径（表里以单人为准，多人另写备注）⇒ 只解析单人那段
        int multi = text.IndexOf("多人：", StringComparison.Ordinal);
        if (multi > 0) text = text.Substring(0, multi);
        text = Regex.Replace(text, "^(单人|多人)：", "").Trim();
        foreach (var raw in text.Split('、'))
        {
            string chunk = raw.Trim().TrimEnd('。', '，', ',', '.');
            if (chunk.Length == 0) continue;
            if (chunk.IndexOf("洗牌", StringComparison.Ordinal) >= 0) continue;   // 洗牌是标记不是牌
            int paren = chunk.IndexOf('（');
            if (paren < 0) paren = chunk.IndexOf('(');
            string namePart = paren > 0 ? chunk.Substring(0, paren) : chunk;
            string parenPart = paren > 0 ? chunk.Substring(paren) : "";
            bool upgraded = namePart.EndsWith("+", StringComparison.Ordinal);
            if (upgraded) namePart = namePart.Substring(0, namePart.Length - 1);
            namePart = namePart.Trim();
            if (namePart.Length == 0) continue;

            var card = new Dictionary<string, object>
            {
                ["name"] = namePart,
                ["type"] = "",
                ["cost"] = 0,
                ["fromNote"] = true,
            };
            if (upgraded) card["Upgraded"] = true;
            if (parenPart.IndexOf("没费", StringComparison.Ordinal) >= 0)
            {
                card["played"] = false;
                card["reason"] = "没费";
            }
            else if (parenPart.IndexOf("不出", StringComparison.Ordinal) >= 0
                     || parenPart.IndexOf("保留", StringComparison.Ordinal) >= 0)
            {
                card["played"] = false;
                card["reason"] = "留在手牌";
            }
            yield return card;
        }
    }
    private static void AnnotatePlayed(List<object> cards, int energy, bool[] subroutine,
                                       ref int carry, bool iceCream, bool ignoreEnergy = false)
    {
        int remaining = Math.Max(0, energy);
        int nextTurn = 0;                 // 「星星」这类是**下回合**给能量
        bool broke = false;
        foreach (var o in cards)
        {
            var card = o as Dictionary<string, object>;
            if (card == null) continue;

            if (card.ContainsKey("fromNote")) continue;   // Note 反解出来的牌：费用未知，按 Note 的标注为准

            // ★ 2026-11-26：剧本可以用 `ReasonOverride` 直接写死这一列要显示的原因（优先级最高）。
            if (card.TryGetValue("ReasonOverride", out var ovr) && ovr is string ovrText && ovrText.Length > 0)
            {
                card["played"] = false;
                card["reason"] = ovrText;
                continue;
            }

            string notPlayedBy = null;
            if (card.ContainsKey("Burned")) notPlayedBy = "被烧掉";
            else if (card.ContainsKey("HoldInstead")) notPlayedBy = "留在手牌";
            else if (card.ContainsKey("NotDrawn")) notPlayedBy = "没抽到";
            else if (card.ContainsKey("DiscardInstead") || card.ContainsKey("DiscardInsteadWhenMultiplayer") || card.ContainsKey("DiscardInsteadSingleOnly")) notPlayedBy = "被丢弃";
            else if (card.ContainsKey("DisplayOnly")) notPlayedBy = "仅显示意图";

            if (notPlayedBy != null)
            {
                card["played"] = false;
                card["reason"] = notPlayedBy;
                continue;
            }

            // 自动打出 / 免费 / 奇巧（被丢弃后自动打出）：不消耗能量
            // ★ 2026-11-26（用户：「杂耍战的意图有误，没有把杂耍会打出的牌放到打出那里」）：
            //   `StampedePlay`（惊逃在回合结束替她打出去的那张）也算「本回合会打出」，不吃她的能量。
            bool free = card.ContainsKey("AutoPlayed") || card.ContainsKey("FreeThisTurn")
                        || card.ContainsKey("Sly") || card.ContainsKey("HellraiserFree")
                        || card.ContainsKey("ToolsDiscard") || card.ContainsKey("StampedePlay");
            if (free)
            {
                card["played"] = true;
                card["freePlay"] = true;
                continue;
            }

            bool xCost = card.ContainsKey("xCost");
            int cost = card.ContainsKey("CostThisTurn") ? Convert.ToInt32(card["CostThisTurn"])
                     : card.ContainsKey("CostOverride") ? Convert.ToInt32(card["CostOverride"])
                     : Convert.ToInt32(card["cost"] ?? 0);

            // 费用 -1 = 诅咒/状态/不可打出（笨拙、愧疚、贪婪、疑虑…）⇒ 这一手根本打不出来
            if (cost < 0)
            {
                card["played"] = false;
                card["reason"] = "打不出·诅咒或状态";
                continue;
            }

            // ★ v0.2.939：以前这里一遇「没费」就 `broke = true` ⇒ **后面所有牌（哪怕是 0 费）全被标成没费**，
            //   这就是用户说的「大量卡牌被错误标注成没费」。现在逐张判定，0 费牌照常算打得出。
            //   `ignoreEnergy`（塔罗愚专用，见下）为真时干脆不判费用 —— 她的能量来自塔罗牌自己 + 冰激凌结转，
            //   静态账算不准，用户口径是「除第 1 回合后两张神话形态，其余都打得出」。
            if (!ignoreEnergy && cost > remaining)
            {
                card["played"] = false;
                // ★ 2026-11-26（用户：「灵魂骨除了第一回合外，所有不出的牌的备注都不应该是没费…
                //   而应该是（有费就打）」）：剧本可以标 `WouldPlayIfEnergy` ⇒ 这一列写「有费就打」。
                card["reason"] = card.ContainsKey("WouldPlayIfEnergy") ? "有费就打" : "没费";
                continue;
            }

            card["played"] = true;
            if (xCost) remaining = 0;
            else remaining -= Math.Max(0, cost);

            // ★ 2026-11-23：「奇迹」打出去**回 1 点能量**（反编译 `WatcherMiracle.OnPlay`
            //   就是 `PlayerCmd.GainEnergy(1, Owner)`）⇒ 记账时要还回来，否则它后面那张牌
            //   会被错标成「没费」（疾风观第 3 回合的「心灵堡垒」就是这么被标错的，实测她打得出）。
            if (string.Equals(card["name"] as string, "奇迹", StringComparison.Ordinal)) remaining += 1;

            // ★ v0.2.939（用户 2026-11-25：「wiki 中塔罗愚的意图有误，大量卡牌被错误标注成没费」）：
            //   塔罗牌里有三张自己给能量，账要记上（否则后面所有牌都会被算成「没费」）：
            //   · 皇帝 `TheEmperor` 打出时立刻 +3（升级 +4）；
            //   · 太阳 `TheSun`   打出时立刻 +2（升级 +3）；
            //   · 星星 `TheStar`  给的是**下回合** +3（升级 +4）⇒ 记进 nextTurn。
            bool up = card.ContainsKey("Upgraded");
            string nm2 = card["name"] as string ?? "";
            if (string.Equals(nm2, "皇帝", StringComparison.Ordinal)) remaining += up ? 4 : 3;
            else if (string.Equals(nm2, "太阳", StringComparison.Ordinal)) remaining += up ? 3 : 2;
            else if (string.Equals(nm2, "星星", StringComparison.Ordinal)) nextTurn += up ? 4 : 3;

            // 「子程序」：每打出一张**能力牌** +1 能量（下一张牌用得上）——能力机那套的能量就靠它
            string ct = card.ContainsKey("cardType") ? Convert.ToString(card["cardType"]) : null;
            if (subroutine != null && subroutine.Length > 0 && subroutine[0] && ct == "Power") remaining += 1;
            if (string.Equals(card["name"] as string, "子程序", StringComparison.Ordinal) && subroutine != null) subroutine[0] = true;
        }

        // 收尾：冰激凌（`IceCream`）把没花完的能量留到下一回合；「星星」的下回合能量也一起带上。
        carry = nextTurn + (iceCream ? Math.Max(0, remaining) : 0);
    }
    /// <summary>
    /// 某只 Boss 在**某一幕**的进阶增强（升级牌 / 额外遗物），格式与变体那边一致：`"8: 缓冲"`。
    ///
    /// 两张表都要读：`AscensionUpgrades`（对所有幕生效）+ `AscensionUpgradesByAct`（只对指定幕生效；
    /// 元组的 Item2 是**幕号**而不是类型，所以不能直接丢给 `DumpPairs`）。
    /// </summary>
    private static List<string> DumpAscensionForAct(object def, int act, bool relic)
    {
        var outl = new List<string>();
        string plainField = relic ? "AscensionRelics" : "AscensionUpgrades";
        string byActField = relic ? "AscensionRelicsByAct" : "AscensionUpgradesByAct";

        foreach (var item in AsEnumerable(GetField(def, plainField)))
        {
            if (item == null) continue;
            var t = item.GetType();
            int minAsc = t.GetField("Item1")?.GetValue(item) is int i ? i : 0;
            var type = t.GetField("Item2")?.GetValue(item) as Type;
            if (type == null) continue;
            outl.Add(minAsc + ": " + (relic ? RelicName(ShortName(type)) : CardName(ShortName(type))));
        }

        foreach (var item in AsEnumerable(GetField(def, byActField)))
        {
            if (item == null) continue;
            var t = item.GetType();
            int minAsc = t.GetField("Item1")?.GetValue(item) is int i ? i : 0;
            int itemAct = t.GetField("Item2")?.GetValue(item) is int a ? a : -1;
            // ⚠ v0.2.874 修正：`byAct` 是 **1 起**幕号（0 = 所有幕），
            //   与模组自己的读法逐字一致（`CharBossMonster.AscensionUpgradeTypes`：`int act = ActIndex + 1;`
            //   与 `ShadowPlayerFactory`：`if (byAct != 0 && byAct != act + 1) continue;`）。
            //   第一版按 0 起比 ⇒ 每条都**错落到下一幕**、第一幕的条目整条丢掉（用户：「肯定就是你漏了」）。
            if (itemAct != 0 && itemAct != act + 1) continue;
            var type = t.GetField("Item3")?.GetValue(item) as Type;
            if (type == null) continue;
            outl.Add(minAsc + ": " + (relic ? RelicName(ShortName(type)) : CardName(ShortName(type))));
        }

        outl.Sort(StringComparer.Ordinal);
        return outl;
    }

    private static List<string> StrList(object list)
    {
        var o = new List<string>();
        foreach (var item in AsEnumerable(list))
        {
            var s = item?.ToString() ?? "";
            if (s.Length > 0) o.Add(s);
        }
        return o;
    }

    /// <summary>能力面板：**直接调运行期 API `BossInfoTable.TextOf(key)`**（设置界面就是调它），
    /// 拿不到才退回读静态字典 —— 只读字段会漏掉运行期合并进来的那些。</summary>
    private static bool _infoProbeDone;
    private static string InfoOf(System.Collections.IDictionary? table, string? designKey, string? encounterId, System.Reflection.Assembly? asm)
    {
        try
        {
            if (!_infoProbeDone)
            {
                _infoProbeDone = true;
                var ks = new List<string>();
                if (table != null) foreach (var kk in table.Keys) { ks.Add(kk?.ToString() ?? "?"); if (ks.Count >= 80) break; }
                Console.WriteLine("[INFO] table=" + (table == null ? "null" : table.Count.ToString()) +
                                  " keys=[" + string.Join(" | ", ks) + "]");
                Console.WriteLine("[INFO] 第一只设计 key=" + designKey);
            }
        }
        catch (Exception ex) { Console.WriteLine("[INFO] probe failed: " + ex.Message); }
        var candidates = new List<string?>();
        candidates.Add(designKey);
        candidates.Add(encounterId);
        if (!string.IsNullOrEmpty(encounterId)) candidates.Add(encounterId.Replace("_BOSS_ENCOUNTER", ""));

        System.Reflection.MethodInfo? textOf = null;
        try
        {
            foreach (var a in new[] { asm }.Concat(AppDomain.CurrentDomain.GetAssemblies()))
            {
                var tp = a?.GetType("CharacterBosses.Core.BossInfoTable");
                if (tp == null) continue;
                textOf = tp.GetMethod("TextOf", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                break;
            }
        }
        catch { /* 忽略 */ }

        foreach (var k in candidates)
        {
            if (string.IsNullOrEmpty(k)) continue;
            try
            {
                if (textOf != null && textOf.Invoke(null, new object?[] { k }) is string s && s.Length > 0) return s;
            }
            catch { /* 继续 */ }
            try
            {
                if (table != null && table.Contains(k) && table[k] != null)
                {
                    var v = table[k]?.ToString();
                    if (!string.IsNullOrEmpty(v)) return v!;
                }
            }
            catch { /* 继续 */ }
        }
        return "";
    }
    private static List<Dictionary<string, object>> DumpPairs(object list)
    {
        var outl = new List<Dictionary<string, object>>();
        foreach (var item in AsEnumerable(list))
        {
            if (item == null) continue;
            var t = item.GetType();
            outl.Add(new Dictionary<string, object>
            {
                ["ascension"] = t.GetField("Item1")?.GetValue(item) is int i ? i : 0,
                ["type"] = ShortName(t.GetField("Item2")?.GetValue(item) as Type),
            });
        }
        return outl;
    }

    // 卡牌费用缓存：key = (类型, 是否升级) ⇒ (费用, 是否 X 费)
    private static readonly Dictionary<(Type, bool), (int Cost, bool XCost)> CostCache = new();
    private static readonly Dictionary<Type, bool> UpgradeCostCache = new();

    /// <summary>
    /// 一张牌（可选升级态）的**能量费用**，X 费另外返回。
    ///
    /// ★ 2026-11-26（用户：「放血战第一回合的意图有误，全身撞击+ 是 0 费可以打出」）：
    /// 以前这里恒按**未升级**取费用 ⇒ 升级后降费的牌在 wiki 里被错标成「没费」。
    /// 为什么不能简单「实例化 + Upgrade()」：`CardModel` 上**没有** `Upgrade()`；
    /// `UpgradeInternal(bool)` / `EnergyCost.UpgradeBy()` 都会先 `AssertMutable()`，而我们用
    /// `Activator` 造的是 **canonical（模板）实例**，不可变 ⇒ 升级被吞掉；`ToMutable()` 又需要
    /// ModelDb（离线 dump 时抛 `ModelNotFoundException: Model id=CARD.XXX not found`）。
    ///
    /// 所以改成**读这张牌自己的 `OnUpgrade()` IL**：StS2 里「升级降费」就是
    /// `EnergyCost.UpgradeBy(-1)`，IL 里能扫到这个调用的就按 −1 记（不会低于 0）。
    /// 一次扫描结果进缓存，导出性能不受影响。
    /// </summary>
    private static (int Cost, bool XCost) CardCost(Type t, bool upgraded)
    {
        if (t == null) return (0, false);
        if (CostCache.TryGetValue((t, upgraded), out var hit)) return hit;
        var result = (0, false);
        try
        {
            var inst = Activator.CreateInstance(t);
            if (inst != null)
            {
                int cost = 0;
                bool x = false;
                var mCost = t.GetMethod("get_CanonicalEnergyCost", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var vCost = mCost?.Invoke(inst, null);
                if (vCost is int c) cost = c;
                var mX = t.GetMethod("get_HasEnergyCostX", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var vX = mX?.Invoke(inst, null);
                if (vX is bool bx) x = bx;

                if (upgraded && !x && cost > 0 && OnUpgradeLowersCost(t))
                    cost = Math.Max(0, cost - 1);

                result = (cost, x);
            }
        }
        catch { /* 造不出来就按 0 费算 */ }
        CostCache[(t, upgraded)] = result;
        return result;
    }

    /// <summary>这张牌的 `OnUpgrade()` 里有没有 `EnergyCost.UpgradeBy(...)`（= 升级会改费用）。</summary>
    private static bool OnUpgradeLowersCost(Type t)
    {
        if (UpgradeCostCache.TryGetValue(t, out var cached)) return cached;
        bool lowers = false;
        try
        {
            var m = t.GetMethod("OnUpgrade", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
            var il = m?.GetMethodBody()?.GetILAsByteArray();
            if (il != null && m != null)
            {
                var mod = m.Module;
                for (int i = 0; i + 4 < il.Length && !lowers; i++)
                {
                    if (il[i] != 0x28 && il[i] != 0x6F) continue;      // call / callvirt
                    int token = BitConverter.ToInt32(il, i + 1);
                    try
                    {
                        var callee = mod.ResolveMethod(token);
                        if (callee != null && callee.Name == "UpgradeBy"
                            && string.Equals(callee.DeclaringType?.Name, "CardEnergyCost", StringComparison.Ordinal))
                            lowers = true;
                    }
                    catch { /* 无效 token：那几字节只是别的指令的操作数 */ }
                }
            }
        }
        catch { /* 读不到就按「不影响费用」处理 */ }
        UpgradeCostCache[t] = lowers;
        return lowers;
    }
    private static string DesignName(string defKey, int act)
        => MainDesign.TryGetValue(defKey + "|" + act, out var v) ? v.Name : (defKey + " 第" + act + "幕");

    private static string DesignEncounter(string defKey, int act)
        => MainDesign.TryGetValue(defKey + "|" + act, out var v) ? v.Encounter : defKey;

    private static string CharacterName(Type t, string defKey)
    {
        if (defKey.StartsWith("WATCHER", StringComparison.Ordinal)) return "观者";
        string n = t?.Name ?? "";
        switch (n)
        {
            case "Ironclad": return "铁甲战士";
            case "Silent": return "静默猎手";
            case "Defect": return "故障机器人";
            case "Necrobinder": return "亡灵契约师";
            case "Regent": return "储君";
            case "Fool": return "愚者";
            default: return n;
        }
    }

    // ── 小工具 ──────────────────────────────────────────────
    private static IEnumerable<KeyValuePair<string, object>> EnumerateDictionary(object dict)
    {
        if (dict == null) yield break;
        if (dict is IDictionary d)
        {
            foreach (DictionaryEntry e in d)
                yield return new KeyValuePair<string, object>(Convert.ToString(e.Key), e.Value);
            yield break;
        }
        if (dict is IEnumerable en)
        {
            foreach (var item in en)
            {
                if (item == null) continue;
                var t = item.GetType();
                yield return new KeyValuePair<string, object>(
                    Convert.ToString(t.GetProperty("Key")?.GetValue(item)),
                    t.GetProperty("Value")?.GetValue(item));
            }
        }
    }

    private static object GetField(object o, string name)
        => o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(o);

    private static IEnumerable<object> CallEnumerable(object o, string method, int arg)
    {
        var m = o.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (m == null) yield break;
        object r = null;
        try { r = m.Invoke(o, new object[] { arg }); } catch { yield break; }
        foreach (var x in AsEnumerable(r)) yield return x;
    }

    private static IEnumerable<object> AsEnumerable(object o)
    {
        if (o == null || o is string) yield break;
        if (o is IEnumerable e)
        {
            foreach (var x in e) yield return x;
        }
    }

    private static List<string> TypeNames(object list)
    {
        var names = new List<string>();
        foreach (var x in AsEnumerable(list))
        {
            if (x is Type ty) names.Add(ShortName(ty));
            else if (x != null) names.Add(x.ToString());
        }
        return names;
    }

    private static string ShortName(Type t) => t?.Name ?? "";

    private static object ArrayAt(Array a, int i) => a == null || i < 0 || i >= a.Length ? null : a.GetValue(i);
    private static int IntAt(Array a, int i) => ArrayAt(a, i) is int n ? n : 0;
    private static string StrAt(Array a, int i) => ArrayAt(a, i) as string ?? "";

    private static T Prop<T>(object o, string name)
    {
        var p = o.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (p == null) return default;
        var v = p.GetValue(o);
        return v is T t ? t : default;
    }
}
