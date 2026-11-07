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
/// 由 <see cref="SharedFatePower"/> 在她每回合第一次弃牌时挂上；一回合最多挂一次，不叠加。
///
/// 时机：原版 <c>AfterPlayerTurnStart</c> 是在**手牌抽完之后**派的（和「烘焙手套」用同一条钩子 ——
/// 反编译 `ToastyMittens.AfterPlayerTurnStart` → `CardSelectCmd.FromHand(...)` → `CardCmd.Exhaust`），
/// 所以这时手牌是满的，正好让玩家挑一张丢。
/// </summary>
public sealed class SharedFateDiscardPower : PowerModel
{
    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Single;

    /// <summary>不按人数放大（原版会给敌人施加的 power 层数乘人数；这层只是一个"要不要弃牌"的标记）。</summary>
    public override bool ShouldScaleInMultiplayer => false;

    /// <summary>层数没有数值意义，图标上不显示数字。</summary>
    public override int DisplayAmount => 0;

    /// <summary>你的回合开始（手牌已抽好）→ 选 1 张丢掉，然后移除自己。</summary>
    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        try
        {
            if (player?.Creature == null || !ReferenceEquals(player.Creature, Owner)) return;

            var hand = player.PlayerCombatState?.Hand;
            int handCount = hand?.Cards?.Count ?? 0;
            if (handCount <= 0)
            {
                Logger.Log("[CharBoss] 命运同担：玩家回合开始时手牌为空 → 不用弃牌，效果结束");
                await PowerCmd.Remove(this);
                return;
            }

            var prefs = new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt, 1);
            var picked = await CardSelectCmd.FromHandForDiscard(
                choiceContext, player, prefs, null, this);

            int discarded = 0;
            if (picked != null)
            {
                foreach (var card in picked)
                {
                    if (card == null) continue;
                    await CardCmd.Discard(choiceContext, card);
                    discarded++;
                    Logger.Log($"[CharBoss] 命运同担：玩家被迫弃掉 {SafeEntry(card)}");
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
