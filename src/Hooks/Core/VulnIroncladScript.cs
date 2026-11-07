using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// 铁甲战士（三层：**易伤战**）的剧本 —— 用户 2026-10-24 设计、**2026-10-25 大改**（易伤轴）。
///
/// 用户原话要点（2026-10-25 版）：
///   · 卡组（14 张）：战栗、岿然不动、岩石铠甲、拆卸、焚烧、挑衅、
///     主宰（**附魔灵魂之力**）、打击×3、防御×3、痛击；
///   · 遗物：燃烧之血、涅奥的祝福、意外光滑的石头、佩尔的士兵、**奥利哈钢**、低语耳环、**臂甲**；
///   · 特殊效果「**主宰**」：每当铁甲战士给予易伤时，同样给予 **1 层虚弱**（`Powers/DominateWeakPower.cs`）；
///   · 进阶 8：升级岩石铠甲 + 获得**护喉甲**；进阶 9：升级主宰（都写在 `BossDefs.Ironclad`）；
///   · 多人同场时**永远排最右边**（见 `Patches/BossEnemySpreadPatch.cs`）；
///   · 每回合 **3 费**（这一版把吊灯换掉了，所以不再有第 3 回合的额外能量）；
///   · 第 **6～9** 回合循环。
///
/// 剧本里两张「主宰」（第 3、第 8 回合）**都**附魔灵魂之力（`SoulsPower`）。
/// </summary>
public static class VulnIroncladScript
{
    /// <summary>每回合固定 3 费。</summary>
    private const int TurnEnergy = 3;

    /// <summary>第三幕用的剧本（9 回合，之后 6～9 循环）。</summary>
    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            Turn("岩石铠甲（4 层覆甲）、防御、战栗（3 层易伤）", 1,
                C<StoneArmor>(), C<DefendIronclad>(), C<Tremble>()),

            Turn("拆卸（有易伤就打两次）、焚烧（群攻 2×4）、挑衅（6 格挡 + 1 易伤）", 2,
                C<Dismantle>(), C<Conflagration>(), C<Taunt>()),

            Turn("岿然不动（30 格挡）、主宰+灵魂之力（易伤换力量）、打击（没费）", 3,
                C<Impervious>(), Souls<Dominate>(), C<StrikeIronclad>()),

            Turn("打击、欺凌（0 费，每层易伤 +2）、闪电霹雳（群攻 4 + 1 易伤）", 4,
                C<StrikeIronclad>(), C<Bully>(), C<Thunderclap>()),

            Turn("防御、痛击（8 伤 + 2 易伤）、防御（没费）", 5,
                C<DefendIronclad>(), C<Bash>(), C<DefendIronclad>()),

            Turn("洗牌。拆卸、打击、挑衅", 6,
                C<Dismantle>(), C<StrikeIronclad>(), C<Taunt>()),

            Turn("焚烧、防御、闪电霹雳", 7,
                C<Conflagration>(), C<DefendIronclad>(), C<Thunderclap>()),

            Turn("欺凌（0 费）、防御、主宰+灵魂之力", 8,
                C<Bully>(), C<DefendIronclad>(), Souls<Dominate>()),

            Turn("打击、痛击、防御（没费）", 9,
                C<StrikeIronclad>(), C<Bash>(), C<DefendIronclad>()),
        };

        return new BossScript
        {
            Turns = turns,
            LoopFrom = 6,     // 6～9 回合循环（2026-10-25 用户改）
            LoopLength = 4,
        };
    }

    /// <summary>第三幕的卡组（用户清单 14 张）。</summary>
    public static List<Type> Deck() => new()
    {
        typeof(Tremble), typeof(Impervious), typeof(StoneArmor),
        typeof(Dismantle), typeof(Conflagration), typeof(Taunt), typeof(Dominate),
        typeof(StrikeIronclad), typeof(StrikeIronclad), typeof(StrikeIronclad),
        typeof(DefendIronclad), typeof(DefendIronclad), typeof(DefendIronclad),
        typeof(Bash),
    };

    /// <summary>遗物（用户清单 7 件；燃烧之血是起始遗物，写出来只为顺序显示）。</summary>
    public static List<Type> Relics() => new()
    {
        typeof(BurningBlood),        // 燃烧之血（起始）
        typeof(NeowBlessing),        // 涅奥的祝福（已在身上时会被跳过）
        typeof(OddlySmoothStone),    // 意外光滑的石头
        typeof(PaelsLegion),         // 佩尔的士兵
        typeof(Orichalcum),          // 奥利哈钢：回合结束没有格挡 → +6 格挡（走 BossRelicHookBridge 补跑）
        typeof(WhisperingEarring),   // 低语耳环
        typeof(Vambrace),            // 臂甲：本场第一次从卡牌获得的格挡翻倍
    };

    private static BossTurn Turn(string note, int turnIndex, params ScriptCard[] cards)
        => new() { Note = $"{turnIndex}. {note}", Cards = new List<ScriptCard>(cards), Energy = TurnEnergy };

    private static ScriptCard C<T>(bool upgraded = false, string from = "deck")
        where T : MegaCrit.Sts2.Core.Models.CardModel
        => new() { Card = typeof(T), Upgraded = upgraded, From = from };

    /// <summary>附魔「灵魂之力」的那张牌。</summary>
    private static ScriptCard Souls<T>(bool upgraded = false)
        where T : MegaCrit.Sts2.Core.Models.CardModel
        => new() { Card = typeof(T), Upgraded = upgraded, From = "deck", EnchantSouls = true };
}
