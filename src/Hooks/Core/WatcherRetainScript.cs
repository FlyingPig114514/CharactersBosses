#if STS2_111   // 观者 Boss 只在 0.113 构建里（107 不加，见 csproj 的 STS2_111）
using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// **二层 2 号观者 Boss「保留观」**的剧本（用户 2026-11-17 设计）。
///
/// 用户原话（逐字）：
/// <code>
/// 1.确立基础+、打击、时之沙（保留不出）
/// 2.警惕、坚韧（保留不出）、旋转打击（保留不出）、时之沙（2费，保留不出）、奇迹（保留不出）
/// 3.奇迹、时之沙（0费）、欺瞒现实、改造现实、防御、平安（保留不出）、惩恶（保留不出）、
///   坚韧（0费，保留不出）、旋转打击（0费，保留不出）
/// 4.心灵堡垒、义愤填膺、惩恶（0费）、平安（0费）、坚韧（0费）、结末、旋转打击（0费，保留不出）
/// 5.旋转打击（0费）、停顿、安宁
/// 6.洗牌。欺瞒现实、改造现实、时之沙（0费）、惩恶（保留不出）、平安（保留不出）
/// 7.打击、坚韧（0费，保留不出）、旋转打击（0费，保留不出）、惩恶（0费，保留不出）、平安（0费，保留不出）
/// 8.义愤填膺、坚韧（0费）、防御、惩恶（0费）、平安（0费）、结末、旋转打击（0费，保留不出）
/// 9.旋转打击（0费）、停顿、警惕
/// 之后6～9循环
/// </code>
/// ★ v0.2.734（用户 2026-11-17 订正）：「修改 2、3 回合（**奇迹被我漏掉了**）」⇒ 第 2 回合末尾补一张
///   奇迹（保留不出）、第 3 回合开头补打一张奇迹（0 费、打出 +1 能量 ⇒ 防御这才付得起）。
///
/// ## 能量（用户：「能量除了 4、8 回合都应该是 2，浪费也没关系。4、8 回合因为退出平静都是 4 费」）
/// | 回合 | 打出的牌（费） | 合计 | 覆盖值 |
/// |---|---|---|---|
/// | 1 | 确立基础+ 1 + 打击 1 | 2 | **2** |
/// | 2 | 警惕 2（奇迹 0，保留不出 ⇒ 无能量） | 2 | **2** |
/// | 3 | 奇迹 0（**+1 能量**）+ 时之沙 0 + 欺瞒现实 1 + 改造现实 1 + 防御 1 | 3 | **2**（2 + 奇迹 1 = 3） |
/// | 4 | 心灵堡垒 1 + 义愤填膺 1 + 惩恶 0 + 平安 0 + 坚韧 0 + 结末 1 | 3 | **4**（义愤填膺进愤怒 ⇒ 退出平静 +2） |
/// | 5 | 旋转打击 0 + 停顿 0 + 安宁 1 | 1 | **2**（浪费 1） |
/// | 6 | 欺瞒现实 1 + 改造现实 1 + 时之沙 0 | 2 | **2** |
/// | 7 | 打击 1（其余全保留不出） | 1 | **2**（浪费 1） |
/// | 8 | 义愤填膺 1 + 坚韧 0 + 防御 1 + 惩恶 0 + 平安 0 + 结末 1 | 3 | **4**（同上，退出平静 +2） |
/// | 9 | 旋转打击 0 + 停顿 0 + 警惕 2 | 2 | **2** |
///
/// ## 保留成长（用户 2026-11-17 订正：「在手牌 ≠ 保留」「确立基础的减费、旋转打击/坚韧的加值
/// 都是在**她回合结束时**触发的，所以界面显示的应该是**还没结算**的值」）
///
/// 「（保留不出）」的牌才算被保留 ⇒ 回合结束时结算一次：**费用 −1**（确立基础能力，原版 Establishment
/// 就是「每被保留一次费用 −1」）+ **旋转打击 +4 伤害 / 坚韧 +2 格挡**（牌自己的保留成长）。
/// 时之沙还额外吃它自己的「保留 −1 费」⇒ **一次保留 −2 费**（4 → 2 → 0）。
///
/// 显示值（已结算次数 × 成长，用户逐格给的）：
/// | 回合 | 时之沙 费 | 旋转打击 伤害 | 坚韧 格挡 |
/// |---|---|---|---|
/// | 1 | 4 | 7（未上手） | 5（未上手） |
/// | 2 | 2 | 7（1 次） | 5（1 次） |
/// | 3 | 0（打出→弃牌堆，之后恒 0） | 11（2 次） | 7（2 次） |
/// | 4 | 0 | 15（3 次） | 9（打出，不再保留） |
/// | 5 | 0 | 19（打出） | 9（在弃牌堆） |
/// | 6 | 0 | 19 | 9 |
/// | 7 | 0 | 19 | 9（第 3 次） |
/// | 8 | 0 | 23（4 次） | 11（打出） |
/// | 9 | 0 | 27（5 次） | 11 |
///
/// 用户 2026-11-17：「之后旋转打击和坚韧的数值还要接着涨」⇒ 6～9 每循环一次，
/// 旋转打击再 +2 次保留（7、8 回合结束各一次）、坚韧再 +1 次（7 回合结束一次）。
///
/// ## 其它
/// · 「洗牌」= 第 6 回合先洗她自己的抽牌堆（`BossTurn.ShuffleDrawPile`）。
/// · **拦掉原版的印牌**：改造现实 ⇒ 惩恶、欺瞒现实 ⇒ 平安 的原版生成被
///   `CharBossMonster.BlocksGeneratedCardsEntries` 拦掉，这两张由剧本自己给
///   （和印牌观同一套；她那两只牌不触发创世之柱/武器库，因为她没有那两件遗物）。
/// · 进阶 8：升级 心灵堡垒 / **欺瞒现实**；进阶 9：只升级 **改造现实**
///   （★ v0.2.734 用户 2026-11-17 订正：原来是「心灵堡垒/停顿/警惕」+「改造现实/旋转打击」）。
/// </summary>
public static class WatcherRetainScript
{
    /// <summary>旋转打击每被保留一次的加伤（原版 `WindmillStrike`：+4）。</summary>
    private const int WindmillPerRetain = 4;
    /// <summary>旋转打击基础伤害（原版 7）。</summary>
    private const int WindmillBase = 7;
    /// <summary>坚韧每被保留一次的加格挡（原版 `Perseverance`：+2）。</summary>
    private const int PerseverancePerRetain = 2;
    /// <summary>坚韧基础格挡（原版 5）。</summary>
    private const int PerseveranceBase = 5;

    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            // ── 1 ── 确立基础+ / 打击 / 时之沙（保留不出）   （2 费）
            Turn(1, 2, "1. 确立基础+、打击、时之沙（保留不出）",
                C(WatcherCards.Establishment, upgraded: true),
                C(WatcherCards.Strike),
                Hold(WatcherCards.SandsOfTime, cost: -1)),

