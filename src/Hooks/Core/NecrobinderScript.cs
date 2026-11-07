using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// 亡灵契约师（**第一幕 Boss**）的剧本。
///
/// 设计者（用户）原话要点，写在这里免得后面改乱：
///   · 遗物：缚魂命匣（起始）、涅奥的祝福、闪亮口红（第 3 回合 +1 力量/+1 敏捷）、船夹板；
///   · 特殊效果「时候未到」：
///       ① 玩家的格挡将要因回合开始消失时，抵消等量**灾厄**；
///       ② 玩家回合将要结束时，若会因灾厄死亡，立刻用格挡抵消灾厄直到不会死；
///   · 亡灵契约师每打出「能量汲取」，玩家获得 3 层精神过载（NeurosurgePower）；
///   · 意图：施加灾厄显示为**向下红箭头**（不是攻势）；碎骨/友谊额外带**牺牲**（蓝色向下箭头）；
///     能量汲取 = 攻势 + 强化 + 向下红箭头；
///   · 11 回合一套，之后 7～11 循环。
///   · **用户 2026-11-01（削弱）**：第 8 回合的「无处可逃」与第 11 回合的**第一张**「打击」**调换位置**
///     ⇒ 第 8 回合只剩 死亡使者 + 打击（防御仍没费），无处可逃挪到第 11 回合（替掉一张打击）。
///   · **升级只看「能量汲取」顺序**（<c>CharBossMonster.DrainUpgradeOrder</c>）：
///     1 次 鞭打+死亡之门 → 2 次 死亡使者+播种 → 3 次 荒疫打击+无处可逃（进阶 9 跳过）→ 4~7 次打击+防御。
///     ⇒ 第 5 回合打完第一次能量汲取后，鞭打/死亡之门就一直是升级版；
///       而**死亡使者要到第 11 回合之后**才升级 ⇒ 第 8 回合那张是**未升级**的 21 灾厄
///       （用户 2026-10-25 订正：剧本里原先写死 `Upgraded = true`，与升级顺序矛盾）。
///
/// 能量：定义值 3、涅奥的祝福 −1 → 实际 2 费。剧本里逐回合写了覆盖值（<see cref="BossTurn.Energy"/>），
/// 因为用户的设计里第 7 回合起多一费、而第 9 回合又只有 2 费（按设计记录逐条对齐）。
/// 能量不够的牌由 <see cref="BossScriptPlanner"/> 自动跳过 —— 这正是设计里「XX 没费打不了」的来源。
/// </summary>
public static class NecrobinderScript
{
    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            TurnE(2, "播种（群攻 8）；防御、防御（第二张防御没费）",
                C<Sow>(), C<DefendNecrobinder>(), C<DefendNecrobinder>()),

            TurnE(2, "死亡使者（21 灾厄 + 1 虚弱）；打击、防御都没费",
                C<Deathbringer>(), C<StrikeNecrobinder>(), C<DefendNecrobinder>()),

            TurnE(2, "闪亮口红触发（+1 力量/+1 敏捷）；鞭打（13 灾厄、抽 1 张打击）、死亡之门；防御与抽到的打击没费",
                C<Scourge>(), C<DeathsDoor>(), C<DefendNecrobinder>()),

            TurnE(2, "厄运之衣（成衣后每次给灾厄得 3 格挡）、荒疫打击；打击没费",
                C<Shroud>(), C<BlightStrike>(), C<StrikeNecrobinder>()),

            TurnE(2, "能量汲取（升级鞭打与死亡使者；玩家 +3 精神过载）、无处可逃；打击没费",
                C<DrainPower>(), C<NoEscape>(), C<StrikeNecrobinder>()),

            TurnE(2, "友谊（−2 力量，口红已给 +1 → 实际 −1；之后每回合 +1 力量）、出击；护卫没费",
                C<Friendship>(), C<Unleash>(), C<Bodyguard>()),

            // 用户 2026-10-25：鞭打+ 抽 2 张打击 ⇒ 这里把**第二张**抽到的打击也补上（原来只写了一张）。
            TurnE(3, "洗牌。鞭打+（16 灾厄、抽 2 张打击）、死亡之门、防御；两张抽到的打击没费",
                C<Scourge>(true), C<DeathsDoor>(), C<DefendNecrobinder>(),
                C<StrikeNecrobinder>(false, "draw"), C<StrikeNecrobinder>(false, "draw")),

