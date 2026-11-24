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
        new("REGENT_CHOICE",        4, 2, 3, typeof(RegentBoss)),          // 储君 3 号：抉择储（用户 2026-11-10）
        new("IRONCLAD_VULN",        0, 2, 1, typeof(IroncladBoss)),        // 铁甲 1 号：易伤战
        new("IRONCLAD_BOULDER",     0, 2, 2, typeof(IroncladBoss)),        // 铁甲 2 号：巨石战
#if STS2_111
#if STS2_113
        new("FOOL_TAROT",           5, 2, 1, typeof(FoolTarotBoss)),      // 愚者 1 号：塔罗愚（用户 2026-11-14）
#endif
#endif
#if STS2_111
        // 观者 Boss 占位（用户 2026-11-15）：只有装了观者 mod 才会进候选表（见 RosterFor）
        new("WATCHER_DIVINITY",         6, 2, 1, typeof(WatcherBoss)),
        // 三层 2 号：**Ω观**（用户 2026-11-16）—— 同一观者角色的第二套设计（阿尔法/贝塔/欧米伽 + 特殊效果「斋戒」）
        new("WATCHER_OMEGA",            6, 2, 2, typeof(WatcherOmegaBoss)),
        // 二层 1 号：**印牌观**（用户 2026-11-16）—— 武器库/创世之柱 印牌流 + 特殊效果「操控现实」
        new("WATCHER_CREATE",            6, 1, 1, typeof(WatcherCreateBoss)),
        // 二层 2 号：**保留观**（用户 2026-11-17）—— 保留牌流 + 特殊效果「确立基础」
        new("WATCHER_RETAIN",            6, 1, 2, typeof(WatcherRetainBoss)),
        // 一层 1 号：**预见观**（用户 2026-11-17）—— 预见/先见之明流 + 特殊效果「通晓万物」
        new("WATCHER_FORESIGHT",         6, 0, 1, typeof(WatcherForesightBoss)),
        // 一层 2 号：**疾风观**（用户 2026-11-23）—— 疾风连击/以手拒之 + 特殊效果「疾风连击」
        new("WATCHER_FLURRY",            6, 0, 2, typeof(WatcherFlurryBoss)),
        // 三层 3 号：**红蓝观**（用户 2026-11-18）—— 红蓝姿态无限流 + 特殊效果「节制」。
        new("WATCHER_RUSHDOWN",          6, 2, 3, typeof(WatcherRushdownBoss)),
#endif
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
                "REGENT_CHOICE",        // 3 层 10 号（储君 3 号：抉择储，用户 2026-11-10）
                "IRONCLAD_VULN",        // 3 层 10 号
                "IRONCLAD_BOULDER",     // 3 层 11 号
#if STS2_111
                "FOOL_TAROT",           // 3 层 12 号：塔罗愚（愚者，用户 2026-11-14）
#endif
                // 观者 Boss 占位（用户 2026-11-15）：**只有装了观者 mod 才会进候选表**。
                // ⚠ 不能用 `#if`（编译期不知道别人装没装）⇒ 在 return 之前按运行期探测结果拼。
            }.Concat(WatcherRoster()).ToArray();
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
                // ★ v0.2.712（用户 2026-11-16）：二层 3 号是**印牌观**（观者 mod）⇒ 运行期按观者闸门追加
            }.Concat(WatcherRosterAct2()).ToArray();
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
            // ★ v0.2.746（用户 2026-11-17）：一层追加**预见观**（观者 mod，装了才进表）
        }.Concat(WatcherRosterAct1()).ToArray();
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
        5 => "愚者",
        6 => "观者",
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
#if STS2_111
        ["FOOL_TAROT"] = "塔罗愚",   // 三层 1 号（愚者）
#if STS2_111
    ["WATCHER_DIVINITY"] = "神格观",  // 三层 1 号（观者 mod）
    ["WATCHER_OMEGA"] = "Ω观",        // 三层 2 号（观者 mod，用户 2026-11-16）
    ["WATCHER_CREATE"] = "印牌观",      // 二层 1 号（观者 mod，用户 2026-11-16）
    ["WATCHER_RETAIN"] = "保留观",      // 二层 2 号（观者 mod，用户 2026-11-17）
        ["WATCHER_RUSHDOWN"] = "红蓝观",   // 三层 3 号（观者 mod，用户 2026-11-18）
    ["WATCHER_FORESIGHT"] = "预见观",   // 一层 1 号（观者 mod，用户 2026-11-17）
