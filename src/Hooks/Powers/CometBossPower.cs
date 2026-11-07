using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace CharacterBosses.Powers;

/// <summary>
/// 第一幕「碎屑储君」的特殊效果「**彗星**」（用户设计，2026-10-24 改版）：
///
///   · 每当储君生成**碎屑**（DEBRIS）时，反而把它加入**你的抽牌堆** ——
///     见 <c>Patches/CometDebrisPatch.cs</c>；
///   · 每当你打出碎屑时：**所有玩家获得 10 点格挡** + 清掉所有玩家的虚弱/易伤 +
///     **所有敌方各失去 10 点生命**（用户 2026-10-24：原来是"清负面 + 敌方各掉 15"，
///     现在多给格挡、掉血 15 → 10）。
///
/// 多人备注：用户说明这是「专属特殊效果，只对被他跟踪锁定的玩家生效」。
/// 单人时被锁定的玩家就是唯一的玩家；多人按"除她以外的玩家"判定。
/// </summary>
public sealed class CometBossPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    /// <summary>打出碎屑 → 所有玩家 +10 格挡、清虚弱/易伤，所有敌方各失去 10 点生命。</summary>
    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        try
        {
            if (Owner == null || cardPlay?.Card == null) return;
            if (!IsDebris(cardPlay.Card)) return;

            var actor = CharacterBosses.Core.GameCompat.ActorOf(cardPlay);
            if (actor == null) return;
            if (ReferenceEquals(actor.Creature, Owner)) return;      // 她自己打的不算

            // ⚠ 用户 2026-10-29：「彗星」是**单体（对应型）特殊效果** ——
            //   只有「被这只储君锁定」的那位玩家打出碎屑才算（单人时恒成立，行为不变）。
            //   以前漏了这个判据：谁打碎屑都触发，多人时就变成全局效果了。
            if (!Core.BossLockOn.SpecialEffectAppliesTo(Owner.Monster as Monsters.CharBossMonster, actor)) return;

            var combat = Owner.CombatState;
            var players = combat?.Players;

            // ① 所有玩家获得 10 点格挡（用户 2026-10-24 新增）
            try
            {
                if (players != null)
                {
                    foreach (var p in players)
                    {
                        var creature = p?.Creature;
                        if (creature == null || !creature.IsAlive) continue;
                        if (creature.Monster != null) continue;      // 影子 Player / 怪物跳过
                        await CreatureCmd.GainBlock(creature, CometBlockGain,
                            MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered, null);
                    }
                    Logger.Log($"[CharBoss] 彗星：玩家打出碎屑 → 所有玩家 +{CometBlockGain} 格挡");
                }
            }
            catch (Exception e)
            {
                Logger.Log("[CharBoss] 彗星（给格挡）失败: " + e.Message);
            }

            // ② 清掉所有玩家的虚弱 / 易伤
            int cleared = 0;
            if (players != null)
            {
                foreach (var p in players)
                {
                    var creature = p?.Creature;
                    if (creature == null || !creature.IsAlive) continue;
                    int weak = PowerAmount<WeakPower>(creature);
                    int vulnerable = PowerAmount<VulnerablePower>(creature);
                    if (weak > 0)
                        await PowerCmd.Apply<WeakPower>(new BlockingPlayerChoiceContext(), creature, -weak, Owner, null, false);
                    if (vulnerable > 0)
                        await PowerCmd.Apply<VulnerablePower>(new BlockingPlayerChoiceContext(), creature, -vulnerable, Owner, null, false);
                    if (weak > 0 || vulnerable > 0) cleared++;
                }
            }

            if (cleared > 0)
            {
                Flash();
                Logger.Log($"[CharBoss] 彗星：玩家打出碎屑 → 清掉 {cleared} 名玩家的虚弱/易伤");
            }

            // ③ 所有敌方（所有 Boss）各失去 10 点生命：真正的"失去生命"，不吃格挡/力量/易伤。
            try
            {
                var enemies = combat?.Enemies;
                if (enemies != null)
                {
                    foreach (var enemy in enemies)
                    {
                        if (enemy == null || !enemy.IsAlive) continue;
                        enemy.LoseHpInternal(CometEnemyHpLoss, MegaCrit.Sts2.Core.ValueProps.ValueProp.Unblockable
                                                          | MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered);
                        Logger.Log($"[CharBoss] 彗星：玩家打出碎屑 → {enemy.Name} 失去 {CometEnemyHpLoss} 点生命" +
                                   $"（剩余 {enemy.CurrentHp}）");
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Log("[CharBoss] 彗星（掉血）失败: " + e.Message);
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 彗星（清负面）失败: " + e.Message);
        }
    }

    /// <summary>玩家打出碎屑时，所有玩家获得的格挡。</summary>
    internal const decimal CometBlockGain = 10m;

    /// <summary>玩家打出碎屑时，每只敌方失去的生命（用户 2026-10-24：15 → 10）。</summary>
    internal const decimal CometEnemyHpLoss = 10m;

    /// <summary>这张牌是不是「碎屑」。</summary>
    internal static bool IsDebris(CardModel? card)
    {
        try { return card != null && card.Id.Entry == "DEBRIS"; }
        catch { return false; }
    }

    private static int PowerAmount<TPower>(MegaCrit.Sts2.Core.Entities.Creatures.Creature? creature)
        where TPower : PowerModel
    {
        try { return creature?.GetPowerAmount<TPower>() ?? 0; }
        catch { return 0; }
    }
}