            // 用户 2026-10-25 订正：这里**不能**写死升级。死亡使者是「能量汲取」第 2 次才升的一对
            //（`CharBossMonster.DrainUpgradeOrder`：1 次 鞭打+死亡之门 → 2 次 死亡使者+播种），
            // 而这回合是第 8 回合、第 2 次能量汲取在第 11 回合 ⇒ 此时它还是**未升级**的 21 灾厄。
            // 用户 2026-11-01（削弱）：**第 8 回合的「无处可逃」与第 11 回合第一张「打击」调换**——
            // 这一回合从此只打 死亡使者 + 打击（2+1=3 费打完），防御照旧没费。
            TurnE(3, "死亡使者（21 灾厄 + 1 虚弱；要等第 11 回合那次能量汲取才升级）、打击；防御没费",
                C<Deathbringer>(), C<StrikeNecrobinder>(), C<DefendNecrobinder>()),

            TurnE(3, "播种（群攻 8）、荒疫打击（−2 力量后打 5，附带灾厄）、防御",
                C<Sow>(), C<BlightStrike>(), C<DefendNecrobinder>()),

            TurnE(3, "护卫（召唤 5）、出击（奥斯提伤害）、防御",
                C<Bodyguard>(), C<Unleash>(), C<DefendNecrobinder>()),

            // 用户 2026-11-01（削弱）：「无处可逃」从第 8 回合挪到这里，替掉原来的**第一张**打击
            //（另一张打击与能量汲取照旧）。3 费正好：无处可逃 1 + 打击 1 + 能量汲取 1。
            TurnE(3, "无处可逃、打击、能量汲取（升级无处可逃与荒疫打击；玩家再 +3 精神过载）",
                C<NoEscape>(), C<StrikeNecrobinder>(), C<DrainPower>()),
        };

        return new BossScript
        {
            Turns = turns,
            LoopFrom = 7,     // 7～11 回合循环
            LoopLength = 5,
            // 用户设计：每循环一轮按顺序升级——出击+护卫 → 播种+死亡之门 → 之后每次 1 对打击与防御
            UpgradeByCycle = new List<Type[]>
            {
                new[] { typeof(Unleash), typeof(Bodyguard) },
                new[] { typeof(Sow), typeof(DeathsDoor) },
                new[] { typeof(StrikeNecrobinder), typeof(DefendNecrobinder) },
            },
        };
    }

    /// <summary>
    /// 第一幕的卡组（剧本实际造牌，这里用于自检与展示）。
    /// 用户 2026-10-24：**碎骨 → 播种**（削弱灾厄骨），仍是 4 张打击 + 4 张防御（共 20 张）。
    /// </summary>
    public static List<Type> Deck() => new()
    {
        typeof(Sow), typeof(Sow),
        typeof(DefendNecrobinder), typeof(DefendNecrobinder),
        typeof(DefendNecrobinder), typeof(DefendNecrobinder),
        typeof(StrikeNecrobinder), typeof(StrikeNecrobinder),
        typeof(StrikeNecrobinder), typeof(StrikeNecrobinder),
        typeof(Deathbringer), typeof(Scourge), typeof(DeathsDoor), typeof(Shroud),
        typeof(BlightStrike), typeof(DrainPower), typeof(NoEscape), typeof(Friendship),
        typeof(Unleash), typeof(Bodyguard),
    };

    /// <summary>第一幕的遗物：缚魂命匣是起始遗物（角色自带），这里写的是额外给的。</summary>
    public static List<Type> Relics() => new()
    {
        typeof(SparklingRouge),   // 闪亮口红：第 3 回合 +1 力量 / +1 敏捷
        typeof(Relics.NeowBlessing),   // 涅奥的祝福：所有 Boss 都是第 2 件（用户 2026-10-27 口径）
        typeof(HornCleat),        // 船夹板：第 2 回合开始获得格挡
    };

    private static BossTurn TurnE(int energy, string note, params ScriptCard[] cards)
        => new() { Note = note, Cards = new List<ScriptCard>(cards), Energy = energy };

    private static ScriptCard C<T>(bool upgraded = false, string from = "deck")
        where T : MegaCrit.Sts2.Core.Models.CardModel
        => new() { Card = typeof(T), Upgraded = upgraded, From = from };
}
