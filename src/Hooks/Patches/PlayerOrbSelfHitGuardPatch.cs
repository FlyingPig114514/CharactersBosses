using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;

namespace CharacterBosses.Patches;

/// <summary>
/// **玩家自己的闪电球绝不电到自己人**（玩家 2026-11-20 反馈：「我用机宝对战打击战的时候
/// 电球怎么电自己了」）。
///
/// 反编译（0.113）`LightningOrb.ApplyLightningDamage(value, Creature? target, ctx, isEvoke)`：
/// <code>
/// var pool = CombatState.GetOpponentsOf(Owner.Creature).Where(e =&gt; e.IsHittable).ToList();
/// var targets = target == null
///     ? new[] { Owner.RunState.Rng.CombatTargets.NextItem(pool) }   // 被动 / 激发：随机一个
///     : new[] { target };                                           // ← 被动那条**目标是从外面传进来的**
/// await CreatureCmd.Damage(ctx, targets, value, ValueProp.Unpowered, Owner.Creature);
/// </code>
/// 也就是说「被动打谁」完全取决于**调用方传进来的那个 target**。这个 target 由球的触发流程
/// （`OrbModel.TriggerPassive` 的调用方）决定，而那份选靶在某些时序下会落到「玩家自己这一侧」
/// （我们为角色 Boss 做的「可命中敌人 = 玩家侧」翻侧 / 「只打锁定玩家」收窄都是按**全场**语义写的，
/// 一旦那层窗口没有被正确关闭，玩家自己的球就会拿到玩家侧的生物）。
///
/// 这里做一道**兜底**（与机制无关，任何原因都拦得住）：
/// 只有当球的主人是**真人玩家**、而传进来的目标是**同一侧**的生物时，把这个 target 抹成 null
/// ⇒ 让原版按「对手列表里随机取一个」自己重选。她（角色 Boss）自己的球一个字节都不动。
/// </summary>
[HarmonyPatch]
internal static class PlayerOrbSelfHitGuardPatch
{
    private static int _logs;

    [HarmonyPrepare]
    public static bool Prepare() => TargetMethod() != null;

    internal static MethodBase? TargetMethod()
    {
        try
        {
            return typeof(LightningOrb).GetMethod("ApplyLightningDamage",
                BindingFlags.Instance | BindingFlags.NonPublic);
        }
        catch { return null; }
    }

    [HarmonyPrefix]
    public static void Prefix(LightningOrb? __instance, object[] __args)
    {
        try
        {
            if (__instance == null || __args == null || __args.Length < 2) return;

            // 参数位置：0 = value, 1 = target, 2 = choiceContext, 3 = isEvoke
            if (__args[1] is not Creature target || target == null) return;

            var ownerCreature = __instance.Owner?.Creature;
            if (ownerCreature == null) return;
            // 她（角色 Boss）自己的球不归这道兜底管：按设计就该打玩家侧
            if (ownerCreature.Monster != null) return;
            // 只认真人玩家的球
            if (ownerCreature.Player == null) return;
            // 目标和她同一侧 ⇒ 等于打自己人（玩家自己的球不该发生）
            if (target.Side != ownerCreature.Side) return;

            __args[1] = null;      // 抹成 null ⇒ 让原版从「对手列表」里重选
            if (_logs++ < 10)
            {
                Logger.Log($"[CharBoss] 充能球兜底：玩家的闪电球被动原本要电到自己人" +
                           $"（{SafeName(target)}，与主人同侧）⇒ 改成由原版随机选对手");
            }
        }
        catch { /* 绝不影响球的正常结算 */ }
    }

    private static string SafeName(Creature? c)
    {
        try { return c?.Monster?.Id.Entry ?? c?.Player?.NetId.ToString() ?? "?"; }
        catch { return "?"; }
    }
}
