#if STS2_111   // 观者 Boss 只在这个构建里
using System;
using System.Collections.Generic;
using System.Linq;

namespace CharacterBosses.Core;

/// <summary>
/// **三层 3 号观者 Boss「红蓝观」**的剧本（内部 Key `WATCHER_RUSHDOWN` = 猛虎下山，
/// 用户 2026-11-18 逐字设计，原文见 `docs/plans/boss-watcher-rushdown.md`）。
///
/// ## 卡名 → id（全部查自观者 pck 的 `zhs/cards.json`，见 `WatcherCards`）
/// 猛虎下山 `WATCHER_RUSHDOWN`（1 费能力）、暴怒+ `WATCHER_ERUPTION_P`（2 费攻击）、
/// 警惕 `WATCHER_VIGILANCE`（2 费）、打击 `WATCHER_STRIKE_P`（1）、防御 `WATCHER_DEFEND_P`（1）、
/// 心灵堡垒 `WATCHER_MENTAL_FORTRESS`（1）、渐强 `WATCHER_CRESCENDO`（1）、
/// 粉碎关节 `WATCHER_CRUSH_JOINTS`（1）、安宁 `WATCHER_TRANQUILITY`（1）、奇迹 `WATCHER_MIRACLE`（0，+1 费）、
/// 义愤填膺 `WATCHER_INDIGNATION`（1）、供奉 `WATCHER_CONSECRATE`（0）、碰撞连击 `WATCHER_BOWLING_BASH`（1）、
/// 冥想 `WATCHER_MEDITATE`（1）、不惧妖邪 `WATCHER_FEAR_NO_EVIL`（1）、内心宁静 `WATCHER_INNER_PEACE`（1）、
/// 改造现实 `WATCHER_CARVE_REALITY`（1）、腾越 `WATCHER_VAULT`（3）、当头棒喝 `WATCHER_WALLOP`（2）、
/// 净化+ = 本体无色 `PURITY`（0）、腐朽 = 本体诅咒 `DECAY`（打不出）。
///
/// ## 逐回合能量（= 本回合**真的要打出去**的牌费之和；标了「没费、不弃」的牌不算）
/// 1 = 猛虎下山1 + 暴怒+2 + 警惕2 → **5**
/// 2 = 心灵堡垒1 + 渐强1 + 腰带抽打1 + 安宁1 → **4**
/// 3 = 义愤填膺1 + 供奉0 + 碰撞连击1 + 打击1 + 冥想1 → **4**（之后「节制」满 12 张 ⇒ 她的回合当场结束）
/// 4 = 暴怒+2 + 打击1 + 防御1 + 不惧妖邪1 → **5**
/// 5 = 当头棒喝2 → **2**
/// 6 = 打击1 + 内心宁静1 + 净化+0（消耗 腐朽·改造现实·防御×3）→ **2**
/// 7 = 碰撞连击1 + 猛虎下山1 → **2**（腾越没费不弃）
/// 8 = 奇迹0(+1) + 腾越3 → **3**（洗牌；腾越给她额外回合；同时「节制」第二次满 12 张结束本回合）
/// 额外回合 = 内心宁静1 + 碰撞连击1 + 净化+0 → **2**
/// 9 = 暴怒+2 + 当头棒喝2 + 义愤填膺1 → **5**
/// 10 = 供奉0 + 冥想1（捞内心宁静）→ **1**
/// 11 = 洗牌；内心宁静1 + 暴怒+2 + 净化+0（消耗 5 张）→ **3**（警惕触发特殊效果结束回合）
/// 12+ 默认 = 暴怒+×4(8) + 内心宁静×2(2) + 警惕×2(4) + 供奉×3(0) → **14**
/// 12+ 切换（玩家打出攻击牌）= 暴怒+×4(8) + 不惧妖邪×4(4) + 供奉×3(0) → **12**
///
/// ## 进阶
/// · 进阶 8：升级 **心灵堡垒 / 警惕**
/// · 进阶 9：升级 **义愤填膺 / 腰带抽打 / 当头棒喝 / 供奉**（腰带抽打 = 原「粉碎关节」那格，v0.2.769 换的）
/// · 恐惧模式：特殊效果换成「时间扭曲」（另计被锁定玩家的出牌并终止其回合）——
///   见 `Powers.BossTemperancePower`
///
/// ## 「净化+」
/// 所有 净化+ 都附魔「灵魂之力」（`ScriptCard.EnchantSouls` ⇒ 去掉「消耗」关键字），
/// 被她亲手打出的那几张还会把它下面点名的牌**真的消耗掉**（`ScriptCard.ExhaustWith`）。
/// </summary>
public static class WatcherRushdownScript
{
    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            // ── 1 ── 猛虎下山 / 暴怒+ / 警惕 / 打击（没费不弃）/ 防御（没费不弃）   （5 费）
            Turn(5, "1. 猛虎下山、暴怒+、警惕、打击（没费不弃）、防御（没费不弃）",
                Play(WatcherCards.Rushdown),
                Play(WatcherCards.Crescendo),
                Play(WatcherCards.Vigilance),
                Hold(WatcherCards.Strike),
                Hold(WatcherCards.Defend)),

