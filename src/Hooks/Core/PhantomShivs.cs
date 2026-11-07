using System;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Monsters;

namespace CharacterBosses.Core;

/// <summary>
/// 幻影之刃（<c>PhantomBlades</c>）的**意图补正** —— 用户 2026-10-24 削刀猎：
/// 「打出幻影之刃后伤害预览要给小刀加上幻影之刃的增伤（如果原版已经有了就不要重复加了）」。
///
/// 反编译依据（`MegaCrit.Sts2.Core.Models.Powers.PhantomBladesPower`）：
/// <code>
/// public override decimal ModifyDamageAdditive(target, amount, props, dealer, cardSource, cardPlay)
/// {
///     if (!props.IsPoweredAttack()) return 0;
///     if (cardSource == null || !cardSource.Tags.Contains(CardTag.Shiv)) return 0;   // ← ①
///     if (dealer != Owner) return 0;
///     int played = History.CardPlaysFinished.Count(e =&gt; e.HappenedThisTurn(CombatState)
///                     &amp;&amp; e.CardPlay.Card.Tags.Contains(Shiv) &amp;&amp; e.CardPlay.Player == Owner.Player);
///     if (played &gt; 0) return 0;                                                     // ← ②
///     return Amount;                                                                // = 9（升级 12）
/// }
/// </code>
/// 于是：
///   · **①** 意图节点（反编译 `AttackIntent.GetSingleDamage`）传的 <c>cardSource</c> 是 <c>null</c>
///     ⇒ 原版**不会**替意图加这 9 点 —— 我们自己补不会重复；
///   · **②** 只有「本回合第一张小刀」吃 —— 所以补正只能落在每回合的**第一把刀**上；
///   · 牌面 <c>PowerVar&lt;PhantomBladesPower&gt;(9)</c>，升级 +3；层数是 Counter（重复打出会叠加）。
/// </summary>
public static class PhantomShivs
{
    /// <summary>幻影之刃的卡牌 entry。</summary>
    public const string CardEntry = "PHANTOM_BLADES";

    /// <summary>牌面上那条 <c>PowerVar&lt;PhantomBladesPower&gt;</c> 的键名（= 类型名去 `Power`）。</summary>
    public const string VarKey = "PhantomBladesPower";

    /// <summary>这张牌是不是幻影之刃。</summary>
    public static bool IsCard(CardModel? card)
    {
        try { return card != null && card.Id.Entry == CardEntry; }
        catch { return false; }
    }

    /// <summary>她身上幻影之刃现在几层（没有 = 0）。</summary>
    public static int AmountOf(CharBossMonster? boss)
    {
        try
        {
            return Math.Max(0,
                boss?.Creature?
                    .GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.PhantomBladesPower>() ?? 0);
        }
        catch { return 0; }
    }

    /// <summary>
    /// 这回合的「第一把小刀」还在不在 —— 也就是原版 ② 号判据：
    /// 本回合她**还没**打出过任何一张带 <c>CardTag.Shiv</c> 的牌。
    ///
    /// 为什么用历史条目而不是自己数：她的小刀是 `CardCmd.AutoPlay` 打出去的，
    /// 原版同样会记一条 <c>CardPlayFinishedEntry</c>，读历史就是读原版同一把尺子
    /// （和 <c>PhantomBladesPower</c> 自己用的判据完全一致）。读不到（历史为空/异常）按「还没打过」处理。
    /// </summary>
    public static bool Available(CharBossMonster? boss)
    {
        try
        {
            var combat = boss?.CombatState;
            var player = boss?.Shadow;
            var history = MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.History;
            if (combat == null || player == null || history == null) return true;

            foreach (var entry in history.CardPlaysFinished)
            {
                try
                {
                    if (!entry.HappenedThisTurn(combat)) continue;
                    var play = entry.CardPlay;
                    if (CharacterBosses.Core.GameCompat.ActorOf(play) is not { } playActor || !ReferenceEquals(playActor, player)) continue;
                    var tags = play.Card?.Tags;
                    if (tags != null && tags.Contains(CardTag.Shiv)) return false;
                }
                catch { /* 单条读不出来就跳过 */ }
            }
        }
        catch { /* 忽略 */ }
        return true;
    }
}
