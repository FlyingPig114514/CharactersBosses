#if STS2_111   // 观者 Boss 只在这个构建里
using System;
using System.Collections.Generic;

namespace CharacterBosses.Core;

/// <summary>
/// **一层 2 号观者 Boss「疾风观」**的剧本（用户 2026-11-23 设计，内部 Key = `WATCHER_FLURRY`，
/// 因为「疾风连击」的英文是 **Flurry of Blows**）。
///
/// 用户原话（逐字）：
/// <code>
/// 1.以手拒之、疾风连击、打击
/// 2.警惕、疾风连击、打击×2（没费）、奇迹（不出）
/// 3.奇迹、斋戒、防御、防御（没费）
/// 4.供奉、追击、腰带抽打
/// 5.化体为空、疾风连击、心灵堡垒、安宁、疾风连击
/// 6.洗牌、疾风连击、防御、防御（没费）
/// 7.同 4
/// 8.打击、打击×2（没费）
/// 9.化体为空、疾风连击、警惕、疾风连击、打击（没费）
/// 之后 6～9 循环
/// </code>
///
/// ## 卡费（反编译两份观者 mod 的构造函数 `: base(cost, …)` 逐个核过）
/// | 牌 | 费 | | 牌 | 费 |
/// |---|---|---|---|---|
/// | 以手拒之 TalkToTheHand | 1 | | 疾风连击 FlurryOfBlows | **0** |
/// | 打击 Strike | 1 | | 防御 Defend | 1 |
/// | 警惕 Vigilance | 2 | | 奇迹 Miracle | 0（打出 +1 能量） |
/// | 斋戒 Fasting | 2 | | 供奉 Consecrate | **0** |
/// | 追击 FollowUp | 1 | | 腰带抽打 SashWhip | 1 |
/// | 化体为空 EmptyBody | 1 | | 心灵堡垒 MentalFortress | 1 |
/// | 安宁 Tranquility | 1 | | | |
///
/// ## 每回合能量（`BossDefs.Watcher.Energy 3` − 涅奥的祝福 1 = **基础 2**）
/// 用户给的「（没费）」就是能量不够、那张牌**留在手里不打**。逐回合按「真要打的牌」的费用反推：
/// 1 = 以手拒之1 + 疾风0 + 打击1 ⇒ **2**
/// 2 = 警惕2 + 疾风0 ⇒ **2**（打击×2、奇迹 都没费/不出）
/// 3 = 奇迹0(+1) + 斋戒2 + 防御1 ⇒ 2 + 1 = 3 ⇒ **2**（第二张防御没费）
/// 4 = 供奉0 + 追击1 + 腰带抽打1 ⇒ **2**
/// 5 = 化体为空1 + 疾风0 + 心灵堡垒1 + 安宁1 + 疾风0 ⇒ 3 ⇒ **3**
/// 6 = 疾风0 + 防御1 ⇒ **1**（洗牌回合，第二张防御没费）
/// 7 = 同 4 ⇒ **2**
/// 8 = 打击1 ⇒ **1**（另两张打击没费）
/// 9 = 化体为空1 + 疾风0 + 警惕2 + 疾风0 ⇒ 3 ⇒ **3**（打击没费）
///
/// 第 5 回合的 3 点与第 9 回合的 3 点都按用户清单里「这几张都打出来了」反推，
/// 不额外假设平静/神格的进出（她这两回合都**不进**平静：安宁在第 5 回合才打出）。
///
/// ## 进阶（用户 2026-11-23）
/// · 进阶 8：升级 **以手拒之 / 心灵堡垒**
/// · 进阶 9：升级 **供奉 / 斋戒**
/// · 恐惧模式：用户没单独指定 ⇒ 与普通一致（升级表为空）。
/// </summary>
public static class WatcherFlurryScript
{
    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            // ── 1 ── 以手拒之 / 疾风连击 / 打击        （2 费 = 1 + 0 + 1）
            Turn(2, "1. 以手拒之、疾风连击、打击",
                Play(WatcherCards.TalkToTheHand),
                Play(WatcherCards.FlurryOfBlows),
                Play(WatcherCards.Strike)),

            // ── 2 ── 警惕 / 疾风连击 / 打击×2（没费）/ 奇迹（不出）   （2 费 = 2 + 0）
            Turn(2, "2. 警惕、疾风连击、打击×2（没费）、奇迹（不出）",
                Play(WatcherCards.Vigilance),
                Play(WatcherCards.FlurryOfBlows),
                Hold(WatcherCards.Strike), Hold(WatcherCards.Strike),
                Hold(WatcherCards.Miracle, cost: 0)),

            // ── 3 ── 奇迹 / 斋戒 / 防御 / 防御（没费）   （2 费 + 奇迹 1 = 3 = 0 + 2 + 1）
            Turn(2, "3. 奇迹、斋戒、防御、防御（没费）",
                Play(WatcherCards.Miracle, cost: 0),
                Play(WatcherCards.Fasting),
                Play(WatcherCards.Defend),
                Hold(WatcherCards.Defend)),

            // ── 4 ── 供奉 / 追击 / 腰带抽打            （2 费 = 0 + 1 + 1）
            Turn(2, "4. 供奉、追击、腰带抽打",
                Play(WatcherCards.Consecrate),
                Play(WatcherCards.FollowUp),
                Play(WatcherCards.SashWhip)),

            // ── 5 ── 化体为空 / 疾风连击 / 心灵堡垒 / 安宁 / 疾风连击   （3 费 = 1 + 0 + 1 + 1 + 0）
            Turn(3, "5. 化体为空、疾风连击、心灵堡垒、安宁、疾风连击",
                Play(WatcherCards.EmptyBody),
                Play(WatcherCards.FlurryOfBlows),
                Play(WatcherCards.MentalFortress),
                Play(WatcherCards.Tranquility),
                Play(WatcherCards.FlurryOfBlows)),

