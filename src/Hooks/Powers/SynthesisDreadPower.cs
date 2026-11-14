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
/// **恐惧模式**下的「**人工合成**」（物理机，用户 2026-11-13）：
/// 名字与正常模式完全相同，只是层数从 **3 → 5**。
///
/// 用户 2026-11-13 定的规则：「每个 boss 都用特殊效果，也都有特殊效果的名字；
/// 恐惧症模式下所有特殊效果都用和普通模式一样的名字」⇒ 所以这是**同一个效果名的加强版**，
/// 由 <c>BossDefs</c> 的 <c>SpecialPowerDreadByAct</c> 在恐惧模式下替换 <see cref="SynthesisPower"/>。
/// </summary>
public sealed class SynthesisDreadPower : PowerModel
{
    /// <summary>恐惧模式的层数（正常模式是 3）。</summary>
    public const int ArtifactAmount = 5;

    public override PowerType Type => PowerType.Buff;

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
            catch { /* 用默认 5 */ }
            if (amount <= 0) return;

            var ctx = new BlockingPlayerChoiceContext();
            await PowerCmd.Apply<ArtifactPower>(ctx, owner, amount, owner, null, false);
            Logger.Log($"[CharBoss] 人工合成（恐惧模式）：{owner.Name} 获得 {amount} 层人工制品");
        }
        catch (System.Exception e)
        {
            Logger.Log("[CharBoss] 人工合成（恐惧模式，给人工制品）失败: " + e.Message);
        }
    }
}