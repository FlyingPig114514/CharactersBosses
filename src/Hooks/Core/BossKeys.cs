using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Encounters;
using CharacterBosses.Monsters;

namespace CharacterBosses.Core;

/// <summary>
/// **Boss 标识**（= 角色 + 变体设计），阵容、站位、去重都以它为单位。
///
/// 用户 2026-10-26 的背景说明：正文里每一层都有**变体**（例如第一幕有「暗港」「密林」，
/// 两个变体的小怪/精英/Boss 都不同），所以「刀猎」和「炼药猎」虽然都是静默猎手，
/// 却是**两个不同的 Boss**（分别属于第三幕的两个变体）。官方目前只做了第一幕的变体，
/// 第二、三层还没出，但本模组先把三层的两个变体 Boss 都做出来。
///
/// 因为 Boss 数量还不够，现在**不同变体的 Boss 混合匹配**：任何变体的 Boss 都可能出现在
/// 当层的任意变体里（所以炼药猎也可能当第三幕「荣耀」的 Boss）。等官方补齐变体，把
/// <see cref="RosterFor"/> 按变体拆开即可 —— 这套标识已经按「变体」建好了。
///
/// Key 的命名规则：档位遭遇 id 去掉 `_ENCOUNTER` 与 `_BOSS` 后缀
/// （`SILENT_BREWER_BOSS_ENCOUNTER` → `SILENT_BREWER`），所以 <see cref="KeyOfEncounter"/>
/// 对任何新加的遭遇都自动成立，两边不会写叉。
/// </summary>
internal static class BossKeys
{
    /// <summary>
    /// 一条 Boss 标识：Key / 角色下标（0 铁甲 1 静默 2 故障 3 亡灵 4 储君）/ 幕（0 起）/ **该角色在本幕的编号（1 起）** / 怪物类型。
    ///
    /// ★ 2026-11-05（用户口径）：编号是**同一个角色内部**的 ——
    ///   「猎人分 1 号（刀猎）和 2 号（炼药猎），机器人分 1~3 号（能力机、事件机、状态机）」。
    ///   所以「3 层 1 号」在不同角色下可以并存（刀猎 = 猎人 3 层 1 号、能力机 = 机器人 3 层 1 号）。
    /// </summary>
    internal readonly record struct BossInfo(string Key, int Character, int Act, int Number, Type MonsterType);

