using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Core;
using CharacterBosses.Monsters;

namespace CharacterBosses.Powers;

/// <summary>
/// 一层「黑球机」的「**眼部攻击**」buff（用户 2026-10-26 设计）：
/// **本回合她将要打出「眼部攻击」时**，她自己获得这层 buff（状态栏可见、可悬浮）。
///
/// 效果：**如果有玩家对她打出攻击牌（含波及她的群攻牌）**，她那记「眼部攻击」就会触发
/// 「如果敌人的意图是攻击」的特效 —— 也就是**给予 1 层虚弱**。
///
/// 为什么要专门做（原版判不到）：`GoForTheEyes.OnPlay` 里那句是
/// <code>
/// if (cardPlay.Target.Monster.IntendsToAttack) PowerCmd.Apply&lt;WeakPower&gt;(...)
/// </code>
/// 而她的攻击目标是**玩家**（`Target.Monster` 为 null）⇒ 原版永远不触发。
/// 所以这里自己判：记录「本回合有真人玩家打过攻击牌」，等她打出眼部攻击时补上 1 层虚弱。
///
/// 时序：计划是在**玩家回合**算好的，玩家打牌发生在她出手之前 ⇒ 正好能记到。
/// 她回合结束时这层 buff 自动移除（一回合的东西）。
/// </summary>
public sealed class GoForTheEyesAimPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    /// <summary>本回合是否有真人玩家**用攻击牌真的打到了她**。</summary>
    private bool _playerAttacked;

    private bool _logged;

    /// <summary>本回合是否已有真人玩家用攻击牌**打到她**（给 `CharBossMonster.PlayOne` 结算虚弱用）。</summary>
    internal bool PlayerAttackHit => _playerAttacked;

    /// <summary>
    /// ① 判定「有玩家用攻击牌**打到她**」。
    ///
    /// 用户 2026-10-27 订正：「如果有玩家用攻击牌打了**别的 Boss** 而没有打到它，不应该触发眼部攻击的判定」。
    /// 所以判据不能是「打出了攻击牌」，而要看**伤害真的落到她身上**：
    /// `AfterDamageReceived` 的 `target` 就是挨打的那个生物 —— 单体打她、群攻波及她都会走这里，
    /// 打别的 Boss 时 target 不是她 ⇒ 不置位。（被格挡全挡住也算「打到」，`DamageResult` 照常产生。）
    /// </summary>
    public override Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target,
                                             MegaCrit.Sts2.Core.Entities.Creatures.DamageResult result,
                                             MegaCrit.Sts2.Core.ValueProps.ValueProp props,
                                             Creature? dealer, CardModel? cardSource)
    {
        try
        {
            if (_playerAttacked) return Task.CompletedTask;
            if (target == null || !ReferenceEquals(target, Owner)) return Task.CompletedTask;   // 打别人：不算
            if (cardSource == null) return Task.CompletedTask;                                  // 不是牌打的

            MegaCrit.Sts2.Core.Entities.Cards.CardType type;
            try { type = cardSource.Type; } catch { return Task.CompletedTask; }
            if (type != MegaCrit.Sts2.Core.Entities.Cards.CardType.Attack) return Task.CompletedTask;

            // 出牌的那一方必须是**真人玩家**（不能是她自己，也不能是玩家的召唤物）
            var cardOwner = cardSource.Owner;
            var shadow = Owner?.Player;
            if (cardOwner == null || shadow == null || ReferenceEquals(cardOwner, shadow)) return Task.CompletedTask;

            _playerAttacked = true;
            if (!_logged)
            {
                _logged = true;
                Logger.Log($"[CharBoss] 眼部攻击 buff：玩家用 {SafeEntry(cardSource)} **打到了她** → " +
                           "她这记「眼部攻击」会额外给予 1 层虚弱");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 眼部攻击 buff 判定失败: " + e.Message);
        }
        return Task.CompletedTask;
    }

    // ⚠ **不要在这里 await `PowerCmd.Apply`**（用户 2026-10-27 实测：她的「眼部攻击」打出后**停在半空**）。
    // `AfterCardPlayed` 是卡牌结算链里的一条钩子，这时牌的飞行/结算动画还没结束；
    // 在钩子里 await 一个会播动画的指令（给玩家上虚弱）会让双方的完成信号互相等 ⇒ 牌永远悬着。
    // 所以这里**只做记录**，虚弱由 `CharBossMonster.PlayOne` 在牌**结算完之后**补（那里已经在 await 之外）。
    /// <summary>她回合结束 → 这层 buff 到期（一回合的东西）。</summary>
    public override Task AfterSideTurnEnd(PlayerChoiceContext choiceContext,
                                          MegaCrit.Sts2.Core.Combat.CombatSide side,
                                          System.Collections.Generic.IEnumerable<Creature> participants)
    {
        try
        {
            if (side != MegaCrit.Sts2.Core.Combat.CombatSide.Enemy) return Task.CompletedTask;
            _ = PowerCmd.Remove(this);
        }
        catch { /* 忽略 */ }
        return Task.CompletedTask;
    }

    private static string SafeEntry(CardModel card)
    {
        try { return card.Id.Entry; } catch { return "?"; }
    }
}
