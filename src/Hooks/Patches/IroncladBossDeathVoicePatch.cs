using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace CharacterBosses.Patches;

/// <summary>
/// **铁甲战士 boss 的死亡音效换成一代那三句语音**（用户 2026-11-10：
/// 「所有铁甲战士boss死亡的音效都改成 STS_VO_Ironclad_2a、2b、2c 这 3 个中随机一个」）。
///
/// 为什么挂 `NCreature.StartDeathAnim(bool shouldRemove)` 的**后置**（反编译 `sts2_107/111` 一致）：
/// <code>
/// public float StartDeathAnim(bool shouldRemove)      // ★ 同步方法，返回 float
/// {
///     DisableInteractionForDeath();
///     if (DeathAnimationTask != null &amp;&amp; !DeathAnimationTask.IsCompleted) return 0f;
///     float num = 0f;
///     if (_spineAnimator != null) {                   // ★ 没有 spine animator 就整段跳过
///         if (monster != null &amp;&amp; monster.HasDeathSfx) SfxCmd.PlayDeath(Entity.Monster);
///         if (Entity.Player != null) SfxCmd.PlayDeath(Entity.Player);
///         SetAnimationTrigger("Dead");
///         num = GetCurrentAnimationLength();
///     }
///     DeathAnimationTask = AnimDie(shouldRemove, ...);
///     ...
/// }
/// </code>
/// 两个关键理由：
///   ① `SfxCmd.PlayDeath` 那条**被 `_spineAnimator != null` 门住** —— 我们的 boss 走角色模型立绘，
///      不一定有 spine animator，挂在那里可能永远不会被调用；而 `StartDeathAnim` 只要有生物节点就会被调。
///   ② 必须用 **Postfix** 而不是 Prefix：Prefix 返 false 会把 `SetAnimationTrigger("Dead")` /
///      `AnimDie` / `DeathAnimationTask` 全跳过 ⇒ **boss 尸体不消失、战斗卡住**。
///
/// 原版那条 FMOD 死亡事件由 <see cref="Monsters.IroncladBoss.HasDeathSfx"/> 覆写成 false 压掉，
/// 所以这里只负责「播我们自己的」，不用 return false。
/// </summary>
[HarmonyPatch(typeof(NCreature), "StartDeathAnim", new[] { typeof(bool) })]
internal static class IroncladBossDeathVoicePatch
{
    /// <summary>已经为她播过语音的生物（`StartDeathAnim` 可能因复活/重播被再走一遍）。</summary>
    private static readonly HashSet<object> Played = new(ReferenceEqualityComparer.Instance);

    private static void Postfix(NCreature __instance)
    {
        try
        {
            var creature = __instance?.Entity;
            if (creature == null) return;
            if (creature.Monster is not Monsters.IroncladBoss) return;     // 只接管铁甲战士系列（含各幕变体）
            if (!Played.Add(creature)) return;                             // 同一只只喊一次

            Core.BossVoice.PlayDeathVoice(creature);
        }
        catch (Exception e)
        {
            try { Logger.Log("[CharBoss] 铁甲战士死亡语音失败: " + e.Message); } catch { /* 忽略 */ }
        }
    }
}
