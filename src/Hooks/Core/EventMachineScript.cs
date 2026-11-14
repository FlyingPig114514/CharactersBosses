using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// 三层「**事件机**」（故障机器人 · 事件牌轴，Key `DEFECT_EVENT`）—— 用户 2026-11-03 设计稿。
///
/// 这是三层故障机器人的**第二套设计**（第一套是「能力机」`DefectScript`），
/// 与「能力机 / 事件机」「根除骨 / 养手骨」「刀猎 / 炼药猎」同一套机制：
/// **以房间遭遇为准**（`DEFECT_EVENT_BOSS_ENCOUNTER`），拿不到遭遇才按种子 50/50。
///
/// 完整设计稿（用户原话 + 名字→类对照表）见
/// `docs/plans/boss-snake-eyes-and-event-machine.md` 第二节。
///
/// ## 特殊效果「一心化万」
/// 「故障机器人的万物一心只会将弃牌堆中的 0 费**攻击牌**放回它的手牌。
///   每当它有一张牌从弃牌堆回到手牌，**所有单位**都获得 1 点力量。」
/// 原版 `AllForOne` 会把 0 费的攻击/技能/能力**全部**拿回来（反编译 `Filter`），
/// 而且不给力量 ⇒ 这里由剧本点名「返手哪几张」（<see cref="ScriptCard.ReturnsToHand"/>），
/// 原版那条随机收窄由 `Patches/BossAllForOneFilterPatch` 关掉，
/// 真正的「搬牌 + 全体加力量」在 <see cref="EventMachineMechanics.AllForOneReturnsAsync"/>。
///
/// ## 为什么很多牌写着 0 费
/// 她的牌有一大半是**其它牌印出来的**（原版效果都是「本场/本回合免费打出」）：
///   · 羽化（<c>Metamorphosis</c>）→ 印 3 张攻击牌，**本场**免费 ⇒ 万物一心 / 冰之长枪 / 超能光束 全 0 费；
///   · 声东击西（<c>Distraction</c>）→ 印 1 张**技能**，本回合免费 ⇒ 白噪声 0 费；
///   · 白噪声（<c>WhiteNoise</c>）→ 印 1 张**能力**，本回合免费 ⇒ 吞噬暗影 0 费；
///   · 疯狂科学（<c>MadScience</c> 的 Chaos rider）→ 印 1 张牌，本回合免费 ⇒ 启动流程/能量涌动、全息影像 0 费。
/// 原版那几条随机印牌都被 `Patches/ShadowBossNoRandomCardPatch` 拦掉了
/// （不然会多出一张和剧本不一样、意图也不可预测的牌），
/// 印出来的那张牌**由剧本直接摆在手上**（`From = "print"`），费用写 0。
///
/// ## 羽化的展示
/// 羽化印进的是**抽牌堆**（玩家看不见），所以只有它需要「摆出来给玩家看一眼」；
/// 印进**手牌**的那几张（疯狂科学等）本来就看得见，不展示（用户 2026-11-03 明确要求）。
///
/// ## 能量
/// 基础 3 − 涅奥的祝福 1 + **南瓜蜡烛**（`PumpkinCandle`：亮着时每回合 +1 能量）1 = **3**，
/// 逐回合写死 3（蜡烛的充能是「每场战斗结束 -1」，一场战斗内不会熄灭）。
/// 各回合花掉的能量都 ≤ 3，见每回合的 Note。
/// </summary>
public static class EventMachineScript
{
    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            // 1：羽化 2 + 飞跃 1 = 3
            new()
            {
                Note = "1. 羽化(2)：把 0 费的万物一心 / 冰之长枪 / 超能光束印进抽牌堆并展示；飞跃(1，选择悖论印的，带保留)",
                Energy = 3,
                Cards = Cards(
                    Prints<Metamorphosis>(2, typeof(AllForOne), typeof(IceLance), typeof(Hyperbeam)),
                    Free<Leap>(1, retain: true)),
            },

            // 2：声东击西 1 + 白噪声 0 + 吞噬暗影 0 + 冰之长枪 0 + 双重释放 1 = 2
            new()
            {
                Note = "2. 声东击西(1，印白噪声)、白噪声(0，印吞噬暗影)、吞噬暗影(0)、冰之长枪(0)、双重释放(1)",
                Energy = 3,
                Cards = Cards(
                    Play<Distraction>(1), Free<WhiteNoise>(0), Free<ConsumingShadow>(0),
                    Free<IceLance>(0), Play<Dualcast>(1)),
            },

