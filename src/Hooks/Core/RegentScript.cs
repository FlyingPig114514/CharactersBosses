using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// 储君（二层：**铸剑储君**）的剧本。用户设计（原话要点）：
///
///   · 核心是**铸造（Forge）**：熔炉/招架/崇拜/追踪之刃这些牌会铸造，
///     「君王之剑」（<c>SovereignBlade</c>，衍生牌）的伤害随铸造值增长；
///     战斗开始时「击剑指南」(<c>FencingManual</c>) 会先铸造一次并把君王之剑放进他手里
///     —— 所以第 1 回合虽然多了这张牌，抽牌数不变。
///   · 遗物：天赋君权(<c>DivineRight</c>)、涅奥的祝福(本 mod)、击剑指南(<c>FencingManual</c>)、
///     佩尔的士兵(<c>PaelsLegion</c>)、迷你储君(<c>MiniRegent</c>)。
///   · 特殊效果「何人僭越」（与卡同名）：一回合内他每累计失去 25 点生命，
///     就失去 3 点铸造，然后所有玩家本回合失去 1 点力量；每回合最多 3 次。
///   · 9 回合之后 5～9 循环。
///
/// 每回合能量按用户意图里的"能打出/打不出"倒推写死（2,2,2,2,4,2,2,2,3）：
/// 例如第 1 回合 君王之剑(2) + 群星斗篷(0) 正好 2 费 → 两张防御没费；
/// 第 5 回合 剑圣+(2) + 打击(1) + 打击(1) = 4 费 → 防御没费。
/// </summary>
public static class RegentScript
{
    /// <summary>第二幕用的剧本（9 回合，之后 5～9 循环）。</summary>
    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            Turn(2, "君王之剑（击剑指南在战斗开始就铸造并把剑放进他手里，所以本回合抽牌数不变）、群星斗篷（迷你储君 +1 力、佩尔的士兵给 14 格挡）；两张防御没费", 1,
                C<SovereignBlade>(), C<CloakOfStars>(),
                C<DefendRegent>(), C<DefendRegent>()),

            // 用户订正（2/3 回合互换）：第 2 回合从「下砸、招架」改成「熔炉、崇拜」。
            Turn(2, "熔炉、崇拜、何人僭越", 2,
                C<Furnace>(), C<Venerate>(), C<KnowThyPlace>()),

            Turn(2, "下砸、招架、陨星（易伤 + 力量）", 3,
                C<CrushUnder>(), C<Parry>(), C<FallingStar>()),

            Turn(2, "追踪之刃、汇流；剑圣+ 没费打不出（被汇流保留，第 5 回合才打，**只打一次**）", 4,
                C<SeekingEdge>(), C<Convergence>(),
                // 剑圣+ 这一回合**要显示在手里但打不出**（费不够，被汇流保留到第 5 回合）：
                // 用展示牌的方式造进手牌、规划期跳过 → 亮/暗规则里它落进「暗」的那一类。
                // 用户实测指出：之前剧本里干脆不列它 → 手牌重建后它直接消失（应为暗牌）。
                new() { Card = typeof(SwordSage), Upgraded = true, From = "display", DisplayOnly = true }),

            // 能量 3 = 基础 2 + 上一回合「汇流」给的下回合 +1（剧本把这两笔直接写进回合能量）。
            // 于是 剑圣+(1) + 打击(1) + 打击(1) = 3 正好打完，**防御没费**（留在手里、压暗）。
            // 另外必须用 FreeTop() 标上 FreePlayTopEntry —— 以前这里只写了 Show()，
            // 注释说着「回合结束免费打所向无敌」，实际没有任何标记，所以那句从来没发生过（用户实测）。
            Show(FreeTop(Turn(3, "剑圣+、打击、锻打成型；防御没费（能量 2+1=3）。所向无敌在他回合结束时正好在抽牌堆顶 → 免费打出", 5,
                C<SwordSage>(true), C<StrikeRegent>(), C<BeatIntoShape>(), C<DefendRegent>()))),

            Turn(2, "洗牌：下砸、防御；第二张防御没费", 6,
                C<CrushUnder>(), C<DefendRegent>(), C<DefendRegent>()),

            Turn(2, "君王之剑（铸造已叠高）、群星斗篷（+1 力）；防御没费", 7,
                C<SovereignBlade>(), C<CloakOfStars>(), C<DefendRegent>()),

            Turn(2, "崇拜、汇流、陨星", 8,
                C<Venerate>(), C<Convergence>(), C<FallingStar>()),

