#if STS2_111   // 观者 Boss 只在 0.113 构建里（用户：107 不加这个 Boss）
using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Core;

/// <summary>
/// 三层 1 号观者 Boss「**神格观**」的剧本（用户 2026-11-15 设计，完整需求见
/// `docs/plans/boss-watcher-divinity.md`）。
///
/// 每回合的牌表**逐字照用户给的顺序**排；标了「有费就打」的牌就排在后面 ——
/// `BossScriptPlanner` 按剧本顺序出牌、能量不够自动跳过，正好等于「有费就打」（同 `SoulBoneScript` 的口径）。
///
/// 不覆盖每回合能量（`BossTurn.Energy = 0` ⇒ 用 `BossDefinition.EnergyForAct` + 遗物算出来的值）：
/// 用户口径是「回合开始 2 费」（`BossDefs.Watcher.Energy = 3` 已含 涅奥的祝福 −1 / 圣水 +1 之类的净额，
/// 具体账在 `docs/plans/boss-watcher-divinity.md`），多出来的能量来自「奇迹」与**进神格**。
///
/// 牌全部是观者模组的牌 ⇒ 只能运行期反射拿 `Type`（见 <see cref="WatcherCards"/>）；
/// 某张取不到（没装观者模组 / 该版本改名）就**从这一回合里剔掉**，不让整场崩。
///
/// ⚠ 两处待确认（见文档）：
///   · 第 7 回合的「洗牌」在观者与本体两份 cards.json 里都查不到 ⇒ 暂时跳过；
///   · 第 1 回合的「许愿（选择永生不死）」需要指定选项，等确认走哪个接口。
/// </summary>
public static class WatcherScript
{
    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            T("1. 许愿（永生不死）、打击（有费就打）、警惕（有费就打）、奇迹（不出）",
                W(WatcherCards.Wish), W(WatcherCards.Strike), W(WatcherCards.Vigilance),
                W(WatcherCards.Miracle, holdInstead: true)),

            T("2. 光辉、防御、敬拜（有费就打）、奇迹（不出）",
                W(WatcherCards.Brilliance), W(WatcherCards.Defend), W(WatcherCards.Worship),
                W(WatcherCards.Miracle, holdInstead: true)),

            T("3. 奇迹、聚能成刃（X 费）、供奉、贪婪",
                W(WatcherCards.Miracle), W(WatcherCards.ConjureBlade),
                W(WatcherCards.Consecrate), W(WatcherCards.Greed)),

            T("4. 化拳为空、摆手、欺瞒现实（有费就打）、平安（有费就打）",
                W(WatcherCards.EmptyFist), W(WatcherCards.WaveOfTheHand, upgradeMinAscension: 8),
                W(WatcherCards.DeceiveReality), W(WatcherCards.Safety)),

            T("5. 灭除之刃（段数 = 本回合 X 费）、虔信、旋身（有费就打）",
                W(WatcherCards.Expunger), W(WatcherCards.Devotion), W(WatcherCards.Swivel)),

            T("6. 打击、圣洁、结末（有费就打）",
                W(WatcherCards.Strike), W(WatcherCards.Sanctity), W(WatcherCards.Conclude)),

            // 7～11 循环（LoopFrom = 7 / LoopLength = 5）。
            // 「洗牌」查不到 ⇒ 本回合实际从「光辉」开始，待用户确认后再补。
            T("7. 洗牌（待确认）、光辉、敬拜（有费就打）、贪婪",
                W(WatcherCards.Brilliance), W(WatcherCards.Worship), W(WatcherCards.Greed)),

            T("8. 打击、供奉、警惕（有费就打）",
                W(WatcherCards.Strike), W(WatcherCards.Consecrate), W(WatcherCards.Vigilance)),

            T("9. 灭除之刃、防御、旋身（有费就打）",
                W(WatcherCards.Expunger), W(WatcherCards.Defend), W(WatcherCards.Swivel)),

            T("10. 打击、圣洁、结末（有费就打）",
                W(WatcherCards.Strike), W(WatcherCards.Sanctity), W(WatcherCards.Conclude)),

            T("11. 化拳为空、摆手、欺瞒现实（有费就打）、平安（有费就打）",
                W(WatcherCards.EmptyFist), W(WatcherCards.WaveOfTheHand, upgradeMinAscension: 8),
                W(WatcherCards.DeceiveReality), W(WatcherCards.Safety)),
        };

        return new BossScript
        {
            Turns = turns,
            LoopFrom = 7,        // 第 7 回合起循环（用户：之后 7～11 循环）
            LoopLength = 5,
        };
    }

    /// <summary>卡组：她靠剧本吃饭，卡组只放几张备用牌（真正的每回合手牌由剧本生成）。</summary>
    public static List<Type> Deck()
    {
        var deck = new List<Type>();
        if (WatcherCards.Strike is Type s) { deck.Add(s); deck.Add(s); deck.Add(s); }
        if (WatcherCards.Defend is Type d) { deck.Add(d); deck.Add(d); deck.Add(d); }
        return deck;
    }

    /// <summary>
    /// 遗物：圣水、涅奥的祝福、灯笼、佩尔的士兵、意外光滑的石头、布质果实、护喉甲。
    /// 圣水是**观者模组**的遗物（`WatcherMod.HolyWater`）⇒ 反射取；其余是本体/我们自己的。
    /// </summary>
    public static List<Type> Relics()
    {
        var relics = new List<Type>();
        if (WatcherCards.T("PureWater") is Type pure) relics.Add(pure);      // 至纯之水（观者初始遗物；用户 2026-11-15 订正：不是圣水）
        relics.Add(typeof(CharacterBosses.Relics.NeowBlessing));                    // 涅奥的祝福（我们自己复制的一份）
        relics.Add(typeof(MegaCrit.Sts2.Core.Models.Relics.Lantern));
        relics.Add(typeof(MegaCrit.Sts2.Core.Models.Relics.PaelsLegion));
        relics.Add(typeof(MegaCrit.Sts2.Core.Models.Relics.OddlySmoothStone));
        relics.Add(typeof(MegaCrit.Sts2.Core.Models.Relics.LoomingFruit));
        relics.Add(typeof(MegaCrit.Sts2.Core.Models.Relics.Gorget));
        return relics;
    }

    /// <summary>进阶 9 的额外遗物：手摇鼓（`WatcherMod.Damaru`，观者模组的遗物）。</summary>
    public static List<Type>? AscensionRelics()
    {
        var extra = new List<Type>();
        if (WatcherCards.T("Damaru") is Type drum) extra.Add(drum);
        return extra.Count > 0 ? extra : null;
    }

    // ── 剧本小工具 ─────────────────────────────────────────────────────────
    private static BossTurn T(string note, params ScriptCard?[] cards)
    {
        var list = new List<ScriptCard>();
        foreach (var c in cards) if (c != null) list.Add(c);
        return new BossTurn { Note = note, Cards = list };
    }

    /// <summary>观者的牌：类型是运行期反射拿的，取不到就返回 null（这一格会被剔掉）。</summary>
    private static ScriptCard? W(Type? card, bool upgraded = false, int upgradeMinAscension = 0, bool holdInstead = false)
    {
        if (card == null) return null;
        return new ScriptCard
        {
            Card = card,
            Upgraded = upgraded,
            UpgradeMinAscension = upgradeMinAscension,   // 进阶 8 起升级「摆手」
            HoldInstead = holdInstead,                   // 有费也不出（留在手里）
            From = "deck",
        };
    }
}
#endif
