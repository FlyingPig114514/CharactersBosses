#if STS2_111   // 观者 Boss 只在这个构建里
using System;
using System.Collections.Generic;

namespace CharacterBosses.Core;

/// <summary>
/// **一层 2 号观者 Boss「疾风观」**的剧本（用户 2026-11-23 设计，内部 Key = `WATCHER_FLURRY`，
/// 因为「疾风连击」的英文是 **Flurry of Blows**）。
///
/// 用户原话（逐字，2026-11-23 第六轮「削弱 + 调换顺序」定稿）：
/// <code>
/// 1.以手拒之、疾风连击、防御
/// 2.打击、防御、防御（没费）、奇迹（不出）
/// 3.化体为空、疾风连击、警惕、疾风连击、打击、奇迹（不出）
/// 4.奇迹、斋戒、心灵堡垒、防御（没费）
/// 5.供奉、追击、腰带抽打
/// 6.洗牌、疾风连击、防御、防御（没费）（恐惧模式下打出）
/// 7.供奉、追击、腰带抽打
/// 8.防御、防御（没费）（恐惧模式下打出）、打击（没费）
/// 9.化体为空、疾风连击、警惕、疾风连击、打击（没费）（恐惧模式下打出）
/// 之后 6～9 循环
/// </code>
/// （用户 2026-11-23：「我就这改了这几个回合，**后面的不变**」⇒ 第 6～9 回合沿用上一版。）
///
/// ★ 2026-11-23 第六轮（用户「削弱，调换了一下一些意图的顺序」）：第 1～5 回合换成新顺序
///   （「化体为空」那套从第 5 挪到第 3 并多一张打击 + 奇迹不出；「奇迹/斋戒」那套挪到第 4；
///   「供奉」那套挪到第 5；第 2 回合的第二张防御改成「没费」），**第 6～9 回合不变**。
///   能力面板的单体高压回合同时改成 `3、5、7、9`。
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
/// 1 = 以手拒之1 + 疾风0 + 防御1 ⇒ **2**
/// 2 = 打击1 + 防御1 ⇒ **2**（第二张防御没费、奇迹不出）
/// 3 = 化体1 + 疾风0 + 警惕2 + 疾风0 + 打击1 ⇒ 4 ⇒ **4**（奇迹不出）
/// 4 = 奇迹0(+1) + 斋戒2 + 心灵堡垒1 ⇒ 2 + 1 = 3 ⇒ **2**（防御没费）
/// 5 = 供奉0 + 追击1 + 腰带抽打1 ⇒ **2**
/// 6 = 疾风0 + 防御1 ⇒ **1**（洗牌回合，第二张防御没费）
/// 7 = 同 5 ⇒ **2**
/// 8 = 防御1 ⇒ **1**（第二张防御、打击没费）
/// 9 = 化体1 + 疾风0 + 警惕2 + 疾风0 ⇒ 3 ⇒ **3**（打击没费）
///
/// ⚠ 第 3 回合的 4 点：她开局就在平静（泪滴吊坠盒），「化体为空」退出平静时原版还会 +2，
/// 所以那一回合实际富余（多出来的能量不会让她多打牌，剧本只打清单里的牌）。
///
/// ## 进阶（用户 2026-11-23）
/// · 进阶 8：升级 **以手拒之 / 心灵堡垒**
/// · 进阶 9：升级 **供奉 / 斋戒**
/// · 恐惧模式：**升级疾风连击**。
/// </summary>
public static class WatcherFlurryScript
{
    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            // ── 1 ── 以手拒之 / 疾风连击 / 防御        （2 费 = 1 + 0 + 1）
            Turn(2, "1. 以手拒之、疾风连击、防御",
                Play(WatcherCards.TalkToTheHand),
                Play(WatcherCards.FlurryOfBlows),
                Play(WatcherCards.Defend)),

            // ── 2 ── 打击 / 防御 / 防御（没费）/ 奇迹（不出）   （2 费 = 1 + 1）
            Turn(2, "2. 打击、防御、防御（没费）、奇迹（不出）",
                Play(WatcherCards.Strike),
                Play(WatcherCards.Defend),
                Hold(WatcherCards.Defend),
                Hold(WatcherCards.Miracle, cost: 0)),

