using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// 二层故障机器人的**第二套设计「彩虹机」**（Key `DEFECT_RAINBOW`，用户 2026-11-07 设计）。
/// 同层第一套是「物理机」（<see cref="ClawDefectScript"/>，爪击 / 0 费牌轴）。
///
/// 用户原话（逐字）：
/// > 特殊效果「**彩虹**」：故障机器人每生成 5 个充能球，就获得 1 点集中。黑暗充能球的被动不会触发。
/// > 遗物：破损核心、涅奥的祝福、节拍器、棱彩宝石、战纹涂料
/// > 进阶 8：升级启动流程、冷却剂。进阶 9：升级冰雹风暴、同步
/// > （12 回合剧本，之后 8～12 循环）
/// > 拦掉原版的抽牌、捞牌
/// > 多人时，模仿学习选择目标的优先级从高到低：涂毒猎、铸剑储、放血战、王储、仆从储、灵魂骨、杂耍战、蛇眼猎。
/// >   因为理论上不会和物理机在同一场战斗，所以这里优先级在蛇眼猎之下。
/// >   其它幕的 boss 理论上也不会和它出现在同一场战斗，优先级也在蛇眼猎之下
/// > 通过模仿涂毒猎获得「无尽刀刃」后，之后的每回合会打出一张小刀
/// > 在会触发节拍器的牌（第八回合的彩虹，不包括后续循环）下面标上 30 无来源群伤
/// > 在特殊效果中添加玻璃充能球和节拍器的警告，格式与之前相同
/// > 藏品：陨石打击
///
/// 能量账（用户剧本逐回合核对得上）：
///   定义第二幕 4 +「棱彩宝石」+1（`PrismaticGem.ModifyMaxEnergy`）−「涅奥的祝福」1 = **4 点/回合**。
///   · 第 1 回合 启动流程0 + 彩虹+2 + 碎片整理1 = 3；
///   · 第 2 回合 扩容1 + 冰雹风暴1 + 雷霆1 = 3；
///   · 第 8 回合 彩虹+2 + 聚变1 + 玻璃工艺1 = **4**（正好花完 ⇒ 只有这一回合必须 4 费）。
/// 所以这里 <see cref="TurnEnergy"/> 只是**日志与规划期的兜底**，实际值仍由
/// `CharBossMonster` 按「定义值 + 遗物修正」算（见 `CharBossMonster.Turn.cs`）。
/// </summary>
public static class DefectRainbowScript
{
    /// <summary>兜底能量（真实值由定义值 + 遗物算出，通常也是 4）。</summary>
    private const int TurnEnergy = 4;

    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            // 1：启动流程(0) + 彩虹+(2) + 碎片整理(1)【单人】/ 模仿学习(1)【多人】
            //    碎片整理 → 模仿学习的换牌在 `MultiplayerCardSwap` 里（多人且人数 ≥2 才换）。
            new BossTurn
            {
                Note = "1. 启动流程、彩虹+、碎片整理（单人）/ 模仿学习（多人）",
                Cards = new List<ScriptCard>
                {
                    C<BootSequence>(), C<Rainbow>(true), C<Defragment>(),   // 顺序：先彩虹+，再碎片整理/模仿学习（用户 2026-11-07）
                },
            },

            // 2：扩容(1) + 冰雹风暴(1) + 雷霆(1)
            Turn("2. 扩容、冰雹风暴、雷霆", 2,
                C<Capacitor>(), C<Hailstorm>(), C<Thunder>()),

            // 3：聚变+(1) + 玻璃工艺(1) + 防御(1)
            Turn("3. 聚变+、玻璃工艺、防御", 3,
                C<Fusion>(true), C<Glasswork>(), C<DefendDefect>()),

            // 4：冷却剂(1) + 防御(1) + 贪婪（诅咒，打不出）
            Turn("4. 冷却剂、防御、贪婪（诅咒，打不出）", 4,
                C<Coolant>(), C<DefendDefect>(), C<Greed>()),