    /// <summary>
    /// 全部档位 Boss。**每一条 = 「第 N 层 + 某角色的第 M 号」**。
    ///
    /// ★ 2026-11-05（用户规则）：
    ///   · Boss **只按层数分**，同层同角色的几个设计**完全平权**（没有「主设计 / 变体」之分），
    ///     只在**角色内部**排号：猎人 1 号刀猎 / 2 号炼药猎；故障 1~3 号能力机 / 事件机 / 状态机……
    ///   · **同一场战斗里，每个角色最多出现一只**（不会同时出刀猎 + 炼药猎）；
    ///   · **主 Boss 的角色整局不重复**（跨幕也不重复）。
    /// </summary>
    private static readonly BossInfo[] All =
    {
        // ── 第一幕（密林 / 暗港）──
        new("SILENT_SLY",           1, 0, 1, typeof(SilentBoss)),          // 猎人 1 号：奇巧猎
        new("DEFECT_DARK",          2, 0, 1, typeof(DefectBoss)),          // 故障 1 号：黑球机
        new("NECROBINDER_DOOM",     3, 0, 1, typeof(NecrobinderBoss)),     // 亡灵 1 号：灾厄骨
        new("REGENT_DEBRIS",        4, 0, 1, typeof(RegentBoss)),          // 储君 1 号：碎屑储君
        new("REGENT_BOMBARD",       4, 0, 2, typeof(RegentBoss)),          // 储君 2 号：轰击储
        new("DEFECT_THUNDER",      2, 0, 2, typeof(DefectBoss)),          // 故障 2 号：电球机（用户 2026-11-05）
        new("IRONCLAD_ANGER",       0, 0, 1, typeof(IroncladBoss)),        // 铁甲 1 号：愤怒战
        new("IRONCLAD_STRIKE",      0, 0, 2, typeof(IroncladBoss)),        // 铁甲 2 号：打击战（用户 2026-11-07）
        new("NECROBINDER_FLESH",   3, 0, 2, typeof(NecrobinderBoss)),      // 亡灵 2 号：血肉骨（用户 2026-11-10）
        new("SILENT_SNAKEBITE",    1, 0, 2, typeof(SilentBoss)),          // 猎人 2 号：蛇咬猎（用户 2026-11-08）
        // ── 第二幕（巢穴）──
        new("SILENT_ENVENOM",       1, 1, 1, typeof(SilentBoss)),          // 猎人 1 号：涂毒猎
        new("SILENT_SNECKO",        1, 1, 2, typeof(SilentBoss)),          // 猎人 2 号：蛇眼猎
        new("DEFECT_CLAW",          2, 1, 1, typeof(DefectBoss)),          // 故障 1 号：物理机
        new("DEFECT_RAINBOW",       2, 1, 2, typeof(DefectBoss)),          // 故障 2 号：彩虹机（用户 2026-11-07）
        new("NECROBINDER_SOUL",     3, 1, 1, typeof(NecrobinderBoss)),     // 亡灵 1 号：灵魂骨
        new("NECROBINDER_ETHEREAL",     3, 1, 2, typeof(NecrobinderBoss)),     // 亡灵 2 号：虚无骨（用户 2026-11-09）
        new("REGENT_FORGE",         4, 1, 1, typeof(RegentBoss)),          // 储君 1 号：铸剑储君
        new("REGENT_KING",          4, 1, 2, typeof(RegentBoss)),          // 储君 2 号：王储
        new("IRONCLAD_BLOOD",       0, 1, 1, typeof(IroncladBoss)),        // 铁甲 1 号：放血战
        new("IRONCLAD_JUGGLER",     0, 1, 2, typeof(IroncladBoss)),        // 铁甲 2 号：杂耍战
        new("REGENT_MINION",       4, 1, 3, typeof(RegentBoss)),          // 储君 3 号：仆从储（用户 2026-11-05）
        // ── 第三幕（荣耀 + 另一个变体）──
        new("SILENT_SHIV",          1, 2, 1, typeof(SilentBoss)),          // 猎人 1 号：刀猎
        new("SILENT_BREWER",        1, 2, 2, typeof(SilentBoss)),          // 猎人 2 号：炼药猎
        new("SILENT_GRAND_FINALE",  1, 2, 3, typeof(SilentBoss)),          // 猎人 3 号：华丽猎（用户 2026-11-09；内部名用「华丽收场」的英文）
        new("DEFECT_AI",            2, 2, 1, typeof(DefectBoss)),          // 故障 1 号：能力机
        new("DEFECT_EVENT",         2, 2, 2, typeof(DefectBoss)),          // 故障 2 号：事件机
        new("DEFECT_STATUS",        2, 2, 3, typeof(DefectBoss)),          // 故障 3 号：状态机（原本在二层，2026-11-05 挪到三层）
        new("NECROBINDER_ERADICATE",3, 2, 1, typeof(NecrobinderBoss)),     // 亡灵 1 号：根除骨
        new("NECROBINDER_OSTY",     3, 2, 2, typeof(NecrobinderBoss)),     // 亡灵 2 号：养手骨
        new("REGENT_COLORLESS",     4, 2, 1, typeof(RegentBoss)),          // 储君 1 号：无色储君
        new("REGENT_STAR",          4, 2, 2, typeof(RegentBoss)),          // 储君 2 号：辉星储
        new("IRONCLAD_VULN",        0, 2, 1, typeof(IroncladBoss)),        // 铁甲 1 号：易伤战
        new("IRONCLAD_BOULDER",     0, 2, 2, typeof(IroncladBoss)),        // 铁甲 2 号：巨石战
    };