            // ── 3 ── 化体为空 / 疾风连击 / 警惕 / 疾风连击 / 打击 / 奇迹（不出）
            //        （4 费 = 化体1 + 疾风0 + 警惕2 + 疾风0 + 打击1；泪滴吊坠盒让她开局在平静，
            //          「化体为空」退出平静原版还给 +2，所以这回合实际有富余）
            Turn(4, "3. 化体为空、疾风连击、警惕、疾风连击、打击、奇迹（不出）",
                Play(WatcherCards.EmptyBody),
                Play(WatcherCards.FlurryOfBlows),
                Play(WatcherCards.Vigilance),
                Play(WatcherCards.FlurryOfBlows),
                Play(WatcherCards.Strike),
                Hold(WatcherCards.Miracle, cost: 0)),

            // ── 4 ── 奇迹 / 斋戒 / 心灵堡垒 / 防御（没费）   （2 费 + 奇迹 1 = 3 = 0 + 2 + 1）
            Turn(2, "4. 奇迹、斋戒、心灵堡垒、防御（没费）",
                Play(WatcherCards.Miracle, cost: 0),
                Play(WatcherCards.Fasting),
                Play(WatcherCards.MentalFortress),
                Hold(WatcherCards.Defend)),

            // ── 5 ── 供奉 / 追击 / 腰带抽打            （2 费 = 0 + 1 + 1）
            Turn(2, "5. 供奉、追击、腰带抽打",
                Play(WatcherCards.Consecrate),
                Play(WatcherCards.FollowUp),
                Play(WatcherCards.SashWhip)),

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

            // ── 8 ── 防御 / 防御（没费）/ 打击（没费）    （1 费 = 1）
            Turn(1, "8. 防御、防御（没费）、打击（没费）",
                Play(WatcherCards.Defend),
                Hold(WatcherCards.Defend),
                Hold(WatcherCards.Strike)),

