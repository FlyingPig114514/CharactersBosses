using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// 二层亡灵契约师**第二套设计「虚无骨」**（Key `NECROBINDER_VOID`，用户 2026-11-09 设计）。
/// 同层第一套是「灵魂骨」（<see cref="SoulBoneScript"/>）。
///
/// 用户原话（逐条照抄，改设计时对着看）：
/// > 2层2号亡灵契约师boss虚无骨
/// > 特殊效果：虚空之唤：亡灵契约师首次打出女妖之嚎时，所有玩家的所有卡牌获得虚无，直到亡灵契约师死亡。
/// > 遗物：缚魂命匣、涅奥的祝福、不安油灯、空鸟笼、臂甲
/// > 进阶8：升级灰烬之灵、违逆。进阶9：升级友谊。
/// > 1.违逆、防御、防御（没费）
/// > 2.打击、恐惧、领域（没费）
/// > 3.玷污、友谊、领会（没费）
/// > 4.女妖之嚎（此时为3费）、鬼火、打击
/// > 5.致死性、亡魂牵引、防御
/// > 6.灰烬之灵、弱化之触、防御
/// > 7.女妖之嚎（0费）、违逆、防御
/// > 8.玷污、打击、防御
/// > 9.亡魂牵引、防御、防御
/// > 10.弱化之触、打击、恐惧
/// > 之后7～10循环
/// > PS：特殊效果「虚空之唤」的实现方法为第四回合的那张女妖之嚎会给所有玩家添加恶咒
/// >     （记得给那一张女妖之嚎添加策略意图，后面的不添加）。
/// >     多人时，记得让亡灵契约师（虚无骨）死亡后移除所有玩家的恶咒。
///
/// ## 为什么「女妖之嚎」的费用要写死
/// 它的卡面是「对所有敌人造成 33 点伤害；**本场战斗中每打出过一张虚无牌，此牌的耗能就减少 2**」
/// （反编译 `BansheesCry`，基础 9 费）。而她的牌是**每回合按剧本重建、由 `CardCmd.AutoPlay` 打出**的，
/// 战斗历史里的「虚无牌」计数和玩家打牌时并不一样 ⇒ 卡面自然算出来的费用不可靠。
/// 用户给的剧本明确写了「第 4 回合**此时为 3 费**、第 7 回合 **0 费**」⇒ 这两张用
/// `ScriptCard.CostThisTurn` **写死**。
/// </summary>
public static class VoidBoneScript
{
    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            // 1：违逆、防御、防御（第三张没费）
            new BossTurn
            {
                Note = "1. 违逆、防御；防御（没费）",
                Energy = 2,
                Cards = new List<ScriptCard>
                {
                    C<Defy>(), C<DefendNecrobinder>(), C<DefendNecrobinder>(),
                },
            },

            // 2：打击、恐惧、领域（没费）
            new BossTurn
            {
                Note = "2. 打击、恐惧；领域（没费）",
                Energy = 2,
                Cards = new List<ScriptCard>
                {
                    C<StrikeNecrobinder>(), C<Fear>(), C<Demesne>(),
                },
            },

            // 3：玷污、友谊、领会（没费）
            new BossTurn
            {
                Note = "3. 玷污、友谊；领会（没费）",
                Energy = 2,
                Cards = new List<ScriptCard>
                {
                    C<Defile>(), C<Friendship>(), C<Parse>(),
                },
            },

            // 4：女妖之嚎（写死 3 费，**带策略意图**）、鬼火、打击
            //    ★ 这一张是「虚空之唤」的触发点（首次打出女妖之嚎 ⇒ 所有玩家挂恶咒）。
            new BossTurn
            {
                Note = "4. 女妖之嚎（3 费，虚空之唤：首次给她全体挂恶咒）、鬼火、打击",
                // 女妖 3 + 鬼火 0（回 1） + 打击 1 ⇒ 起手要 3 费
                Energy = 3,
                Cards = new List<ScriptCard>
                {
                    Cost(BansheeWail(), 3, debuffIntent: true),
                    C<Wisp>(), C<StrikeNecrobinder>(),
                },
            },

            // 5：致死性、亡魂牵引、防御
            new BossTurn
            {
                Note = "5. 致死性、亡魂牵引、防御",
                Energy = 3,
                Cards = new List<ScriptCard>
                {
                    C<Lethality>(), C<PullFromBelow>(), C<DefendNecrobinder>(),
                },
            },

