using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace CharacterBosses.Patches;

/// <summary>
/// 药水的**抛掷动画**要朝玩家飞，而不是朝她自己。
///
/// 用户 2026-10-26：「镣铐药水、爆炸药水这些对所有敌方生效的药水应该扔向玩家而不是自己」
/// （改过目标选择后仍然「依旧扔给自己」—— 因为目标选择根本不影响这个动画）。
///
/// 反编译 `PotionModel.OnUseWrapper` 才找到真凶：
/// <code>
/// if (TargetType.IsSingleTarget())
///     targetPosition = NCombatRoom.Instance?.GetCreatureNode(target)?.GetBottomOfHitbox() ?? Zero;
/// else
/// {
///     var list = TargetType != TargetType.AllEnemies
///         ? combatState.GetCreaturesOnSide(CombatSide.Player).Where(c =&gt; c.IsHittable).ToList()
///         : combatState.GetCreaturesOnSide(CombatSide.Enemy).Where(c =&gt; c.IsHittable).ToList();   // ← 写死「敌方阵营」
///     foreach (var c in list) targetPosition += node.VfxSpawnPosition;
///     targetPosition /= list.Count;
/// }
/// var vfx = NItemThrowVfx.Create(nCreature.VfxSpawnPosition, targetPosition, potion.Image);
/// </code>
/// 也就是说：**「对所有敌方」的药水直接把目标点算成 `CombatSide.Enemy` 的平均位置**，
/// 而我们的 Boss 就在敌方阵营 ⇒ 药水当然朝她自己飞（效果其实已经打在玩家身上了）。
///
/// 做法：使用药水期间（<see cref="Arm"/>）把 `NItemThrowVfx.Create` 的 `targetPosition` 换成
/// 我们事先算好的「玩家侧平均位置」。不动阵营、不动任何效果逻辑，只改这一个动画参数。
/// </summary>
[HarmonyPatch(typeof(NItemThrowVfx), nameof(NItemThrowVfx.Create))]
public static class PotionThrowAtPlayerPatch
{
    /// <summary>下一次 `NItemThrowVfx.Create` 要用的目标点（null = 不干预）。</summary>
    private static Vector2? _override;

    /// <summary>在调用 `potion.OnUseWrapper` 之前调：指定「这一瓶要朝哪里飞」。</summary>
    internal static void Arm(Vector2? targetPosition) => _override = targetPosition;

    /// <summary>用完清掉（一定要成对调用，免得影响后面别的抛掷动画）。</summary>
    internal static void Disarm() => _override = null;

    [HarmonyPrefix]
    public static void Prefix(ref Vector2 __1)
    {
        try
        {
            if (_override is not Vector2 pos) return;
            _override = null;              // 一次性：只改这一发
            __1 = pos;
        }
        catch { /* 动画而已，失败就用原值 */ }
    }
}