            // ── 9 ── 同第 5 回合                        （3 费）
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
            // ★ v0.2.904（用户 2026-11-23，实机截图后订正）：恐惧模式有两回合和普通不一样
            //   —— 第 5 回合换顺序（心灵堡垒先落地，后面的「化体为空」退姿态才算得上格挡），
            //   第 8 回合多 1 费、多打一张打击。
            //   为什么走 `Resolve` 而不在 `Build()` 里判 `DreadMode.IsActive`：`BossDefs` 是
            //   **静态初始化**，那时 `DreadMode.IsActive` 恒为 false（同养手骨 / 华丽猎 / 轰击储的先例）。
            Resolve = DreadTurnOverride,
        };
    }

    /// <summary>
    /// **恐惧模式的回合覆盖**（用户 2026-11-23，第二版削弱后的口径）：
    /// 用户原话里带「（恐惧模式下打出）」的那几张，就是**恐惧模式才打得出来**的牌
    /// —— 恐惧模式下她自身免疫「能量降低」（`InstallFastingNoEnergyDown`），斋戒不再吞她那 1 点能量：
    /// · 第 9 回合 → 多打一张 **打击**（4 费；普通模式 3 费、打击没费留手）；
    /// · 第 6 回合 → 两张 **防御** 都打出来（2 费；普通模式 1 费、第二张没费）；
    /// · 第 8 回合 → 两张 **防御** 都打出来（2 费；普通模式 1 费、只打一张防御），打击永远没费。
    /// ⚠ 第 5 回合**不再有恐惧覆盖**（2026-11-23 第六轮把「化体为空」那套挪到第 3 回合了，
    ///   第 5 回合现在和普通模式一样是「供奉、追击、腰带抽打」）。
    /// 返回 null ⇒ 交回静态回合表。
    /// </summary>
    private static BossTurn? DreadTurnOverride(int turn)
    {
        if (!Core.DreadMode.IsActive) return null;

        if (turn == 9)
        {
            // 第 9 回合仍是「化体为空」那一套：恐惧模式多打一张打击
            // （4 费 = 化体1 + 疾风0 + 警惕2 + 疾风0 + 打击1）；普通模式那张是「打击（没费）」留手。
            return Turn(4, "9.（恐惧）化体为空、疾风连击、警惕、疾风连击、打击",
                Play(WatcherCards.EmptyBody),
                Play(WatcherCards.FlurryOfBlows),
                Play(WatcherCards.Vigilance),
                Play(WatcherCards.FlurryOfBlows),
                Play(WatcherCards.Strike));
        }

        if (turn == 6)
        {
            return TurnEnergy(2, true, "6.（恐惧）洗牌、疾风连击、防御、防御",
                Play(WatcherCards.FlurryOfBlows),
                Play(WatcherCards.Defend),
                Play(WatcherCards.Defend));
        }

        if (turn == 8)
        {
            // ★ 2026-11-23 用户：「第 8 回合的 2 费应该只足以打出 1 防 1 打」→ 随后给了权威清单：
            //   `8. 防御、防御（没费）（恐惧模式下打出）、打击（没费）`
            //   ⇒ 恐惧模式 = 两张防御都打（2 费），打击没费；普通模式只打一张防御（1 费）。
            return Turn(2, "8.（恐惧）防御、防御、打击（没费）",
                Play(WatcherCards.Defend),
                Play(WatcherCards.Defend),
                Hold(WatcherCards.Strike));
        }

        return null;
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
    /// · 打击 3 张 —— 第 2 回合两张、第 8 回合两张（含恐惧版两张）；
    /// · 防御 3 张 —— 第 3 / 6 回合各两张，多备一张防洗牌；
    /// · 奇迹 2 张 —— 至纯之水战斗开始也会给一张。
    /// ★ v0.2.905（用户 2026-11-23 削弱版）：**去掉「安宁」**（她不再需要主动进平静 ——
    ///   泪滴吊坠盒已经让她每场战斗开局就在平静里）。
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
        Add(WatcherCards.Vigilance, 2);
        Add(WatcherCards.Miracle, 2);
        Add(WatcherCards.Strike, 3);
        Add(WatcherCards.Defend, 3);
        return deck;
    }

    /// <summary>
    /// 遗物（用户 2026-11-23 削弱版）：**至纯之水、涅奥的祝福、手里剑、泪滴吊坠盒**。
    /// · 至纯之水 = 观者 mod 的初始遗物（反射取，战斗开始给一张奇迹）；
    /// · 涅奥的祝福 = 本 mod 的遗物（每回合 −1 能量 / −2 抽牌，她高血量的代价）；
    /// · 手里剑 `Shuriken`（本体）：每打出 3 张攻击牌 +1 力量；
    /// · 泪滴吊坠盒 `TeardropLocket`（观者 mod 遗物）：每场战斗开始时处于**平静**。
    ///   ★ 用户 2026-11-23：「削弱，**苦无换成泪滴吊坠盒**」—— 苦无（每 3 张攻击 +1 敏捷）
    ///   让她越打越肉，换成泪滴吊坠盒后没有敏捷成长了（平静本身不给格挡）。
    ///   ⚠ 副作用：她开局就在平静，第 5 / 9 回合的「化体为空」退出平静时**原版会给 +2 能量**
    ///   （v0.2.716 用户口径：那 2 费不要压），所以那两回合她手上会多出 2 点余量。
    /// </summary>
    public static List<Type> Relics()
    {
        var relics = new List<Type>();
        if (WatcherCards.PureWater is Type pure) relics.Add(pure);
        else Logger.Log("[CharBoss] 疾风观遗物：至纯之水 未解析（观者模组没加载？）");
        relics.Add(typeof(CharacterBosses.Relics.NeowBlessing));
        if (WatcherCards.Shuriken is Type shuriken) relics.Add(shuriken);
        if (WatcherCards.TeardropLocket is Type locket) relics.Add(locket);
        else Logger.Log("[CharBoss] 疾风观遗物：泪滴吊坠盒 未解析（观者模组没加载？）");
        Logger.Log("[CharBoss] 疾风观遗物（本次解析）：[" +
                   string.Join(", ", relics.ConvertAll(t => t.Name)) + "]");
        return relics;
    }

    /// <summary>恐惧模式额外升级的牌：**疾风连击**（用户 2026-11-23「恐惧强化：升级疾风连击」）。</summary>
    public static List<Type> DreadUpgradeTypes()
    {
        var list = new List<Type>();
        if (WatcherCards.FlurryOfBlows is Type flurry) list.Add(flurry);
        return list;
    }
}
#endif