            // ── 2 ── 心灵堡垒 / 渐强 / 腰带抽打 / 安宁 / 净化+（保留不出）/ 打击·防御（没费）/ 奇迹（保留不出）   （4 费）
            Turn(4, "2. 心灵堡垒、渐强、腰带抽打、安宁、净化+（保留不出）、打击（没费）、防御（没费）、奇迹（保留不出）",
                Play(WatcherCards.MentalFortress),
                Play(WatcherCards.Eruption, upgraded: true),
                Play(WatcherCards.SashWhip),
                Play(WatcherCards.Tranquility),
                HoldPurify(),
                Hold(WatcherCards.Strike),
                Hold(WatcherCards.Defend),
                Hold(WatcherCards.Miracle, cost: 0)),

            // ── 3 ── 义愤填膺 / 供奉 / 碰撞连击 / 打击 / 冥想（捞暴怒+）⇒ 节制满 12 张，回合结束   （4 费）
            Turn(4, "3. 义愤填膺、供奉、碰撞连击、打击、冥想（捞暴怒+）⇒ 节制触发结束回合",
                Play(WatcherCards.Indignation),
                Play(WatcherCards.Consecrate),
                Play(WatcherCards.BowlingBash),
                Play(WatcherCards.Strike),
                Play(WatcherCards.Meditate, returns: WatcherCards.Eruption),
                HoldPurify(),
                Hold(WatcherCards.Defend),
                Hold(WatcherCards.Miracle, cost: 0)),

            // ── 4 ── 暴怒+ / 打击 / 防御 / 不惧妖邪 / 净化+（保留不出）/ 防御×2（没费）/ 奇迹（保留不出）   （5 费）
            Turn(5, "4. 暴怒+、打击、防御、不惧妖邪、净化+（保留不出）、防御×2（没费）、奇迹（保留不出）",
                Play(WatcherCards.Eruption, upgraded: true),
                Play(WatcherCards.Strike),
                Play(WatcherCards.Defend),
                Play(WatcherCards.FearNoEvil),
                HoldPurify(),
                Hold(WatcherCards.Defend, 1),
                Hold(WatcherCards.Defend, 1),
                Hold(WatcherCards.Miracle, cost: 0)),

            // ── 5 ── 当头棒喝 / 净化+（保留不出）/ 防御×3（没费）/ 奇迹（保留不出）   （2 费）
            Turn(2, "5. 当头棒喝、净化+（保留不出）、防御×3（没费）、奇迹（保留不出）",
                Play(WatcherCards.Wallop),
                HoldPurify(),
                Hold(WatcherCards.Defend, 1),
                Hold(WatcherCards.Defend, 1),
                Hold(WatcherCards.Defend, 1),
                Hold(WatcherCards.Miracle, cost: 0)),

            // ── 6 ── 打击 / 内心宁静 / 净化+（消耗 腐朽·改造现实·防御×3）   （2 费）
            Turn(2, "6. 打击、内心宁静、净化+（消耗 腐朽·改造现实·防御×3）",
                Play(WatcherCards.Strike),
                Play(WatcherCards.InnerPeace),
                PlayPurify(WatcherCards.Decay, WatcherCards.CarveReality,
                           WatcherCards.Defend, WatcherCards.Defend, WatcherCards.Defend),
                Exhausted(WatcherCards.Decay),
                Exhausted(WatcherCards.CarveReality),
                Exhausted(WatcherCards.Defend),
                Exhausted(WatcherCards.Defend),
                Exhausted(WatcherCards.Defend),
                Hold(WatcherCards.Miracle, cost: 0)),