            Show(FreeTop(Turn(3, "打击、打击、锻打成型。所向无敌在他回合结束时正好在抽牌堆顶 → 免费打出", 9,
                C<StrikeRegent>(), C<StrikeRegent>(), C<BeatIntoShape>()))),
        };

        return new BossScript
        {
            Turns = turns,
            LoopFrom = 6,     // 6～9 回合循环（用户订正）
            LoopLength = 4,
        };
    }

    /// <summary>第二幕的卡组（用户清单，16 张）。</summary>
    public static List<Type> Deck() => new()
    {
        typeof(Furnace), typeof(SeekingEdge), typeof(Parry), typeof(KnowThyPlace),
        typeof(SwordSage), typeof(CrushUnder), typeof(Convergence), typeof(IAmInvincible),
        typeof(BeatIntoShape),
        typeof(CloakOfStars),
        typeof(StrikeRegent), typeof(StrikeRegent),
        typeof(DefendRegent), typeof(DefendRegent), typeof(DefendRegent),
        typeof(FallingStar), typeof(Venerate),
    };

    /// <summary>遗物（用户清单）。涅奥的祝福由 <c>CarriesNeowBlessing</c> 自动带，这里写出来是为了顺序显示。</summary>
    public static List<Type> Relics() => new()
    {
        typeof(DivineRight),    // 天赋君权
        typeof(NeowBlessing),   // 涅奥的祝福（已在身上时会被跳过）
        typeof(FencingManual),  // 击剑指南：战斗开始铸造并给君王之剑
        typeof(PaelsLegion),    // 佩尔的士兵
        typeof(MiniRegent),     // 迷你储君
    };

    /// <summary>
    /// 第三幕：**无色储君**（用户设计）。
    ///
    /// 特殊效果「共鸣」：**玩家生成卡牌时，也会触发储君的「创世之柱」**
    /// （＝玩家那边每生成一张牌，储君按自己创世之柱的层数补一次格挡）。
    /// 卡组走「生成牌」轴：光谱偏移 / 君权自授 / 发现 / 机关盒 都在印牌，
    /// 配合超质量体（按"自己生成过几张牌"加伤）、究极打击/流星锤这些生成的牌。
    /// 14 回合，之后 12～14 循环。
    /// </summary>
    public static BossScript BuildAct3()
    {
        var turns = new List<BossTurn>
        {
            T("光谱偏移（机关盒印的，本回合 0 费）、创世之柱（进阶 8 时升级）、流光溢彩+、超质量体", 1,
                new ScriptCard { Card = typeof(SpectrumShift), From = "box", FreeThisTurn = true },
                new ScriptCard { Card = typeof(PillarOfCreation), UpgradeMinAscension = 8 },
                C<Glitterstream>(true), C<Supermassive>()),

            T("闪亮登场（印牌）、炸弹、下砸+、何人僭越+", 2,
                C<DramaticEntrance>(false, "generated"), C<TheBomb>(), C<CrushUnder>(true),
                C<KnowThyPlace>(true)),

            T("发现（印创世之柱，本回合 0 费）、创世之柱、君权自授（印究极打击）、究极打击；究极防御+没费", 3,
                C<Discovery>(),
                // 这张创世之柱就是「发现」印出来的那张 → 本回合 0 费（用户订正）
                new ScriptCard { Card = typeof(PillarOfCreation), FreeThisTurn = true },
                C<ManifestAuthority>(), C<UltimateStrike>(), C<UltimateDefend>(true)),

            T("黑暗镣铐、君权自授（印流星锤）、流星锤、炸弹；防御没费", 4,
                C<DarkShackles>(), C<ManifestAuthority>(), C<Bolas>(), C<TheBomb>(), C<DefendRegent>()),

            T("非凡技艺、流星锤、超质量体、下砸+、流光溢彩+没费", 5,
                C<Prowess>(), C<Bolas>(), C<Supermassive>(), C<CrushUnder>(true), C<Glitterstream>(true)),

            T("黑暗镣铐、流星锤、究极防御+、防御、何人僭越+", 6,
                C<DarkShackles>(), C<Bolas>(), C<UltimateDefend>(true), C<DefendRegent>(), C<KnowThyPlace>(true)),

            T("闪亮登场、流星锤、炸弹、究极打击、何人僭越+", 7,
                C<DramaticEntrance>(false, "generated"), C<Bolas>(), C<TheBomb>(), C<UltimateStrike>(),
                C<KnowThyPlace>(true)),

            T("非凡技艺、流星锤、超质量体、下砸+、究极防御+", 8,
                C<Prowess>(), C<Bolas>(), C<Supermassive>(), C<CrushUnder>(true), C<UltimateDefend>(true)),

            T("黑暗镣铐、流星锤、流光溢彩+、君权自授、延伸、防御没费", 9,
                C<DarkShackles>(), C<Bolas>(), C<Glitterstream>(true), C<ManifestAuthority>(),
                C<Prolong>(), C<DefendRegent>()),
        };

        return new BossScript
        {
            Turns = turns,
            LoopFrom = 7,     // 7～9 回合循环（用户订正）
            LoopLength = 3,
        };
    }

    /// <summary>第三幕「无色储君」的卡组（用户清单 9 张）。</summary>
    public static List<Type> DeckAct3() => new()
    {
        typeof(SpectrumShift), typeof(PillarOfCreation), typeof(TheBomb), typeof(ManifestAuthority),
        typeof(Glitterstream), typeof(CrushUnder), typeof(KnowThyPlace), typeof(UltimateDefend),
        typeof(DefendRegent),
    };

    /// <summary>第三幕「无色储君」的遗物（涅奥的祝福由 CarriesNeowBlessing 自动带）。
    /// ⚠ 君王矿石（Regalite）2026-10-24 从常备遗物里拿掉、改成**进阶 8 的奖励遗物**
    /// （见 <c>BossDefs.Regent.AscensionRelicsByAct</c>）；这里换成永冻冰晶。</summary>
    public static List<Type> RelicsAct3() => new()
    {
        typeof(Lantern),          // 灯笼：第 1 回合 +1 能量
        typeof(YummyCookie),      // 美味饼干
        typeof(VexingPuzzlebox),  // 烦人机关盒：第 1 回合印一张 0 费牌（剧本第 1 回合的超质量体）
        // 用户 2026-10-31：**天鹅绒颈圈 → 贤者之石**。
        //   原因：多人时刀猎的「刀刃交响曲」会让无色储君多出 2 张牌，于是「何人僭越」的打出被颈圈的
        //   「每回合最多 6 张」拦住。贤者之石也是每回合 +1 能量（所以剧本能量/意图都不用改），
        //   额外的「所有敌人 +1 力量」对 Boss 来说就是**给所有玩家 +1 力量**，
        //   由 BossHookBridge 在战斗开始时手动补（影子玩家的遗物收不到原版派发）。
        typeof(PhilosophersStone),   // 贤者之石：+1 能量／每场战斗开始给所有玩家 +1 力量
        typeof(Permafrost),       // 永冻冰晶：本场第一次打出能力牌时给 7 格挡
    };

    /// <summary>第三幕用：不带能量覆盖的回合（能量按定义值 + 遗物算）。</summary>
    private static BossTurn T(string note, int turnIndex, params ScriptCard[] cards)
        => new() { Note = $"{turnIndex}. {note}", Cards = new List<ScriptCard>(cards) };

    private static BossTurn Turn(int energy, string note, int turnIndex, params ScriptCard[] cards)
        => new() { Note = $"{turnIndex}. {note}", Cards = new List<ScriptCard>(cards), Energy = energy };

    private static ScriptCard C<T>(bool upgraded = false, string from = "deck")
        where T : MegaCrit.Sts2.Core.Models.CardModel
        => new() { Card = typeof(T), Upgraded = upgraded, From = from };

    /// <summary>
    /// 在自己这张牌后面补一张**只显示意图**的所向无敌（<c>I_AM_INVINCIBLE</c>）：
    /// 它实际是在他回合结束时从摸牌堆顶免费打出的（<see cref="BossTurn.FreePlayTopEntry"/>），
    /// 但玩家要能在意图里看到这 10 点格挡（用户要求）。
    /// </summary>
    private static BossTurn Show(BossTurn turn)
    {
        var cards = new List<ScriptCard>(turn.Cards)
        {
            // AutoPlayed = 本回合**确实会打出去**（回合结束从摸牌堆顶免费打出）→ 手牌栏画亮。
            // 用户实测：它虽然是「展示牌」，但和剑圣那种「打不出」的牌不能同样压暗。
            new() { Card = typeof(IAmInvincible), From = "display", DisplayOnly = true, AutoPlayed = true },
        };
        return new BossTurn
        {
            Note = turn.Note,
            Cards = cards,
            Energy = turn.Energy,
            FreePlayTopEntry = turn.FreePlayTopEntry,
        };
    }

    /// <summary>
    /// 标记这个回合**结束时**免费打出所向无敌（<c>I_AM_INVINCIBLE</c>）：
    /// 用户设计是「它正好躺在抽牌堆顶 → 被 0 费打出」，实现见 <see cref="BossTurn.FreePlayTopEntry"/>。
    /// </summary>
    private static BossTurn FreeTop(BossTurn turn) => new()
    {
        Note = turn.Note,
        Cards = turn.Cards,
        Energy = turn.Energy,
        FreePlayTopEntry = "I_AM_INVINCIBLE",
    };
}
