using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Core;

namespace CharacterBosses.Powers;

/// <summary>
/// 「命运同担」落在**玩家身上**的那一层：**你的下个回合开始时，选择 1 张手牌丢弃，然后这层效果结束**。
///
/// ★ 2026-11-14（用户改口径）：
/// > 你把她给的 debuff「命运同担」改一下，图标换成「抑制」的，并且给它添加可叠加的等级，
/// > 1 级让玩家在下回合开始时弃 1 张牌，2 级就是弃 2 张。
/// 所以现在是**可叠加**的：层数 = **你的回合开始时要弃几张**，弃完把整层清掉；
/// 层数由 <see cref="SharedFatePower"/> 在她每回合第一次弃牌时给（普通模式 1 层 / 恐惧模式 2 层）。
///
/// 时机：原版 <c>AfterPlayerTurnStart</c> 是在**手牌抽完之后**派的（和「烘焙手套」用同一条钩子 ——
/// 反编译 `ToastyMittens.AfterPlayerTurnStart` → `CardSelectCmd.FromHand(...)` → `CardCmd.Exhaust`），
/// 所以这时手牌是满的，正好让玩家按层数挑牌丢。
/// </summary>
public sealed class SharedFateDiscardPower : PowerModel
{
    public override PowerType Type => PowerType.Debuff;

    /// <summary>★ v0.2.654：可叠加（层数 = 要弃几张）。</summary>
    public override PowerStackType StackType => PowerStackType.Counter;

    /// <summary>不按人数放大（层数由我们自己算，不能因为人多就成倍）。</summary>
    public override bool ShouldScaleInMultiplayer => false;

    /// <summary>你的回合开始（手牌已抽好）→ 按层数选牌丢掉，然后把整层清掉。</summary>
    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        try
        {
            if (player?.Creature == null || !ReferenceEquals(player.Creature, Owner)) return;

            var hand = player.PlayerCombatState?.Hand;
            int handCount = hand?.Cards?.Count ?? 0;
            int want;
            try { want = (int)Amount; } catch { want = 1; }
            if (want < 1) want = 1;

            if (handCount <= 0)
            {
                Logger.Log($"[CharBoss] 命运同担：玩家回合开始时手牌为空 → 不用弃牌，效果结束（{want} 层）");
                await PowerCmd.Remove(this);
                return;
            }

            int toDiscard = Math.Min(want, handCount);
            var prefs = new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt, toDiscard);
            var picked = await CardSelectCmd.FromHandForDiscard(
                choiceContext, player, prefs, null, this);

            int discarded = 0;
            if (picked != null)
            {
                foreach (var card in picked)
                {
                    if (card == null) continue;
                    if (discarded >= toDiscard) break;
                    await CardCmd.Discard(choiceContext, card);
                    discarded++;
                    Logger.Log($"[CharBoss] 命运同担（{want} 层）：玩家被迫弃掉 {SafeEntry(card)}");
                }
            }

            if (discarded == 0) Logger.Log("[CharBoss] 命运同担：玩家没有选中任何牌（可能都被保留/无法丢弃）");
            await PowerCmd.Remove(this);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 命运同担（玩家侧弃牌）失败: " + e.Message);
        }
    }

    private static string SafeEntry(CardModel card)
    {
        try { return card.Id.Entry; } catch { return "?"; }
    }
}