            // 5：热修复+(0) + 全息影像+(1) + 热修复+(0) + 疑虑（诅咒，打不出）
            Turn("5. 热修复+、全息影像+、热修复+、疑虑（诅咒，打不出）", 5,
                C<Hotfix>(true), C<Hologram>(true), C<Hotfix>(true), C<Doubt>()),

            // 6：同步(1) + 弹幕齐射(1) + 防御(1)
            Turn("6. 同步、弹幕齐射、防御", 6,
                C<Synchronize>(), C<Barrage>(), C<DefendDefect>()),

            // 7：编译冲击（抽 5）+ 放血 + 残影 + 战火铸就 + 违逆 + 特斯拉线圈 + 君王之剑 + 防御×2（没费）
            // 能量账（用户 2026-11-07）：3 基础 − 1 涅奥 + 1 棱彩宝石 + 1（本回合额外那点）+ 2（放血）= **6 费**，
            // 编译冲击1 + 残影1 + 战火铸就2 + 违逆0 + 君王之剑2 刚好花完 ⇒ 两张防御都打不出。
            new BossTurn
            {
                Note = "7. 编译冲击（抽5）、放血、残影、战火铸就、违逆、特斯拉线圈、君王之剑、防御×2（没费）",
                Cards = new List<ScriptCard>
                {
                    C<CompileDriver>(), C<Bloodletting>(), C<Blur>(), C<WroughtInWar>(), C<Defy>(),
                    C<TeslaCoil>(), C<SovereignBlade>(), C<DefendDefect>(), C<DefendDefect>(),
                },
                Energy = 6,
                IntentShiftLeft = 260,      // 意图太多：这一回合的意图栏整体左移，别被屏幕边缘切掉
            },

            // 8：彩虹+(2) + 聚变(1) + 玻璃工艺(1) = 4 费（正好花完）
            //   ★ 这一张「彩虹+」会踩到「节拍器」的第 5 颗球 ⇒ 额外 30 点**无来源**群伤
            //   （`Metronome`：`AfterOrbChanneled` 里计数到 `OrbCount` 就
            //    `CreatureCmd.Damage(..., ValueProp.Unpowered, ...)` 打全体）。
            //   ⏳ 意图上那条 30 还没画（待办：需要改手牌栏的逐张意图渲染）。
            new BossTurn
            {
                Note = "8. 彩虹+、聚变、玻璃工艺（这张彩虹会触发节拍器：额外 30 无来源群伤）",
                Cards = new List<ScriptCard>
                {
                    // ExtraAoe = 30 ⇒ 手牌栏会在这张牌下面标一条「30 无来源群伤」（用户 2026-11-07）
                    new() { Card = typeof(Rainbow), Upgraded = true, From = "deck", ExtraAoe = 30 },
                    C<Fusion>(true), C<Glasswork>(),
                },
                Energy = TurnEnergy,
            },

            // 9：热修复+(0) + 全息影像+(1) + 防御(1)
            // 用户 2026-11-07：第 9 回合改成「热修复+、全息影像+、热修复+、防御」
            Turn("9. 热修复+、全息影像+、热修复+、防御", 9,
                C<Hotfix>(true), C<Hologram>(true), C<Hotfix>(true), C<DefendDefect>()),

            // 10：同步(1) + 疑虑（诅咒）+ 防御(1)
            Turn("10. 同步、疑虑（诅咒，打不出）、防御", 10,
                C<Synchronize>(), C<Doubt>(), C<DefendDefect>()),

            // 11：弹幕齐射(1) + 防御(1) + 贪婪（诅咒）
            Turn("11. 弹幕齐射、防御、贪婪（诅咒，打不出）", 11,
                C<Barrage>(), C<DefendDefect>(), C<Greed>()),