    /// <summary>每一幕的候补（顺序 = 站位偏好）。</summary>
    private static readonly Dictionary<string, BossInfo> ByKey = BuildIndex();

    private static Dictionary<string, BossInfo> BuildIndex()
    {
        var map = new Dictionary<string, BossInfo>(StringComparer.Ordinal);
        foreach (var info in All) map[info.Key] = info;
        return map;
    }

    /// <summary>
    /// 这一幕的候补 Boss（Key 数组），按「角色次序（猎→机→骨→储→战）+ 同角色内部编号」排好。
    ///
    /// ★ 2026-11-05（用户口径）：编号是**同角色内部**的 ——
    ///   三层猎人 = 1 号刀猎 / 2 号炼药猎，三层故障 = 1 号能力机 / 2 号事件机 / 3 号状态机。
    ///   阵容抽的时候**每个角色最多出一只**（见 <c>BossLineups.Build</c>），所以「同角色多号」
    ///   是给**不同局 / 不同场**抽的，不会在同一场里塞两只。
    /// </summary>
    internal static string[] RosterFor(int act)
    {
        if (act >= 2)   // 第三幕：11 个（猎 2 / 机 3 / 骨 2 / 储 2 / 战 2）
        {
            return new[]
            {
                "SILENT_SHIV",          // 3 层 1 号
                "SILENT_BREWER",        // 3 层 2 号
                "SILENT_GRAND_FINALE",  // 3 层 2.5 号（猎人第三套设计）
                "DEFECT_AI",            // 3 层 3 号
                "DEFECT_EVENT",         // 3 层 4 号
                "DEFECT_STATUS",        // 3 层 5 号
                "NECROBINDER_ERADICATE",// 3 层 6 号
                "NECROBINDER_OSTY",     // 3 层 7 号
                "REGENT_COLORLESS",     // 3 层 8 号
                "REGENT_STAR",          // 3 层 9 号
                "IRONCLAD_VULN",        // 3 层 10 号
                "IRONCLAD_BOULDER",     // 3 层 11 号
            };
        }
        if (act == 1)   // 第二幕：10 个（猎 2 / 机 2 / 骨 1 / 储 3 / 战 2）
        {
            return new[]
            {
                "SILENT_ENVENOM",       // 二层 猎人 1 号：涂毒猎
                "SILENT_SNECKO",        // 二层 猎人 2 号：蛇眼猎
                "DEFECT_CLAW",          // 二层 故障 1 号：物理机
                "DEFECT_RAINBOW",       // 二层 故障 2 号：彩虹机（用户 2026-11-07）
                "NECROBINDER_SOUL",     // 二层 亡灵 1 号：灵魂骨
                "NECROBINDER_ETHEREAL",     // 二层 亡灵 2 号：虚无骨（用户 2026-11-09）
                "REGENT_FORGE",         // 二层 储君 1 号：铸剑储君
                "REGENT_KING",          // 二层 储君 2 号：王储
                "REGENT_MINION",        // 二层 储君 3 号：仆从储（用户 2026-11-05）
                "IRONCLAD_BLOOD",       // 二层 铁甲 1 号：放血战
                "IRONCLAD_JUGGLER",     // 二层 铁甲 2 号：杂耍战
            };
        }
        // 第一幕：7 个（猎 1 / 机 2 / 骨 1 / 储 2 / 战 1）
        return new[]
        {
            "SILENT_SLY",               // 一层 猎人 1 号：奇巧猎
            "DEFECT_DARK",              // 一层 故障 1 号：黑球机
            "DEFECT_THUNDER",           // 一层 故障 2 号：电球机（用户 2026-11-05）
            "NECROBINDER_DOOM",         // 一层 亡灵 1 号：灾厄骨
            "NECROBINDER_FLESH",        // 一层 亡灵 2 号：血肉骨（用户 2026-11-10）
            "REGENT_DEBRIS",            // 一层 储君 1 号：碎屑储君
            "REGENT_BOMBARD",           // 一层 储君 2 号：轰击储
            "IRONCLAD_ANGER",           // 一层 铁甲 1 号：愤怒战
            "IRONCLAD_STRIKE",          // 一层 铁甲 2 号：打击战（用户 2026-11-07）
            "SILENT_SNAKEBITE",         // 一层 猎人 2 号：蛇咬猎（用户 2026-11-08）
        };
    }

