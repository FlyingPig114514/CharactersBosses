using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// 第三幕「**养手骨**」（亡灵契约师 · 奥斯提轴，Key `NECROBINDER_OSTY`）—— 用户 2026-11-03 设计稿。
/// 这是三层亡灵契约师的**第二套设计**（第一套是「根除骨」`NecrobinderAct3Script`），
/// 与「刀猎 / 炼药猎」「易伤战 / 巨石战」「无色储君 / 辉星储」「铸剑储君 / 王储」同一套机制：
/// **以房间遭遇为准**（`NECROBINDER_OSTY_BOSS_ENCOUNTER`），拿不到遭遇才按种子 50/50。
///
/// ## 与「根除骨」的区别（用户要求「和根除骨做好区分」）
///   · 根除骨：灾厄 / 根除 / 能量汲取轴，特殊效果「预借时间」；
///   · **养手骨**：全靠**奥斯提**（召唤 → 奥斯提攻击牌），特殊效果「**不死**」
///     （奥斯提不会死；它替她挡未被格挡的攻击伤害直到只剩 1 HP，之后由她替它挡）。
///
/// ## 遗物（用户清单）
/// **无界命匣**（回合开始召唤 2、战斗开始召唤 5）、涅奥的祝福、骨笛（奥斯提攻击 → 格挡）、
/// **欧洛巴斯之触**（把起始遗物升级成先古版本 —— 「缚魂命匣 → 无界命匣」这件事就是它做的；它自己会留在遗物栏里）、
/// 剃刀牙（打出攻击/技能牌时本场升级它）、钗（回合开始获得格挡）、
/// 书签（**回合结束时被保留的牌费用 -1**，直到打出）。
///
/// ★ v0.2.345（用户 2026-11-03）：「死者苏生还是没打出来。算了这样吧，不搞注能牌了，
/// 你把她的放电异虾改成欧洛巴斯之触，把缚魂命匣改成无界命匣（召唤奥斯提相关代码参考缚魂命匣）」
/// ⇒ ① 「注能 · 死者苏生+」那套**整段删掉**（不再造牌、不再 AutoPlay）；
///   ② 遗物换成上面这一对；③ 召唤走「无界命匣」自己的数值：
///      战斗开始 5（`StartOfCombat`，在 `BossHookBridge.FireCombatStart` 里结算）、
///      每回合开始 2（`StartOfTurn`，由 `BossOsty` 结算，`Patches/OstyNoCombatStartSummonPatch`
///      把原版那条对我们这只 Boss 拦掉，避免双份）。
///
/// ## 剧本（用户 2026-11-03 的**第三次修订**，v0.2.339 落地）
///
/// 用户原话：
/// <code>
/// 战斗开始：注能死者苏生+
/// 1.响指（为吸引仇恨添加保留）、护卫、吸引仇恨（没费，被添加保留）
/// 2.紧追不放、重压（0费）、猛晃、吸引仇恨（被保留并因为书签减至1费，不出）
/// 3.吸引仇恨（0费）、友谊、出击、增生（不出，保留）
/// 4.致死性、击掌、增生（被书签减至0费）、 笨拙
/// 5.钙化、来生、死亡之舞
/// 6.洗牌。护卫+、出击+、吸引仇恨+（没费、不出，保留）
/// 7.紧追不放+、重压+、猛晃+、吸引仇恨+（被书签减至1费）
/// 8.响指+、击掌+、增生+（没费，不出，保留）
/// 9.洗牌。增生+（被书签减至0费）、护卫+、出击+、吸引仇恨+（没费、不出，保留）
/// 之后7～9循环
/// </code>
///
/// 能量（★ 用户 2026-11-03 订正）：**第 1～3 回合每回合 2 点**，**第 4 回合起每回合 3 点** ——
/// 因为第 3 回合打出了「友谊」（Friendship：之后每回合开始时 +1 能量），那一笔从她的第 4 回合起生效。
/// 逐回合核对（反编译的卡面费用：响指 1 / 护卫 1 / 吸引仇恨 2 / 紧追不放 1 / 重压 2（奥斯提攻击过 ⇒ 0）/
/// 猛晃 1 / 增生 1 / 友谊 1 / 出击 1 / 致死性 1 / 击掌 2 / 钙化 1 / 来生 1 / 死亡之舞 1）：
///   · 第 1 回合（2 费）响指 1 + 护卫 1 = 2 ⇒ 吸引仇恨（2）**没费**，被响指加上「保留」留在手里；
///   · 第 2 回合（2 费）紧追不放 1 + 重压 **0**（紧追不放已经让奥斯提打过）+ 猛晃 1 = 2；
///   · 第 3 回合（2 费）吸引仇恨 **0**（保留过两次，书签 -2）+ 友谊 1 + 出击 1 = 2，增生留手；
///   · 第 4 回合起（3 费，友谊的 +1 到账）致死性 1 + 击掌 2 + 增生 **0**（保留过一次，书签 -1）= 3；
///   · 第 5 回合 钙化 1 + 来生 1 + 死亡之舞 1 = 3；
///   · 第 6 回合 护卫+ 1 + 出击+ 1 = 2 ⇒ 吸引仇恨+（2）没费、保留；
///   · 第 7 回合 紧追不放+ 1 + 重压+ **0** + 猛晃+ 1 + 吸引仇恨+ **1**（书签 -1）= 3；
///   · 第 8 回合 响指+ 1 + 击掌+ 2 = 3，增生+ 留手（保留）；
///   · 第 9 回合 增生+ **0**（书签 -1）+ 护卫+ 1 + 出击+ 1 = 2 ⇒ 吸引仇恨+（2）没费、保留。
/// 「书签」的减费在原版是**回合结束时**对被保留的牌生效；她的手牌每回合都是剧本重建的，
/// 所以剧本直接把**减完之后**的费用写进 `CostThisTurn`（卡面、规划、扣费三者一致）。
///
/// ## 战斗开始的召唤
/// 「无界命匣」卡面：战斗开始时召唤 5、你的回合开始时召唤 2。
/// 影子玩家收不到遗物钩子 ⇒ 由 `BossHookBridge.FireCombatStart`（战斗开始 5）
/// 与 `BossOsty`（每回合开始 2）分别结算。
///
/// ## 多人
/// 「吸引仇恨」换成「骸骨军团」（场景 `<c>MultiplayerCardSwap</c>`），升级逻辑与其它牌一致。
/// 其它 Boss 的奥斯提**不受**「不死」影响（<see cref="Powers.BossOstyUndyingPower"/> 只认自己主人）。
/// </summary>
public static class OstyKeeperScript
{
    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            // 1：响指 1 + 护卫 1 = 2；吸引仇恨（2 费）没费，被响指加上「保留」
            new()
            {
                Note = "1. 响指(1，给吸引仇恨加保留)、护卫(1)；吸引仇恨(2 费，没费 ⇒ 带保留留手)",
                Energy = 2,
                PickOrder = new List<Type> { typeof(PullAggro) },   // 响指的「给一张手牌加保留」点名吸引仇恨
                Cards = Cards(
                    Play<Snap>(1), Play<Bodyguard>(1),
                    Hold<PullAggro>(2, retain: true)),
            },

