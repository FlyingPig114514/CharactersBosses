using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.Models.Powers;
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

    private static string SafeEntry(PowerModel? power)
    {
        try { return power?.Id.Entry ?? "?"; } catch { return "?"; }
    }

    /// <summary>每次她给目标上负面 ⇒ 自己获得 4 点格挡（并补血肉戏法的伤害）。</summary>
    public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power,
                                                      decimal amount, Creature? applier, CardModel? cardSource)
    {
        try
        {
            var owner = Owner;
            if (owner == null || !owner.IsAlive) return;

            // 诊断（限流）：先记一行「钩子确实被叫到了」，含目标与施加者。
            // 用户 2026-11-10 报「命运同担不对玩家造成伤害」时，日志里连一行都没有 ⇒ 需要先确认
            // 这个钩子在这条路径上到底有没有被派发（`PowerCmd.ModifyAmount` 里
            // `if (modifiedOffset != 0) await Hook.AfterPowerAmountChanged(...)`）。
            if (_logs++ < 30)
            {
                Logger.Log($"[CharBoss] 吞噬生命钩子：power={SafeEntry(power)} amount={amount:0.#} " +
                           $"目标={power?.Owner?.Name ?? "?"} 施加者={(power?.Applier?.Name ?? applier?.Name ?? "null")}");
            }

            if (power == null || amount == 0m) return;

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

            // ★ 2026-11-10 第二轮（用户：「命运同担还是不对玩家造成伤害」）：
            //   以前「血肉戏法」那 9 点放在 `Patches/SleightOfFleshBossPatch` 里（接管**她身上那层
            //   `SleightOfFleshPower`** 的回调）。但那条链要靠原版把这层 power 也派发一遍，
            //   一旦某次派发没走到（实测 22:42 那次 `STRENGTH_POWER −2` 连本行日志都没有），
            //   9 点就整条丢掉。而「吞噬生命」是她**必然带着**的特殊效果 —— 同一次派发里一定会到。
            //   ⇒ 把 9 点挪到这里一起算：只要她身上有血肉戏法（`SleightOfFleshPower`），
            //     就对那个玩家补一次 `Amount` 点**无来源伤害**（与反编译原版那一句完全一致）。
            int sleight = 0;
            try { sleight = Math.Max(0, owner.GetPowerAmount<SleightOfFleshPower>()); }
            catch { sleight = 0; }

            if (sleight > 0)
            {
                await CreatureCmd.Damage(choiceContext, target, sleight, ValueProp.Unpowered, owner);
            }

            Logger.Log($"[CharBoss] 吞噬生命：她给 {target.Name} 上了 {SafeEntry(power)}" +
                       $"（{amount:0.#}）⇒ 获得 {FleshBone.DevourBlock} 点格挡" +
                       (sleight > 0 ? $" + 血肉戏法 {sleight} 点无来源伤害" : ""));
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 吞噬生命触发失败: " + e.Message);
        }
    }
}
