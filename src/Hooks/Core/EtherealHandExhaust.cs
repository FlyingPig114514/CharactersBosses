using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Monsters;

namespace CharacterBosses.Core;

/// <summary>
/// **她手牌里的「虚无」牌在回合结束自行消耗**（二层「杂耍战」用户 2026-11-02）。
///
/// 用户口径：
/// > 第二回合被复制的打击因为没有被打出和虚无应该自行消耗，并因为无惧疼痛给战士起甲
/// > （起甲时间为他回合结束，所以不会影响奥利哈刚的触发）。
///
/// 两件事原版都不会替她做：
///   1. 原版「回合结束把手牌里的虚无牌消耗掉」是跑在**真人玩家**的回合收尾流程里的，
///      我们的影子玩家不在 `CombatState.Players` 里 ⇒ 从来不会消耗；
///   2. 「无惧疼痛」（`FeelNoPainPower.AfterCardExhausted`）是**她身上的一层 power**，
///      而 power 的钩子扇出同样到不了影子玩家（见 `SneakyOnBossAttack` 的注释）⇒ 消耗了也不给格挡。
///
/// 所以这里在她回合结束、把牌丢进弃牌堆**之前**：
///   · 把所有带「虚无」关键字的手牌挪进**消耗牌堆**；
///   · 每消耗一张，直接调她身上「无惧疼痛」自己的实现（`AfterCardExhausted`）给格挡。
///
/// ⚠ 时机：放在「惊逃」之后。奥利哈钢（`Orichalcum`）看的是**她回合开始**（`BeforeSideTurnStart`）
/// 那一刻有没有格挡，与这里的回合结束无关 ⇒ 不会被这笔格挡影响触发（用户确认过）。
/// </summary>
internal static class EtherealHandExhaust
{
    internal static async Task RunAtTurnEnd(CharBossMonster boss, PlayerChoiceContext ctx)
    {
        try
        {
            var shadow = boss?.Shadow;
            var pcs = shadow?.PlayerCombatState;
            if (pcs == null) return;

            var doomed = new List<CardModel>();
            foreach (var card in pcs.Hand.Cards)
            {
                if (card == null) continue;
                bool ethereal;
                try { ethereal = card.Keywords != null && card.Keywords.Contains(CardKeyword.Ethereal); }
                catch { ethereal = false; }
                if (ethereal) doomed.Add(card);
            }
            if (doomed.Count == 0) return;

            var names = new List<string>();
            foreach (var card in doomed)
            {
                try { names.Add(SafeEntry(card)); } catch { names.Add("?"); }

                // 先挪牌（数据层，不出动画），再触发「无惧疼痛」
                try
                {
                    pcs.Hand.RemoveInternal(card, true);
                    pcs.ExhaustPile.AddInternal(card, -1, true);
                }
                catch (Exception e)
                {
                    Logger.Log($"[CharBoss] 虚无消耗：{SafeEntry(card)} 挪进消耗堆失败: " + e.Message);
                    continue;
                }

                // 「无惧疼痛」：每消耗一张 +Amount 格挡（她身上的 power，钩子扇出到不了 ⇒ 手动调）
                try
                {
                    var power = boss!.Creature?.GetPower<FeelNoPainPower>();
                    if (power != null) await power.AfterCardExhausted(ctx, card, false);
                }
                catch (Exception e)
                {
                    Logger.Log("[CharBoss] 无惧疼痛结算失败: " + e.Message);
                }
            }

            int block = -1;
            try { block = (int)Math.Round((double)(boss!.Creature?.Block ?? 0m)); } catch { /* 忽略 */ }
            Logger.Log($"[CharBoss] 回合结束：{doomed.Count} 张「虚无」牌自行消耗并进消耗堆 " +
                       $"[{string.Join(",", names)}]（无惧疼痛已结算，现有格挡 {block}）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 虚无消耗流程失败: " + e.Message);
        }
    }

    private static string SafeEntry(CardModel? card)
    {
        try { return card?.Id.Entry ?? "?"; } catch { return "?"; }
    }
}
