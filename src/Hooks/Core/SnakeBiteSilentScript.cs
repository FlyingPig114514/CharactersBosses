using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// 一层静默猎手的**第二套设计「蛇咬猎」**（Key `SILENT_SNAKEBITE`，用户 2026-11-08 设计）。
/// 同层第一套是「奇巧猎」（<see cref="SilentSlyScript"/>）。
///
/// 用户原话：
/// > 特殊效果「**群蛇形态**」（单体）：静默猎手的手牌数没有上限。
/// >   每当你或静默猎手洗牌时，将一张蛇咬洗入自己的抽牌堆。
/// >   ⇒ 补充口径（用户 2026-11-08）：
/// >     ① 她每洗牌一次（剧本第 6 回合）就给自己抽牌堆加 1 张蛇咬（**不用表现动画**），
/// >        于是第 9 回合手牌里的蛇咬比之前多一张；
/// >     ② **被她锁定的玩家**洗牌时，往**那个玩家的**抽牌堆塞 1 张蛇咬，**用怪物塞牌的动画**。
/// > 遗物：蛇之戒指、涅奥的祝福、斗篷扣、红面具
/// > 进阶 8：升级残影、逃脱计划。进阶 9：升级背刺、飞镖。
/// > （9 回合剧本，之后 6～9 循环）
/// > 拦掉所有原版抽牌，但要让速行者正常触发
/// > 每次循环到第九回合，都往她保留在手的蛇咬多一张（之后每回合都会保留），这会使她的飞镖多一段伤害
/// > 令逃脱计划一定会抽到技能牌，每次她打出它都会获得对应格挡
///
/// 能量账：角色基础 3 − 涅奥的祝福 1 = **2 点/回合**（剧本里（没费）那几张就是花完之后打不出的）。
/// </summary>
public static class SnakeBiteSilentScript
{
    private const int TurnEnergy = 2;

    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            // 1：速行者+、背刺、逃脱计划；保留不出：心神不宁 / 蛇咬 / 睡眠不佳
            Turn("1. 速行者+、背刺、逃脱计划（保留不出：心神不宁、蛇咬、睡眠不佳）", 1,
                C<Speedster>(true), C<Backstab>(), C<EscapePlan>(),
                Kept<Restlessness>(), Kept<Snakebite>(), Kept<PoorSleep>()),

            // 2：生存者（弃第二张防御）、防御、防御（被丢弃）
            new BossTurn
            {
                Note = "2. 生存者（弃防御）、防御、防御（被丢弃）",
                Cards = new List<ScriptCard>
                {
                    C<Survivor>(), C<DefendSilent>(), Discard(C<DefendSilent>()),
                    Kept<Restlessness>(), Kept<Snakebite>(), Kept<PoorSleep>(),
                },
                Energy = TurnEnergy,
            },

            // 3：飞镖、残影、打击（没费）
            Turn("3. 飞镖、残影、打击（没费）", 3,
                C<Flechettes>(), C<Blur>(), C<StrikeSilent>(),
                Kept<Restlessness>(), Kept<Snakebite>(), Kept<PoorSleep>()),

            // 4：突然一拳、手上技法、翻越撑击（没费）
            Turn("4. 突然一拳、手上技法、翻越撑击（没费）", 4,
                C<SuckerPunch>(), C<HandTrick>(), C<FlickFlack>(),
                Kept<Restlessness>(), Kept<Snakebite>(), Kept<PoorSleep>()),

            // 5：投掷匕首（弃触不可及）、触不可及（奇巧）、孢子心灵、偏折
            new BossTurn
            {
                Note = "5. 投掷匕首（弃触不可及）、触不可及（奇巧）、孢子心灵、偏折",
                Cards = new List<ScriptCard>
                {
                    C<DaggerThrow>(), SlyDiscard<Untouchable>(), C<SporeMind>(), C<Deflect>(),
                    Kept<Restlessness>(), Kept<Snakebite>(), Kept<PoorSleep>(),
                },
                Energy = TurnEnergy,
            },

            // 6：洗牌；投掷匕首（弃翻越撑击）、翻越撑击（奇巧）、防御；防御（没费）
            new BossTurn
            {
                Note = "6. 洗牌；投掷匕首（弃翻越撑击）、翻越撑击（奇巧）、防御；防御（没费）",
                ShuffleDrawPile = true,
                Cards = new List<ScriptCard>
                {
                    C<DaggerThrow>(), SlyDiscard<FlickFlack>(), C<DefendSilent>(), C<DefendSilent>(),
                    Kept<Restlessness>(), Kept<Snakebite>(), Kept<PoorSleep>(),
                },
                Energy = TurnEnergy,
            },

