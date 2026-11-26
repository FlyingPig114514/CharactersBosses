#if STS2_111   // 观者 Boss 只在这个构建里
using System;
using System.Collections.Generic;
using System.Linq;

namespace CharacterBosses.Core;

/// <summary>
/// **一层 1 号观者 Boss「预见观」**的剧本（用户 2026-11-17 设计）。
///
/// 用户原话（逐字）：
/// <code>
/// 1.涅槃、打击、笨拙
/// 2.奇迹、心灵堡垒、警惕、打击（没费）
/// 3.天眼（丢弃迂回、防御）、迂回、先见之明、打击（没费）
/// 4.（先见之明丢弃受伤、防御）、迂回、化体为空、斩破命运（弃暴怒、贪婪）、迂回、防御、打击
/// 5.（先见之明不弃）迂回、碰撞连击、安宁、打击（没费）
/// 6.洗牌（美琅脂弃暴怒、迂回）、（先见之明弃贪婪）、迂回、天眼（弃打击、防御）、迂回、防御、打击（没费）
/// 7.（先见之明弃受伤）、迂回、化体为空、斩破命运（弃打击）、迂回、防御、碰撞连击
/// 8.（先见之明不弃）、迂回、警惕、打击（没费）、防御（没费）
/// 之后6～8循环
/// </code>
///
/// ## 先见之明 / 天眼 / 斩破命运 / 美琅脂 的「弃 X」= **她在抽牌堆里预见掉的牌**
/// 用户：「预见是从抽牌堆里弃牌的，所以**被弃的牌不用在她手牌中显示**」
/// ⇒ 那些牌在剧本里用 <see cref="Scried"/> 表示：**不进手牌**（`NotDrawn`）、只记账，
/// 因为预见本来就是从抽牌堆弃，玩家看不到它们出现在她手上。
/// （同名的「迂回」她手里还有别的份 ⇒ 被预见掉的那张和真的要打出去的那张是**两张牌**。）
///
/// ## 能量（按 A 版观者卡费反推，反编译 `Watcher.dll`）
/// | 牌 | 费 | | 牌 | 费 |
/// |---|---|---|---|---|
/// | 涅槃 Nirvana（Power，MagicNumber 3） | 1 | | 天眼 ThirdEye | 1 |
/// | 迂回 Weave（0 费攻击，伤害 4） | **0** | | 斩破命运 CutThroughFate | 1 |
/// | 先见之明 Foresight（Power，预见 3） | 1 | | 碰撞连击 BowlingBash（伤害 7） | 1 |
/// | 心灵堡垒 MentalFortress | 1 | | 警惕 Vigilance | 2 |
/// | 化体为空 EmptyBody | 1 | | 防御 Defend | 1 |
/// | 打击 Strike | 1 | | 安宁 Tranquility | 1 |
/// | 奇迹 Miracle | 0（打出 +1 能量） | | 笨拙 Clumsy（诅咒，打不出） | — |
///
/// 逐回合能量（基础 2 = `BossDefs.Watcher.Energy 3` − 涅奥的祝福 1；第 4、7 回合 **4** 是因为
/// 「化体为空」退出平静，原版 `Calm` 离场 +2）：
/// 1 = 涅槃1 + 打击1 → **2**
/// 2 = 奇迹0(+1) + 心灵堡垒1 + 警惕2 → **2**（打击没费）
/// 3 = 天眼1 + 迂回0 + 先见之明1 → **2**（打击没费）
/// 4 = 迂回0 + 化体为空1 + 斩破命运1 + 迂回0 + 防御1 + 打击1 = 4；退出平静 +2 ⇒ **4**
/// 5 = 迂回0 + 碰撞连击1 + 安宁1 → **2**（打击没费）
/// 6 = 迂回0 + 天眼1 + 迂回0 + 防御1 → **2**（打击没费）
/// 7 = 迂回0 + 化体为空1 + 斩破命运1 + 迂回0 + 防御1 + 碰撞连击1 = 5；退出平静 +2 ⇒ **4**
/// 8 = 迂回0 + 警惕2 → **2**（打击、防御都没费）
///
/// ## 进阶
/// · 进阶 8：升级 **涅槃 / 心灵堡垒**
/// · 进阶 9：升级 **碰撞连击 / 斩破命运**
/// · 恐惧模式：**升级迂回**（其余同普通）
/// </summary>
public static class WatcherForesightScript
{
    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            // ── 1 ── 涅槃 / 打击 / 笨拙（诅咒留手）   （2 费）
            Turn(2, "1. 涅槃、打击、笨拙（诅咒留手）",
                Play(WatcherCards.Nirvana),
                Play(WatcherCards.Strike),
                Curse(WatcherCards.Clumsy)),