            // ── 2 ── 警惕 / 坚韧（保留不出）/ 旋转打击（保留不出）/ 时之沙（2费，保留不出）/ 奇迹（保留不出）   （2 费）
            //   ★ v0.2.734（用户 2026-11-17 补：「奇迹被我漏掉了」）
            Turn(2, 2, "2. 警惕、坚韧（保留不出）、旋转打击（保留不出）、时之沙（2费，保留不出）、奇迹（保留不出）",
                C(WatcherCards.Vigilance),
                Hold(WatcherCards.Perseverance, cost: -1, block: PerseveranceFor(2)),
                Hold(WatcherCards.WindmillStrike, cost: -1, damage: WindmillFor(2)),
                Hold(WatcherCards.SandsOfTime, cost: 2),
                Hold(WatcherCards.Miracle, cost: 0)),

            // ── 3 ── 奇迹 / 时之沙（0费）/ 欺瞒现实 / 改造现实 / 防御 / 平安·惩恶·坚韧·旋转打击（保留不出）   （2 费 + 奇迹 1 = 3）
            //   ★ v0.2.734（用户 2026-11-17）：奇迹打出来给 +1 能量 ⇒ 3 费正好够
            //   奇迹0 + 时之沙0 + 欺瞒现实1 + 改造现实1 + 防御1 = 3
            Turn(3, 2, "3. 奇迹、时之沙（0费）、欺瞒现实、改造现实、防御、平安·惩恶·坚韧·旋转打击（保留不出）",
                C(WatcherCards.Miracle, cost: 0),
                C(WatcherCards.SandsOfTime, cost: 0),
                C(WatcherCards.DeceiveReality),
                C(WatcherCards.CarveReality),
                C(WatcherCards.Defend),
                Hold(WatcherCards.Safety, cost: -1),
                Hold(WatcherCards.Smite, cost: -1),
                Hold(WatcherCards.Perseverance, cost: 0, block: PerseveranceFor(3)),
                Hold(WatcherCards.WindmillStrike, cost: 0, damage: WindmillFor(3))),

