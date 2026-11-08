using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Cards;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Monsters;

namespace CharacterBosses.Powers;

/// <summary>
/// 一层「打击战」的特殊效果「**痛击**」（Key `IRONCLAD_STRIKE`，用户 2026-11-07）。
///
/// 用户最终口径（2026-11-08 修订，**按牌堆实时统计**）：
/// > 「痛击」会计算你的**抽牌堆、手牌、弃牌堆、消耗堆**里名字中包含「打击」的牌的数量，
/// >   再加上她自己的 7 张（打击×4、预备打击、灰烬打击、完美打击）；
/// > 每 1 张 ⇒ 完美打击 +2（进阶 0～8）／+3（进阶 9～10）；
/// > 战斗内**印**出来的打击要算（+）、**消耗**掉的照样算（只是换了牌堆 ⇒ 不变）、
/// > **变化掉**（不再是打击牌、已不在任何牌堆）的要减掉。
///
/// ⇒ 所以这里**不维护任何「池子」**：每次要数字就直接按当前牌堆重数一遍，
///   天然满足「印了要加 / 消耗不变 / 变化要减」，也彻底去掉了「打出一张就永久少一份」的旧逻辑。
/// </summary>
public sealed class BossBashPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    /// <summary>她自己的名字含「打击」的牌张数（打击×4 + 预备打击 + 灰烬打击 + 完美打击 = 7）。</summary>
    internal const int OwnStrikeCards = 7;

    /// <summary>每张牌给多少伤害（进阶 9～10 = 3，其余 = 2）。</summary>
    internal static int PerCard()
    {
        try { return Core.BossLineups.Ascension() >= 9 ? 3 : 2; }
        catch { return 2; }
    }

    /// <summary>这张牌算不算「名字包含打击」（牌 id 里带 STRIKE，或本身带 Strike 标签）。</summary>
    internal static bool IsStrike(CardModel? card)
    {
        try
        {
            if (card == null) return false;
            string entry = card.Id.Entry ?? string.Empty;
            if (entry.Contains("STRIKE", StringComparison.OrdinalIgnoreCase)) return true;
            var tags = card.Tags;
            return tags != null && tags.Contains(CardTag.Strike);
        }
        catch { return false; }
    }

    /// <summary>**现在**一共有多少张：她自己 7 张 + 每位真人玩家四个牌堆里数到的。</summary>
    internal static int CountNow(CharBossMonster? boss)
    {
        int count = OwnStrikeCards;
        try
        {
            var combat = boss?.CombatState;
            if (combat == null) return count;
            foreach (var player in Core.BossLockOn.RealPlayers(combat))
            {
                var pcs = player?.PlayerCombatState;
                if (pcs == null) continue;
                CountCards(pcs.Hand?.Cards, ref count);
                CountCards(pcs.DrawPile?.Cards, ref count);
                CountCards(pcs.DiscardPile?.Cards, ref count);
                CountCards(pcs.ExhaustPile?.Cards, ref count);
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 痛击：统计牌堆失败（用已知部分）: " + e.Message);
        }
        return count;
    }

    private static void CountCards(IEnumerable<CardModel>? cards, ref int count)
    {
        if (cards == null) return;
        try
        {
            foreach (var card in cards)
            {
                if (IsStrike(card)) count++;
            }
        }
        catch { /* 忽略 */ }
    }

    /// <summary>
    /// 你在本场战斗里**打出过**多少张名字含打击的牌。
    ///
    /// ★ v0.2.483（用户 2026-11-08 澄清）：「我打出打击他完美打击伤害减少仍然生效，
    ///   只是**增加了**新的伤害增加／减少途径」——
    ///   即：牌堆实时统计（印牌加、消耗不变、变化减）**之外**，打出 1 张打击仍然额外少一份加成。
    ///   （打出的那张还在弃牌堆里被数到，所以净效果就是每打一张 −1 张的额度。）
    /// </summary>
    private int PlayedStrikes;

    /// <summary>真人玩家打出一张「名字含打击」的牌 ⇒ 计数 +1（她自己打的不算）。</summary>
    public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        try
        {
            var card = cardPlay?.Card;
            if (card == null || !IsStrike(card)) return Task.CompletedTask;
            var owner = card.Owner;
            if (owner != null && ReferenceEquals(owner, Owner?.Player)) return Task.CompletedTask;

            PlayedStrikes++;
            Logger.Log($"[CharBoss] 痛击：你打出 {card.Id.Entry} ⇒ 已打出打击 {PlayedStrikes} 张" +
                       $"（完美打击额度再少 {PerCard()}）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 痛击：记录打出打击失败: " + e.Message);
        }
        return Task.CompletedTask;
    }

    /// <summary>完美打击**现在**应有的总伤害 = 基础 6 + 每张加成 ×（牌堆统计张数 − 已打出的打击张数）。</summary>
    internal int TargetDamage(CharBossMonster? boss)
    {
        int cards = Math.Max(0, CountNow(boss) - Math.Max(0, PlayedStrikes));
        return 6 + PerCard() * cards;
    }
}