    internal static bool TryGet(string? key, out BossInfo info)
    {
        info = default;
        if (string.IsNullOrEmpty(key)) return false;
        return ByKey.TryGetValue(key!, out info);
    }

    /// <summary>角色下标（认不出返回 -1）。</summary>
    internal static int CharacterOf(string? key) => TryGet(key, out var info) ? info.Character : -1;

    internal static Type? MonsterTypeOf(string? key) => TryGet(key, out var info) ? info.MonsterType : null;

    /// <summary>这一条 Boss 属于第几幕（拿不到返回 -1）。</summary>
    internal static int ActOf(string? key) => TryGet(key, out var info) ? info.Act : -1;

    /// <summary>**该角色在本幕的编号**（1 起；拿不到返回 0）。用户口径：「猎人 1 号刀猎 / 2 号炼药猎」。</summary>
    internal static int NumberOf(string? key) => TryGet(key, out var info) ? info.Number : 0;

    /// <summary>角色的中文简称（日志/调试用）。</summary>
    internal static string CharacterName(int character) => character switch
    {
        0 => "铁甲",
        1 => "猎人",
        2 => "故障",
        3 => "亡灵",
        4 => "储君",
        _ => "?",
    };

    /// <summary>
    /// **设计的中文短名**（配置界面的勾选格用，用户 2026-11-06）。
    /// 和「层号/编号」那套标签(`LabelOf`)是两回事：这里只给玩家看的那个两三字名字。
    /// 未登记的 Key 原样返回（不至于显示空白）。
    /// </summary>
    private static readonly Dictionary<string, string> DesignNames = new(StringComparer.Ordinal)
    {
        // 一层
        ["SILENT_SLY"] = "奇巧猎",
        ["NECROBINDER_DOOM"] = "灾厄骨",
        ["DEFECT_DARK"] = "黑球机",
        ["DEFECT_THUNDER"] = "电球机",
        ["IRONCLAD_ANGER"] = "愤怒战",
        ["IRONCLAD_STRIKE"] = "打击战",
        ["SILENT_SNAKEBITE"] = "蛇咬猎",
        ["REGENT_DEBRIS"] = "碎屑储",
        ["REGENT_BOMBARD"] = "轰击储",
        // 二层
        ["SILENT_ENVENOM"] = "涂毒猎",
        ["SILENT_SNECKO"] = "蛇眼猎",
        ["NECROBINDER_SOUL"] = "灵魂骨",
        ["DEFECT_CLAW"] = "物理机",
        ["DEFECT_RAINBOW"] = "彩虹机",
        ["IRONCLAD_BLOOD"] = "放血战",
        ["IRONCLAD_JUGGLER"] = "杂耍战",
        ["REGENT_FORGE"] = "铸剑储",
        ["REGENT_KING"] = "王储",
        ["REGENT_MINION"] = "仆从储",
        // 三层
        ["SILENT_SHIV"] = "刀猎",
        ["SILENT_BREWER"] = "炼药猎",
        ["SILENT_GRAND_FINALE"] = "华丽猎",
        ["DEFECT_AI"] = "能力机",
        ["DEFECT_EVENT"] = "事件机",
        ["DEFECT_STATUS"] = "状态机",
        ["NECROBINDER_ERADICATE"] = "根除骨",
        ["NECROBINDER_OSTY"] = "养手骨",
        ["NECROBINDER_ETHEREAL"] = "虚无骨",
        ["NECROBINDER_FLESH"] = "血肉骨",
        ["REGENT_COLORLESS"] = "无色储",
        ["REGENT_STAR"] = "辉星储",
        ["IRONCLAD_VULN"] = "易伤战",
        ["IRONCLAD_BOULDER"] = "巨石战",
    };