            // ── 4 ── 心灵堡垒 / 义愤填膺 / 惩恶(0) / 平安(0) / 坚韧(0) / 结末 / 旋转打击（0费，保留不出）   （4 费 = 退出平静）
            Turn(4, 4, "4. 心灵堡垒、义愤填膺、惩恶(0)、平安(0)、坚韧(0)、结末、旋转打击（0费，保留不出）",
                C(WatcherCards.MentalFortress),
                C(WatcherCards.Indignation),
                C(WatcherCards.Smite, cost: 0),
                C(WatcherCards.Safety, cost: 0),
                C(WatcherCards.Perseverance, cost: 0, block: PerseveranceFor(4)),
                C(WatcherCards.Conclude),
                Hold(WatcherCards.WindmillStrike, cost: 0, damage: WindmillFor(4))),

            // ── 5 ── 旋转打击(0) / 停顿 / 安宁   （2 费）
            Turn(5, 2, "5. 旋转打击(0)、停顿、安宁",
                C(WatcherCards.WindmillStrike, cost: 0, damage: WindmillFor(5)),
                C(WatcherCards.Halt),
                C(WatcherCards.Tranquility)),

            // ── 6 ── 洗牌 / 欺瞒现实 / 改造现实 / 时之沙(0) / 惩恶·平安（保留不出）   ← 6～9 循环起点   （2 费）
            TurnEnergy(6, 2, "6. 洗牌。", true,
                C(WatcherCards.DeceiveReality),
                C(WatcherCards.CarveReality),
                C(WatcherCards.SandsOfTime, cost: 0),
                Hold(WatcherCards.Smite, cost: -1),
                Hold(WatcherCards.Safety, cost: -1)),

            // ── 7 ── 打击 / 坚韧·旋转打击·惩恶·平安（0费，保留不出）   （2 费）
            Turn(7, 2, "",
                C(WatcherCards.Strike),
                Hold(WatcherCards.Perseverance, cost: 0, block: PerseveranceFor(7)),
                Hold(WatcherCards.WindmillStrike, cost: 0, damage: WindmillFor(7)),
                Hold(WatcherCards.Smite, cost: 0),
                Hold(WatcherCards.Safety, cost: 0)),

            // ── 8 ── 义愤填膺 / 坚韧(0) / 防御 / 惩恶(0) / 平安(0) / 结末 / 旋转打击（0费，保留不出）   （4 费 = 退出平静）
            Turn(8, 4, "8. 义愤填膺、坚韧(0)、防御、惩恶(0)、平安(0)、结末、旋转打击（0费，保留不出）",
                C(WatcherCards.Indignation),
                C(WatcherCards.Perseverance, cost: 0, block: PerseveranceFor(8)),
                C(WatcherCards.Defend),
                C(WatcherCards.Smite, cost: 0),
                C(WatcherCards.Safety, cost: 0),
                C(WatcherCards.Conclude),
                Hold(WatcherCards.WindmillStrike, cost: 0, damage: WindmillFor(8))),

