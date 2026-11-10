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

    /// <summary>
    /// **本回合玩家已经打出几张牌**。
    ///
    /// ★ 2026-11-10（用户：「我打三张牌它集中不掉」）：以前这个计数直接存在 `Amount` 上，
    /// 玩家回合开始时用 `Apply(-Amount)` 把它减到 0 —— 而反编译 `PowerCmd.ModifyAmount` 末尾是
    /// <code>
    /// power.SetAmount(newAmount, silent);
    /// if (power.ShouldRemoveDueToAmount()) await Remove(power);   // ← 数量到 0 ⇒ **整条 power 被移除**
    /// </code>
    /// ⇒ 每回合刚开始这层特殊效果就被删掉了，之后玩家再怎么出牌都没人计数，集中当然不掉。
    /// （`BorrowedTimeBossPower` 早就踩过同一个坑，所以它把图标数字钳在 1..10。）
    /// 现在计数存在**字段**里，`Amount` 只负责显示「还差几张触发」，永远 ≥ 1。
    /// </summary>
    private int _playedThisTurn;

    /// <summary>日志限流用。</summary>
    private int _logs;

    /// <summary>图标数字 = **还差几张牌**触发（3 → 1，永远不为 0）。</summary>
    private void SyncAmount()
    {
        try
        {
            int remain = CardsPerFocus - (_playedThisTurn % CardsPerFocus);   // 3,2,1
            SetAmount(Math.Clamp(remain, 1, CardsPerFocus), true);
        }
        catch { /* 忽略 */ }
    }

    /// <summary>刚挂上时把图标数字刷成 3（还差 3 张），并清空计数。</summary>
    public override Task AfterApplied(Creature applier, MegaCrit.Sts2.Core.Models.CardModel cardSource)
    {
        _playedThisTurn = 0;
        SyncAmount();
        return Task.CompletedTask;
    }

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

            _playedThisTurn++;
            int count = _playedThisTurn;
            SyncAmount();

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

    /// <summary>玩家回合开始 ⇒ 计数清零（每回合重新数）。
    /// ⚠ 只清**字段**，不再 `Apply(-Amount)` —— 那会把 `Amount` 减到 0，而原版
    /// `PowerCmd.ModifyAmount` 一旦发现 `ShouldRemoveDueToAmount()` 就会把整条 power 移除
    /// （这就是「集中不掉」的根因，见 <see cref="_playedThisTurn"/> 的注释）。</summary>
    public override Task AfterSideTurnStart(MegaCrit.Sts2.Core.Combat.CombatSide side,
                                            IReadOnlyList<Creature> participants,
                                            MegaCrit.Sts2.Core.Combat.ICombatState combatState)
    {
        try
        {
            var owner = Owner;
            if (owner == null) return Task.CompletedTask;
            if (side != MegaCrit.Sts2.Core.Combat.CombatSide.Player) return Task.CompletedTask;
            if (participants.Contains(owner)) return Task.CompletedTask;   // 她自己的回合，不管
            if (_playedThisTurn == 0) return Task.CompletedTask;

            _playedThisTurn = 0;
            SyncAmount();
            Logger.Log("[CharBoss] 引雷针：玩家回合开始 ⇒ 计数清零（图标回到 3）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 引雷针（回合重置计数）失败: " + e.Message);
        }
        return Task.CompletedTask;
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
