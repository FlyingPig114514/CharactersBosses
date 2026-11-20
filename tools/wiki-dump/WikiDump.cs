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
            // 先算出代码登记的卡组：剧本没标洗牌时要用「卡组张数 ÷ 每回合抽牌」推算首次洗牌回合
            var deck = TypeNames(ArrayAt(deckByAct, act));
            var script = DumpScript(ArrayAt(scriptByAct, act), IntAt(energy, act), IntAt(handSize, act), deck.Count);
            var variants = new List<object>();
            foreach (var v in CallEnumerable(def, "VariantsForAct", act))
            {
                if (v == null) continue;
                var dv = DumpVariant(v, IntAt(energy, act), IntAt(handSize, act));
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
    private static Dictionary<string, object> DumpVariant(object v, int actEnergy, int actHandSize)
    {
        var script = DumpScript(Prop<object>(v, "Script"), actEnergy, actHandSize, TypeNames(Prop<object>(v, "Deck")).Count);
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
            ["deck"] = DeckFromScriptOrRegistered(script, Prop<object>(v, "Deck")),
            ["deckRegistered"] = TypeNames(Prop<object>(v, "Deck")).Select(CardName).ToList(),
            ["deckNote"] = DeckNote(script),
            ["relics"] = TypeNames(Prop<object>(v, "Relics")).Select(RelicName).ToList(),
            ["ascensionUpgrades"] = DumpPairs(Prop<object>(v, "AscensionUpgrades")).Select(p => $"{p["ascension"]}: {CardName((string)p["type"])}").ToList(),
            ["ascensionRelics"] = DumpPairs(Prop<object>(v, "AscensionRelics")).Select(p => $"{p["ascension"]}: {RelicName((string)p["type"])}").ToList(),
            ["dreadUpgrades"] = TypeNames(Prop<object>(v, "DreadUpgrades")).Select(CardName).ToList(),
            ["script"] = script,
        };
    }

    private static Dictionary<string, object> DumpScript(object script, int actEnergy, int handSize, int registeredCount)
    {
        if (script == null) return null;
        var turns = new List<object>();
        int shuffleTurn = 0;      // 首次洗牌发生在第几回合（0 = 剧本里没标）
        var subroutine = new bool[1];   // 整份剧本共享：「子程序」是否已经打出来过
        foreach (var turn in AsEnumerable(Prop<object>(script, "Turns")))
        {
            if (turn == null) continue;

            var cards = new List<object>();
            foreach (var c in AsEnumerable(Prop<object>(turn, "Cards")))
            {
                if (c != null) cards.Add(DumpCard(c));
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
                    if (fromNote.Count > cards.Count)
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

            // 按「队列顺序 + 费用」走一遍，标出每张牌到底打不打得出
            AnnotatePlayed(cards, energy, subroutine);

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
            var (baseCost, xCost) = CardCost(cardType, false);
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
    private static void AnnotatePlayed(List<object> cards, int energy, bool[] subroutine)
    {
        int remaining = Math.Max(0, energy);
        bool broke = false;
        foreach (var o in cards)
        {
            var card = o as Dictionary<string, object>;
            if (card == null) continue;

            if (card.ContainsKey("fromNote")) continue;   // Note 反解出来的牌：费用未知，按 Note 的标注为准

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
            bool free = card.ContainsKey("AutoPlayed") || card.ContainsKey("FreeThisTurn")
                        || card.ContainsKey("Sly") || card.ContainsKey("HellraiserFree")
                        || card.ContainsKey("ToolsDiscard");
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

            if (broke || cost > remaining)
            {
                card["played"] = false;
                card["reason"] = "没费";
                broke = true;
                continue;
            }

            card["played"] = true;
            if (xCost) remaining = 0;
            else remaining -= Math.Max(0, cost);

            // 「子程序」：每打出一张**能力牌** +1 能量（下一张牌用得上）——能力机那套的能量就靠它
            string ct = card.ContainsKey("cardType") ? Convert.ToString(card["cardType"]) : null;
            if (subroutine != null && subroutine.Length > 0 && subroutine[0] && ct == "Power") remaining += 1;
            if (string.Equals(card["name"] as string, "子程序", StringComparison.Ordinal) && subroutine != null) subroutine[0] = true;
        }
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
                if (upgraded)
                {
                    try
                    {
                        var m = t.GetMethod("Upgrade", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                        m?.Invoke(inst, null);
                    }
                    catch { /* 升级失败就用基础费用 */ }
                }
                // 属性本身在基类上，拿 PropertyInfo 不稳 ⇒ 直接调 getter 方法
                int cost = 0;
                bool x = false;
                var mCost = t.GetMethod("get_CanonicalEnergyCost", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var vCost = mCost.Invoke(inst, null);
                if (vCost is int c) cost = c;
                var mX = t.GetMethod("get_HasEnergyCostX", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var vX = mX.Invoke(inst, null);
                if (vX is bool bx) x = bx;
                result = (cost, x);
            }
        }
        catch { /* 造不出来就按 0 费算 */ }
        CostCache[(t, upgraded)] = result;
        return result;
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
