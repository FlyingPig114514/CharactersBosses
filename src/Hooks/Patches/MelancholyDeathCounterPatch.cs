using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;

namespace CharacterBosses.Patches;

/// <summary>
/// 给「**忧郁**」（<c>Melancholy</c>）补上「别的单位死亡 → 减费」的记账。
///
/// 反编译 `Melancholy.AfterDeath`：
/// <code>
/// if (wasRemovalPrevented) return;
/// if (Pile == null || !Pile.IsCombatPile) return;
/// EnergyCost.AddThisCombat(-DynamicVars.Energy.IntValue);   // 每死一个单位，这张牌永久 -N 费（本场）
/// </code>
/// 也就是说减费**记在那一张卡实例上**。而我们的 Boss 手牌每回合由
/// <c>BossScriptRunner.BuildHand</c> **重建**（新实例）⇒ 上一回合减掉的费用全丢
/// （用户 2026-10-24：「二层的灵魂骨的忧郁不能正常的因为其他单位（如她的奥斯提）的死亡而减费」）。
///
/// 这里只做**记账**：每次「真的死了单位」就给当前战斗 +1；造牌时（`BuildHand`）
/// 给新造的忧郁补上 `-层数 × Energy`。
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterDeath))]
public static class MelancholyDeathCounterPatch
{
    /// <summary>当前这场战斗里「死了几个单位」。</summary>
    private static ICombatState? _combat;
    private static int _deaths;

    /// <summary>本场战斗已经死了几个单位（给造牌用）。</summary>
    internal static int DeathsIn(ICombatState? combat)
    {
        try
        {
            if (combat == null) return 0;
            if (!ReferenceEquals(combat, _combat)) return 0;   // 换场了：还没记过
            return _deaths;
        }
        catch { return 0; }
    }

    [HarmonyPostfix]
    public static void Postfix(ICombatState combatState, Creature creature, bool wasRemovalPrevented)
    {
        try
        {
            if (wasRemovalPrevented) return;          // 和原版判据一致：被"阻止移除"的不算
            if (creature == null) return;
            if (combatState == null) return;

            if (!ReferenceEquals(combatState, _combat))
            {
                _combat = combatState;
                _deaths = 0;
            }
            _deaths++;
            if (_deaths <= 40)
            {
                Logger.Log($"[CharBoss] 忧郁减费记账：{creature.Name} 死亡 → 本场累计 {_deaths} 个");
            }
        }
        catch { /* 忽略 */ }
    }
}
