using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using CharacterBosses.Core;

namespace CharacterBosses.Powers;

/// <summary>
/// 一层亡灵契约师 2 号「**血肉骨**」的特殊效果「**吞噬生命**」（用户 2026-11-10 设计）。
///
/// 用户原话：
/// > 特殊效果：吞噬生命：每当亡灵契约师给予负面效果时，她获得4点格挡。
///
/// 实现依据（反编译 `SleightOfFleshPower`，血肉戏法用的就是同一个钩子）：
/// <code>
/// public override Task AfterPowerAmountChanged(PlayerChoiceContext ctx, PowerModel power,
///                                              decimal amount, Creature? applier, CardModel? cardSource)
/// // 血肉戏法原文：
/// //   if (amount != 0 &amp;&amp; power.GetTypeForAmount(amount) == PowerType.Debuff
/// //       &amp;&amp; power.Owner.IsEnemy &amp;&amp; applier == Owner &amp;&amp; !(power is ITemporaryPower))
/// //       CreatureCmd.Damage(..., power.Owner, Amount, Unpowered, Owner)
/// </code>
/// 我们照同一把尺子，但把「目标是不是敌人」换成「目标是不是**真人玩家**」——
/// `Creature.IsEnemy` 是**绝对阵营**（`Side == CombatSide.Enemy`），而她的负面落在 Side=Player 的
/// 玩家身上，直接用原版那条件对她是永远 false（<c>SleightOfFleshPower</c> 因此需要单独接管，
/// 见 <c>Patches.SleightOfFleshBossPatch</c>）。
/// </summary>
public sealed class BossDevourLifePower : PowerModel
{
    // 特殊效果按 Buff 显示（和引雷针一样：挂在她身上的「设计效果」，不是掉到减益行的 debuff）
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    private int _logs;

    /// <summary>每次她给目标上负面 ⇒ 自己获得 4 点格挡。</summary>
    public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power,
                                                      decimal amount, Creature? applier, CardModel? cardSource)
    {
        try
        {
            var owner = Owner;
            if (owner == null || !owner.IsAlive) return;
            if (power == null || amount <= 0m) return;                  // 只在下「正向层数」的负面时触发

            // 是不是「负面」：原版用 `GetTypeForAmount` 判（负数力量那种也认）
            PowerType kind;
            try { kind = power.GetTypeForAmount(amount); }
            catch { return; }
            if (kind != PowerType.Debuff) return;

            // 「临时降集中」那类（ITemporaryPower）不算 —— 与血肉戏法同一口径
            if (power is ITemporaryPower) return;

            // 目标必须是**真人玩家**（从她视角的敌人）；别的 Boss 的影子玩家不算
            var target = power.Owner;
            if (target == null || target.Monster != null) return;

            // 必须是她给的（applier 优先用 power 自己记的那份，拿不到再用钩子参数）
            var giver = power.Applier ?? applier;
            if (!ReferenceEquals(giver, owner)) return;

            await CreatureCmd.GainBlock(owner, FleshBone.DevourBlock, ValueProp.Unpowered, null);
            if (_logs++ < 40)
            {
                Logger.Log($"[CharBoss] 吞噬生命：她给 {target.Name} 上了 {power.Id.Entry}" +
                           $"（{amount:0.#}）⇒ 获得 {FleshBone.DevourBlock} 点格挡");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 吞噬生命触发失败: " + e.Message);
        }
    }
}