    /// <summary>Key → 设计的中文短名（没登记就返回 Key 本身）。</summary>
    internal static string DesignNameOf(string? key)
    {
        if (string.IsNullOrEmpty(key)) return "?";
        try { if (DesignNames.TryGetValue(key!, out var n) && !string.IsNullOrEmpty(n)) return n; }
        catch { /* 忽略 */ }
        return key!;
    }
    /// <summary>给日志用的标签，例如 <c>3 层 猎人 1 号（SILENT_SHIV）</c>。</summary>
    internal static string LabelOf(string? key)
    {
        if (!TryGet(key, out var info)) return key ?? "null";
        return $"{info.Act + 1} 层 {CharacterName(info.Character)} {info.Number} 号（{info.Key}）";
    }

    /// <summary>
    /// 某一幕里**某个角色**的全部设计，按编号排序（1 号→2 号→…）。
    /// 阵容就是「先定哪几个角色，再在各自的设计里挑一个号」。
    /// </summary>
    internal static List<string> DesignsOf(int act, int character)
    {
        var list = new List<string>();
        foreach (var info in All)
        {
            if (info.Act != act || info.Character != character) continue;
            list.Add(info.Key);
        }
        list.Sort((a, b) => NumberOf(a).CompareTo(NumberOf(b)));
        return list;
    }

    /// <summary>
    /// **同一个角色的「另一个变体」**（三层用）：
    /// 刀猎 `SILENT_SHIV` ↔ 炼药猎 `SILENT_BREWER`、
    /// 易伤战 `IRONCLAD_VULN` ↔ 巨石战 `IRONCLAD_BOULDER`、
    /// 无色储君 `REGENT_COLORLESS` ↔ 辉星储 `REGENT_STAR`。
    /// 没有成对变体的（能力机 / 根除骨 / 一二层的那些）返回 <c>null</c>。
    ///
    /// 用途（用户 2026-10-30：「进阶 10 三层两场用同一套变体」）：
    /// 第二场万一还是拿到了第一场的那个 Key，就把它换成同角色的另一个变体 —— 保证两场不同。
    /// </summary>
    internal static string? ComplementVariant(string? key)
    {
        switch (key)
        {
            case "SILENT_SHIV": return "SILENT_BREWER";
            case "SILENT_BREWER": return "SILENT_GRAND_FINALE";
            case "SILENT_GRAND_FINALE": return "SILENT_SHIV";
            case "IRONCLAD_VULN": return "IRONCLAD_BOULDER";
            case "IRONCLAD_BOULDER": return "IRONCLAD_VULN";
            case "REGENT_COLORLESS": return "REGENT_STAR";
            case "REGENT_STAR": return "REGENT_COLORLESS";
            // 二层铁甲战士的一对变体（放血战 / 杂耍战）
            // 二层静默猎手的一对变体（涂毒猎 / 蛇眼猎）
            case "SILENT_ENVENOM": return "SILENT_SNECKO";
            case "SILENT_SNECKO": return "SILENT_ENVENOM";
            case "IRONCLAD_ANGER": return "IRONCLAD_STRIKE";     // 一层铁甲的一对（愤怒战 / 打击战）
            case "IRONCLAD_STRIKE": return "IRONCLAD_ANGER";
            // 一层静默猎人的一对（奇巧猎 / 蛇咬猎）
            case "SILENT_SLY": return "SILENT_SNAKEBITE";
            case "SILENT_SNAKEBITE": return "SILENT_SLY";
            case "IRONCLAD_BLOOD": return "IRONCLAD_JUGGLER";
            case "IRONCLAD_JUGGLER": return "IRONCLAD_BLOOD";
            // 三层亡灵契约师的一对变体（根除骨 / 养手骨）
            case "NECROBINDER_ERADICATE": return "NECROBINDER_OSTY";
            case "NECROBINDER_OSTY": return "NECROBINDER_ERADICATE";
            // 二层储君的一对变体（铸剑储君 / 征服者储君）
            case "REGENT_FORGE": return "REGENT_KING";
            case "REGENT_KING": return "REGENT_FORGE";
            // 三层故障机器人的三个设计（能力机 / 事件机 / 状态机）：链式互补，保证换到的一定不是它自己
            case "DEFECT_AI": return "DEFECT_EVENT";
            case "DEFECT_EVENT": return "DEFECT_STATUS";
            case "DEFECT_STATUS": return "DEFECT_AI";
            // 一层故障机器人的一对（黑球机 / 电球机）
            case "DEFECT_DARK": return "DEFECT_THUNDER";
            case "DEFECT_THUNDER": return "DEFECT_DARK";
            // 二层故障机器人的一对（物理机 / 彩虹机，用户 2026-11-07）
            case "DEFECT_CLAW": return "DEFECT_RAINBOW";
            case "DEFECT_RAINBOW": return "DEFECT_CLAW";
            // 二层储君的第三套（仆从储）← 王储（REGENT_KING）才是它的互补
            case "REGENT_MINION": return "REGENT_KING";
            // 一层储君的一对（碎屑储君 / 轰击储）
            case "REGENT_DEBRIS": return "REGENT_BOMBARD";
            case "REGENT_BOMBARD": return "REGENT_DEBRIS";
            default: return null;
        }
    }