            // ── 2 ── 奇迹 / 心灵堡垒 / 警惕 / 打击（没费）   （2 费 = 2 + 奇迹 1 − 打击 1）
            Turn(2, "2. 奇迹、心灵堡垒、警惕、打击（没费）",
                Play(WatcherCards.Miracle, cost: 0),
                Play(WatcherCards.MentalFortress),
                Play(WatcherCards.Vigilance),
                Hold(WatcherCards.Strike, cost: 1)),

            // ── 3 ── 天眼（预见掉 迂回/防御）/ 迂回 / 先见之明 / 打击（没费）   （2 费）
            Turn(2, "3. 天眼（弃 迂回·防御）、迂回、先见之明、打击（没费）",
                Play(WatcherCards.ThirdEye),
                Scried(WatcherCards.Weave), Scried(WatcherCards.Defend),
                Play(WatcherCards.Weave),
                Play(WatcherCards.Foresight),
                Hold(WatcherCards.Strike, cost: 1)),

            // ── 4 ── （先见之明弃 受伤/防御）/ 迂回 / 化体为空 / 斩破命运（弃 暴怒·贪婪）/ 迂回 / 防御 / 打击   （4 费）
            // ★ 2026-11-26（用户：**移除第 4 回合的备注**）
            Turn(4, "",
                Scried(WatcherCards.Injury), Scried(WatcherCards.Defend),
                Play(WatcherCards.Weave),
                Play(WatcherCards.EmptyBody),
                Play(WatcherCards.CutThroughFate),
                Scried(WatcherCards.Eruption), Scried(WatcherCards.Greed),
                Play(WatcherCards.Weave),
                Play(WatcherCards.Defend),
                Play(WatcherCards.Strike)),

            // ── 5 ── （先见之明不弃）/ 迂回 / 碰撞连击 / 安宁 / 打击（没费）   （2 费）
            Turn(2, "5. 迂回、碰撞连击、安宁、打击（没费）",
                Play(WatcherCards.Weave),
                Play(WatcherCards.BowlingBash),
                Play(WatcherCards.Tranquility),
                Hold(WatcherCards.Strike, cost: 1)),

            // ── 6 ── 洗牌（美琅脂弃 暴怒·迂回）/（先见之明弃 贪婪）/ 迂回 / 天眼（弃 打击·防御）/ 迂回 / 防御 / 打击（没费）   ← 6~8 循环起点   （2 费）
            TurnEnergy(2, true, "6. 洗牌。",
                Scried(WatcherCards.Eruption), Scried(WatcherCards.Weave),
                Scried(WatcherCards.Greed),
                Play(WatcherCards.Weave),
                Play(WatcherCards.ThirdEye),
                Scried(WatcherCards.Strike), Scried(WatcherCards.Defend),
                Play(WatcherCards.Weave),
                Play(WatcherCards.Defend),
                Hold(WatcherCards.Strike, cost: 1)),

            // ── 7 ── （先见之明弃 受伤）/ 迂回 / 化体为空 / 斩破命运（弃 打击）/ 迂回 / 防御 / 碰撞连击   （4 费）
            // ★ 2026-11-26（用户：**移除第 7 回合的备注**）
            Turn(4, "",
                Scried(WatcherCards.Injury),
                Play(WatcherCards.Weave),
                Play(WatcherCards.EmptyBody),
                Play(WatcherCards.CutThroughFate),
                Scried(WatcherCards.Strike),
                Play(WatcherCards.Weave),
                Play(WatcherCards.Defend),
                Play(WatcherCards.BowlingBash)),