            // 2：紧追不放 1 + 重压 0（奥斯提已攻击过）+ 猛晃 1 = 2
            new()
            {
                Note = "2. 紧追不放(1)、重压(0，紧追不放让它免费)、猛晃(1)；吸引仇恨(1 费，书签减过，不出)",
                Energy = 2,
                Cards = Cards(
                    Play<SicEm>(1), Free<Flatten>(0), Play<Rattle>(1),
                    Hold<PullAggro>(1, retain: true)),
            },

            // 3：吸引仇恨 0（保留两回合 ⇒ 书签 -2）+ 友谊 1 + 出击 1 = 2；增生留手
            new()
            {
                Note = "3. 吸引仇恨(0，书签减两次)、友谊(1)、出击(1)；增生(1 费，不出，保留)",
                Energy = 2,
                Cards = Cards(
                    Free<PullAggro>(0), Play<Friendship>(1), Play<Unleash>(1),
                    Hold<Spur>(1, retain: true)),
            },

            // 4：致死性 1 + 击掌 2 + 增生 0（书签 -1）= 3；笨拙是诅咒
            new()
            {
                Note = "4. 致死性(1)、击掌(2)、增生(0，书签减过)；笨拙留手（诅咒，打不出）",
                Energy = 3,
                Cards = Cards(
                    Play<Lethality>(1), Play<HighFive>(2), Free<Spur>(0),
                    Curse<Clumsy>()),
            },