    /// <summary>Key → 遭遇（正常流程与控制台用的是同一批，见 CharBossEncounters.TierEncounters）。</summary>
    internal static EncounterModel? EncounterOf(string? key)
    {
        switch (key)
        {
            case "IRONCLAD_ANGER": return ModelDb.Encounter<IroncladAngerBossEncounter>();
            case "IRONCLAD_STRIKE": return ModelDb.Encounter<IroncladStrikeBossEncounter>();
            case "SILENT_SNAKEBITE": return ModelDb.Encounter<SilentSnakebiteBossEncounter>();
            case "IRONCLAD_VULN": return ModelDb.Encounter<IroncladVulnBossEncounter>();
            case "IRONCLAD_BOULDER": return ModelDb.Encounter<IroncladBoulderBossEncounter>();
            case "NECROBINDER_DOOM": return ModelDb.Encounter<NecrobinderDoomBossEncounter>();
            case "NECROBINDER_FLESH": return ModelDb.Encounter<NecrobinderFleshBossEncounter>();
            case "NECROBINDER_SOUL": return ModelDb.Encounter<NecrobinderSoulBossEncounter>();
            case "NECROBINDER_ETHEREAL": return ModelDb.Encounter<NecrobinderEtherealBossEncounter>();
            case "NECROBINDER_ERADICATE": return ModelDb.Encounter<NecrobinderEradicateBossEncounter>();
            case "SILENT_ENVENOM": return ModelDb.Encounter<SilentEnvenomBossEncounter>();
            case "SILENT_SLY": return ModelDb.Encounter<SilentSlyBossEncounter>();
            case "DEFECT_DARK": return ModelDb.Encounter<DefectDarkBossEncounter>();
            case "DEFECT_STATUS": return ModelDb.Encounter<DefectStatusBossEncounter>();
            case "DEFECT_THUNDER": return ModelDb.Encounter<DefectThunderBossEncounter>();
            case "REGENT_MINION": return ModelDb.Encounter<RegentMinionBossEncounter>();
            case "IRONCLAD_BLOOD": return ModelDb.Encounter<IroncladBloodBossEncounter>();
            case "IRONCLAD_JUGGLER": return ModelDb.Encounter<IroncladJugglerBossEncounter>();
            case "SILENT_SHIV": return ModelDb.Encounter<SilentShivBossEncounter>();
            case "SILENT_BREWER": return ModelDb.Encounter<SilentBrewerBossEncounter>();
            case "SILENT_GRAND_FINALE": return ModelDb.Encounter<SilentGrandFinaleBossEncounter>();
            case "DEFECT_AI": return ModelDb.Encounter<DefectAiBossEncounter>();
            case "DEFECT_CLAW": return ModelDb.Encounter<DefectClawBossEncounter>();
            case "DEFECT_RAINBOW": return ModelDb.Encounter<DefectRainbowBossEncounter>();
            case "REGENT_DEBRIS": return ModelDb.Encounter<RegentDebrisBossEncounter>();
            case "REGENT_FORGE": return ModelDb.Encounter<RegentForgeBossEncounter>();
            case "REGENT_KING": return ModelDb.Encounter<RegentKingBossEncounter>();
            case "REGENT_COLORLESS": return ModelDb.Encounter<RegentColorlessBossEncounter>();
            case "REGENT_STAR": return ModelDb.Encounter<RegentStarBossEncounter>();
            case "REGENT_BOMBARD": return ModelDb.Encounter<RegentBombardBossEncounter>();
            case "NECROBINDER_OSTY": return ModelDb.Encounter<NecrobinderOstyBossEncounter>();
            case "SILENT_SNECKO": return ModelDb.Encounter<SilentSneckoBossEncounter>();
            case "DEFECT_EVENT": return ModelDb.Encounter<DefectEventBossEncounter>();
            default: return null;
        }
    }

