using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Core;

namespace CharacterBosses.Powers;

/// <summary>
/// 一层「奇巧猎」的特殊效果「**命运同担**」—— 挂在 **Boss 身上**（状态栏显示），
/// 描述里的「你」指玩家：**静默猎手只要在一回合内丢弃过牌，被她锁定的那位玩家下个回合开始时
/// 就要选择 1 张手牌丢弃**。
///
/// 实现（用户 2026-10-26 给的写法）：
///   · 她**每回合第一次**弃牌时，给玩家挂一层 <see cref="SharedFateDiscardPower"/>；一回合最多一次；
///   · **只挂给「被她锁定的那一位」**（用户 2026-10-26 订正：多人时不要全给；
///     单人时锁定判据恒成立，所以单人行为不变）；
///   · 那层 debuff 在玩家**下个回合开始时**让他弃 1 张手牌，然后自己消失；
///   · 「一回合第一次」以**她自己的回合**为界重置。
///
/// 为什么她的弃牌能被这里看到：原版 <c>Hook.AfterCardDiscarded</c> 是广播给
/// <c>CombatState.IterateHookListeners()</c> 的，会把场上每个生物的 <c>Powers</c> 都算进去
/// （我们的 Boss 就在 <c>_enemies</c> 里），所以挂在她身上的 Power 天然收得到
/// （和「袖里乾坤」看玩家出牌是同一条路，见 <see cref="SleeveTrickPower"/>）。
/// </summary>
public sealed class SharedFatePower : PowerModel
{
    /// <summary>她这一回合是否已经触发过（她自己的回合开始时重置）。</summary>
    private bool _firedThisTurn;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    /// <summary>她自己的回合开始 → 重置「本回合第一次」。</summary>
    public override Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side,
                                            IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        if (side == CombatSide.Enemy) _firedThisTurn = false;
        return Task.CompletedTask;
    }

    /// <summary>由 Boss 自己的回合开始显式调一次（不依赖 `BeforeSideTurnStart` 的派发顺序）。</summary>
    internal void ResetForBossTurn() => _firedThisTurn = false;

    /// <summary>她每丢弃一张牌都会走这里：本回合第一次 → 给玩家挂上「下回合弃 1 张」。</summary>
    public override async Task AfterCardDiscarded(PlayerChoiceContext choiceContext, CardModel card)
    {
        try
        {
            if (_firedThisTurn) return;
            if (card == null) return;

            var shadow = Owner?.Player;              // 静默猎手（影子 Player）
            if (shadow == null) return;
            if (!ReferenceEquals(card.Owner, shadow)) return;   // 只算**她自己**的弃牌

            var combat = Owner?.CombatState ?? card.CombatState;
            if (combat == null) return;

            _firedThisTurn = true;

            // 只给**被她锁定的那位玩家**（用户 2026-10-26 订正；单人时恒成立）。
            // `BossLockOn.SpecialEffectAppliesTo` 就是「对应型特殊效果」的统一判据：
            //   单人 → 恒 true；多人 → 只有 `TargetFor(boss)` 那位。
            var boss = Owner?.Monster as Monsters.CharBossMonster;
            int count = 0;
            foreach (var player in BossLockOn.RealPlayers(combat))
            {
                if (player?.Creature == null || !player.Creature.IsAlive) continue;
                if (player.Creature.Monster != null) continue;       // 只给真人玩家，不给召唤物
                if (!BossLockOn.SpecialEffectAppliesTo(boss, player)) continue;
                int discardAmount = Core.DreadMode.IsActive ? 2 : 1;   // 2026-11-13 dread
                await PowerCmd.Apply<SharedFateDiscardPower>(
                    choiceContext, player.Creature, discardAmount, Owner, null);
                count++;
            }

            Logger.Log($"[CharBoss] 命运同担：她本回合第一次弃牌（{SafeEntry(card)}）→ " +
                       $"{count} 名玩家下回合开始时各弃 1 张手牌" +
                       (count == 1 ? "（多人时只有被她锁定的那位）" : string.Empty));
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 命运同担触发失败: " + e.Message);
        }
    }

    private static string SafeEntry(CardModel card)
    {
        try { return card.Id.Entry; } catch { return "?"; }
    }
}