            // ── 8 ── （先见之明不弃）/ 迂回 / 警惕 / 打击（没费）/ 防御（没费）   （2 费）
            Turn(2, "8. 迂回、警惕、打击（没费）、防御（没费）",
                Play(WatcherCards.Weave),
                Play(WatcherCards.Vigilance),
                Hold(WatcherCards.Strike, cost: 1),
                Hold(WatcherCards.Defend, cost: 1)),
        };

        return new BossScript
        {
            Turns = turns,
            // 用户：「之后 6～8 循环」；`LoopFrom` 是 **1 起**的回合号
            LoopFrom = 6,
            LoopLength = 3,
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

    /// <summary>「（没费）」留手的那张。</summary>
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

    /// <summary>诅咒（打不出，只是留在手牌里，例如笨拙）。</summary>
    private static ScriptCard? Curse(Type? card)
    {
        if (card == null) return null;
        return new ScriptCard { Card = card, From = "deck" };
    }

    /// <summary>
    /// **被她「预见」掉的牌**（天眼 / 先见之明 / 斩破命运 / 美琅脂）。
    ///
    /// 用户 2026-11-18 明确：「被预见丢弃的其它牌是**直接从抽牌堆到弃牌堆**，不需要展示在手牌里
    /// （这和猎人从手牌中弃牌的逻辑不一样）」⇒ 用 <see cref="ScriptCard.ScriedFromDraw"/>：
    /// 造手牌时**不创建这张牌、不进手牌**，只把她抽牌堆里那张真实实例挪进弃牌堆
    /// （见 `BossScriptRunner.MoveScriedFromDrawToDiscard`）。
    /// 同时保留 `DiscardInstead`（规划器不把它当本回合要打的牌）与 `NotDrawn`（谋杀加伤不数它）。
    /// </summary>
    private static ScriptCard? Scried(Type? card)
    {
        if (card == null) return null;
        return new ScriptCard
        {
            Card = card,
            From = "deck",
            ScriedFromDraw = true,
            NotDrawn = true,
            DiscardInstead = true,
        };
    }

    /// <summary>卡组（剧本里出现过的牌，按最大张数收；「迂回」同回合最多两张）。</summary>
    public static List<Type> Deck()
    {
        var deck = new List<Type>();
        void Add(Type? t, int count = 1)
        {
            if (t == null) return;
            for (int i = 0; i < count; i++) deck.Add(t);
        }
        Add(WatcherCards.Nirvana);
        Add(WatcherCards.MentalFortress);
        Add(WatcherCards.Foresight);
        Add(WatcherCards.ThirdEye);
        Add(WatcherCards.CutThroughFate);
        Add(WatcherCards.BowlingBash, 2);
        Add(WatcherCards.Weave, 4);          // 第 3/4/6/7 回合各要两张（打出的 + 被预见的），多备两张
        Add(WatcherCards.Vigilance, 2);
        Add(WatcherCards.EmptyBody, 2);
        Add(WatcherCards.Tranquility);
        Add(WatcherCards.Miracle, 2);
        Add(WatcherCards.Strike, 3);
        Add(WatcherCards.Defend, 3);
        Add(WatcherCards.Injury);
        Add(WatcherCards.Greed);
        Add(WatcherCards.Eruption);
        Add(WatcherCards.Clumsy);
        return deck;
    }

    /// <summary>
    /// 遗物（用户 2026-11-17）：**至纯之水、涅奥的祝福、美琅脂、手里剑**。
    /// · 至纯之水 = 观者 mod 的初始遗物（反射取）；
    /// · 美琅脂 `Melange`（观者 mod）：「每次洗抽牌堆时预见 3」—— 第 6 回合的「洗牌弃牌」就是它；
    /// · 手里剑 `Shuriken`（本体）：每打出 3 张攻击牌 +1 力量。
    /// </summary>
    public static List<Type> Relics()
    {
        var relics = new List<Type>();
        if (WatcherCards.PureWater is Type pure) relics.Add(pure);
        relics.Add(typeof(CharacterBosses.Relics.NeowBlessing));
        if (WatcherCards.Melange is Type melange) relics.Add(melange);
        if (WatcherCards.Shuriken is Type shuriken) relics.Add(shuriken);
        return relics;
    }

    /// <summary>恐惧模式额外升级的牌：**迂回**（用户 2026-11-17「恐惧增强：升级迂回」）。</summary>
    public static List<Type> DreadUpgradeTypes()
    {
        var list = new List<Type>();
        if (WatcherCards.Weave is Type weave) list.Add(weave);
        return list;
    }
}
#endif
