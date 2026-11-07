using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using CharacterBosses.Core;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// 记录「**真人玩家这一回合已经出过几张牌**」——只给「回合开始抽牌 vs 回合内抽牌」的判定用。
///
/// 背景（用户 2026-10-28，崩坠「亡命之徒」的**自弑之欲**）：
/// `Patches/SoulBoundDrawPatch` 原来只用 `fromHandDraw` 区分——
/// 原版「回合开始补手牌」是 `fromHandDraw: true`（跳过），其余都算「回合内额外抽牌」。
/// 但**回合开始时由 power 施放的抽牌**（小提琴 / 机器学习 / 崩坠的自弑之欲…）走的是
/// `fromHandDraw: false`，于是被错误地算进灵魂骨的「本回合抽牌条目」，
/// 连带把死亡行军的伤害预补抬上去（实测：`本回合抽牌条目=2`、`死亡行军预补 12`）。
///
/// 判据改成：`fromHandDraw == true` **或** 这回合玩家还**一张牌都没打** ⇒ 视为回合开始抽牌。
/// 这类 power 都在「玩家出牌之前」结算，而真正回合内的抽牌（杂技 / 后空翻…）必然发生在打出牌之后。
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardPlayed))]
internal static class PlayerCardPlayedTracker
{
    private static int _turn = int.MinValue;

    /// <summary>真人玩家**这一回合**已经打出的牌数（含宠物/召唤物打出的不算）。</summary>
    internal static int PlaysThisTurn { get; private set; }

    [HarmonyPrefix]
    public static void Prefix(ICombatState combatState, PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        try
        {
            // 用己方回合数做「换回合」判定（读不到就沿用上一次的计数，不影响主流程）
            int turn = 0;
            try
            {
                var anyPlayer = System.Linq.Enumerable.FirstOrDefault(combatState?.Players ?? System.Array.Empty<MegaCrit.Sts2.Core.Entities.Players.Player>());
                turn = anyPlayer?.PlayerCombatState?.TurnNumber ?? 0;
            }
            catch { turn = 0; }
            if (turn != _turn)
            {
                _turn = turn;
                PlaysThisTurn = 0;
            }

            var creature = cardPlay?.Card?.Owner?.Creature;
            if (creature == null) return;
            if (creature.Monster is CharBossMonster) return;      // 影子（Boss 自己）不算
            PlaysThisTurn++;
        }
        catch { /* 忽略：算不出来就退回旧口径 */ }
    }
}