            // 5：来生 1 + 来生 1 + 死亡之舞 1 = 3
            // ★ v0.2.367（用户 2026-11-04：「养手骨削弱，把钙化换成来生（所以第五回合会出 2 张来生，
            //   进阶 8 把这两张都升级）」）—— 原来这回合是「钙化 + 来生 + 死亡之舞」，
            //   现在钙化整张从她的卡组里拿掉，换成第二张来生。进阶 8 的 `AscensionUpgrades` 已经是
            //   `(8, typeof(Afterlife))`，升级**按类型** ⇒ 两张来生会一起变「来生+」。
            new()
            {
                // ★ 2026-11-13（用户）：「第五回合的第一张来生替换为钙化+」⇒ 回到「钙化+ + 来生 + 死亡之舞」（能量账仍是 3）
                Note = "5. 钙化+(1)、来生(1)、死亡之舞(1)",
                Energy = 3,
                Cards = Cards(
                    Play<Calcify>(1, upgraded: true), Play<Afterlife>(1), Play<DanseMacabre>(1)),
            },

            // 6：洗牌。护卫+ 1 + 出击+ 1 = 2；吸引仇恨+（2 费）没费、保留
            new()
            {
                Note = "6. 洗牌。护卫+(1)、出击+(1)；吸引仇恨+(2 费，没费 ⇒ 带保留留手)",
                Energy = 3,
                Cards = Cards(
                    Play<Bodyguard>(1, upgraded: true), Play<Unleash>(1, upgraded: true),
                    Hold<PullAggro>(2, retain: true, upgraded: true)),
            },

            // 7：紧追不放+ 1 + 重压+ 0 + 猛晃+ 1 + 吸引仇恨+ 1（书签 -1）= 3
            new()
            {
                Note = "7. 紧追不放+(1)、重压+(0)、猛晃+(1)、吸引仇恨+(1，书签减过)",
                Energy = 3,
                Cards = Cards(
                    Play<SicEm>(1, upgraded: true), Free<Flatten>(0, upgraded: true),
                    Play<Rattle>(1, upgraded: true), Free<PullAggro>(1, upgraded: true)),
            },

            // 8：响指+ 1 + 击掌+ 2 = 3；增生+（1 费）没费、被响指加上保留
            new()
            {
                Note = "8. 响指+(1，给增生+加保留)、击掌+(2)；增生+(1 费，没费 ⇒ 带保留留手)",
                Energy = 3,
                PickOrder = new List<Type> { typeof(Spur) },        // 响指+ 的「加保留」点名增生+
                Cards = Cards(
                    Play<Snap>(1, upgraded: true), Play<HighFive>(2, upgraded: true),
                    Hold<Spur>(1, retain: true, upgraded: true)),
            },