#endif
#endif
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
        ["REGENT_CHOICE"] = "抉择储",
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
            // 观者三层两只（进阶 10 两场别都取同一只）
            case "WATCHER_DIVINITY": return "WATCHER_OMEGA";
            case "WATCHER_OMEGA": return "WATCHER_DIVINITY";
            case "IRONCLAD_BOULDER": return "IRONCLAD_VULN";
            case "REGENT_COLORLESS": return "REGENT_STAR";
            case "REGENT_STAR": return "REGENT_CHOICE";      // 三层储君三套设计的链式互补
            case "REGENT_CHOICE": return "REGENT_COLORLESS";
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
        key = SubstituteIfUnavailable(key);
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
            case "REGENT_CHOICE": return ModelDb.Encounter<RegentChoiceBossEncounter>();
            case "REGENT_BOMBARD": return ModelDb.Encounter<RegentBombardBossEncounter>();
            case "NECROBINDER_OSTY": return ModelDb.Encounter<NecrobinderOstyBossEncounter>();
            case "SILENT_SNECKO": return ModelDb.Encounter<SilentSneckoBossEncounter>();
            case "DEFECT_EVENT": return ModelDb.Encounter<DefectEventBossEncounter>();
#if STS2_111
#if STS2_113
            case "FOOL_TAROT": return ModelDb.Encounter<FoolTarotBossEncounter>();
#endif
#if STS2_111
            case "WATCHER_DIVINITY": return ModelDb.Encounter<WatcherDivinityBossEncounter>();
            case "WATCHER_OMEGA": return ModelDb.Encounter<WatcherOmegaBossEncounter>();
            case "WATCHER_CREATE": return ModelDb.Encounter<WatcherCreateBossEncounter>();
            case "WATCHER_RUSHDOWN": return ModelDb.Encounter<WatcherRushdownBossEncounter>();
            case "WATCHER_RETAIN": return ModelDb.Encounter<WatcherRetainBossEncounter>();
            case "WATCHER_FORESIGHT": return ModelDb.Encounter<WatcherForesightBossEncounter>();
            case "WATCHER_FLURRY": return ModelDb.Encounter<WatcherFlurryBossEncounter>();