            // 12：同第 7 回合
            new BossTurn
            {
                Note = "12. 编译冲击（抽5）、放血、残影、战火铸就、违逆、特斯拉线圈、君王之剑、防御×2（没费）",
                Cards = new List<ScriptCard>
                {
                    C<CompileDriver>(), C<Bloodletting>(), C<Blur>(), C<WroughtInWar>(), C<Defy>(),
                    C<TeslaCoil>(), C<SovereignBlade>(), C<DefendDefect>(), C<DefendDefect>(),
                },
                Energy = 6,
                IntentShiftLeft = 260,
            },
        };

        return new BossScript
        {
            Turns = turns,
            LoopFrom = 8,        // 用户：「之后 8～12 循环」
            LoopLength = 5,
        };
    }

    /// <summary>卡组（按剧本里出现的最大张数收集，用于展示与自检）。</summary>
    public static List<Type> Deck() => new()
    {
        typeof(BootSequence),
        typeof(Rainbow), typeof(Rainbow),
        typeof(Defragment),
#if STS2_111
        typeof(ImitationLearning),   // 0.107 没有这张牌 ⇒ 多人那一档退化成碎片整理
#endif
        typeof(Capacitor), typeof(Capacitor),
        typeof(Hailstorm), typeof(Hailstorm),
        typeof(Thunder), typeof(Thunder),
        typeof(Fusion), typeof(Fusion), typeof(Fusion),
        typeof(Glasswork), typeof(Glasswork), typeof(Glasswork),
        typeof(Coolant),
        typeof(Hotfix), typeof(Hotfix), typeof(Hotfix), typeof(Hotfix),
        typeof(Hologram), typeof(Hologram), typeof(Hologram),
        typeof(Synchronize), typeof(Synchronize), typeof(Synchronize),
        typeof(Barrage), typeof(Barrage), typeof(Barrage),
        typeof(CompileDriver), typeof(CompileDriver),
        typeof(Bloodletting), typeof(Bloodletting),
        typeof(Blur), typeof(Blur),
        typeof(WroughtInWar), typeof(WroughtInWar),
        typeof(Defy), typeof(Defy),
        typeof(TeslaCoil), typeof(TeslaCoil),
        typeof(SovereignBlade),        // 只铸一把：剧本第 7/12 回合打的是同一把（TakeExistingBlade）
        typeof(DefendDefect), typeof(DefendDefect), typeof(DefendDefect), typeof(DefendDefect),
        typeof(DefendDefect), typeof(DefendDefect), typeof(DefendDefect),
        typeof(Greed), typeof(Greed), typeof(Doubt),
    };

    /// <summary>
    /// 遗物（用户 2026-11-07 指定）：破损核心 / 涅奥的祝福 / **节拍器** / **棱彩宝石** / 战纹涂料。
    ///   · 破损核心 `CrackedCore`：战斗开始生成 1 个闪电球；
    ///   · 节拍器 `Metronome`：每生成 5 个充能球 → 对所有敌人 30 点无来源伤害（第 8 回合那张彩虹踩到它）；
    ///   · 棱彩宝石 `PrismaticGem`：`ModifyMaxEnergy` +1 ⇒ 每回合能量 +1；
    ///   · 战纹涂料 `WarPaint`：拾起时随机升级技能牌（影子玩家收不到拾起钩子 ⇒ 实际只占位）。
    /// </summary>
    public static List<Type> Relics() => new()
    {
        typeof(CrackedCore),
        typeof(NeowBlessing),
        typeof(Metronome),
        typeof(PrismaticGem),
        typeof(WarPaint),
    };

    private static BossTurn Turn(string note, int turnIndex, params ScriptCard[] cards)
        => new()
        {
            Note = $"{turnIndex}. {note}",
            Cards = new List<ScriptCard>(cards),
            Energy = TurnEnergy,
        };

    private static ScriptCard C<T>(bool upgraded = false)
        where T : CardModel
        => new() { Card = typeof(T), Upgraded = upgraded, From = "deck" };
}
