using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// **第一幕「碎屑储君」**的剧本、卡组与遗物（用户设计，2026-10）。
///
/// 8 回合，之后 **5～8 循环**。用户逐条给的（2026-10-24 又削了一轮）：
///   1. 隐秘藏品+、群星斗篷、防御、碰撞轨迹（进阶 9 **不**升级）、防御（没费打不了）
///   2. 群星之子（进阶 8 升级）、太阳打击、碰撞轨迹（**进阶 9 升级**）
///   3. 彗星、笨拙、贪婪（两张都是诅咒，出不了）
///   4. 防御、防御、疑虑（诅咒，打不出；回合结束在手 → 自己吃 1 虚弱）
///   5. 洗牌 → 碰撞轨迹（进阶 9 **不**升级）、太阳打击、碰撞轨迹（**进阶 9 升级**）
///   6. 隐秘藏品+、群星斗篷、防御
///   7. 彗星、防御、贪婪（诅咒，出不了）
///   8. 防御、防御、疑虑（诅咒，打不出；回合结束在手 → 自己吃 1 虚弱）
///
/// ⚠ 2026-10-24 用户：**删掉所有「如此甚好」**（MAKE_IT_SO，原第 1、6 回合各有一张）；
///   进阶 9 的升级改成「第 2 回合的碰撞轨迹 + 第 5 回合**第二张**碰撞轨迹」，
///   第 1 回合和第 5 回合**第一张**碰撞轨迹保持原样。
///
/// 特殊效果「彗星」见 <c>Powers/CometBossPower.cs</c>。
/// 遗物：天赋君权（DIVINE_RIGHT）、准备背包（BAG_OF_PREPARATION）。
/// 进阶：进阶 8 获得遗物「星系尘埃」（GALACTIC_DUST）+ 群星之子升级；
///       进阶 9 升级上面点名的那两张碰撞轨迹（按张标 <see cref="ScriptCard.UpgradeMinAscension"/>）。
/// </summary>
public static class DebrisRegentScript
{
    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            T("隐秘藏品+、防御、防御、碰撞轨迹、防御（最后一张没费打不了）", 1,
                // 用户 2026-10-30：**群星斗篷 → 防御**（群星斗篷是 0 费、防御是 1 费
                // ⇒ 第 1 回合会有一张防御没费打不了，这是用户要的效果）
                C<HiddenCache>(true), C<DefendRegent>(), C<DefendRegent>(),
                // 用户 2026-10-24：删掉「如此甚好」；第 1 回合这张碰撞轨迹**不进阶升级**
                C<CollisionCourse>(),
                // 末尾这张防御是「亮在手里但没费打」的那张（和其它回合的「防御没费」同一个写法）
                C<DefendRegent>()),

            T("群星之子（进阶8升级）、下砸（群攻）、碰撞轨迹（进阶9升级）", 2,
                // 用户 2026-10-30：**太阳打击 → 下砸（CRUSH_UNDER）**，1 费打全体 8 伤（群攻增强）
                At(8, typeof(ChildOfTheStars)), C<CrushUnder>(), At(9, typeof(CollisionCourse))),

            T("彗星、笨拙、贪婪（两张都是诅咒，出不了）", 3,
                C<Comet>(), C<Clumsy>(), C<Greed>()),

            T("防御、防御、疑虑（诅咒，打不出；回合结束还在手里 → 自己吃 1 虚弱）", 4,
                // 用户 2026-11-01（削弱）：**伤口 → 疑虑**（疑虑也是诅咒、同样打不出，
                // 但它 HasTurnEndInHandEffect：回合结束时还在手里 ⇒ 持有者（她自己）获得 1 层虚弱
                // ⇒ 从那回合起攻击 −25%，比纯占位的伤口更难受。结算走 RunCurseTurnEndHooks）
                C<DefendRegent>(), C<DefendRegent>(), C<Doubt>()),

            T("洗牌 → 碰撞轨迹（进阶9不升）、下砸（群攻）、碰撞轨迹（进阶9升级）", 5,
                C<CollisionCourse>(), C<CrushUnder>(), At(9, typeof(CollisionCourse))),

            T("隐秘藏品+、防御、防御（第三张防御 1 费，没费打不了）", 6,
                // 用户 2026-10-30：**群星斗篷 → 防御**（同上，第 6 回合同样会有一张防御打不了）
                C<HiddenCache>(true), C<DefendRegent>(), C<DefendRegent>()),

            T("彗星、防御、贪婪（诅咒，出不了）", 7,
                C<Comet>(), C<DefendRegent>(), C<Greed>()),

            T("防御、防御、疑虑（诅咒，打不出；回合结束还在手里 → 自己吃 1 虚弱）", 8,
                C<DefendRegent>(), C<DefendRegent>(), C<Doubt>()),
        };

        return new BossScript
        {
            Turns = turns,
            LoopFrom = 5,     // 5～8 回合循环
            LoopLength = 4,
        };
    }

    /// <summary>第一幕的卡组（剧本实际造牌，这里用于自检与展示）。</summary>
    public static List<Type> Deck() => new()
    {
        typeof(HiddenCache), typeof(ChildOfTheStars),
        // 用户 2026-10-30：太阳打击 → 下砸（群攻）；群星斗篷 → 防御
        typeof(CrushUnder), typeof(CollisionCourse), typeof(CollisionCourse),
        typeof(Comet),
        typeof(StrikeRegent), typeof(DefendRegent), typeof(DefendRegent),
        typeof(DefendRegent), typeof(DefendRegent),
    };

    /// <summary>第一幕的遗物（涅奥的祝福由 CarriesNeowBlessing 自动带）。用户订正：**去掉手里剑**。</summary>
    public static List<Type> Relics() => new()
    {
        typeof(DivineRight),         // 天赋君权
        typeof(Relics.NeowBlessing),   // 涅奥的祝福：所有 Boss 都是第 2 件（用户 2026-10-27 口径）
        typeof(BagOfPreparation),    // 准备背包
        // 用户 2026-10-30：**加「迷你储君」**（每回合第一次花辉星 → +1 力量），**不限进阶**（所以放基础列表）。
        // 实现见 CharBossMonster 的「MiniRegent」手动广播（影子玩家的遗物收不到原版派发）。
        typeof(MiniRegent),          // 迷你储君
    };

    private static BossTurn T(string note, int turnIndex, params ScriptCard[] cards)
        => new() { Note = $"{turnIndex}. {note}", Cards = new List<ScriptCard>(cards) };

    private static ScriptCard C<T>(bool upgraded = false, string from = "deck")
        where T : CardModel
        => new() { Card = typeof(T), Upgraded = upgraded, From = from };

    /// <summary>「这张牌在进阶 N 起是升级版」（用户设计：进阶 9 只升级**第一张**碰撞轨迹）。</summary>
    private static ScriptCard At(int minAscension, Type card)
        => new() { Card = card, UpgradeMinAscension = minAscension, From = "deck" };
}
