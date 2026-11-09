using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Monsters;

namespace CharacterBosses.Powers;

/// <summary>
/// 一层故障机器人 2 号「**电球机**」的特殊效果「**引雷针**」（用户 2026-11-05 设计）。
///
/// 用户原话：
/// > 单体特殊效果：你每在一回合内打出 3 张牌，就使故障机器人本回合失去 1 点集中。
/// > （补充确认：每打满 3 张就扣 1 点，**单回合没有上限**，每回合重置；
/// >   「本回合失去」= 回合结束自动还回来）
///
/// 实现要点：
///   · **只数被这只 Boss 锁定的那位玩家**打出的牌（<see cref="Core.BossLockOn.SpecialEffectAppliesTo"/>）；
///   · 计数按回合：玩家回合开始（`AfterSideTurnStart(Player)`）清零；
///   · 每满 3 张 ⇒ `FocusPower` −1，并把扣掉的总量记在 <c>_stolen</c>；
///   · 她自己的回合结束时（`AfterSideTurnEnd(Enemy)`）把 <c>_stolen</c> 一次性还回去 ⇒ 「本回合失去」。
///   · 计数会显示在这层 Power 的数字上（`Amount`），玩家能看到「还差几张」。
/// </summary>
public sealed class BossLightningRodPower : PowerModel
{
    // ★ 2026-11-05（用户：电球机「身上没有特殊效果引雷针」）：Debuff 会掉到血条下方的减益行、
    //   视觉上像是「没有这个特殊效果」。它是**她的特殊效果**，与其它特殊效果一致按 Buff 显示。
    public override PowerType Type => PowerType.Buff;

    /// <summary>计数型 ⇒ 图标右下角显示已打出的张数。</summary>
    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldPlayVfx => false;

    /// <summary>每打几张牌扣 1 点集中。</summary>
    private const int CardsPerFocus = 3;

    /// <summary>本回合已经扣掉多少点集中（她回合结束时还回去）。</summary>
    private int _stolen;

    private int _logs;

    /// <summary>玩家打出牌 ⇒ 计数 +1，每满 3 张扣 1 点集中。</summary>
    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        try
        {
            var owner = Owner;
            if (owner == null || !owner.IsAlive) return;

            var card = cardPlay?.Card;
            if (card == null) return;
            if (card.Owner?.Creature == owner) return;        // 她自己出的牌不算
            // 单体：只有被这只 Boss 锁定的那位玩家出的牌才算
            if (!Core.BossLockOn.SpecialEffectAppliesTo(owner.Monster as CharBossMonster, card.Owner)) return;

            int count = (int)Math.Round((double)Amount) + 1;
            await PowerCmd.Apply<BossLightningRodPower>(choiceContext, owner, 1m, owner, null, false);

            if (count % CardsPerFocus != 0) return;

            await PowerCmd.Apply<FocusPower>(choiceContext, owner, -1m, owner, null, false);
            _stolen++;
            if (_logs++ < 40)
            {
                Logger.Log($"[CharBoss] 引雷针：玩家本回合已打出 {count} 张牌 ⇒ 她本回合失去 1 点集中" +
                           $"（累计已扣 {_stolen}）");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 引雷针（玩家出牌计数）失败: " + e.Message);
        }
    }

    /// <summary>玩家回合开始 ⇒ 计数清零（每回合重新数）。</summary>
    public override async Task AfterSideTurnStart(MegaCrit.Sts2.Core.Combat.CombatSide side,
                                                  IReadOnlyList<Creature> participants,
                                                  MegaCrit.Sts2.Core.Combat.ICombatState combatState)
    {
        try
        {
            var owner = Owner;
            if (owner == null) return;
            if (side != MegaCrit.Sts2.Core.Combat.CombatSide.Player) return;
            if (!participants.Contains(owner) && Amount > 0)
            {
                // 玩家的回合开始：计数归零（用「减到 0」而不是直接改字段，保持与同步一致）
                await PowerCmd.Apply<BossLightningRodPower>(null, owner, -Amount, owner, null, false);
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 引雷针（回合重置计数）失败: " + e.Message);
        }
    }

    /// <summary>她自己的回合结束 ⇒ 把本回合扣掉的集中还回来。</summary>
    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext,
                                                 MegaCrit.Sts2.Core.Combat.CombatSide side,
                                                 IEnumerable<Creature> participants)
    {
        try
        {
            var owner = Owner;
            if (owner == null || _stolen <= 0) return;
            if (side != MegaCrit.Sts2.Core.Combat.CombatSide.Enemy) return;
            if (!participants.Contains(owner)) return;

            int give = _stolen;
            _stolen = 0;
            await PowerCmd.Apply<FocusPower>(choiceContext, owner, give, owner, null, false);
            Logger.Log($"[CharBoss] 引雷针：她回合结束 ⇒ 把本回合扣掉的 {give} 点集中还回去");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 引雷针（回合结束返还集中）失败: " + e.Message);
        }
    }
}