            // ── 7 ── 碰撞连击 / 猛虎下山 / 腾越（没费不弃）/ 奇迹（保留不出）   （2 费）
            // ★ v0.2.771（用户 2026-11-19：「第七和第八回合打腰带抽打干什么？那两张都应该是碰撞连击」）：
            //   v0.2.769 那次「所有粉碎关节 → 腰带抽打」的全局替换把这两张**碰撞连击**也换掉了，
            //   现在改回来（T2 的腰带抽打保留，T3 的碰撞连击位置不动）。
            Turn(2, "7. 碰撞连击、猛虎下山、腾越（没费不弃）、奇迹（保留不出）",
                Play(WatcherCards.BowlingBash),
                Play(WatcherCards.Rushdown),
                Hold(WatcherCards.Vault, 3),
                Hold(WatcherCards.Miracle, cost: 0)),

            // ── 8 ── 洗牌；奇迹(+1) / 腾越 ⇒ 节制第二次满 12 张结束回合，但腾越给额外回合   （3 费）
            //   ★ v0.2.771：额外回合里的那张是**碰撞连击**（用户 2026-11-19），不是腰带抽打。
            Turn(5, "8. 洗牌；奇迹、腾越（节制第二次满 12 张 ⇒ 结束回合，腾越给出额外回合）",
                shuffle: true,
                cards: new[]
                {
                    Play(WatcherCards.Miracle, cost: 0),
                    Play(WatcherCards.Vault),
                    Play(WatcherCards.InnerPeace),
                    Play(WatcherCards.BowlingBash),
                    PlayPurify(WatcherCards.Strike, WatcherCards.Strike, WatcherCards.Strike,
                               WatcherCards.Strike, WatcherCards.Defend),
                    Exhausted(WatcherCards.Strike),
                    Exhausted(WatcherCards.Strike),
                    Exhausted(WatcherCards.Strike),
                    Exhausted(WatcherCards.Strike),
                    Exhausted(WatcherCards.Defend),
                    Hold(WatcherCards.FearNoEvil),
                }),

            // ── 9 ── 暴怒+ / 当头棒喝 / 义愤填膺 / 不惧妖邪（没费不弃）/ 冥想（没费不弃）   （5 费）
            Turn(5, "9. 暴怒+、当头棒喝、义愤填膺、不惧妖邪（没费不弃）、冥想（没费不弃）",
                Play(WatcherCards.Eruption, upgraded: true),
                Play(WatcherCards.Wallop),
                Play(WatcherCards.Indignation),
                Hold(WatcherCards.FearNoEvil),
                Hold(WatcherCards.Meditate)),

            // ── 10 ── 供奉 / 冥想（捞内心宁静）/ 腰带抽打·警惕·不惧妖邪（不出）   （1 费）
            Turn(1, "10. 供奉、冥想（捞内心宁静）、腰带抽打（不出）、警惕（不出）、不惧妖邪（不出）",
                Play(WatcherCards.Consecrate),
                Play(WatcherCards.Meditate, returns: WatcherCards.InnerPeace),
                Hold(WatcherCards.SashWhip),
                Hold(WatcherCards.Vigilance, 2),
                Hold(WatcherCards.FearNoEvil)),

            // ── 11 ── 洗牌；内心宁静 / 暴怒+ / 净化+（消耗 5 张）/ 警惕（触发节制结束回合）/ 不惧妖邪（没费不弃）   （3 费）
            Turn(5, "11. 洗牌；内心宁静、暴怒+、净化+（消耗 腰带抽打·碰撞连击·当头棒喝·冥想·义愤填膺）、警惕（触发节制结束回合）、不惧妖邪（没费不弃）",
                shuffle: true,
                cards: new[]
                {
                    Play(WatcherCards.InnerPeace),
                    Play(WatcherCards.Eruption, upgraded: true),
                    PlayPurify(WatcherCards.SashWhip, WatcherCards.BowlingBash, WatcherCards.Wallop,
                               WatcherCards.Meditate, WatcherCards.Indignation),
                    Exhausted(WatcherCards.SashWhip),
                    Exhausted(WatcherCards.BowlingBash),
                    Exhausted(WatcherCards.Wallop),
                    Exhausted(WatcherCards.Meditate),
                    Exhausted(WatcherCards.Indignation),
                    Play(WatcherCards.Vigilance),
                    Hold(WatcherCards.FearNoEvil),
                }),