            // ── 6 ── 洗牌 / 疾风连击 / 防御 / 防御（没费）   ← 6~9 循环起点   （1 费 = 0 + 1）
            TurnEnergy(1, true, "6. 洗牌、疾风连击、防御、防御（没费）",
                Play(WatcherCards.FlurryOfBlows),
                Play(WatcherCards.Defend),
                Hold(WatcherCards.Defend)),

            // ── 7 ── 同第 4 回合                       （2 费）
            Turn(2, "7. 供奉、追击、腰带抽打",
                Play(WatcherCards.Consecrate),
                Play(WatcherCards.FollowUp),
                Play(WatcherCards.SashWhip)),

            // ── 8 ── 打击 / 打击×2（没费）              （1 费 = 1）
            Turn(1, "8. 打击、打击×2（没费）",
                Play(WatcherCards.Strike),
                Hold(WatcherCards.Strike), Hold(WatcherCards.Strike)),

            // ── 9 ── 化体为空 / 疾风连击 / 警惕 / 疾风连击 / 打击（没费）   （3 费 = 1 + 0 + 2 + 0）
            Turn(3, "9. 化体为空、疾风连击、警惕、疾风连击、打击（没费）",
                Play(WatcherCards.EmptyBody),
                Play(WatcherCards.FlurryOfBlows),
                Play(WatcherCards.Vigilance),
                Play(WatcherCards.FlurryOfBlows),
                Hold(WatcherCards.Strike)),
        };

        return new BossScript
        {
            Turns = turns,
            // 用户：「之后 6～9 循环」；`LoopFrom` 是 **1 起**的回合号
            LoopFrom = 6,
            LoopLength = 4,
        };
    }

    // ── 剧本小工具 ─────────────────────────────────────────────────────────
    private static BossTurn Turn(int energy, string note, params ScriptCard?[] cards)
        => TurnEnergy(energy, false, note, cards);

    private static BossTurn TurnEnergy(int energy, bool shuffle, string note, params ScriptCard?[] cards)
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

    /// <summary>真要打出去的那张（`cost` &gt;= 0 时覆盖本回合费用）。</summary>
    private static ScriptCard? Play(Type? card, int cost = -1, int upgradeMinAscension = 0)
    {
        if (card == null) return null;
        return new ScriptCard
        {
            Card = card,
            CostThisTurn = cost,
            UpgradeMinAscension = upgradeMinAscension,
            From = "deck",
        };
    }

    /// <summary>「（没费）」/「（不出）」留手的那张。</summary>
    private static ScriptCard? Hold(Type? card, int cost = 1)
    {
        if (card == null) return null;
        return new ScriptCard
        {
            Card = card,
            CostThisTurn = cost,
            HoldInstead = true,
            From = "deck",
        };
    }

    /// <summary>
    /// 卡组（剧本里出现过的牌，按**同回合最大张数**收）：
    /// · 疾风连击 2 张 —— 第 5 / 9 回合同回合各打两张；
    /// · 打击 3 张 —— 第 8 回合手里三张（一张打、两张没费）；
    /// · 防御 2 张 —— 第 3 / 6 回合各两张，多备一张防洗牌；
    /// · 奇迹 2 张 —— 至纯之水战斗开始也会给一张。
    /// </summary>
    public static List<Type> Deck()
    {
        var deck = new List<Type>();
        void Add(Type? t, int count = 1)
        {
            if (t == null) return;
            for (int i = 0; i < count; i++) deck.Add(t);
        }
        Add(WatcherCards.FlurryOfBlows, 2);
        Add(WatcherCards.TalkToTheHand);
        Add(WatcherCards.FollowUp);
        Add(WatcherCards.Fasting);
        Add(WatcherCards.Consecrate);
        Add(WatcherCards.SashWhip);
        Add(WatcherCards.EmptyBody, 2);
        Add(WatcherCards.MentalFortress);
        Add(WatcherCards.Tranquility);
        Add(WatcherCards.Vigilance, 2);
        Add(WatcherCards.Miracle, 2);
        Add(WatcherCards.Strike, 3);
        Add(WatcherCards.Defend, 3);
        return deck;
    }

    /// <summary>
    /// 遗物（用户 2026-11-23）：**至纯之水、涅奥的祝福、手里剑、苦无**。
    /// · 至纯之水 = 观者 mod 的初始遗物（反射取，战斗开始给一张奇迹）；
    /// · 涅奥的祝福 = 本 mod 的遗物（每回合 −1 能量 / −2 抽牌，她高血量的代价）；
    /// · 手里剑 `Shuriken`（本体）：每打出 3 张攻击牌 +1 力量；
    /// · 苦无 `Kunai`（本体）：每打出 3 张攻击牌 +1 敏捷。
    /// </summary>
    public static List<Type> Relics()
    {
        var relics = new List<Type>();
        if (WatcherCards.PureWater is Type pure) relics.Add(pure);
        relics.Add(typeof(CharacterBosses.Relics.NeowBlessing));
        if (WatcherCards.Shuriken is Type shuriken) relics.Add(shuriken);
        if (WatcherCards.Kunai is Type kunai) relics.Add(kunai);
        return relics;
    }

    /// <summary>恐惧模式额外升级的牌：用户没指定 ⇒ 空表（与普通一致）。</summary>
    public static List<Type> DreadUpgradeTypes() => new List<Type>();
}
#endif
