using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// 第一幕「**黑球机**」（故障机器人 · 暗黑充能球轴）—— 用户 2026-10-26 的设计稿。
///
/// 特殊效果「**冷却剂**」：战斗开始时给**被她锁定的那位玩家** **1 张「冷光」**（只进手牌，
/// 用户 2026-10-31「增强黑球机」把原来的 2 张/手牌+抽牌堆削成 1 张/手牌）；
/// 玩家回合结束时手里还留着冷光，故障机器人的**所有暗黑充能球本回合不使用被动**（见 <c>Powers/BossCoolantPower</c>）。
/// 冷光是 0 费 Token 技能「获得能量」⇒ 打掉它就能白拿能量，代价是她这回合的黑球不打人。
///
/// 每回合能量（用户稿里的「没费」都按这套账对得上）：
///   角色基础 3 − 涅奥的祝福 1 − **面包**（第 1 回合 -2、其余回合 +1）
///   ⇒ 第 1 回合 = **0**（2026-10-27 削弱：**去掉灯笼**，所以没有那 +1），第 2 回合起 = **3**。
/// 直接写进剧本（和其它 Boss 一样），免得被遗物钩子的实现细节影响意图。
///
/// 循环：第 **5～7** 回合。
/// </summary>
public static class DarkOrbBossScript
{
    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            // 1：能量 **0**（3 −1 涅奥 −2 面包；用户 2026-10-27 去掉灯笼之后就没有 +1 了）
            //    ⇒ 只有 0 费的「启动流程」打得出去，冰川(2)、打击(1) 都打不出
            // ⚠ 必须用 `ForceZeroEnergy` 而不是 `Energy = 0`（后者等于「不覆盖」，见 BossTurn.Energy 注释）：
            //   否则规划期按 2 费算，会把 2 费的「冰川/休眠」预告成会打出（用户 2026-10-29 实测）。
            new BossTurn
            {
                Note = "启动流程（0 费，10 格挡）；冰川、打击都没费",
                Energy = 0,
                ForceZeroEnergy = true,
                Cards = new List<ScriptCard> { C<BootSequence>(), C<Glacier>(), C<StrikeDefect>() },
            },

            // 2：飞跃(1) + 眼部攻击(0) + 热修复(0)。眼部攻击打出前她会先拿到「眼部攻击」buff
            Turn("飞跃、眼部攻击（打出前她会获得「眼部攻击」buff）、热修复（本回合 +2 集中）", 2, 3,
                C<Leap>(), C<GoForTheEyes>(), C<Hotfix>()),

            // 3：空值(2) + 笨拙（诅咒，打不出）+ 防御(1)
            Turn("空值（10 伤 + 虚弱 + 生 1 个黑球）、笨拙（诅咒，打不出）、防御", 3, 3,
                C<Null>(), C<Clumsy>(), C<DefendDefect>()),

            // 4：遗传算法（16 点格挡，剧本写死）+ 弹回(1) + 趁势打击(1)
            Turn("遗传算法（16 点格挡）、弹回、趁势打击（打出后这张牌降为 0 费）", 4, 3,
                C<GeneticAlgorithm>(blockOverride: 16), C<Rebound>(), C<MomentumStrike>()),

            // 5：趁势打击 + 双重释放 + 眼部攻击（同样先拿 buff）
            Turn("趁势打击、冰川（2 费，6 格挡 + 2 个冰霜球）、眼部攻击（打出前获得「眼部攻击」buff）", 5, 3,
                new() { Card = typeof(MomentumStrike), From = "deck", CostThisTurn = 0 }, C<Glacier>(), C<GoForTheEyes>()),

            // 6：空值(2) + 飞跃(1) ⇒ 打击没费
            Turn("空值、飞跃；打击没费", 6, 3,
                C<Null>(), C<Leap>(), C<StrikeDefect>()),

            // 7：弹回 + 趁势打击 + 防御
            Turn("弹回、趁势打击、防御", 7, 3,
                C<Rebound>(), new() { Card = typeof(MomentumStrike), From = "deck", CostThisTurn = 0 }, C<DefendDefect>()),
        };

        return new BossScript
        {
            Turns = turns,
            LoopFrom = 5,     // 用户：「之后 5～7 循环」
            LoopLength = 3,
        };
    }

    /// <summary>卡组（把剧本里出现过的牌按张数收集起来，共 21 张）。</summary>
    public static List<Type> Deck() => new()
    {
        typeof(BootSequence), typeof(Glacier), typeof(Glacier),
        typeof(StrikeDefect), typeof(StrikeDefect),
        typeof(Leap), typeof(Leap),
        typeof(GoForTheEyes), typeof(GoForTheEyes),
        typeof(Hotfix),
        typeof(Null), typeof(Null),
        typeof(Clumsy),
        typeof(DefendDefect), typeof(DefendDefect),
        typeof(GeneticAlgorithm),
        typeof(Rebound), typeof(Rebound),
        typeof(MomentumStrike), typeof(MomentumStrike), typeof(MomentumStrike),
    };

    /// <summary>
    /// 遗物（用户清单 5 件 → 2026-10-27 削弱去掉灯笼，现 4 件）。涅奥的祝福本来就会自动带，写出来是为了状态栏顺序。
    /// 破损核心给 1 个闪电球、共生病毒给 1 个黑暗球 —— 两者都由
    /// <c>BossHookBridge</c> 在战斗开始时替影子玩家手动引导（原版遗物钩子到不了影子玩家）。
    /// </summary>
    public static List<Type> Relics() => new()
    {
        typeof(CrackedCore),      // 破损核心：战斗开始 1 个闪电球
        typeof(NeowBlessing),     // 涅奥的祝福（已在身上时跳过）
        typeof(Bread),            // 面包：第 1 回合 −2 能量，其余回合 +1 能量
        typeof(SymbioticVirus),   // 共生病毒：战斗开始 1 个黑暗球
        typeof(DataDisk),         // 数据磁盘（用户 2026-10-31「增强黑球机」）：战斗开始 +1 集中
        // 灯笼已按用户 2026-10-27「太超模，削弱」去掉（第 1 回合不再 +1 能量）
    };

    private static BossTurn Turn(string note, int turnIndex, int energy, params ScriptCard[] cards)
        => new() { Note = $"{turnIndex}. {note}", Cards = new List<ScriptCard>(cards), Energy = energy };

    /// <summary>剧本里的一张牌。<paramref name="blockOverride"/> = 写死格挡（遗传算法要 16）。</summary>
    private static ScriptCard C<T>(bool upgraded = false, int blockOverride = 0)
        where T : CardModel
        => new() { Card = typeof(T), Upgraded = upgraded, From = "deck", BlockOverride = blockOverride };
}
