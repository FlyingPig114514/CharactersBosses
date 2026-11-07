using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// **暗黑充能球**在多人的**跟踪锁定**：用户 2026-10-29
/// 「做，让暗黑充能球攻击被锁定玩家而非血最少的玩家」。
///
/// 原版实现（反编译 `DarkOrb.&lt;Evoke&gt;d__11.MoveNext`）：
/// <code>
/// IReadOnlyList&lt;Creature&gt; hittableEnemies = darkOrb.CombatState.HittableEnemies;
/// if (hittableEnemies.Count != 0)
/// {
///     var weakestEnemy = hittableEnemies.MinBy(c =&gt; c.CurrentHp);   // ← 血最少的那个
///     darkOrb.ActivateEvoke(weakestEnemy);
///     await CreatureCmd.Damage(…, weakestEnemy, darkOrb.EvokeVal, Unpowered, owner);
/// }
/// </code>
/// 它走的是 `HittableEnemies`（不是 `GetOpponentsOf`），所以我们的「只留锁定玩家」收窄管不到它 ⇒
/// 多人时会打**血最少的玩家**，哪怕那位根本没被这只 Boss 锁定。这与跟踪锁定文案
/// 「充能球只会攻击你」不符。
///
/// 做法与 <see cref="SerpentFormLockOnPatch"/>（群蛇形态）完全同一套：
/// 给这条状态机的 `MoveNext` 挂前缀，在**状态机刚开始**（`&lt;&gt;1__state == -1`，也就是还没跑到
/// 第一次 `await`、`HittableEnemies` 还没被读）时，用
/// <see cref="HittableEnemiesSidePatch.RequestNarrowOnce"/> 把「可命中敌人」一次性收窄成
/// **这只 Boss 锁定的那位玩家** —— 后面的 `MinBy` 在只有一个候选时自然只能选他。
///
/// 为什么只设一次（判 `&lt;&gt;1__state`）：一次性收窄两帧内有效，若在后续的 `MoveNext` 里重复设置，
/// 万一这次唤起提前结束，残留的收窄可能被同一两帧内别的 `HittableEnemies` 读取吃走。
///
/// 单人零回归：单人时「锁定的那位」就是唯一的玩家，和「血最少的玩家」结果一致。
/// 真人玩家的暗黑球一律不动（只对我们 Boss 的球生效）。
/// </summary>
internal static class DarkOrbLockOnPatch
{
    private static bool _installed;

    internal static void TryInstall()
    {
        if (_installed) return;
        _installed = true;

        try
        {
            var orbType = typeof(MegaCrit.Sts2.Core.Models.Orbs.DarkOrb);
            var prefix = new HarmonyMethod(typeof(DarkOrbLockOnPatch)
                .GetMethod(nameof(BeforeEvoke), BindingFlags.Static | BindingFlags.NonPublic)!);

            var harmony = new Harmony("CharacterBosses.darkorblockon");
            int patched = 0;
            foreach (var nested in orbType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (nested.Name.IndexOf("Evoke", StringComparison.Ordinal) < 0) continue;
                var moveNext = nested.GetMethod("MoveNext",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (moveNext == null) continue;
                harmony.Patch(moveNext, prefix: prefix);
                patched++;
            }

            Logger.Log(patched > 0
                ? $"[CharBoss] 暗黑充能球：已挂 {patched} 个 Evoke 状态机" +
                  "（多人时由「血最少的玩家」改为「只打被这只 Boss 锁定的那位」）"
                : "[CharBoss] 暗黑充能球：没找到 Evoke 状态机 ⇒ 跟踪锁定未挂（版本不符？）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 暗黑充能球跟踪锁定挂载失败（忽略）: " + e.Message);
        }
    }

    private static void BeforeEvoke(object? __instance)
    {
        try
        {
            if (__instance == null) return;
            var type = __instance.GetType();

            // 只在状态机**刚开始**（还没跑到第一次 await）时设一次，避免残留的一次性收窄串到别处
            var stateField = type.GetField("<>1__state",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (stateField != null && stateField.GetValue(__instance) is int state && state != -1) return;

            var selfField = type.GetField("<>4__this",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var orb = selfField?.GetValue(__instance) as MegaCrit.Sts2.Core.Models.OrbModel;
            var owner = orb?.Owner;
            var boss = owner?.Creature?.Monster as CharBossMonster;
            if (boss == null) return;      // 真人玩家的暗黑球：原版行为（血最少）

            // 「她锁定的那位」；缺失时退到「任意一位活着的真人玩家」（语义仍是只打玩家）
            var locked = Core.BossLockOn.TargetFor(boss)?.Creature;
            if (locked == null || !locked.IsAlive)
            {
                try
                {
                    foreach (var p in Core.BossLockOn.RealPlayers(boss.CombatState))
                    {
                        if (p?.Creature == null || !p.Creature.IsAlive) continue;
                        locked = p.Creature;
                        break;
                    }
                }
                catch { /* 忽略 */ }
            }
            if (locked == null || !locked.IsAlive) return;

            HittableEnemiesSidePatch.RequestNarrowOnce(locked);
        }
        catch { /* 忽略：出问题就按原版「血最少」 */ }
    }
}
