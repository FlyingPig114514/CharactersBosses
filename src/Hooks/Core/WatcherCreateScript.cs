#if STS2_111   // 观者 Boss 只在 0.113 构建里（用户：107 不加这个 Boss）
using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Core;

/// <summary>
/// **二层 1 号观者 Boss「印牌观」**的剧本（用户 2026-11-16 设计，v0.2.713 按用户订正重排）。
///
/// 用户原话（逐字）：
/// ```
/// 1.武器库+、操控现实+、改造现实、惩恶+（没费）
/// 2.奇迹、心灵堡垒、警惕、打击（没费）、惩恶+（没费）
/// 3.义愤填膺、惩恶+、创世之柱
/// 4.供奉、暴怒+、内心宁静
/// 5.欺瞒现实、平安+、何人僭越、化拳为空（没费）
/// 6.洗牌。改造现实、化拳为空、警惕、惩恶+（没费）
/// 7.暴怒+、欺瞒现实、惩恶+、平安+、打击（没费）
/// 8.供奉、义愤填膺、内心宁静（没费）
/// 之后6～8循环。
/// ```
///
/// ## 能量（用户 2026-11-16 订正：「你好好算算，你有费打吗？每回合只有2费」）
/// ⚠ `BossTurn.Energy` **会覆盖她那个回合的真实能量**（`CharBossMonster.Turn.cs` 的
/// `if (scriptedTurn != null &amp;&amp; scriptedTurn.Energy &gt; 0) energy = scriptedTurn.Energy;`），
/// 而 `CardCmd.AutoPlay` **不扣费** ⇒ 写大了就会出现「没费的牌照样被打出来」。
/// 所以这一栏必须严格按**真实卡费**算：
///
/// | 牌 | 费 | 牌 | 费 |
/// |---|---|---|---|
/// | 操控现实+ | **0**（升级 −1） | 暴怒+ | **1**（升级 −1） |
/// | 供奉 / 何人僭越 / 奇迹 | 0 | 武器库 / 创世之柱 / 改造现实 / 欺瞒现实 / 惩恶 / 平安 / 义愤填膺 / 内心宁静 / 化拳为空 / 打击 / 心灵堡垒 | 1 |
/// | 警惕 | 2 | （奇迹打出时 +1 能量） | |
///
/// 逐回合预算（= 用户列出的**要打出来**那些牌的费用之和；标了「没费」的不计入，
/// 并保证它们再差 1 点打不出来）：
/// 1 → **2**（武器库+ 1 + 操控现实+ 0 + 改造现实 1；惩恶+ 还差 1 ⇒ 没费 ✔）
/// 2 → **2**（奇迹 0 但 +1 能量 + 心灵堡垒 1 + 警惕 2 = 3；打击 / 惩恶+ 还差 1 ✔）
/// 3 → **3**（义愤填膺 1 + 惩恶+ 1 + 创世之柱 1）
/// 4 → **2**（供奉 0 + 暴怒+ 1 + 内心宁静 1）
/// 5 → **2**（欺瞒现实 1 + 平安+ 1 + 何人僭越 0；化拳为空还差 1 ✔）
/// 6 → **3**（改造现实 1 + 警惕 2；化拳为空 1 再打就差 1 ⇒ 没费 ✔，惩恶+ 同样 ✔）
/// 7 → **4**（暴怒+ 1 + 欺瞒现实 1 + 惩恶+ 1 + 平安+ 1；打击还差 1 ✔）
/// 8 → **1**（供奉 0 + 义愤填膺 1；内心宁静还差 1 ✔）
///
/// ## 「拦掉原版的印牌」
/// 「改造现实」会把一张「惩恶」、「欺瞒现实」会把一张「平安」塞进她手牌（反编译确认）。
/// 而剧本本来就写着这两张 ⇒ 由 `CharBossMonster.BlocksGeneratedCardsEntries` 拦掉原版那份，
/// **改由我们手动结算一次「生成了一张牌」**（触发她的「创世之柱」+格挡 /「武器库」+力量），
/// 见 `BossMasterRealityPower.TriggerPrintAsync`。
///
/// ## 进阶（用户给的）
/// · **进阶 8**：升级「创世之柱」与「心灵堡垒」；
/// · **进阶 9**：升级「改造现实」「供奉」「义愤填膺」。
/// </summary>
public static class WatcherCreateScript
{
    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            // ── 1 ── 武器库+ / 操控现实+ / 改造现实 / 惩恶+（没费）   （2 费）
            Turn(2, "1. 武器库+、操控现实+、改造现实、惩恶+（没费）",
                W(WatcherCards.Arsenal, upgraded: true),
                W(WatcherCards.MasterReality, upgraded: true),
                W(WatcherCards.CarveReality, upgradeMinAscension: 9),
                W(WatcherCards.Smite, upgraded: true)),

            // ── 2 ── 奇迹 / 心灵堡垒（进阶 8 起升级）/ 警惕 / 打击（没费）/ 惩恶+（没费）   （2 费 + 奇迹 1 = 3）
            Turn(2, "2. 奇迹、心灵堡垒（进阶8起升级）、警惕、打击（没费）、惩恶+（没费）",
                W(WatcherCards.Miracle),
                W(WatcherCards.MentalFortress, upgradeMinAscension: 8),
                W(WatcherCards.Vigilance),
                W(WatcherCards.Strike),
                W(WatcherCards.Smite, upgraded: true)),

            // ── 3 ── 义愤填膺（进阶 9 起升级）/ 惩恶+ / 创世之柱（进阶 8 起升级）   （3 费）— 单体高压
            Turn(3, "3. 义愤填膺（进阶9起升级）、惩恶+、创世之柱（进阶8起升级）",
                W(WatcherCards.Indignation, upgradeMinAscension: 9),
                W(WatcherCards.Smite, upgraded: true),
                W(WatcherCards.PillarOfCreation, upgradeMinAscension: 8)),