            // ── 12+ 默认无限 ──（14 费）洗牌；暴怒+/供奉/内心宁静 ×3 组、暴怒+/供奉/警惕、暴怒+/供奉/警惕（第二个警惕触发节制）
            Turn(14, "12+（默认）：洗牌；暴怒+、供奉、内心宁静 ×3 组、暴怒+、供奉、警惕（触发节制结束回合）、暴怒+、供奉、警惕、不惧妖邪（不出）、净化+（不出）",
                shuffle: true,
                cards: new[]
                {
                    Play(WatcherCards.Eruption, upgraded: true), Play(WatcherCards.Consecrate),
                    Play(WatcherCards.InnerPeace),
                    Play(WatcherCards.Eruption, upgraded: true), Play(WatcherCards.Consecrate),
                    Play(WatcherCards.InnerPeace),
                    Play(WatcherCards.Eruption, upgraded: true), Play(WatcherCards.Consecrate),
                    Play(WatcherCards.Vigilance),
                    Play(WatcherCards.Eruption, upgraded: true), Play(WatcherCards.Consecrate),
                    Play(WatcherCards.Vigilance),
                    Hold(WatcherCards.FearNoEvil),
                    HoldPurify(),
                }),

        };

        return new BossScript
        {
            Turns = turns,
            // 用户：「在 12 及以后的回合中」进入无限 ⇒ 第 12 回合起循环。
            // 循环区间先只取**默认**那一段（第 12 回合）；「玩家打出攻击牌就切换」那一段由
            // `BossTemperancePower` / 计划切换把本回合的计划换成第 13 回合那一段。
            LoopFrom = 12,
            LoopLength = 1,
            // 第 12 回合起：玩家打出攻击牌 ⇒ 换成「不惧妖邪」那一套无限（见 SwitchPlan）
            SwitchTurn = SwitchPlan(),
            SwitchFromTurn = 12,
        };
    }

    /// <summary>
    /// **第 12 回合起「玩家打出攻击牌」时切换成的那一套**（用户 2026-11-18）：
    /// 洗牌；暴怒+、供奉、不惧妖邪 ×3 组、暴怒+、供奉、不惧妖邪（触发节制结束回合）、
    /// 警惕（不出）、内心宁静（不出）、净化+（不出）。（12 费）
    ///
    /// ⚠ 它**不在** `Turns` 里（否则会把循环编号顶错一格 —— 用户 2026-11-18 实测「复读的回合错误」），
    /// 由 `BossTemperancePower` / 回合内切换补丁取用。
    /// </summary>
    public static BossTurn SwitchPlan() => Turn(12,
        "12+（玩家打出攻击牌后切换）：洗牌；暴怒+、供奉、不惧妖邪 ×3 组、暴怒+、供奉、不惧妖邪（触发节制结束回合）、警惕（不出）、内心宁静（不出）、净化+（不出）",
        shuffle: true,
        cards: new[]
        {
            Play(WatcherCards.Eruption, upgraded: true), Play(WatcherCards.Consecrate),
            Play(WatcherCards.FearNoEvil),
            Play(WatcherCards.Eruption, upgraded: true), Play(WatcherCards.Consecrate),
            Play(WatcherCards.FearNoEvil),
            Play(WatcherCards.Eruption, upgraded: true), Play(WatcherCards.Consecrate),
            Play(WatcherCards.FearNoEvil),
            Play(WatcherCards.Eruption, upgraded: true), Play(WatcherCards.Consecrate),
            Play(WatcherCards.FearNoEvil),
            Hold(WatcherCards.Vigilance, 2),
            Hold(WatcherCards.InnerPeace),
            HoldPurify(),
        });
    // ── 剧本小工具 ─────────────────────────────────────────────────────────
    private static BossTurn Turn(int energy, string note, params ScriptCard?[] cards)
        => Turn(energy, note, shuffle: false, cards);

    private static BossTurn Turn(int energy, string note, bool shuffle, params ScriptCard?[] cards)
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
    private static ScriptCard? Play(Type? card, int cost = -1, int upgradeMinAscension = 0, Type? returns = null,
                                   bool upgraded = false)
    {
        if (card == null) return null;
        return new ScriptCard
        {
            Card = card,
            CostThisTurn = cost,
            UpgradeMinAscension = upgradeMinAscension,
            Upgraded = upgraded,
            From = "deck",
            ReturnsToHand = returns == null ? Array.Empty<Type>() : new[] { returns },
        };
    }

    /// <summary>「（没费、不弃）」「（保留不出）」这类留在手里的牌。</summary>
    private static ScriptCard? Hold(Type? card, int cost = 1, bool upgraded = false)
    {
        if (card == null) return null;
        return new ScriptCard
        {
            Card = card,
            CostThisTurn = cost,
            HoldInstead = true,
            Upgraded = upgraded,
            From = "deck",
        };
    }

    /// <summary>净化+（保留不出）：留在手里，仍然附魔「灵魂之力」。</summary>
    private static ScriptCard? HoldPurify()
    {
        if (WatcherCards.Purity is not Type purity) return null;
        return new ScriptCard
        {
            Card = purity,
            CostThisTurn = 0,
            HoldInstead = true,
            Upgraded = true,
            From = "deck",
            EnchantSouls = true,
        };
    }

    /// <summary>净化+（打出去）：0 费，附魔「灵魂之力」，并把 `consumed` 里点名的牌**真的消耗掉**。</summary>
    private static ScriptCard? PlayPurify(params Type?[] consumed)
    {
        if (WatcherCards.Purity is not Type purity) return null;
        var list = new List<Type>();
        foreach (var t in consumed) if (t != null) list.Add(t);
        return new ScriptCard
        {
            Card = purity,
            CostThisTurn = 0,
            Upgraded = true,
            From = "deck",
            EnchantSouls = true,
            ExhaustWith = list.ToArray(),
        };
    }

    /// <summary>「被净化+消耗掉」的那一张（本回合不出手、压在手里给他看）。</summary>
    private static ScriptCard? Exhausted(Type? card) => Hold(card, 1);

    /// <summary>卡组（剧本里出现过的牌，按单回合最大张数收）。</summary>
    public static List<Type> Deck()
    {
        var deck = new List<Type>();
        void Add(Type? t, int count = 1)
        {
            if (t == null) return;
            for (int i = 0; i < count; i++) deck.Add(t);
        }
        Add(WatcherCards.Rushdown, 2);
        Add(WatcherCards.Eruption, 6);
        Add(WatcherCards.Vigilance, 5);
        Add(WatcherCards.Strike, 6);
        Add(WatcherCards.Defend, 10);
        Add(WatcherCards.MentalFortress);
        Add(WatcherCards.Crescendo);
        Add(WatcherCards.SashWhip, 2);
        Add(WatcherCards.Tranquility);
        Add(WatcherCards.Miracle, 4);
        Add(WatcherCards.Indignation, 3);
        Add(WatcherCards.Consecrate, 4);
        Add(WatcherCards.BowlingBash, 3);
        Add(WatcherCards.Meditate, 3);
        Add(WatcherCards.FearNoEvil, 5);
        Add(WatcherCards.InnerPeace, 3);
        Add(WatcherCards.CarveReality, 2);
        Add(WatcherCards.Vault);
        Add(WatcherCards.Wallop, 3);
        Add(WatcherCards.Purity, 2);      // 净化+（本体无色）
        Add(WatcherCards.Decay, 2);       // 腐朽（本体诅咒）
        return deck;
    }

    /// <summary>
    /// 遗物（用户 2026-11-18）：**至纯之水、涅奥的祝福、泪滴吊坠盒、符文金字塔、音叉、布质果实、坚固钳子**。
    /// 布质果实 +31 最大生命 ⇒ 她的血量用主定义的 103（见 `BossDefs` 里那只变体的 `HpBonus = 0`）。
    /// </summary>
    public static List<Type> Relics()
    {
        var relics = new List<Type>();
        if (WatcherCards.PureWater is Type pure) relics.Add(pure);
        relics.Add(typeof(CharacterBosses.Relics.NeowBlessing));
        if (WatcherCards.TeardropLocket is Type locket) relics.Add(locket);
        if (WatcherCards.RunicPyramid is Type pyramid) relics.Add(pyramid);
        if (WatcherCards.TuningFork is Type fork) relics.Add(fork);
        if (WatcherCards.LoomingFruit is Type fruit) relics.Add(fruit);
        if (WatcherCards.SturdyClamp is Type clamp) relics.Add(clamp);
        return relics;
    }

    /// <summary>恐惧模式额外升级的牌：**暴怒**（她的无限要它）。</summary>
    public static List<Type> DreadUpgradeTypes()
    {
        var list = new List<Type>();
        if (WatcherCards.Eruption is Type eruption) list.Add(eruption);
        return list;
    }
}
#endif