            // ── 9 ── 旋转打击(0) / 停顿 / 警惕   （2 费）
            Turn(9, 2, "9. 旋转打击(0)、停顿、警惕",
                C(WatcherCards.WindmillStrike, cost: 0, damage: WindmillFor(9)),
                C(WatcherCards.Halt),
                C(WatcherCards.Vigilance)),
        };

        return new BossScript
        {
            Turns = turns,
            // 用户：「之后 6～9 循环」；`LoopFrom` 是 **1 起**的回合号
            LoopFrom = 6,
            LoopLength = 4,
        };
    }

    // ── 保留成长：显示值（= 已结算次数 × 每级成长）──────────────────────────
    //
    // ⚠ 用户口径（2026-11-17）：「确立基础的减费和旋转打击/坚韧的数值增加是在她**回合结束时**触发的，
    //   所以显示的是**还没结算**的值」⇒ 这里的数字 = 进入该回合时**已经结算过几次**。
    //
    // 旋转打击（第 3 回合起才上手）：
    //   T1 未上手 / T2 0 次 / T3 1 次 / T4 2 次 / T5 3 次 / T6 3 次 / T7 3 次 / T8 4 次 / T9 5 次
    // 坚韧（第 2 回合起）：
    //   T1 未上手 / T2 0 次 / T3 1 次 / T4 2 次 / T7 2 次 / T8 3 次（T5/T6/T9 不在手牌）
    // 循环（回合 ≥ 10，槽位 = 6..9）：每循环旋转打击 +2 次、坚韧 +1 次（用户：「还要接着涨」）。

    /// <summary>旋转打击在这回合显示时的**已结算保留次数**。</summary>
    internal static int WindmillSettles(int turn)
    {
        if (turn <= 1) return 0;
        if (turn <= 9)
        {
            return turn switch { 2 => 0, 3 => 1, 4 => 2, 5 => 3, 6 => 3, 7 => 3, 8 => 4, _ => 5 };
        }
        int slot = 6 + ((turn - 6) % 4);          // 6..9
        int loop = (turn - 6) / 4;                // 第几轮循环（≥1）
        int baseSettles = 3 + 2 * loop;           // 该轮 6 号槽位上的已结算次数
        return slot switch { 6 => baseSettles, 7 => baseSettles, 8 => baseSettles + 1, _ => baseSettles + 2 };
    }

    /// <summary>坚韧在这回合显示时的**已结算保留次数**。</summary>
    internal static int PerseveranceSettles(int turn)
    {
        if (turn <= 1) return 0;
        if (turn <= 9)
        {
            return turn switch { 2 => 0, 3 => 1, 4 => 2, 7 => 2, 8 => 3, _ => 2 };
        }
        int slot = 6 + ((turn - 6) % 4);
        int loop = (turn - 6) / 4;
        int baseSettles = 2 + loop;
        return slot == 8 ? baseSettles + 1 : baseSettles;
    }

    /// <summary>旋转打击在这回合的显示伤害。</summary>
    internal static int WindmillFor(int turn) => WindmillBase + WindmillPerRetain * WindmillSettles(turn);

    /// <summary>坚韧在这回合的显示格挡。</summary>
    internal static int PerseveranceFor(int turn) => PerseveranceBase + PerseverancePerRetain * PerseveranceSettles(turn);

    /// <summary>卡组：她靠剧本吃饭，卡组只放备用牌。</summary>
    public static List<Type> Deck()
    {
        var deck = new List<Type>();
        if (WatcherCards.Strike is Type s) { deck.Add(s); deck.Add(s); deck.Add(s); }
        if (WatcherCards.Defend is Type d) { deck.Add(d); deck.Add(d); deck.Add(d); }
        return deck;
    }

    /// <summary>
    /// 遗物（用户 2026-11-17）：至纯之水、涅奥的祝福、斗篷扣、**佩尔的士兵**、**臂甲**。
    /// 至纯之水是观者模组的遗物（反射取）；涅奥的祝福是我们复制的那份；臂甲 = `Vambrace`
    /// （「每场战斗中你第一次从卡牌中获得的格挡值翻倍」，虚无骨 / 易伤战已经在用）。
    /// </summary>
    public static List<Type> Relics()
    {
        var relics = new List<Type>();
        if (WatcherCards.T("PureWater") is Type pure) relics.Add(pure);          // 至纯之水
        relics.Add(typeof(CharacterBosses.Relics.NeowBlessing));                  // 涅奥的祝福
        relics.Add(typeof(MegaCrit.Sts2.Core.Models.Relics.CloakClasp));           // 斗篷扣
        relics.Add(typeof(MegaCrit.Sts2.Core.Models.Relics.PaelsLegion));          // 佩尔的士兵
        relics.Add(typeof(MegaCrit.Sts2.Core.Models.Relics.Vambrace));             // 臂甲
        return relics;
    }

    // ── 剧本小工具 ─────────────────────────────────────────────────────────
    private static BossTurn Turn(int turn, int energy, string note, params ScriptCard?[] cards)
        => TurnEnergy(turn, energy, note, false, cards);

    private static BossTurn TurnEnergy(int turn, int energy, string note, bool shuffle,
                                       params ScriptCard?[] cards)
    {
        var list = new List<ScriptCard>();
        foreach (var c in cards) if (c != null) list.Add(c);
        return new BossTurn
        {
            Note = note,
            Cards = list,
            Energy = energy,
            ShuffleDrawPile = shuffle,
        };
    }

    /// <summary>
    /// **打出去**的那张。`damage` / `block` &gt; 0 时覆盖牌面数值 ——
    /// 用于「被保留过几次所以数值已经涨了」的那些牌（界面显示的必须是涨过之后的值）。
    /// </summary>
    private static ScriptCard? C(Type? card, bool upgraded = false, int upgradeMinAscension = 0,
                                int cost = -1, int damage = 0, int block = 0)
    {
        if (card == null) return null;
        return new ScriptCard
        {
            Card = card,
            Upgraded = upgraded,
            UpgradeMinAscension = upgradeMinAscension,
            CostOverride = cost,
            DamageOverride = damage,
            BlockOverride = block,
            From = "deck",
        };
    }

    /// <summary>「保留不出」的那张：留手牌、压暗、本回合不打出（`HoldInstead`）。</summary>
    private static ScriptCard? Hold(Type? card, int cost = -1, int damage = 0, int block = 0)
    {
        if (card == null) return null;
        return new ScriptCard
        {
            Card = card,
            HoldInstead = true,
            CostOverride = cost,
            DamageOverride = damage,
            BlockOverride = block,
            From = "deck",
        };
    }
}
#endif
