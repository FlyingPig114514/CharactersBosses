using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Monsters;

namespace CharacterBosses.Core;

/// <summary>
/// 二层「杂耍战」的「**惊逃**」（<c>StampedePower</c>）：**在她回合结束时**打出手牌里的攻击牌。
///
/// 原版实现（反编译 `StampedePower.AfterAutoPostPlayPhaseEntered`）：
/// <code>
/// if (player != Owner.Player) return;
/// var hand = PileType.Hand.GetPile(Owner.Player);
/// for (int i = 0; i &lt; Amount; i++)
/// {
///     var card = Rng.CombatTargets.NextItem(hand.Cards.Where(c =&gt; c.Type == CardType.Attack
///                                                             &amp;&amp; !c.Keywords.Contains(CardKeyword.Unplayable)));
///     if (card != null) await CardCmd.AutoPlay(choiceContext, card, null);
/// }
/// </code>
/// 两点和我们不一样：
///   1. 它只派发给 `CombatState.Players` 里的真人 —— **影子玩家收不到**；
///   2. 它是**随机**挑一张攻击牌，而设计稿要的是「**剧本点名的那一张**」
///      （用户 2026-11-02：「第二回合应该显示有 4 张打击，前 2 张费用打出，**第三张惊逃打出**，
///       第四张杂耍复制然后因为虚无消耗」）⇒ 随机挑的话可能挑到那张复制牌。
///
/// 所以这里自己实现：优先打**剧本标了 `ScriptCard.StampedePlay`** 的那张（按牌型在手里找），
/// 找不到才退回原版的行为（直接把 `IsThisCardMarked` 之外的攻击牌里挑一张，仍然**不打复制牌**，
/// 复制牌带虚无+消耗、本来就该自己消失）。
/// </summary>
internal static class StampedePlays
{
    /// <summary>她回合结束时调一次。</summary>
    internal static async Task RunAtTurnEnd(CharBossMonster boss, PlayerChoiceContext ctx)
    {
        try
        {
            var power = boss?.Creature?.GetPower<StampedePower>();
            if (power == null) return;

            var shadow = boss!.Shadow;
            var pcs = shadow?.PlayerCombatState;
            if (shadow == null || pcs == null) return;

            int stacks = Math.Max(1, power.Amount);
            var picks = PickCards(boss!, pcs, stacks);
            if (picks.Count == 0)
            {
                Logger.Log("[CharBoss] 惊逃：手里没有可打出的攻击牌 ⇒ 本回合无事发生");
                return;
            }

            Logger.Log($"[CharBoss] 惊逃：回合结束打出手牌里的攻击牌 {picks.Count} 张（层数 {stacks}）→ " +
                       $"[{string.Join(",", picks.ConvertAll(c => SafeEntry(c)))}]");

            foreach (var card in picks)
            {
                try
                {
                    if (boss!.Creature == null || boss.Creature.IsDead) break;
                    if (!pcs.Hand.Cards.Contains(card)) continue;
                    await CardCmd.AutoPlay(ctx, card, null, AutoPlayType.Default);
                    Logger.Log($"[CharBoss] 惊逃：已打出 {SafeEntry(card)}");
                }
                catch (Exception e)
                {
                    Logger.Log($"[CharBoss] 惊逃：打出 {SafeEntry(card)} 失败: " + e.Message);
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 惊逃结算失败: " + e.Message);
        }
    }

    /// <summary>
    /// 挑要打的攻击牌：① 剧本点名（`StampedePlay`）→ ② 其它攻击牌（**跳过杂耍的复制牌**，
    /// 那些带虚无+消耗，本来就该在回合结束自己消失）。
    /// </summary>
    private static List<CardModel> PickCards(CharBossMonster boss, MegaCrit.Sts2.Core.Entities.Players.PlayerCombatState pcs,
                                             int stacks)
    {
        var picks = new List<CardModel>();
        try
        {
            var scriptTurn = boss.Def?.ScriptForAct(boss.ActIndex)?.TurnAt(boss.TurnNumber);
            var markedTypes = new List<Type>();
            if (scriptTurn?.Cards != null)
            {
                foreach (var entry in scriptTurn.Cards)
                {
                    if (entry.StampedePlay && entry.Card != null) markedTypes.Add(entry.Card);
                }
            }

            var handAttacks = new List<CardModel>();
            foreach (var card in pcs.Hand.Cards)
            {
                if (card == null) continue;
                try { if (card.Type != CardType.Attack) continue; } catch { continue; }
                if (IsJugglingCopy(card)) continue;                 // 复制牌自己不参与
                handAttacks.Add(card);
            }

            // ① 剧本点名的先上
            foreach (var type in markedTypes)
            {
                if (picks.Count >= stacks) break;
                foreach (var card in handAttacks)
                {
                    if (card == null || picks.Contains(card)) continue;
                    if (card.GetType() != type) continue;
                    picks.Add(card);
                    break;
                }
            }

            // ② 还不够就按「手里顺序」补（确定性，不用随机数）
            if (picks.Count < stacks)
            {
                foreach (var card in handAttacks)
                {
                    if (picks.Count >= stacks) break;
                    if (card == null || picks.Contains(card)) continue;
                    picks.Add(card);
                }
            }

            // ③ 还没有 ⇒ 退而打**复制牌**（用户 2026-11-02 第三回合的用法：
            //   「杂耍复制踩踏，然后回合结束时被复制的踩踏自动打出然后消耗」——
            //   踩踏的结算**先于**虚无，所以它能在回合结束被惊逃打出去，然后因为「消耗」进消耗牌堆）。
            if (picks.Count < stacks)
            {
                foreach (var card in pcs.Hand.Cards)
                {
                    if (picks.Count >= stacks) break;
                    if (card == null || picks.Contains(card)) continue;
                    try { if (card.Type != CardType.Attack) continue; } catch { continue; }
                    if (!IsJugglingCopy(card)) continue;
                    picks.Add(card);
                }
            }
        }
        catch { /* 忽略：挑不出来就当没牌 */ }
        return picks;
    }

    /// <summary>这张牌是不是「杂耍的复制牌」（剧本用 `ScriptCard.JugglingCopy` 造出来、带虚无+消耗）。</summary>
    private static bool IsJugglingCopy(CardModel card)
    {
        try
        {
            if (card == null) return false;
            var keywords = card.Keywords;
            if (keywords == null) return false;
            // 复制牌的判据：**同时**带虚无与消耗，而且它不属于剧本里「本来就该打」的那几张
            //（彼岸咆哮之类天生带消耗，但它不是虚无）
            bool ethereal = keywords.Contains(CardKeyword.Ethereal);
            bool exhaust = keywords.Contains(CardKeyword.Exhaust);
            return ethereal && exhaust;
        }
        catch { return false; }
    }

    private static string SafeEntry(CardModel? card)
    {
        try { return card?.Id.Entry ?? "?"; } catch { return "?"; }
    }
}