            // 7：生存者（弃触不可及）、触不可及（奇巧）、突然一拳
            Turn("7. 生存者（弃触不可及）、触不可及（奇巧）、突然一拳", 7,
                C<Survivor>(), SlyDiscard<Untouchable>(), C<SuckerPunch>(),
                Kept<Restlessness>(), Kept<Snakebite>(), Kept<PoorSleep>()),

            // 8：逃脱计划、残影、手上技法、打击（没费）+ 保留不出三张
            Turn("8. 逃脱计划、残影、手上技法、打击（没费）", 8,
                C<EscapePlan>(), C<Blur>(), C<HandTrick>(), C<StrikeSilent>(),
                Kept<Restlessness>(), Kept<Snakebite>(), Kept<PoorSleep>()),

            // 9：飞镖、偏折；保留不出：蛇咬（每循环 +1 张）、心神不宁、睡眠不佳
            Turn("9. 飞镖、偏折（保留不出：蛇咬、心神不宁、睡眠不佳）", 9,
                C<Flechettes>(), C<Deflect>(),
                Kept<Snakebite>(), Kept<Restlessness>(), Kept<PoorSleep>()),
        };

        return new BossScript
        {
            Turns = turns,
            LoopFrom = 6,      // 用户：「6～9 循环」
            LoopLength = 4,
        };
    }

    /// <summary>卡组（按剧本里出现的最大张数收集）。</summary>
    public static List<Type> Deck() => new()
    {
        typeof(Speedster), typeof(Backstab), typeof(Backstab),
        typeof(EscapePlan), typeof(EscapePlan),
        typeof(Restlessness), typeof(Restlessness),
        typeof(Snakebite), typeof(Snakebite), typeof(Snakebite),
        typeof(PoorSleep), typeof(PoorSleep),
        typeof(Survivor), typeof(Survivor),
        typeof(DefendSilent), typeof(DefendSilent), typeof(DefendSilent), typeof(DefendSilent),
        typeof(Flechettes), typeof(Flechettes), typeof(Flechettes),
        typeof(Blur), typeof(Blur), typeof(Blur),
        typeof(StrikeSilent), typeof(StrikeSilent),
        typeof(SuckerPunch), typeof(SuckerPunch),
        typeof(HandTrick), typeof(HandTrick), typeof(HandTrick),
        typeof(FlickFlack), typeof(FlickFlack),
        typeof(DaggerThrow), typeof(DaggerThrow),
        typeof(Untouchable), typeof(Untouchable),
        typeof(SporeMind),
        typeof(Deflect), typeof(Deflect),
    };

    /// <summary>
    /// 遗物（用户 2026-11-08 第一次定稿）：蛇之戒指 / 涅奥的祝福 / 斗篷扣 / 红面具。
    /// ★ 同一天第二次调整（用户：「另外增强一下蛇咬猎，红面具换闪亮口红」）：
    ///   红面具只在**战斗开始**给玩家 1 层虚弱，对「拖着中毒慢慢耗」的她不划算；
    ///   闪亮口红（`SparklingRouge`）在她**第 3 回合**（格挡清空那一刻，遗物自己的
    ///   `AfterBlockCleared` + `TurnNumber == 3` 判定）**+1 力量 / +1 敏捷**，整场都吃 —— 是实打实的增强。
    ///   ⚠ 影子玩家的回合数已经由 `CharBossMonster.PrepareTurn` 里的 `AlignTurnNumber` 对齐，
    ///     所以这个「第 3 回合」判定对她成立（灾厄骨 / 辉星储 已经这么用）。
    /// </summary>
    public static List<Type> Relics() => new()
    {
        typeof(RingOfTheSnake),
        typeof(NeowBlessing),
        typeof(CloakClasp),
        typeof(SparklingRouge),
    };

    /// <summary>「保留不出」：回合结束时留在手里、这一回合**不参与出牌**。</summary>
    private static ScriptCard Kept<T>() where T : CardModel
        => new() { Card = typeof(T), From = "deck", Retain = true, DisplayOnly = true };

    /// <summary>被上一张牌**丢弃**、并且带「奇巧」（丢弃时自动打出）的那张。</summary>
    private static ScriptCard SlyDiscard<T>() where T : CardModel
        => new() { Card = typeof(T), From = "deck", DiscardInstead = true, Sly = true };
    private static ScriptCard Sly(ScriptCard card)
        => new() { Card = card.Card, From = card.From, Sly = true };

    private static ScriptCard Discard(ScriptCard card)
        => new() { Card = card.Card, From = card.From, DiscardInstead = true };

    private static BossTurn Turn(string note, int turnIndex, params ScriptCard[] cards)
        => new()
        {
            Note = $"{turnIndex}. {note}",
            Cards = new List<ScriptCard>(cards),
            Energy = TurnEnergy,
        };

    private static ScriptCard C<T>(bool upgraded = false) where T : CardModel
        => new() { Card = typeof(T), Upgraded = upgraded, From = "deck" };
}