            // 3：疯狂科学 1 + 启动流程/能量涌动 0 + 超能光束 0 = 1（疑虑是诅咒，打不出）
            new()
            {
                Note = "3. 疯狂科学(1，印启动流程/能量涌动)、启动流程 单人 / 能量涌动 多人(0)、超能光束(0)；疑虑留手",
                Energy = 3,
                Cards = Cards(
                    Play<MadScience>(1), Free<BootSequence>(0), Free<Hyperbeam>(0), Curse<Doubt>()),
            },

            // 4：全是 0 费；万物一心返手 2 张 ⇒ 所有单位 +2 力量
            new()
            {
                Note = Core.DreadMode.IsActive
                    ? "4. 疯狂进食(0)、热修复+(0)、万物一心(0，返手超能光束+冰之长枪 ⇒ 所有单位 +2 力量)、疯狂进食+(0)、冰之长枪(0)、超能光束(0)"
                    : "4. 疯狂进食(0)、热修复+(0)、万物一心(0，返手超能光束+冰之长枪 ⇒ 所有单位 +2 力量)、冰之长枪(0)、超能光束(0)",
                Energy = 3,
                Cards = Core.DreadMode.IsActive
                    ? Cards(
                        Free<FeedingFrenzy>(0), Free<Hotfix>(0, upgraded: true),
                        Returns<AllForOne>(0, typeof(IceLance), typeof(Hyperbeam)),
                        Free<FeedingFrenzy>(0, upgraded: true),   // 2026-11-13 (dread): 万物一心后再补一张疯狂进食+
                        Free<IceLance>(0), Free<Hyperbeam>(0))
                    : Cards(
                        Free<FeedingFrenzy>(0), Free<Hotfix>(0, upgraded: true),
                        Returns<AllForOne>(0, typeof(IceLance), typeof(Hyperbeam)),
                        Free<IceLance>(0), Free<Hyperbeam>(0)),
            },

            // 5：防御（附魔漩涡）1 + 打击 1 = 2（贪婪是诅咒）
            new()
            {
                Note = "5. 防御(1，附魔「漩涡」打出两次)、打击(1)、贪婪留手",
                Energy = 3,
                Cards = Cards(
                    Spiral<DefendDefect>(1), Play<StrikeDefect>(1), Curse<Greed>()),
            },

            // 6：超能光束 0 + 飞跃 1 + 冰之长枪 0 = 1
            new()
            {
                Note = "6. 超能光束(0)、飞跃(1)、冰之长枪(0)",
                Energy = 3,
                Cards = Cards(
                    Free<Hyperbeam>(0), Play<Leap>(1), Free<IceLance>(0)),
            },

            // 7：疯狂科学 1 + 全息影像 0 + 冰之长枪 0 + 双重释放 1 = 2（贪婪是诅咒）
            new()
            {
                Note = "7. 疯狂科学(1，只印全息影像)、全息影像(0)、冰之长枪(0)、双重释放(1)、贪婪留手",
                Energy = 3,
                Cards = Cards(
                    Play<MadScience>(1), Free<Hologram>(0), Free<IceLance>(0),
                    Play<Dualcast>(1), Curse<Greed>()),
            },

            // 8：热修复+ 0 + 打击 1 = 1（疑虑是诅咒）
            new()
            {
                Note = "8. 热修复+(0)、打击(1)；疑虑留手",
                Energy = 3,
                Cards = Cards(
                    Free<Hotfix>(0, upgraded: true), Play<StrikeDefect>(1), Curse<Doubt>()),
            },