#endif
#endif
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

    /// <summary>
    /// 候选表里「按运行期条件才加入」的那几只（目前是 7 只观者）。
    /// 本机真的**用得了**观者 mod 才进候选表，否则空数组 —— 没装时行为和以前一模一样。
    /// </summary>
    /// <summary>
    /// **观者 Boss 的「缺模组就换人」兜底**（用户 2026-11-15）：
    /// > 「如果玩家在一局游戏中遇到观者 boss，但他退出游戏并关闭了观者 mod，
    /// >   用一个别的 boss 替换原本的观者 boss，保证游戏能正常进行（多人时要保持同步）」
    ///
    /// 判据：这只 Key 是观者的，但**现在**本机用不了观者模组（见 <see cref="Core.WatcherModPresence.Usable"/>）。
    /// 替换目标**写死、按幕**（一层愤怒战 / 二层放血战 / 三层塔罗愚）—— 不用随机数、不看本地设置，
    /// 多人两端各跑一遍得到的也是同一只 ⇒ 不会分叉。
    ///
    /// ★ v0.2.922：以前一律换三层 1 号塔罗愚，而塔罗愚是**钉死第三幕**的 Boss ⇒
    ///   一层/二层的观者一旦兜底就是「三层 Boss 打低层玩家」，实测表现是不出牌。
    /// </summary>
    internal static string SubstituteIfUnavailable(string? key)
    {
        try
        {
            // 观者的**所有**设计都要走这条兜底（用户 2026-11-16 新增 Ω观后，
            // 只认 WATCHER_DIVINITY 会让关掉观者模组后那只旧档里的 Ω观不被替换 ⇒ 坏档 + 多人分叉）
            if (key == null) return string.Empty;
            if (!key.StartsWith("WATCHER_", StringComparison.Ordinal)) return key;
#if STS2_111
            if (Core.WatcherModPresence.Usable) return key!;
#else
            return key!;                    // 107 里根本不会有观者 Boss
#endif
            // ★★ v0.2.922（用户 2026-11-24 玩家报的 bug：「2 层没装观者 mod 却遇到保留观，
            //   然后变成不出牌占位的愚者」）：
            //   ① 闸门从 `Installed`（只看有没有名字含 Watcher 的程序集）换成 `Usable`
            //      （卡牌层 + 实现身份 + 角色类型都得解析得到，缺一样就算「用不了」）。
            //   ② 替补**按幕**挑同幕的普通 Boss —— 以前固定 `FOOL_TAROT`，而塔罗愚是**三层** Boss，
            //      塞进一层/二层就是「三层 Boss 打二层玩家」，也是那段「不出牌」的一半原因。
            int act = ActOf(key);
            if (act < 0) act = 2;
            string substitute = SubstituteKeyForAct(act);
            Logger.Log($"[CharBoss] 观者 Boss：这一局是装过观者模组时开的，但现在**用不了**它" +
                       $"（{key}）⇒ 换成同幕的 {substitute} 继续");
            return substitute;
        }
        catch { return key ?? string.Empty; }
    }

    /// <summary>
    /// 「观者 Boss 用不了」时的**替补 Key**（按幕写死 ⇒ 不看随机数、不看本地设置，
    /// 多人两端各跑一遍得到的也是同一只），以及撞车时用的第二选择。
    ///
    /// ★ v0.2.922：为什么要「按幕」——以前一律换三层 1 号「塔罗愚」，
    ///   可塔罗愚的遭遇/怪物是**钉死第三幕**的 ⇒ 一层、二层的观者 Boss 一旦兜底，
    ///   玩家就会在二层遇到三层的 Boss（血量/牌序/脚本全按三层算），实测表现就是**不出牌**。
    /// </summary>
    private static (string First, string Second) SubstituteKeysForAct(int act)
    {
        switch (act)
        {
            case 0: return ("IRONCLAD_ANGER", "IRONCLAD_STRIKE");      // 一层：愤怒战 / 打击战
            case 1: return ("IRONCLAD_BLOOD", "IRONCLAD_JUGGLER");     // 二层：放血战 / 杂耍战
            default:
#if STS2_113
                return ("FOOL_TAROT", "IRONCLAD_VULN");               // 三层：塔罗愚 / 易伤战
#else
                return ("IRONCLAD_VULN", "IRONCLAD_BOULDER");         // 0.111 没有愚者 ⇒ 两只铁甲
#endif
        }
    }

    /// <summary>「观者 Boss 用不了」时换成的那只 Key（按幕）。</summary>
    internal static string SubstituteKeyForAct(int act) => SubstituteKeysForAct(act < 0 ? 2 : act).First;

    /// <summary>
    /// 观者遭遇用不了 ⇒ 换成**同幕**的替补遭遇（`actModel` 给了就顺手避开本幕另一槽那一只）。
    /// </summary>
    private static EncounterModel? SubstituteEncounterForAct(int act, ActModel? actModel)
    {
        try
        {
            var (first, second) = SubstituteKeysForAct(act < 0 ? 2 : act);
            var pick = EncounterOf(first);
            try
            {
                // 进阶 ≥ 10 的第三幕有两场：替补千万别和另一槽撞成同一只
                var other = actModel?.BossEncounter;
                if (pick != null && other != null && pick.Id == other.Id)
                {
                    var alt = EncounterOf(second);
                    if (alt != null)
                    {
                        Logger.Log($"[CharBoss] 观者 Boss 缺模组：替补 {first} 与本幕另一槽重复 ⇒ 改用 {second}");
                        return alt;
                    }
                }
            }
            catch { /* 忽略 */ }
            return pick;
        }
        catch { return null; }
    }

    /// <summary>
    /// **最后一道闸**（v0.2.806，用户 2026-11-19：「不开启观者 mod 时也可能碰到观者 boss，
    /// 然后就会变成占位愚者」）。
    ///
    /// 上面的 <see cref="SubstituteIfUnavailable"/> 只管「按 **Key** 造遭遇」那条路；
    /// 而真正的 Boss 房走 `ActModel.PullNextEncounter`，它的「**沿用地图上那只**」分支会
    /// **原样返回存档槽里那个遭遇**（那一局是装过观者 mod 时开的）⇒ 观者 mod 不在时观者 Boss 照样上场：
    /// 立绘退化成占位愚者、名字显示成 `Characterbosses`、特殊效果却是「斋戒」。
    /// 这里按**遭遇**再兜一次：观者的遭遇 + 本机用不了观者 mod ⇒ 换成**同幕**的普通 Boss。
    ///
    /// ★ v0.2.922：闸门从 `Installed` 换成 `Usable`，替补也从「固定三层塔罗愚」改成「按幕」。
    /// </summary>
    internal static EncounterModel? FixUnavailableEncounter(EncounterModel? encounter, ActModel? actModel = null)
    {
#if STS2_111
        try
        {
            if (encounter == null) return encounter;
            if (Core.WatcherModPresence.Usable) return encounter;
            string? key = KeyOfEncounter(encounter);
            if (key == null || !key.StartsWith("WATCHER_", StringComparison.Ordinal)) return encounter;
#if STS2_113
            int act = -1;
            try { act = actModel?.Index ?? -1; } catch { act = -1; }
            if (act < 0) act = ActOf(key);
            var substitute = SubstituteEncounterForAct(act, actModel);
            if (substitute != null)
            {
                Logger.Log($"[CharBoss] 观者 Boss：这一场是 {key}（存档槽/地图里留着的），但本机**用不了**观者 mod " +
                           $"⇒ 换成同幕的 {substitute.Id}（占位愚者不上场）");
                return substitute;
            }
            return encounter;
#else
            return encounter;               // 0.111 正式版没有愚者遭遇：替补遭遇可能造不出来，保持原样
#endif
        }
        catch { return encounter; }
#else
        return encounter;
#endif
    }

    /// <summary>
    /// 观者 Boss **这一局能不能进候选表** = 本机真的**用得了**观者模组
    /// （<see cref="Core.WatcherModPresence.Usable"/>：卡牌层 + 实现身份 + 角色类型三样齐）
    /// **且**联机时所有玩家装的都是同一份实现（<see cref="Core.BossPicksSync.WatcherAllowedHere"/>）。
    ///
    /// ★ v0.2.922（用户 2026-11-24 玩家 bug：「2 层没装观者 mod 却遇到保留观」）：
    ///   以前只问 `WatcherModPresence.Installed`（= 有没有名字含 `Watcher` 的程序集）——
    ///   程序集在、但卡牌/角色解析不出来时，观者 Boss 照样被抽中 ⇒ 上场就是
    ///   「名字是保留观、立绘是占位愚者、一张牌都不出」。
    /// </summary>
    private static bool WatcherBossReady()
    {
#if STS2_111
        try
        {
            return Core.WatcherModPresence.Usable && Core.BossPicksSync.WatcherAllowedHere();
        }
        catch { return false; }
#else
        return false;
#endif
    }

    /// <summary>
    /// **第一幕**的观者候补（用户 2026-11-17：一层 1 号「预见观」；
    /// 2026-11-23 追加一层 2 号「疾风观」）。闸门与另外两幕完全相同（用得了观者 mod + 全端同一实现）。
    /// </summary>
    private static string[] WatcherRosterAct1()
    {
#if STS2_111
        try
        {
            if (WatcherBossReady())
                return new[] { "WATCHER_FORESIGHT", "WATCHER_FLURRY" };
        }
        catch { /* 探测失败就当没装 */ }
#endif
        return Array.Empty<string>();
    }

    /// <summary>
    /// **第二幕**的观者候补（2026-11-16：二层 1 号「印牌观」；2026-11-17 追加二层 2 号「保留观」）。
    /// 闸门与另外两幕完全相同（用得了观者 mod + 全端同一实现）。
    /// </summary>
    private static string[] WatcherRosterAct2()
    {
#if STS2_111
        try
        {
            if (WatcherBossReady())
                return new[] { "WATCHER_CREATE", "WATCHER_RETAIN" };
        }
        catch { /* 探测失败就当没装 */ }
#endif
        return Array.Empty<string>();
    }

    private static string[] WatcherRoster()
    {        // ★ 2026-11-15 第二轮（用户要「神格观」上场）：设计做完 ⇒ **恢复登场**。
        //   条件：装了观者模组才进候选表；没装时行为和以前一模一样。
        //
        // ★★ v0.2.711（用户 2026-11-16）：
        //   「只有当所有玩家都装了相同的观者 mod（指统一为 A 或 B 版）时才让观者有可能作为 boss 出现」。
        //   为什么必须加这一条：候补表的**条数**会随观者进出一变，而阵容是
        //   `(种子, 幕, 场, 候补表)` 的纯函数 ⇒ 一端有观者、一端没有，第三幕两场的阵容都可能分叉；
        //   而且客人那边解不出观者时 `SubstituteIfUnavailable` 只会换成**同幕的普通 Boss** ⇒ 两端仍然打不同的 Boss。
        //   判定在 `BossPicksSync.WatcherAllowedHere()`：单人只看本机；多人要「玩家数 − 1」份客人上报、
        //   且每份的观者实现都跟房主一致（客人则用房主广播下来的结论，没收到就保守地不放）。
#if STS2_111
        try
        {
            // 三层两只观者设计：1 号神格观 / 2 号 Ω观（用户 2026-11-16）。
            // 顺序 = 配置界面同一列里的行序（见 BossConfigModConfig 的 PickColumns）。
            if (WatcherBossReady())
                return new[] { "WATCHER_DIVINITY", "WATCHER_OMEGA" };
        }
        catch { /* 探测失败就当没装 */ }
#endif
        return Array.Empty<string>();
    }
}
