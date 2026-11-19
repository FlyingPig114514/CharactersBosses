using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
using CharacterBosses.Monsters;

namespace CharacterBosses.Core;

/// <summary>
/// **我们 Boss 的充能球被动该打谁**（用户 2026-11-19）：
/// > 「boss 的闪电充能球现在好像可以先电亡灵契约师。电球和黑球都应该**先索敌奥斯提**。」
///
/// 用户口径（已确认）：**先打「被这只 Boss 锁定的那位玩家」的召唤物（奥斯提等），没有召唤物才打玩家本人**。
///
/// 为什么需要接管：原版 `LightningOrb.Passive` / `DarkOrb.Passive` 在 `target` 为空时会从
/// **施法者视角的 `HittableEnemies`** 里随机挑一个 —— 而我们这只 Boss 的「敌人」里除了真人玩家，
/// 还包含**别的 Boss 的影子玩家**和它们的随从（多 Boss 同场），于是球会电到自己人身上
/// （用户实测：电到了另一只 Boss「亡灵契约师」）。
///
/// 做法：只对「球的主人是我们的 Boss」改写目标，其它情况（真人玩家自己的球）**一律不动**。
/// 挑召唤物时按 `CombatState.Creatures` 的**列表顺序**取第一个 ⇒ 两端确定性一致，不会造成不同步。
/// </summary>
internal static class BossOrbTargeting
{
    /// <summary>球被动/激发该打的目标：先召唤物，再玩家本人（拿不到返回 null = 用原版逻辑）。</summary>
    internal static Creature? Preferred(CharBossMonster? boss)
    {
        try
        {
            var combat = boss?.CombatState;
            if (combat == null) return null;

            // ① 被这只 Boss 锁定的那位玩家（单人就是唯一真人玩家）
            var victim = BossLockOn.TargetFor(boss!);
            if (victim == null)
            {
                try
                {
                    foreach (var p in BossLockOn.RealPlayers(combat))
                    {
                        if (p?.Creature == null) continue;
                        victim = p;
                        break;
                    }
                }
                catch { /* 忽略 */ }
            }
            var victimCreature = victim?.Creature;
            if (victimCreature == null) return null;

            // ② 优先：这位玩家的召唤物（奥斯提等宠物）。按列表顺序取第一个 ⇒ 两端一致。
            try
            {
                foreach (var c in combat.Creatures)
                {
                    if (c == null || ReferenceEquals(c, victimCreature)) continue;
                    if (!c.IsAlive || c.IsDead) continue;
                    bool isPet = false;
                    try { isPet = c.IsPet; } catch { isPet = false; }
                    if (!isPet) continue;
                    try { if (!ReferenceEquals(c.PetOwner, victim)) continue; } catch { /* 拿不到就算他的 */ }
                    return c;
                }
            }
            catch { /* 忽略：退回玩家本人 */ }

            // ③ 没有召唤物 ⇒ 打玩家本人
            return victimCreature;
        }
        catch { return null; }
    }

    /// <summary>球的主人是我们的 Boss 才改写目标。</summary>
    internal static Creature? Rewrite(OrbModel? orb, Creature? given)
    {
        try
        {
            var owner = orb?.Owner?.Creature;
            if (owner?.Monster is not CharBossMonster boss) return given;
            var want = Preferred(boss);
            return want ?? given;
        }
        catch { return given; }
    }
}

/// <summary>
/// 闪电充能球的被动目标（见 <see cref="BossOrbTargeting"/>）。
/// ⚠ 这两个 `Passive` 都是**在具体球类上声明的**（不在 `OrbModel` 上）⇒ 必须分别挂，
///   并且显式写死参数类型（本仓库踩过重载 ⇒ Harmony 抛 AmbiguousMatchException 的坑）。
/// </summary>
[HarmonyPatch(typeof(LightningOrb), nameof(LightningOrb.Passive),
    new[] { typeof(PlayerChoiceContext), typeof(Creature) })]
internal static class LightningOrbBossTargetPatch
{
    [HarmonyPrefix]
    public static void Prefix(LightningOrb __instance, ref Creature target)
    {
        try
        {
            var want = BossOrbTargeting.Rewrite(__instance, target);
            if (want != null) target = want;
        }
        catch { /* 绝不影响战斗 */ }
    }
}

/// <summary>黑暗充能球的被动目标（同上）。</summary>
[HarmonyPatch(typeof(DarkOrb), nameof(DarkOrb.Passive),
    new[] { typeof(PlayerChoiceContext), typeof(Creature) })]
internal static class DarkOrbBossTargetPatch
{
    [HarmonyPrefix]
    public static void Prefix(DarkOrb __instance, ref Creature target)
    {
        try
        {
            var want = BossOrbTargeting.Rewrite(__instance, target);
            if (want != null) target = want;
        }
        catch { /* 绝不影响战斗 */ }
    }
}