            // 6：灰烬之灵、弱化之触、防御
            new BossTurn
            {
                Note = "6. 灰烬之灵、弱化之触、防御",
                Energy = 3,
                Cards = new List<ScriptCard>
                {
                    C<SpiritOfAsh>(), C<EnfeeblingTouch>(), C<DefendNecrobinder>(),
                },
            },

            // 7：女妖之嚎（写死 0 费，**不带**策略意图）、违逆、防御
            new BossTurn
            {
                Note = "7. 女妖之嚎（0 费）、违逆、防御",
                Energy = 2,
                Cards = new List<ScriptCard>
                {
                    Cost(BansheeWail(), 0),
                    C<Defy>(), C<DefendNecrobinder>(),
                },
            },

            // 8：玷污、打击、防御
            new BossTurn
            {
                Note = "8. 玷污、打击、防御",
                Energy = 3,
                Cards = new List<ScriptCard>
                {
                    C<Defile>(), C<StrikeNecrobinder>(), C<DefendNecrobinder>(),
                },
            },

            // 9：亡魂牵引、防御、防御
            new BossTurn
            {
                Note = "9. 亡魂牵引、防御、防御",
                Energy = 3,
                Cards = new List<ScriptCard>
                {
                    C<PullFromBelow>(), C<DefendNecrobinder>(), C<DefendNecrobinder>(),
                },
            },

            // 10：弱化之触、打击、恐惧
            new BossTurn
            {
                Note = "10. 弱化之触、打击、恐惧",
                Energy = 3,
                Cards = new List<ScriptCard>
                {
                    C<EnfeeblingTouch>(), C<StrikeNecrobinder>(), C<Fear>(),
                },
            },
        };

        return new BossScript
        {
            Turns = turns,
            LoopFrom = 7,       // 用户：「之后 7～10 循环」
            LoopLength = 4,
        };
    }

    /// <summary>卡组（按剧本里出现的最大张数收集；只用于展示、自检与抽牌堆张数）。</summary>
    public static List<Type> Deck() => new()
    {
        typeof(Defy), typeof(Defy),
        typeof(DefendNecrobinder), typeof(DefendNecrobinder),
        typeof(StrikeNecrobinder), typeof(StrikeNecrobinder),
        typeof(Fear), typeof(Fear),
        typeof(Demesne),
        typeof(Defile), typeof(Defile),
        typeof(Friendship),
        typeof(Parse),
        typeof(BansheesCry), typeof(BansheesCry),
        typeof(Wisp),
        typeof(Lethality),
        typeof(PullFromBelow), typeof(PullFromBelow),
        typeof(SpiritOfAsh),
        typeof(EnfeeblingTouch), typeof(EnfeeblingTouch),
    };

    /// <summary>遗物（用户清单，顺序照抄）：缚魂命匣 / 涅奥的祝福 / 不安油灯 / 空鸟笼 / 臂甲。</summary>
    public static List<Type> Relics() => new()
    {
        typeof(BoundPhylactery),      // 缚魂命匣
        typeof(NeowBlessing),         // 涅奥的祝福（所有 Boss 都是第 2 件）
        typeof(UnsettlingLamp),       // 不安油灯
        typeof(EmptyCage),            // 空鸟笼
        typeof(Vambrace),             // 臂甲
    };

    // ── 剧本小工具 ─────────────────────────────────────────────

    private static ScriptCard C<T>(bool upgraded = false) where T : MegaCrit.Sts2.Core.Models.CardModel
        => new() { Card = typeof(T), Upgraded = upgraded, From = "deck" };

    /// <summary>女妖之嚎（`BansheesCry`）——单独拿出来是因为两张的「写死费用 + 是否带策略意图」不一样。</summary>
    private static ScriptCard BansheeWail()
        => new() { Card = typeof(BansheesCry), From = "deck" };

    /// <summary>
    /// 把这张牌**本回合的费用写死**，并可要求它显示**策略（减益）意图**。
    ///
    /// 为什么需要 `ForceDebuffIntent`：女妖之嚎自己只造成伤害、卡面上没有任何减益变量，
    /// 所以意图生成器只会画一条攻势；但用户要求「第 4 回合那一张标出策略意图」
    /// （因为那一手会通过「虚空之唤」给所有玩家挂恶咒），后面那些不标 ⇒ 只能在剧本里显式点名。
    /// </summary>
    private static ScriptCard Cost(ScriptCard card, int cost, bool debuffIntent = false)
        => new()
        {
            Card = card.Card,
            Upgraded = card.Upgraded,
            From = card.From,
            CostThisTurn = cost,
            ForceDebuffIntent = debuffIntent,
        };
}