            // 9：洗牌。增生+ 0（书签 -1）+ 护卫+ 1 + 出击+ 1 = 2；吸引仇恨+（2 费）没费、保留
            new()
            {
                Note = "9. 洗牌。增生+(0，书签减过)、护卫+(1)、出击+(1)；吸引仇恨+(2 费，没费 ⇒ 带保留留手)",
                Energy = 3,
                Cards = Cards(
                    Free<Spur>(0, upgraded: true),
                    Play<Bodyguard>(1, upgraded: true), Play<Unleash>(1, upgraded: true),
                    Hold<PullAggro>(2, retain: true, upgraded: true)),
            },
        };

        return new BossScript
        {
            Turns = turns,
            LoopFrom = 7,      // 用户：「之后 7～9 循环」
            LoopLength = 3,
        };
    }

    /// <summary>卡组（剧本出现过的牌按最大张数收集）。</summary>
    public static List<Type> Deck() => new()
    {
        typeof(Snap), typeof(Bodyguard), typeof(Spur), typeof(SicEm), typeof(Flatten),
        typeof(Rattle), typeof(Friendship), typeof(Unleash), typeof(PullAggro),
        typeof(Lethality), typeof(HighFive), typeof(Clumsy),
        typeof(Afterlife), typeof(Afterlife), typeof(DanseMacabre), typeof(LegionOfBone),

    };

    /// <summary>遗物（用户清单；涅奥的祝福由 <c>CarriesNeowBlessing</c> 自动带）。</summary>
    public static List<Type> Relics() => new()
    {
        typeof(PhylacteryUnbound), // 无界命匣：战斗开始召唤 5、每回合开始召唤 2（奥斯提的生命来源；用户 2026-11-03 换掉缚魂命匣）
        typeof(NeowBlessing),      // 涅奥的祝福（所有 Boss 都是第 2 件）
        typeof(BoneFlute),         // 骨笛：奥斯提攻击 → 获得格挡
        typeof(TouchOfOrobas),     // 欧洛巴斯之触：把起始遗物升级成先古版本（缚魂命匣 → 无界命匣；就是它解释了上面那条）
        typeof(RazorTooth),        // 剃刀牙：打出攻击/技能牌时本场升级它
        typeof(Sai),               // 钗：回合开始获得格挡
        typeof(Bookmark),          // 书签：回合结束时被保留的牌费用 -1（吸引仇恨 / 增生逐回合变便宜）
    };

    // ── 小工具 ──────────────────────────────────────────────

    private static List<ScriptCard> Cards(params ScriptCard[] cards) => new(cards);

    /// <summary>按卡面费用打出。</summary>
    private static ScriptCard Play<T>(int cost, bool upgraded = false)
        where T : MegaCrit.Sts2.Core.Models.CardModel
        => new() { Card = typeof(T), Upgraded = upgraded, From = "deck", CostThisTurn = cost };

    /// <summary>本回合**实际费用是 0** 的那张（重压：奥斯提攻击过 / 增生：书签减过）。</summary>
    private static ScriptCard Free<T>(int cost, bool upgraded = false)
        where T : MegaCrit.Sts2.Core.Models.CardModel
        => new() { Card = typeof(T), Upgraded = upgraded, From = "deck", CostThisTurn = cost };

    /// <summary>
    /// 本回合**不出**、留在手里（「没费」或设计如此）的那张。
    /// `retain` = 带「保留」（书签的减费只对被保留的牌生效 ⇒ 这些牌下回合会变便宜）。
    /// </summary>
    private static ScriptCard Hold<T>(int cost, bool retain = false, bool upgraded = false)
        where T : MegaCrit.Sts2.Core.Models.CardModel
        => new()
        {
            Card = typeof(T), Upgraded = upgraded, From = "deck",
            CostThisTurn = cost, HoldInstead = true, Retain = retain,
        };

    /// <summary>诅咒（打不出，只是留在手里触发「回合结束时若在手牌」）。</summary>
    private static ScriptCard Curse<T>()
        where T : MegaCrit.Sts2.Core.Models.CardModel
        => new() { Card = typeof(T), From = "deck" };
}