    /// <summary>
    /// 遭遇 → Key（去掉 `_ENCOUNTER` 和 `_BOSS` 两层后缀）。
    /// 例：`SILENT_BREWER_BOSS_ENCOUNTER` → `SILENT_BREWER`。
    /// 不是我们的档位遭遇（例如按角色的基础遭遇 `SILENT_BOSS_ENCOUNTER`）返回 null。
    /// </summary>
    /// <summary>
    /// **遭遇 entry 字符串 → Key**（例：`SILENT_BREWER_BOSS_ENCOUNTER` → `SILENT_BREWER`）。
    /// 和 <see cref="KeyOfEncounter"/> 同一套后缀剥离规则，只是入口是字符串
    /// （`CurrentEncounterEntry()` 给的就是字符串，见「一场战斗里是哪个变体」的判定）。
    /// </summary>
    internal static string? KeyOfEncounterEntry(string? entry)
    {
        try
        {
            if (string.IsNullOrEmpty(entry)) return null;
            string key = entry!;
            if (key.EndsWith("_ENCOUNTER", StringComparison.Ordinal))
                key = key.Substring(0, key.Length - "_ENCOUNTER".Length);
            if (key.EndsWith("_BOSS", StringComparison.Ordinal))
                key = key.Substring(0, key.Length - "_BOSS".Length);
            return ByKey.ContainsKey(key) ? key : null;
        }
        catch { return null; }
    }
    internal static string? KeyOfEncounter(EncounterModel? encounter)
    {
        try
        {
            string? entry = encounter?.Id.Entry;
            if (string.IsNullOrEmpty(entry)) return null;

            string key = entry!;
            if (key.EndsWith("_ENCOUNTER", StringComparison.Ordinal))
                key = key.Substring(0, key.Length - "_ENCOUNTER".Length);
            if (key.EndsWith("_BOSS", StringComparison.Ordinal))
                key = key.Substring(0, key.Length - "_BOSS".Length);

            return ByKey.ContainsKey(key) ? key : null;
        }
        catch { return null; }
    }
}
