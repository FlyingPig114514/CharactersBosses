using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// **修正「超能光束」（<c>Hyperbeam</c>）的演出朝向**（用户 2026-10-24：
/// 「超能光束打的方向不对，朝下而不是朝玩家」）。
///
/// 反编译依据（`Hyperbeam.OnPlay` 的 `BeforeDamage` 回调）：
/// <code>
/// enemies = CombatState.Enemies.Where(e =&gt; e.IsAlive).ToList();      // ← 注意：是 Enemies，不是 HittableEnemies！
/// NHyperbeamVfx.Create(Owner.Creature, enemies.Last());              // 光束从她眼睛射向「最后一个敌人」
/// foreach (var e in enemies) NHyperbeamImpactVfx.Create(Owner.Creature, e);
/// </code>
/// 玩家打这张牌时「敌人」= Boss 那一侧，所以射向最后一个敌人是对的；
/// 但 **Boss 打这张牌时，「敌人」= 她自己那一侧** ⇒ 光束射向她自己 / 她的随从（脚下）→ 看起来朝下。
///
/// 修法：只对**我们的 Boss**把这两处 VFX 的目标换成玩家侧的真人（优先它锁定的那名玩家）。
/// 伤害结算不走这里（`DamageCmd…TargetingAllOpponents` 自己会走翻侧），所以只影响演出。
/// </summary>
[HarmonyPatch]
public static class HyperbeamAimPatch
{
    /// <summary>光束：把目标换成玩家侧。</summary>
    [HarmonyPatch(typeof(NHyperbeamVfx), nameof(NHyperbeamVfx.Create), new[] { typeof(Creature), typeof(Creature) })]
    [HarmonyPrefix]
    public static void BeamPrefix(Creature owner, ref Creature target)
    {
        try
        {
            var aim = AimFor(owner, target);
            if (aim != null) target = aim;
        }
        catch { /* 忽略：保持原目标 */ }
    }

    /// <summary>命中特效：目标在她自己那一侧时，同样改指玩家侧（否则火花全炸在她脚下）。</summary>
    [HarmonyPatch(typeof(NHyperbeamImpactVfx), nameof(NHyperbeamImpactVfx.Create), new[] { typeof(Creature), typeof(Creature) })]
    [HarmonyPrefix]
    public static void ImpactPrefix(Creature owner, ref Creature target)
    {
        try
        {
            var aim = AimFor(owner, target);
            if (aim != null) target = aim;
        }
        catch { /* 忽略 */ }
    }

    /// <summary>该换成谁：只有「owner 是我们的 Boss」时才换，换成玩家侧一个活着的真人。</summary>
    private static Creature? AimFor(Creature? owner, Creature? current)
    {
        if (owner?.Monster is not CharBossMonster boss) return null;

        var combat = owner.CombatState;
        if (combat == null) return null;

        // 已经是玩家侧的人（理论上不会发生）→ 不动
        if (current != null && current.Player != null) return null;

        var locked = Core.BossLockOn.TargetFor(boss)?.Creature;
        if (locked != null && locked.IsAlive && locked.Player != null) return locked;

        try
        {
            foreach (var creature in combat.PlayerCreatures)
            {
                if (creature == null || !creature.IsAlive) continue;
                if (creature.Player == null) continue;      // 只要真人（召唤物没有"脸"可打）
                return creature;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }
}