            // ── 4 ── 供奉（进阶 9 起升级）/ 暴怒+ / 内心宁静   （2 费）— 群体高压
            Turn(2, "4. 供奉（进阶9起升级）、暴怒+、内心宁静",
                W(WatcherCards.Consecrate, upgradeMinAscension: 9),
                W(WatcherCards.Eruption, upgraded: true),
                W(WatcherCards.InnerPeace)),

            // ── 5 ── 欺瞒现实 / 平安+ / 何人僭越 / 化拳为空（没费）   （2 费）
            Turn(2, "5. 欺瞒现实、平安+、何人僭越、化拳为空（没费）",
                W(WatcherCards.DeceiveReality),
                W(WatcherCards.Safety, upgraded: true),
                W(WatcherCards.KnowThyPlace),
                W(WatcherCards.EmptyFist)),

            // ── 6 ── 洗牌 / 改造现实（进阶 9 起升级）/ 化拳为空 / 警惕 / 惩恶+（没费）   （4 费）← 6～8 循环起点
            Turn(4, "6. 洗牌（她重新洗牌）、改造现实（进阶9起升级）、化拳为空、警惕、惩恶+（没费）",
                W(WatcherCards.CarveReality, upgradeMinAscension: 9),
                W(WatcherCards.EmptyFist),
                W(WatcherCards.Vigilance),
                W(WatcherCards.Smite, upgraded: true)),

            // ── 7 ── 暴怒+ / 欺瞒现实 / 惩恶+ / 平安+ / 打击（没费）   （4 费）— 单体 + 群体高压
            Turn(4, "7. 暴怒+、欺瞒现实、惩恶+、平安+、打击（没费）",
                W(WatcherCards.Eruption, upgraded: true),
                W(WatcherCards.DeceiveReality),
                W(WatcherCards.Smite, upgraded: true),
                W(WatcherCards.Safety, upgraded: true),
                W(WatcherCards.Strike)),

            // ── 8 ── 供奉（进阶 9 起升级）/ 义愤填膺（进阶 9 起升级）/ 内心宁静（没费）   （1 费）
            Turn(1, "8. 供奉（进阶9起升级）、义愤填膺（进阶9起升级）、内心宁静（没费）",
                W(WatcherCards.Consecrate, upgradeMinAscension: 9),
                W(WatcherCards.Indignation, upgradeMinAscension: 9),
                W(WatcherCards.InnerPeace)),
        };

        return new BossScript
        {
            Turns = turns,
            // ⚠ `LoopFrom` 是**1 起**的回合号（见 `BossScript.TurnAt`：`index = LoopFrom - 1 + (turn - LoopFrom) % LoopLength`）
            //   ★ v0.2.714（用户 2026-11-16）：「你循环错了，你现在循环的是5~7，而不是6~8，第八回合被错误的跳过了」
            //   —— 我上一版把 1 起当成了 0 起，填了 5 ⇒ 第 6 回合起跑的是**第 5 回合的牌**。现在写 6。
            LoopFrom = 6,       // 用户：之后 6～8 循环
            LoopLength = 3,
        };
    }

    /// <summary>卡组：她靠剧本吃饭，卡组只放备用牌（真正的每回合手牌由剧本生成）。</summary>
    public static List<Type> Deck()
    {
        var deck = new List<Type>();
        if (WatcherCards.Strike is Type s) { deck.Add(s); deck.Add(s); deck.Add(s); }
        if (WatcherCards.Defend is Type d) { deck.Add(d); deck.Add(d); deck.Add(d); }
        return deck;
    }

    /// <summary>
    /// 遗物（用户原话）：至纯之水、涅奥的祝福、斗篷扣、浮木、水银沙漏。
    /// 至纯之水是**观者模组**的遗物 ⇒ 反射取；涅奥的祝福是我们自己复制的那份；其余是本体遗物。
    /// </summary>
    public static List<Type> Relics()
    {
        var relics = new List<Type>();
        if (WatcherCards.T("PureWater") is Type pure) relics.Add(pure);              // 至纯之水
        relics.Add(typeof(CharacterBosses.Relics.NeowBlessing));                     // 涅奥的祝福
        relics.Add(typeof(MegaCrit.Sts2.Core.Models.Relics.CloakClasp));             // 斗篷扣
        relics.Add(typeof(MegaCrit.Sts2.Core.Models.Relics.Driftwood));              // 浮木
        relics.Add(typeof(MegaCrit.Sts2.Core.Models.Relics.MercuryHourglass));       // 水银沙漏
        return relics;
    }

    // ── 剧本小工具 ─────────────────────────────────────────────────────────
    private static BossTurn Turn(int energy, string note, params ScriptCard?[] cards)
    {
        var list = new List<ScriptCard>();
        foreach (var c in cards) if (c != null) list.Add(c);
        return new BossTurn { Note = note, Cards = list, Energy = energy };
    }

    /// <summary>观者 / 储君的牌：类型是运行期反射拿的，取不到就返回 null（这一格会被剔掉）。</summary>
    private static ScriptCard? W(Type? card, bool upgraded = false, int upgradeMinAscension = 0,
                                 bool holdInstead = false, int costOverride = -1)
    {
        if (card == null) return null;
        return new ScriptCard
        {
            Card = card,
            Upgraded = upgraded,
            UpgradeMinAscension = upgradeMinAscension,
            HoldInstead = holdInstead,
            CostOverride = costOverride,
            From = "deck",
        };
    }
}
#endif
