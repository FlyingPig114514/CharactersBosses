using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// 三层「**事件机**」（`DEFECT_EVENT`）的特殊效果「一心化万」的第二半：
/// **每当她有一张牌从弃牌堆回到手牌，所有单位就获得 1 点力量**。
///
/// 用户 2026-11-03 实测报的 bug：「全息影像的捞牌没有触发它的特殊效果『一心化万』」。
/// 反编译：全息影像这类效果走的是 <c>CardPileCmd.Add(card, PileType.Hand, …)</c>
/// —— 和万物一心同一个入口。所以这里在**这个入口**上判定：
///   · 前缀：这张牌此刻在不在**弃牌堆**里（是 ⇒ 记下来）；
///   · 后置：它确实进了**手牌**、牌主是我们的影子 Boss ⇒ **记账 +1**（记在 Boss 身上）。
///
/// ⚠ v0.2.346（用户 2026-11-03：「和事件机的战斗我怎么没法结束回合？」）：
///   以前后置里**直接起一个异步任务去 `PowerCmd.Apply`** —— 那是在她的出牌队列**之外**开的新链，
///   而 `PowerCmd` 会往战斗动作队列里塞动作，于是可能和「正在 await 的那次出牌」互相等，
///   表现为**她的回合永远不结束**（玩家那边就是「结束回合按了没反应」）。
///   现在补丁只**记账**（`CharBossMonster.PendingReturnStrength++`），
///   真正加力量在 `CharBossMonster.PlayOne` 里、和她这一手出牌同一个 await 链里结算。
///
/// 只对我们自己的 Boss 生效：真人玩家、其它怪物、别的 mod 都不受影响。
/// 「万物一心」那张自己的返手**已被拦掉**（`Patches/BossAllForOneFilterPatch`），
/// 它的力量由 `Core/EventMachineMechanics.AllForOneReturnsAsync` 按剧本点名的张数给，不走这条路。
/// </summary>
[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.Add),
              new[] { typeof(CardModel), typeof(PileType), typeof(CardPilePosition),
                      typeof(AbstractModel), typeof(bool) })]
public static class BossDiscardToHandStrengthPatch
{
    /// <summary>正在搬运的牌的「来源是不是弃牌堆」（按实例记，前缀写、后置读，读完就删）。</summary>
    private static readonly HashSet<CardModel> FromDiscard =
        new(ReferenceEqualityComparer.Instance);

    [HarmonyPrefix]
    public static void Prefix(CardModel card, PileType newPileType)
    {
        try
        {
            if (card == null || newPileType != PileType.Hand) return;
            if (!IsOurBossCard(card)) return;
            bool fromDiscard;
            try { fromDiscard = card.Pile != null && card.Pile.Type == PileType.Discard; }
            catch { fromDiscard = false; }
            if (!fromDiscard) return;
            lock (FromDiscard) { FromDiscard.Add(card); }
        }
        catch { /* 一律忽略：绝不影响原版搬牌 */ }
    }

    [HarmonyPostfix]
    public static void Postfix(CardModel card, PileType newPileType)
    {
        try
        {
            if (card == null) return;
            bool marked;
            lock (FromDiscard) { marked = FromDiscard.Remove(card); }
            if (!marked) return;
            if (newPileType != PileType.Hand) return;

            var boss = card.Owner?.Creature?.Monster as CharBossMonster;
            if (boss == null) return;

            boss.PendingReturnStrength++;      // 只记账，真正的加力量在她出牌流程里结算
            string entry;
            try { entry = card.Id.Entry; } catch { entry = "?"; }
            Logger.Log($"[CharBoss] 一心化万：{entry} 从弃牌堆回到她的手牌 ⇒ 待结算 +1 力量" +
                       $"（累计待结算 {boss.PendingReturnStrength}）");
        }
        catch { /* 一律忽略 */ }
    }

    /// <summary>这张牌是不是我们影子 Boss 的牌。</summary>
    private static bool IsOurBossCard(CardModel card)
    {
        try { return card.Owner?.Creature?.Monster is CharBossMonster; }
        catch { return false; }
    }
}