using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// 第一幕故障机器人的**第二套设计「电球机」**（Key `DEFECT_THUNDER`，用户 2026-11-05 设计）。
/// 第一套是「黑球机」（<see cref="DarkOrbBossScript"/>，暗黑充能球轴）；本套是**闪电球 / 集中**轴。
///
/// 用户原话：
/// > 特殊效果「**引雷针**」（单体特殊效果）：你每在一回合内打出 3 张牌，
/// > 就使故障机器人本回合失去 1 点集中。（实现见 <c>Powers/BossLightningRodPower</c>，
/// > 带计数、回合结束把扣掉的集中还回来、单回合无上限）
/// > 遗物：破损核心、涅奥的祝福、水银沙漏、船甲板。
/// > 进阶 8：升级缓冲、充电。进阶 9：升级扫荡射线、集中打击。
/// > 收藏家藏品：信号增强。
///
/// 能量账：角色基础 3 − 涅奥的祝福 1 = **2**（剧本里标「没费」的那几张都是花完 2 点之后的）。
/// 循环：第 **7～10** 回合。
/// </summary>
public static class DefectThunderScript
{
    /// <summary>每回合能量（3 基础 − 1 涅奥的祝福）。剧本里的「没费」都按这套账对得上。</summary>
    private const int TurnEnergy = 2;

    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            // 1：电击(1) + 防御(1)；愧疚是**诅咒**，打不出来（脚本自动跳过）
            Turn("电击、防御、愧疚（诅咒，打不出）", 1,
                C<Zap>(), C<DefendDefect>(), C<Guilty>()),

            // 2：扫荡射线(1) + 充电(1) ⇒ 集中打击没费；贪婪同样是诅咒
            Turn("扫荡射线、充电、集中打击（没费）、贪婪（诅咒，打不出）", 2,
                C<SweepingBeam>(), C<ChargeBattery>(), C<FocusedStrike>(), C<Greed>()),

            // 3：**基础 2 点 + 上一回合「充电」的 +1 = 3 点**（⚠ 这里只写 2，充电那 1 点由
            //   游戏自己发；上一版写成 3 就变成 2+1+3=4，暴风雨多生成一颗球 —— 用户 2026-11-06 抓到的）。
            //   暴风雨（X 费）把这 3 点全吃掉 ⇒ 生成 **3 个闪电充能球**，两张打击没费
            new BossTurn
            {
                Note = "3. 暴风雨（X 费，2 基础 + 充电 1 = 3 点能量 ⇒ 3 个闪电球）、打击×2（没费）",
                Cards = new List<ScriptCard> { C<Tempest>(), C<StrikeDefect>(), C<StrikeDefect>() },
                Energy = 2,
            },

            // 4：扩容×2 刚好 2 费 ⇒ 防御没费
            Turn("扩容、扩容、防御（没费）", 4,
                C<Capacitor>(), C<Capacitor>(), C<DefendDefect>()),

            // 5：热修复+（0 费）+ 全息影像（捞电击）+ 电击 ⇒ 防御没费
            Turn("热修复+、全息影像（捞电击）、电击、防御（没费）", 5,
                C<Hotfix>(true), C<Hologram>(), C<Zap>(), C<DefendDefect>()),

            // 6：双倍能量+（能量翻倍）+ 信号增强 ⇒ 缓冲由「信号增强」自己复制一份，剧本里**只写一张**
            new BossTurn
            {
                Note = "6. 双倍能量+、信号增强、缓冲（信号增强会自己复制一次）",
                Cards = new List<ScriptCard>
                {
                    C<DoubleEnergy>(true), C<SignalBoost>(),
                    C<MegaCrit.Sts2.Core.Models.Cards.Buffer>(),
                },
                // 双倍能量后能量够：双倍能量 1 + 信号增强 1 + 缓冲 2
                Energy = 5,
            },

            // 7：集中打击(1) + 电击(1) ⇒ 防御没费
            Turn("集中打击、电击、防御（没费）", 7,
                C<FocusedStrike>(), C<Zap>(), C<DefendDefect>()),

            // 8：热修复+（0）+ 防御×2
            Turn("热修复+、防御、防御", 8,
                C<Hotfix>(true), C<DefendDefect>(), C<DefendDefect>()),

            // 9：充电(1) + 扫荡射线(1)；愧疚（诅咒）+ 打击（没费）
            Turn("充电、愧疚（诅咒，打不出）、扫荡射线、打击（没费）", 9,
                C<ChargeBattery>(), C<Guilty>(), C<SweepingBeam>(), C<StrikeDefect>()),

            // 10：同第 3 回合 —— **2 基础 + 第 9 回合「充电」的 +1 = 3 点**（这里只写 2）
            new BossTurn
            {
                Note = "10. 暴风雨（X 费，2 基础 + 充电 1 = 3 点能量 ⇒ 3 个闪电球）、贪婪（诅咒，打不出）、打击（没费）",
                Cards = new List<ScriptCard> { C<Tempest>(), C<Greed>(), C<StrikeDefect>() },
                Energy = 2,
            },
        };

        return new BossScript
        {
            Turns = turns,
            LoopFrom = 7,     // 用户：「之后 7～10 循环」
            LoopLength = 4,
        };
    }

    /// <summary>卡组（把剧本里出现过的牌按张数收集起来，用于展示与自检）。</summary>
    public static List<Type> Deck() => new()
    {
        typeof(Zap), typeof(Zap), typeof(Zap),
        typeof(DefendDefect), typeof(DefendDefect), typeof(DefendDefect),
        typeof(DefendDefect), typeof(DefendDefect),
        typeof(Guilty), typeof(Guilty),
        typeof(Greed), typeof(Greed),
        typeof(SweepingBeam), typeof(SweepingBeam),
        typeof(ChargeBattery), typeof(ChargeBattery),
        typeof(FocusedStrike), typeof(FocusedStrike),
        typeof(Tempest), typeof(Tempest),
        typeof(StrikeDefect), typeof(StrikeDefect), typeof(StrikeDefect), typeof(StrikeDefect),
        typeof(Capacitor), typeof(Capacitor),
        typeof(Hotfix), typeof(Hotfix),
        typeof(Hologram),
        typeof(DoubleEnergy),
        typeof(SignalBoost),
        typeof(MegaCrit.Sts2.Core.Models.Cards.Buffer), typeof(MegaCrit.Sts2.Core.Models.Cards.Buffer),
    };

    /// <summary>
    /// 遗物：破损核心 / 涅奥的祝福 / **永冻冰晶** / 船甲板。
    /// （原「水银沙漏」按用户 2026-11-06「削弱一下」换成了**永冻冰晶** `Permafrost`；
    ///   水银沙漏那套「她回合开始对全体玩家 3 点」的左侧意图代码仍保留在 `BossHandPanel.Extra`，
    ///   只是这个 Boss 现在没带那件遗物，所以不会显示。）
    /// <c>UI/BossHandPanel.Extra</c> 按「抱抱先生」那种**左侧独立意图**画出来，并保证打**所有玩家**。
    /// </summary>
    public static List<Type> Relics() => new()
    {
        typeof(CrackedCore),        // 破损核心：战斗开始 1 个闪电球
        typeof(NeowBlessing),       // 涅奥的祝福（已在身上时跳过）
        typeof(Permafrost),        // 永冻冰晶（2026-11-06 用户：「削弱一下，水银沙漏换成永冻冰晶」）
        typeof(HornCleat),          // 船甲板（船夹板）：战斗开始获得格挡
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
