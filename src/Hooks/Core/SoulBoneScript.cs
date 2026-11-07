using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// 第二层 Boss「**灵魂骨**」（亡灵契约师）——用户设计（2026-10-22 口述，逐条照抄）。
///
/// 特殊效果「**灵魂绑定**」：**你在回合内抽牌时，也视为亡灵契约师抽牌**
/// （会增加「自动化」的计数与「死亡行军」的伤害）。实现见
/// <c>Powers/BossSoulBoundPower</c> + <c>Patches/SoulBoundDrawPatch</c>。
///
/// 遗物：缚魂命匣、涅奥的祝福、战纹涂料、灵体外质、准备背包、腰带扣。
/// 进阶 8：升级死亡之舞、违逆；进阶 9：升级死亡行军、切断。
///
/// 剧本 8 回合，之后 **6～8 循环**。用户每回合逐张给了顺序，其中「有费就打」的牌
/// 就按顺序排在后面 —— <see cref="BossScriptPlanner"/> 按剧本顺序出牌、费不够自动跳过，
/// 正好等于「有费就打」；「没费打不了」同理。
/// </summary>
public static class SoulBoneScript
{
    /// <summary>
    /// 每回合的能量（**全部 0 = 不覆盖**，用 `BossDefinition.EnergyForAct` + 遗物算出来的值）。
    ///
    /// 用户 2026-10-23：「拖延+ 会给下回合 2 费」—— 这就是第 2 回合的费用来源，所以**不要**写死：
    ///   · 基础 3 点（3 − 涅奥的祝福 1 + 灵体外质 1）；
    ///   · 第 1 回合：拖延+(2) + 自动化+(1) + 挽歌(0) = 3 ⇒「切断(2)/防御(1) 没费打不了」天然成立 ✔
    ///   · 第 2 回合：3 + 拖延+ 的 2 = **5** ⇒ 守墓人+(1) + 死者苏生(3) + 死亡行军(1) 刚好 ✔
    ///   · 第 3 回合：3 ⇒ 巨镰(2) + 违逆(1) ✔（羞耻是诅咒，打不出）
    ///   · 第 4～6 回合：3 ⇒ 只够打「必打的」，标了「有费就打」的那些自然打不出 ✔
    ///   · 第 8 回合：第 7 回合又打了拖延+ ⇒ 3 + 2 = 5，多出来的 2 点正好给「出击/防御」这类 ✔
    /// 依据：`HonorsNextTurnEnergyActs = { 2 }`（`BossDefs.Necrobinder`）+ `CharBossMonster.PendingNextTurnEnergyCredit`。
    /// </summary>
    private static readonly int[] TurnEnergy = { 0, 0, 0, 0, 0, 0, 0, 0 };

    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            Turn("拖延+、自动化+、挽歌；切断、防御没费", 1,
                C<Delay>(true), C<Automation>(true), C<Dirge>(),
                C<Severance>(), C<DefendNecrobinder>()),

            Turn("守墓人+、死者苏生、死亡行军", 2,
                C<GraveWarden>(true), C<Reanimate>(), C<DeathMarch>()),

            Turn("巨镰（打 23）、违逆、羞耻（诅咒，打不出）", 3,
                C<TheScythe>(damage: 23), C<Defy>(), C<Shame>()),

            Turn("灵魂、鬼火、坟冢爆射+（捞切断）、切断（印一张灵魂）、灵魂、灵魂风暴、忧郁、防御", 4,
                C<Soul>(), C<Wisp>(), C<Graveblast>(true), C<Severance>(),
                C<Soul>(), C<SoulStorm>(), C<Melancholy>(), C<DefendNecrobinder>()),

            Turn("吞噬生命+、灵魂、灵魂、增生、死亡之舞、出击、防御", 5,
                C<DevourLife>(true), C<Soul>(), C<Soul>(), C<Spur>(),
                C<DanseMacabre>(), C<Unleash>(), C<DefendNecrobinder>()),

            Turn("洗牌。切断（印灵魂）、灵魂、死亡行军、忧郁、防御、羞耻", 6,
                C<Severance>(), C<Soul>(), C<DeathMarch>(), C<Melancholy>(),
                C<DefendNecrobinder>(), C<Shame>()),

            Turn("守墓人+、拖延+、灵魂、灵魂风暴、防御", 7,
                C<GraveWarden>(true), C<Delay>(true), C<Soul>(),
                C<SoulStorm>(), C<DefendNecrobinder>()),

            Turn("灵魂、灵魂、违逆、增生、坟冢爆射+、忧郁、出击、防御", 8,
                C<Soul>(), C<Soul>(), C<Defy>(), C<Spur>(),
                C<Graveblast>(true), C<Melancholy>(), C<Unleash>(), C<DefendNecrobinder>()),
        };

        return new BossScript
        {
            Turns = turns,
            LoopFrom = 6,     // 6～8 回合循环
            LoopLength = 3,
        };
    }

    /// <summary>第二幕的卡组（剧本实际造牌，这里用于自检与展示）。</summary>
    public static List<Type> Deck() => new()
    {
        typeof(Delay), typeof(Automation), typeof(Dirge),
        typeof(GraveWarden), typeof(Reanimate), typeof(DeathMarch),
        typeof(TheScythe), typeof(Defy), typeof(Shame),
        typeof(Soul), typeof(Wisp), typeof(Graveblast), typeof(Severance),
        typeof(SoulStorm), typeof(Melancholy), typeof(DevourLife),
        typeof(Spur), typeof(DanseMacabre), typeof(Unleash),
        typeof(DefendNecrobinder), typeof(DefendNecrobinder),
    };

    /// <summary>第二幕的遗物（涅奥的祝福由 CarriesNeowBlessing 自动带）。</summary>
    public static List<Type> Relics() => new()
    {
        typeof(BoundPhylactery),     // 缚魂命匣
        typeof(Relics.NeowBlessing),   // 涅奥的祝福：所有 Boss 都是第 2 件（用户 2026-10-27 口径）
        typeof(WarPaint),            // 战纹涂料
        typeof(Ectoplasm),           // 灵体外质
        typeof(BagOfPreparation),    // 准备背包
        typeof(BeltBuckle),          // 腰带扣
    };

    private static BossTurn Turn(string note, int turnIndex, params ScriptCard[] cards)
        => new()
        {
            Note = $"{turnIndex}. {note}",
            Cards = new List<ScriptCard>(cards),
            Energy = turnIndex >= 1 && turnIndex <= TurnEnergy.Length ? TurnEnergy[turnIndex - 1] : 0,
        };

    private static ScriptCard C<T>(bool upgraded = false, string from = "deck", int damage = 0)
        where T : MegaCrit.Sts2.Core.Models.CardModel
        => new() { Card = typeof(T), Upgraded = upgraded, From = from, DamageOverride = damage };
}
