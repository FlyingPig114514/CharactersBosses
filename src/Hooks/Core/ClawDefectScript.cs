using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// 故障机器人（二层：**物理机**）的剧本 —— 用户 2026-10-24 设计（爪击 / 0 费牌轴）。
///
/// 用户原话要点（写在这里免得后面改乱）：
///   · 遗物：破损核心（起始）、涅奥的祝福、镀金缆线、浮木、手里剑；
///   · 特殊效果「**人工合成**」：故障机器人在**战斗开始时获得 3 层人工制品**
///     （<c>Powers/SynthesisPower.cs</c>）；
///   · 冰寒的生成数量 = **玩家方单位总数**（真人玩家 + 他们的召唤物），
///     但**不算** 佩尔的士兵 / 异鸟宝宝 / 崩坠（含改版）史莱姆老大的史莱姆
///     （实现见 <c>Patches/CombatSidePatches.cs</c> 的 ChillCountDepth）；
///   · 第 4 回合第一张：**单人 = 冷却剂，多人 = 一心化万**
///     （多人换牌表 <c>MultiplayerCardSwap</c> 里有一条 `(1, DefectBoss, Coolant, OneForAll)`）；
///   · 野性（<c>Feral</c>）从第 3 回合起常驻：每回合打出的**第一张 0 费攻击牌会回手**
///     ⇒ 剧本里那些标 `from: "feral"` 的牌就是「被野性捞回来的那张」；
///   · 超越光速（<c>Ftl</c>）本回合打出的牌数够少时会**抽 1 张** ⇒ 标 `from: "ftl-draw"`
///     的螺旋钻机 / 打击就是「被抽上来的」，所以它们排在同一回合的末尾；
///   · 第 7～10 回合循环。
///
/// 能量为什么写死 2：用户给的每回合账都是 2 费（1：暴涨 2＋冰寒 0＋启动流程 0；
/// 3：野性 2 ⇒ 两张爪击 0 费打得出、打击 1 费打不出；4：冷却剂 1＋超越光速 0＋超越光速 0＋充电 1
/// ⇒ 螺旋钻机 0 费打得出去、打击 1 费打不出；5/9：超能光束 2 ⇒ 两张防御都没费）。
/// 充电（<c>ChargeBattery</c>）给的是**下回合**能量（`EnergyNextTurnPower`），
/// 所以它还进了 <c>BossScriptPlanner.DeferredEnergyCard</c>，规划期不会被算成本回合可用能量。
/// </summary>
public static class ClawDefectScript
{
    /// <summary>每回合固定 2 费。</summary>
    private const int TurnEnergy = 2;

    /// <summary>第二幕用的剧本（10 回合，之后 7～10 循环）。</summary>
    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            Turn("暴涨（失去 2 个充能球栏位、+2 力量 +2 敏捷）、冰寒（玩家方每有一个单位生成 1 个冰霜球）、启动流程（10 格挡）", 1,
                C<BulkUp>(), C<Chill>(), C<BootSequence>()),

            Turn("爪击、打击、防御", 2,
                C<Claw>(), C<StrikeDefect>(), C<DefendDefect>()),

            Turn("野性（之后每回合第一张 0 费攻击牌回手）、爪击、爪击（野性捞回来的那张）、热修复（本回合 +2 集中；原第 6 回合那张换到这里）", 3,
                C<Feral>(), C<Claw>(), C<Claw>(false, "feral"), C<Hotfix>()),

            Turn("冷却剂（单人）/一心化万（多人）、超越光速、超越光速（野性捞）、充电（下回合 +1 费）、螺旋钻机（超越光速抽上来的）、打击（抽上来的，没费）", 4,
                C<Coolant>(), C<Ftl>(), C<Ftl>(false, "feral"), C<ChargeBattery>(),
                C<HelixDrill>(false, "ftl-draw"), C<StrikeDefect>(false, "ftl-draw")),

            Turn("超能光束（24 群攻、失去 3 集中）、防御、防御（没费）", 5,
                C<Hyperbeam>(), C<DefendDefect>(), C<DefendDefect>()),

            Turn("打击（原第 3 回合那张换到这里）、打击、防御（2 费只够两张打击，防御没费）", 6,
                C<StrikeDefect>(), C<StrikeDefect>(), C<DefendDefect>()),

            Turn("洗牌。爪击、爪击（野性捞）、打击、防御", 7,
                C<Claw>(), C<Claw>(false, "feral"), C<StrikeDefect>(), C<DefendDefect>()),

            Turn("超越光速、超越光速（野性捞）、充电、防御、螺旋钻机（抽上来的）、打击（抽上来的，没费）", 8,
                C<Ftl>(), C<Ftl>(false, "feral"), C<ChargeBattery>(), C<DefendDefect>(),
                C<HelixDrill>(false, "ftl-draw"), C<StrikeDefect>(false, "ftl-draw")),

            Turn("超能光束、防御、防御（没费）", 9,
                C<Hyperbeam>(), C<DefendDefect>(), C<DefendDefect>()),

            Turn("爪击、爪击（野性捞）、打击、防御", 10,
                C<Claw>(), C<Claw>(false, "feral"), C<StrikeDefect>(), C<DefendDefect>()),
        };

        return new BossScript
        {
            Turns = turns,
            LoopFrom = 7,     // 7～10 回合循环
            LoopLength = 4,
        };
    }

    /// <summary>第二幕的卡组（剧本实际造牌，这里用于展示与自检）。</summary>
    public static List<Type> Deck() => new()
    {
        typeof(BulkUp), typeof(Chill), typeof(BootSequence),
        typeof(Claw), typeof(Claw),
        typeof(StrikeDefect), typeof(StrikeDefect),
        typeof(DefendDefect), typeof(DefendDefect), typeof(DefendDefect),
        typeof(Feral), typeof(Coolant),
#if STS2_111
        typeof(OneForAll),      // 一心化万（1 费能力牌，111 才有；107 上这张牌不存在）
#endif
        typeof(Ftl), typeof(Ftl), typeof(ChargeBattery),
        typeof(HelixDrill), typeof(Hyperbeam), typeof(Hotfix),
    };

    /// <summary>遗物（用户给的 5 件；破损核心是起始遗物，写出来只为顺序显示）。
    /// 2026-10-24 用户把「手里剑」换成「数据磁盘」（战斗开始 +1 集中）。</summary>
    public static List<Type> Relics() => new()
    {
        typeof(CrackedCore),        // 破损核心（起始）：战斗开始引导 1 个闪电球
        typeof(NeowBlessing),       // 涅奥的祝福（已在身上时会被跳过）
        typeof(GoldPlatedCables),   // 镀金缆线：最右侧的球额外触发一次被动
        typeof(Driftwood),          // 浮木
        typeof(DataDisk),           // 数据磁盘：战斗开始 +1 集中
    };

    private static BossTurn Turn(string note, int turnIndex, params ScriptCard[] cards)
        => new() { Note = $"{turnIndex}. {note}", Cards = new List<ScriptCard>(cards), Energy = TurnEnergy };

    private static ScriptCard C<T>(bool upgraded = false, string from = "deck")
        where T : MegaCrit.Sts2.Core.Models.CardModel
        => new() { Card = typeof(T), Upgraded = upgraded, From = from };
}
