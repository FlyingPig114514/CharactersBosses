using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// 故障机器人（三层：**状态机**）的剧本 —— 用户 2026-11-05 设计。
/// 与同层的「能力机」（<see cref="DefectScript"/>）「事件机」（<see cref="EventMachineScript"/>）**并列**，
/// 靠阵容编号随机（三层第 3 个故障机器人 Boss）。原本在二层（当时叫「同步机」），
/// 用户 2026-11-05 因为「它的伤害已经比所有三层 Boss 都高了」把它挪到三层。
///
/// 用户原话要点：
///   · 特殊效果「**同步**」：每当她**生成状态牌**时，也把复制品放进**你的弃牌堆**；
///     每回合你**首次抽到状态牌**时 —— 消耗它、抽 1 张、+1 能量
///     （实现在 <c>Powers/BossSyncPower.cs</c>；原本还有「她 +1 力量」，2026-11-05 削弱删掉）。
///   · 遗物：破损核心、涅奥的祝福、永冻冰晶、大抱抱、开信刀、**钻石头冠、意外光滑的石头**。
///   · 进阶 8：升级「强撑」「高速脱离」；进阶 9：升级「污秽攻击」+ 获得「数据磁盘」。
///   · 9 个回合的意图见下面 <see cref="Build"/>；**第 6～9 回合循环**。
///   · 第 6 回合那个「洗牌」是**真的洗牌**（触发她的大抱抱 ⇒ 煤灰进她的抽牌堆），
///     并按用户要求**同时给你塞 1 张煤灰**（<see cref="BossTurn.ShuffleDrawPile"/>）。
///
/// 用户三条注意（实现位置）：
///   1. **开信刀 / 烟囱都是打全体玩家**，伤害要显示在意图里
///      ⇒ 现在挂在**触发它的那张牌底下**（`BossIntentBuilder` 的烟囱/开信刀分支）；
///   2. **大抱抱生成的煤灰**（她第 6 回合以及之后每个循环）也要给你塞一张 ⇒ 洗牌处一并处理；
///   3. **散射炮的伤害按「她自己的状态牌数量」算** ⇒ <c>BossIntentBuilder</c> 里 FLAK_CANNON 特判。
/// </summary>
public static class StatusDefectScript
{
    /// <summary>每回合固定 3 费。</summary>
    private const int TurnEnergy = 3;

    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            Turn("烟囱（之后每生成一张牌 ⇒ 对全体玩家 5 点无来源伤害）、强撑、散射炮（没费，留在手里）", 1,
                false, C<Smokestack>(), C<FightThrough>(), C<FlakCannon>()),

            Turn("污秽攻击、高速脱离、防御", 2, false,
                C<GunkUp>(), C<BoostAway>(), C<DefendDefect>()),

            Turn("火箭飞拳（0 费）、防御、压缩、疑虑", 3, false,
                Zero<RocketPunch>(), C<DefendDefect>(), C<Compact>(), C<Doubt>()),

            Turn("超频、内核加速、全息影像（捞散射炮）、散射炮、防御、防御（没费）", 4, false,
                C<Overclock>(), C<Turbo>(), C<Hologram>(),
                C<FlakCannon>(false, "hologram"), C<DefendDefect>(), C<DefendDefect>()),

            Turn("冰寒、电击、热修复", 5, false,
                C<Chill>(), C<Zap>(), C<Hotfix>()),

            // ★ 第 6 回合：真的洗牌（触发大抱抱生成煤灰）+ 给玩家也塞一张
            Turn("**洗牌**（大抱抱 ⇒ 煤灰进她的抽牌堆，并给你也塞 1 张）、污秽攻击、强撑、防御（没费）", 6, true,
                C<GunkUp>(), C<FightThrough>(), C<DefendDefect>()),

            Turn("电击、高速脱离、防御", 7, false,
                C<Zap>(), C<BoostAway>(), C<DefendDefect>()),

            Turn("火箭飞拳（0 费）、防御、压缩、疑虑", 8, false,
                Zero<RocketPunch>(), C<DefendDefect>(), C<Compact>(), C<Doubt>()),

            Turn("超频、内核加速、全息影像（捞高速脱离）、高速脱离、散射炮、防御", 9, false,
                C<Overclock>(), C<Turbo>(), C<Hologram>(),
                C<BoostAway>(false, "hologram"), C<FlakCannon>(), C<DefendDefect>()),
        };

        return new BossScript
        {
            Turns = turns,
            LoopFrom = 6,      // 第 6～9 回合循环
            LoopLength = 4,
        };
    }

    /// <summary>三层「状态机」的卡组（剧本负责造牌，这里用于展示与自检）。</summary>
    public static List<Type> Deck() => new()
    {
        typeof(Smokestack), typeof(FightThrough), typeof(FlakCannon),
        typeof(GunkUp), typeof(BoostAway),
        typeof(DefendDefect), typeof(DefendDefect), typeof(DefendDefect),
        typeof(RocketPunch), typeof(Compact), typeof(Doubt),
        typeof(Overclock), typeof(Turbo), typeof(Hologram),
        typeof(Chill), typeof(Zap), typeof(Hotfix),
        typeof(Soot), typeof(Soot),      // 煤灰（大抱抱生成；写进卡组只为在牌表里可见）
    };

    /// <summary>遗物（用户给的；涅奥的祝福由定义自动带）。</summary>
    public static List<Type> Relics() => new()
    {
        typeof(CrackedCore),        // 破损核心（起始）：战斗开始引导 1 个闪电球
        typeof(NeowBlessing),       // 涅奥的祝福
        typeof(Permafrost),         // 永冻冰晶
        typeof(BiiigHug),           // 大～抱抱：每次洗牌生成煤灰进抽牌堆
        typeof(LetterOpener),       // 开信刀：每打出若干技能牌 ⇒ 对全体玩家造成无来源伤害
        // ★ 2026-11-05（用户：「调整它为它添加遗物：钻石头冠、意外光滑的石头」）：
        //   两件都是 `AfterRoomEntered` 系的遗物，影子玩家收不到原版派发
        //   ⇒ 由 `BossHookBridge.FireCombatStart` 手动补（20 格挡 + 1 模糊 / +1 敏捷），已实现。
        typeof(DiamondDiadem),      // 钻石头冠：战斗开始 20 格挡 + 1 层模糊
        typeof(OddlySmoothStone),   // 意外光滑的石头：每场战斗开始 +1 敏捷
    };

    private static BossTurn Turn(string note, int turnIndex, bool shuffle, params ScriptCard[] cards)
        => new()
        {
            Note = $"{turnIndex}. {note}",
            Cards = new List<ScriptCard>(cards),
            Energy = TurnEnergy,
            ShuffleDrawPile = shuffle,
        };

    private static ScriptCard C<T>(bool upgraded = false, string from = "deck")
        where T : MegaCrit.Sts2.Core.Models.CardModel
        => new() { Card = typeof(T), Upgraded = upgraded, From = from };

    /// <summary>本回合按 0 费打出（火箭飞拳）。</summary>
    private static ScriptCard Zero<T>()
        where T : MegaCrit.Sts2.Core.Models.CardModel
        => new() { Card = typeof(T), From = "deck", CostThisTurn = 0 };
}
