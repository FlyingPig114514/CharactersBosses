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

        LoadLocalization(root);

        Assembly impl;
        try { impl = Assembly.LoadFrom(implPath); }
        catch (Exception e) { Console.Error.WriteLine("load impl failed: " + e); return 3; }

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
            if (char.IsUpper(c) && i > 0) sb.Append('_');
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
            var script = DumpScript(ArrayAt(scriptByAct, act));
            var variants = new List<object>();
            foreach (var v in CallEnumerable(def, "VariantsForAct", act))
            {
                if (v == null) continue;
                var dv = DumpVariant(v);
                if (dv["script"] != null) variants.Add(dv);
            }

            var deck = TypeNames(ArrayAt(deckByAct, act));
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
                ["deck"] = deck.Select(CardName).Where(s => s.Length > 0).ToList(),
                ["relics"] = TypeNames(ArrayAt(relicsByAct, act)).Select(RelicName).ToList(),
                ["potions"] = TypeNames(ArrayAt(potionsByAct, act)).Select(RelicName).ToList(),
                ["dreadUpgrades"] = TypeNames(ArrayAt(dreadUpByAct, act)).Select(CardName).ToList(),
                ["script"] = script,
                ["variants"] = variants,
            });
        }

        return new Dictionary<string, object>
        {
            ["key"] = key,
            ["character"] = CharacterName(GetField(def, "CharacterType") as Type, key),
            ["info"] = infoTable.TryGetValue(key, out var info) ? info : "",
            ["acts"] = actsOut,
        };
    }

    private static Dictionary<string, object> DumpVariant(object v)
    {
        var script = DumpScript(Prop<object>(v, "Script"));
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
            ["deck"] = TypeNames(Prop<object>(v, "Deck")).Select(CardName).ToList(),
            ["relics"] = TypeNames(Prop<object>(v, "Relics")).Select(RelicName).ToList(),
            ["ascensionUpgrades"] = DumpPairs(Prop<object>(v, "AscensionUpgrades")).Select(p => $"{p["ascension"]}: {CardName((string)p["type"])}").ToList(),
            ["ascensionRelics"] = DumpPairs(Prop<object>(v, "AscensionRelics")).Select(p => $"{p["ascension"]}: {RelicName((string)p["type"])}").ToList(),
            ["dreadUpgrades"] = TypeNames(Prop<object>(v, "DreadUpgrades")).Select(CardName).ToList(),
            ["script"] = script,
        };
    }

    private static Dictionary<string, object> DumpScript(object script)
    {
        if (script == null) return null;
        var turns = new List<object>();
        foreach (var turn in AsEnumerable(Prop<object>(script, "Turns")))
        {
            if (turn == null) continue;
            var cards = new List<object>();
            foreach (var c in AsEnumerable(Prop<object>(turn, "Cards")))
            {
                if (c != null) cards.Add(DumpCard(c));
            }
            turns.Add(new Dictionary<string, object> { ["note"] = Prop<string>(turn, "Note") ?? "", ["cards"] = cards });
        }
        if (turns.Count == 0) return null;
        return new Dictionary<string, object>
        {
            ["loopFrom"] = Prop<int>(script, "LoopFrom"),
            ["loopLength"] = Prop<int>(script, "LoopLength"),
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
        foreach (var flag in new[]
        {
            "Upgraded", "Burned", "DiscardInstead", "DiscardInsteadWhenMultiplayer", "DiscardInsteadSingleOnly",
            "Sly", "Retain", "HoldInstead", "DisplayOnly", "NotDrawn", "AutoPlayed", "FreeThisTurn",
            "ToolsDiscard", "HellraiserFree", "EnchantSpiral", "EnchantSouls", "EnchantRoyallyApproved",
            "ShowsDrillGain", "SpendsAllEnergy", "ForceDebuffIntent", "JugglingCopy", "StampedePlay",
            "EmptyDrawPileFirst", "ScriedFromDraw",
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
        var from = t.GetProperty("From")?.GetValue(c) as string;
        if (!string.IsNullOrEmpty(from) && from != "deck") card["from"] = from;

        foreach (var arrName in new[] { "ExhaustWith", "ReturnsToHand", "PrintsToDrawPile" })
        {
            var names = TypeNames(t.GetProperty(arrName)?.GetValue(c)).Select(CardName).Where(s => s.Length > 0).ToList();
            if (names.Count > 0) card[arrName] = names;
        }
        return card;
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
