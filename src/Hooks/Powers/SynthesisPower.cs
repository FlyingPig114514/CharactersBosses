using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace CharacterBosses.Powers;

/// <summary>
/// 故障机器人（二层：**物理机**）的特殊效果「**人工合成**」（用户 2026-10-24）。
///
/// 用户原话：**故障机器人在战斗开始时获得 3 层人工制品**。
///
/// 实现要点：
///   · 这一层 Power 由 <c>BossHookBridge.FireTurnStart</c> 用 <c>PowerCmd.Apply</c> 挂在 Boss 身上
///     （和「偏差认知」同一个入口），所以在它的 <see cref="AfterApplied"/> 里补发人工制品；
///   · 人工制品的层数取**牌面变量** <c>PowerVar&lt;ArtifactPower&gt;(3)</c>，升级不影响它；
///   · 没有 ChoiceContext 可用（<c>AfterApplied</c> 不带 ctx）→ 和「偏差认知」同步数值时一样，
///     临时造一个 <see cref="BlockingPlayerChoiceContext"/>。
/// </summary>
public sealed class SynthesisPower : PowerModel
{
    /// <summary>人工制品的层数（用户设计：3 层）。</summary>
    public const int ArtifactAmount = 3;

    public override PowerType Type => PowerType.Buff;

    /// <summary>不可叠加：有这一层就够了。</summary>
    public override PowerStackType StackType => PowerStackType.Single;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<ArtifactPower>(ArtifactAmount),
    };

    public override async Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        try
        {
            var owner = Owner;
            if (owner == null) return;

            int amount = ArtifactAmount;
            try
            {
                if (DynamicVars.TryGetValue("Artifact", out var v) && v != null)
                    amount = (int)System.Math.Round((double)v.BaseValue);
            }
            catch { /* 用默认 3 */ }
            if (amount <= 0) return;

            var ctx = new BlockingPlayerChoiceContext();
            await PowerCmd.Apply<ArtifactPower>(ctx, owner, amount, owner, null, false);
            Logger.Log($"[CharBoss] 人工合成：{owner.Name} 获得 {amount} 层人工制品");
        }
        catch (System.Exception e)
        {
            Logger.Log("[CharBoss] 人工合成（给人工制品）失败: " + e.Message);
        }
    }
}
