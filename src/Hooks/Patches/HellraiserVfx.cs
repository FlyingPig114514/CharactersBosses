using System;
using MegaCrit.Sts2.Core.Commands;

namespace CharacterBosses.Patches;

/// <summary>
/// 「**地狱狂徒**」自动打出打击时的**专属演出**（用户 2026-11-08：他平时直接打打击的动画不一样，
/// 一定有专属动画 → 确实有）。
///
/// 反编译实证：
///   · `Hellraiser`（卡）的 `OnEnqueuePlayVfx`：往 `NCombatRoom.Instance.CombatVfxContainer`
///     里加一个 `NHellraiserVfx.Create(Owner.Creature)`（＝**脚下浮现那把刀**），
///     再 `CreatureCmd.TriggerAnim(creature, "PowerUp", ...)`；
///   · `HellraiserPower` 的自动打出用的是攻击指令
///     `.WithHitFx("vfx/hellraiser_attack_vfx", ...).WithAttackerAnim("Cast", ...)`（＝**铁山靠**那个动作）。
///
/// 我们的 Boss 收不到原版那条 `AfterCardDrawnEarly`，所以这两样由这里手动补。
/// </summary>
internal static class HellraiserVfx
{
    /// <summary>打出前：脚下浮刀 + 施法动作（Cast）。</summary>
    internal static void Spawn(MegaCrit.Sts2.Core.Entities.Creatures.Creature? her)
    {
        try
        {
            if (her == null) return;
            var node = MegaCrit.Sts2.Core.Nodes.Vfx.Cards.NHellraiserVfx.Create(her);
            if (node != null)
            {
                var room = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance;
                var container = room?.CombatVfxContainer;
                if (container != null) container.AddChild(node);
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 地狱狂徒特效（浮刀）失败（忽略）: " + e.Message); }

        try
        {
            if (her != null) _ = CreatureCmd.TriggerAnim(her, "Cast", 0.6f);
        }
        catch (Exception e) { Logger.Log("[CharBoss] 地狱狂徒特效（动作）失败（忽略）: " + e.Message); }
    }

    /// <summary>兼容旧调用。</summary>
    internal static void Flash(MegaCrit.Sts2.Core.Entities.Creatures.Creature? victim) => Spawn(victim);
}