using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// 静默猎手（二层：**涂毒猎**）的剧本。用户设计，原话要点记在这里免得后面改乱：
///
///   · 遗物：蛇之戒指、涅奥的祝福、异蛇头骨（每次给敌人中毒 +1 层）、烘焙手套、锚。
///   · 烘焙手套（<c>ToastyMittens</c>）= **烧牌**：原版实现是
///     <c>AfterPlayerTurnStart</c> → <c>CardSelectCmd.FromHand(ExhaustSelectionPrompt, 1)</c> → <c>CardCmd.Exhaust</c>，
///     也就是每回合开始从手牌**消耗 1 张**。剧本里用 <c>Burned</c> 点名"这回合烧哪张"
///     （登记成优先消耗，免得自动选择器乱烧要打的牌）。
///   · 第 1～7 回合烧掉：打击 ×4、防御 ×3；洗牌后（8～10 回合）只烧小刀。
///   · 消耗牌（尖啸/背刺/刀刃之舞/狩猎）与能力牌（涂毒/毒雾/无尽刀刃）不参与洗牌 →
///     洗牌后剩余 9 张：防御 ×2、生存者、中和+、带毒刺击、匕首雨、回响斩击、连续反弹、偏折。
///   · 连续反弹是「奇巧」牌：被生存者丢弃时会**免费自动打出** → 剧本里给它 <c>CostOverride = 0</c>，
///     否则按卡面 2 费规划会被误判成"打不起"。
///   · 每回合 2 费（涅奥祝福 −1：基础 3）。
/// </summary>
public static class SilentPoisonScript
{
    private const int TurnEnergy = 2;

    /// <summary>第二幕用的剧本（10 回合，之后 8～10 循环）。</summary>
    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            Turn("涂毒+（2 费：之后每次攻击的未格挡伤害各给 1 层中毒）、背刺、偏折；打击被烘焙手套烧掉，防御没费", 1,
                C<Envenom>(true), C<Backstab>(), C<Deflect>(),
                C<StrikeSilent>(burned: true), C<DefendSilent>()),

            // ⚠ 用户 2026-10-29 订正（最终口径）：
            //   「生存者（丢弃连续反弹）、连续反弹（奇巧）、防御（被烧）」——
            //   也就是**防御仍然由烘焙手套烧掉**（1~7 回合烧「打击×4、防御×3」的账不变），
            //   本回合真正发生的是：生存者打出（1 费）+ 连续反弹被丢弃后奇巧免费打出。
            //   修的是**选择器**：连续反弹原先漏了 `discardInstead: true`，而且烘焙手套的「消耗」
            //   发生在规划之前（名单为空 ⇒ 按「奇巧优先」把连续反弹烧掉了）。
            //   现在 `BossCardSelector.DeriveFromScript` 会在名单为空时按剧本就地补，
            //   于是手套烧的是剧本点名的防御，生存者弃的才是连续反弹。
            Turn("生存者（弃 1 张 → 连续反弹）、连续反弹（奇巧：被丢弃后免费打出）；防御被烧掉", 2,
                C<Survivor>(), C<Ricochet>(discardInstead: true, costOverride: 0),
                C<DefendSilent>(burned: true)),

            Turn("带毒刺击（6 伤 + 3 毒）、匕首雨（4 伤 ×2）；打击被烧掉", 3,
                C<PoisonedStab>(), C<DaggerSpray>(), C<StrikeSilent>(burned: true)),

            Turn("毒雾（每回合给玩家上毒）、尖啸；防御被烧掉", 4,
                C<NoxiousFumes>(), C<PiercingWail>(), C<DefendSilent>(burned: true)),

            Turn("打击、回响斩击（全体 10）；第三张打击被烧掉", 5,
                C<StrikeSilent>(), C<EchoingSlash>(), C<StrikeSilent>(burned: true)),

            Turn("无尽刀刃（之后每回合 1 刀）、狩猎（10 伤，斩杀加成）；打击被烧掉", 6,
                C<InfiniteBlades>(), C<TheHunt>(), C<StrikeSilent>(burned: true)),

            Turn("小刀、中和、刀刃之舞（3 刀）；防御被烧掉。到此累计烧掉 打击×4、防御×3", 7,
                C<Shiv>(), C<Neutralize>(), C<BladeDance>(), C<DefendSilent>(burned: true)),

            Turn("洗牌：小刀被烧掉；带毒刺击、回响斩击、偏折", 8,
                C<Shiv>(burned: true), C<PoisonedStab>(), C<EchoingSlash>(), C<Deflect>()),

            Turn("小刀被烧掉；生存者、连续反弹（奇巧免费打出）、防御", 9,
                C<Shiv>(burned: true), C<Survivor>(), C<Ricochet>(discardInstead: true, costOverride: 0),
                C<DefendSilent>()),

            Turn("小刀被烧掉；匕首雨、中和+、防御", 10,
                C<Shiv>(burned: true), C<DaggerSpray>(), C<Neutralize>(true), C<DefendSilent>()),
        };

        return new BossScript
        {
            Turns = turns,
            LoopFrom = 8,     // 8～10 回合循环
            LoopLength = 3,
        };
    }

    /// <summary>第二幕的卡组（用户清单，23 张）。</summary>
    public static List<Type> Deck() => new()
    {
        typeof(Envenom), typeof(Backstab), typeof(InfiniteBlades), typeof(NoxiousFumes),
        typeof(DefendSilent), typeof(DefendSilent), typeof(DefendSilent),
        typeof(DefendSilent), typeof(DefendSilent),
        typeof(StrikeSilent), typeof(StrikeSilent), typeof(StrikeSilent), typeof(StrikeSilent),
        typeof(Survivor), typeof(Neutralize), typeof(Ricochet), typeof(DaggerSpray),
        typeof(EchoingSlash), typeof(Deflect), typeof(PoisonedStab), typeof(PiercingWail),
        typeof(BladeDance), typeof(TheHunt),
    };

    /// <summary>遗物（用户清单，5 个）。涅奥的祝福由 <c>CarriesNeowBlessing</c> 自动带，这里写出来是为了顺序显示。</summary>
    public static List<Type> Relics() => new()
    {
        typeof(RingOfTheSnake),   // 蛇之戒指
        typeof(NeowBlessing),     // 涅奥的祝福（已在身上时会被跳过，不会重复）
        typeof(SneckoSkull),      // 异蛇头骨：每次给敌人中毒 +1 层
        typeof(ToastyMittens),    // 烘焙手套：每回合开始消耗手牌 1 张（"烧掉"）
        typeof(Anchor),           // 锚：战斗开始给格挡
    };

    private static BossTurn Turn(string note, int turnIndex, params ScriptCard[] cards)
        => new() { Note = $"{turnIndex}. {note}", Cards = new List<ScriptCard>(cards), Energy = TurnEnergy };

    private static ScriptCard C<T>(bool upgraded = false, bool burned = false, int costOverride = -1,
                                   bool discardInstead = false, string from = "deck")
        where T : MegaCrit.Sts2.Core.Models.CardModel
        => new()
        {
            Card = typeof(T), Upgraded = upgraded, From = from,
            Burned = burned, CostOverride = costOverride,
            // 奇巧牌：被上一张牌丢掉 → 免费自动打出（用户 2026-10-29：涂毒猎第 2 回合漏了这个标记，
            // 于是「生存者」按价值挑走了防御，连续反弹一直躺在手里）
            DiscardInstead = discardInstead,
        };
}