            // 9：只有防御要 1 费；万物一心返 2 张 ⇒ 所有单位 +2 力量
            new()
            {
                Note = Core.DreadMode.IsActive
                    ? "9. 疯狂进食(0)、万物一心(0，返手超能光束+冰之长枪 ⇒ 所有单位 +2 力量)、疯狂进食+(0)、冰之长枪(0)、超能光束(0)、防御(1，附魔「漩涡」打出两次)"
                    : "9. 疯狂进食(0)、万物一心(0，返手超能光束+冰之长枪 ⇒ 所有单位 +2 力量)、冰之长枪(0)、超能光束(0)、防御(1，附魔「漩涡」打出两次)",
                Energy = 3,
                Cards = Core.DreadMode.IsActive
                    ? Cards(
                        Free<FeedingFrenzy>(0),
                        Returns<AllForOne>(0, typeof(IceLance), typeof(Hyperbeam)),
                        Free<FeedingFrenzy>(0, upgraded: true),   // 2026-11-13 (dread)
                        Free<IceLance>(0), Free<Hyperbeam>(0), Spiral<DefendDefect>(1))
                    : Cards(
                        Free<FeedingFrenzy>(0),
                        Returns<AllForOne>(0, typeof(IceLance), typeof(Hyperbeam)),
                        Free<IceLance>(0), Free<Hyperbeam>(0), Spiral<DefendDefect>(1)),
            },
        };

        return new BossScript
        {
            Turns = turns,
        LoopFrom = 6,      // 用户 2026-11-07 订正：之后 **6～9** 循环（原先 5 把第 5 回合也卷进循环了）
        LoopLength = 4,
        };
    }

    /// <summary>卡组（剧本出现过的牌各一份；诅咒也在里面，她的剧本手牌不依赖这份清单）。</summary>
    public static List<Type> Deck() => new()
    {
        typeof(Metamorphosis), typeof(Leap), typeof(Distraction), typeof(WhiteNoise),
        typeof(ConsumingShadow), typeof(IceLance), typeof(Dualcast), typeof(MadScience),
        typeof(BootSequence), typeof(EnergySurge),     // 启动流程（单人）/ 能量涌动（多人，见 MultiplayerCardSwap）
        typeof(Hyperbeam), typeof(Doubt), typeof(FeedingFrenzy), typeof(Hotfix),
        typeof(AllForOne), typeof(Hologram), typeof(StrikeDefect), typeof(DefendDefect),
        typeof(Greed),
    };

    /// <summary>
    /// 遗物（用户 2026-11-03 的清单 + 后续补充；顺序照抄）。
    ///
    /// ⚠ 顺序就是**状态栏上的显示顺序**：本作里「**先加的显示在最右**」（见 DefectScript.Relics 的注释），
    /// 所以用户说的「放在 X 的右侧」= 在列表里放到 X **前面**。
    ///
    /// 用户原话：「给他添加一些遗物：玉之剑，放在涅奥的祝福和数据磁盘之间。
    /// 在旺购客户感恩徽章的右侧增加一个已经失效的无礼之茶。给他最后一个遗物后面再添加一个情感芯片。」
    ///
    /// 涅奥的祝福由 `BossDefinition.CarriesNeowBlessing` 自动带（这一份列表里也显式写了，
    /// 和其它 Boss 一样是第 2 件的位置附近）。
    /// ⚠ 数据磁盘在这里是**起始遗物**（能力机是「进阶 9 才给」）——
    /// 它按卡面给「战斗开始 +1 集中」，而原版钩子是 `AfterRoomEntered`
    /// （发生在建影子 Player 之前），所以由 `BossHookBridge` 在战斗开始补一次。
    /// 「玉之剑」同理（`AfterRoomEntered` 给力量）⇒ 也在 `BossHookBridge` 里补。
    /// 「大蘑菇」的 +20 最大生命走变体的 <c>HpBonus</c>（影子玩家收不到拾起钩子）。
    /// 「王室猛毒」的掉血由 `BossHookBridge` 在**战斗开始**扣（用户 2026-11-03 要求改到这个时点）。
    /// </summary>
    public static List<Type> Relics() => new()
    {
        // ★ 2026-11-06（用户：「削弱事件机，进阶 9 不再获得数据磁盘」）：数据磁盘从事件机的**常备遗物**里移除。
        // ★ 2026-11-06 续（用户：「为事件机在**原来数据磁盘的地方**添加意外光滑的石头」）：
        //   就直接补在**数据磁盘原来那一格**（列表第一个 ⇒ 状态栏最右，位置和以前一模一样）。
        //   「意外光滑的石头」卡面是「每场战斗开始时获得 1 点敏捷」，反编译出来和数据磁盘一样挂在
        //   `AfterRoomEntered`（发生在**建影子 Player 之前**，对 Boss 永远不生效）⇒ 由
        //   `BossHookBridge.GrantOddlySmoothStoneDexterityAtCombatStart` 在战斗开始替她补一次
        //   （同一套机制三层「状态机」`StatusDefectScript` 也在用）。
        typeof(OddlySmoothStone),                    // 意外光滑的石头：战斗开始 +1 敏捷
        typeof(NeowBlessing),                        // 涅奥的祝福（所有 Boss 都是第 2 件）
        typeof(TeaOfDiscourtesy),                    // 无礼之茶（**已经失效**：CombatsLeft 置 0，见 ShadowPlayerFactory）
        typeof(PumpkinCandle),                       // 南瓜蜡烛：亮着时每回合 +1 能量（5 充能 = 5 场战斗）
        // ★ v0.2.367（用户 2026-11-04）：「为事件机在南瓜蜡烛和旺购徽章之间增加 2 个遗物：镀金缆线、锚」
        //   ⚠ 本作里「**先加的显示在最右**」（见上面的注释）⇒ 想让它们在状态栏上出现在两者**之间**，
        //     就按这个顺序插在列表的这两行之间。
        typeof(GoldPlatedCables),                    // 镀金缆线：最右侧那颗球的被动额外触发一次（球被动预告/高亮已支持）
        typeof(Anchor),                              // 锚：战斗开始获得 10 点格挡
        typeof(WongoCustomerAppreciationBadge),      // 旺购客户感恩徽章（纯藏品，无战斗效果）
        typeof(ChoicesParadox),                      // 选择悖论：回合开始印牌（她收不到原版钩子，剧本自己给「飞跃」）
        typeof(BigMushroom),                         // 大蘑菇：+20 最大生命 → 走变体 HpBonus
        typeof(RoyalPoison),                         // 王室猛毒：战斗开始掉 4 血（只在战斗开始掉一次，见 RoyalPoisonOncePatch）
        typeof(EmotionChip),                         // 情感芯片（原版钩子对她关掉，由 Core/EmotionChipPlays 结算）
    };

    // ── 小工具 ──────────────────────────────────────────────

    private static List<ScriptCard> Cards(params ScriptCard[] cards) => new(cards);

    /// <summary>按**卡面费用**打出（费用写成 `CostThisTurn` 是为了和剧本里那些 0 费牌统一口径）。</summary>
    private static ScriptCard Play<T>(int cost, bool upgraded = false)
        where T : MegaCrit.Sts2.Core.Models.CardModel
        => new() { Card = typeof(T), Upgraded = upgraded, From = "deck", CostThisTurn = cost };

    /// <summary>本回合**免费**打出（原版效果「本场 / 本回合免费打出」⇒ 费用写死 0）。</summary>
    private static ScriptCard Free<T>(int cost, bool upgraded = false, bool retain = false)
        where T : MegaCrit.Sts2.Core.Models.CardModel
        => new()
        {
            Card = typeof(T), Upgraded = upgraded, From = "print",
            CostThisTurn = cost, Retain = retain,
        };

    /// <summary>「万物一心」：点明它返手哪几张（每返一张 ⇒ 所有单位 +1 力量）。</summary>
    private static ScriptCard Returns<T>(int cost, params Type[] returns)
        where T : MegaCrit.Sts2.Core.Models.CardModel
        => new()
        {
            Card = typeof(T), From = "print", CostThisTurn = cost,
            ReturnsToHand = returns,
        };

    /// <summary>「羽化」：印进抽牌堆的那几张（会摆出来给玩家看）。</summary>
    private static ScriptCard Prints<T>(int cost, params Type[] prints)
        where T : MegaCrit.Sts2.Core.Models.CardModel
        => new()
        {
            Card = typeof(T), From = "deck", CostThisTurn = cost,
            PrintsToDrawPile = prints, ShowPrints = true,
        };

    /// <summary>附魔「漩涡」的那张（打出两次）。</summary>
    private static ScriptCard Spiral<T>(int cost, bool upgraded = false)
        where T : MegaCrit.Sts2.Core.Models.CardModel
        => new()
        {
            Card = typeof(T), Upgraded = upgraded, From = "deck",
            CostThisTurn = cost, EnchantSpiral = true,
        };

    /// <summary>诅咒（打不出，只是留在手里触发「回合结束时若在手牌」）。</summary>
    private static ScriptCard Curse<T>()
        where T : MegaCrit.Sts2.Core.Models.CardModel
        => new() { Card = typeof(T), From = "deck" };
}
