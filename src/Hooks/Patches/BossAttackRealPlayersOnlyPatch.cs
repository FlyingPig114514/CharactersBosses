using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// 让**她的攻击牌只打真人玩家**，不打玩家侧的召唤物/宠物。
///
/// 用户 2026-10-26 反馈：「奇巧猎的连续反弹会打到史莱姆老大的史莱姆」
/// （玩家玩崩坠的史莱姆老大时，分裂出来的小史莱姆是她这一侧的宠物）。
///
/// 为什么 `HittableEnemiesSidePatch` 拦不住：反编译 `AttackCommand.GetPossibleTargets()`：
/// <code>
/// if (IsMultiTargeted) {
///     if (_sourceType == SourceType.Monster) return _combatState.PlayerCreatures;   // ← 含玩家宠物
///     return _combatState.GetOpponentsOf(Attacker);                                 // ← 也含玩家宠物
/// }
/// </code>
/// 「随机敌人」（连续反弹）和「对所有敌人」这类走的是**这两条**，根本不读
/// `HittableEnemies` ⇒ 我们那条「只挑真人玩家」的收窄对它无效。
///
/// 所以这里在 `GetPossibleTargets` 上加 postfix：**攻击者是我们的 Boss** 时，
/// 把结果里的非真人玩家（有 `Monster` 或没有 `Player` 的生物）全部剔掉。
/// 只影响我们的 Boss 出手；玩家自己打牌、原版怪物出手一切照旧。
/// </summary>
[HarmonyPatch(typeof(AttackCommand), "GetPossibleTargets")]
public static class BossAttackRealPlayersOnlyPatch
{
    [HarmonyPostfix]
    public static void Postfix(AttackCommand __instance, ref IReadOnlyList<Creature> __result)
    {
        try
        {
            if (__result == null || __result.Count == 0) return;

            Creature? attacker;
            try { attacker = __instance?.Attacker; } catch { return; }
            if (attacker?.Monster is not CharBossMonster) return;    // 只对我们的 Boss 生效

            var only = new List<Creature>(__result.Count);
            foreach (var creature in __result)
            {
                if (creature == null) continue;
                if (creature.Monster != null) continue;              // 宠物/召唤物/随从
                if (creature.Player == null) continue;               // 不是玩家生物
                only.Add(creature);
            }

            // 一个真人都没有（极端情况）就别动，免得打出「无目标」异常
            if (only.Count == 0 || only.Count == __result.Count) return;

            Logger.Log($"[CharBoss] 攻击选靶收窄：{attacker.Monster?.Id.Entry} 的这次攻击" +
                       $"从 {__result.Count} 个目标里剔掉 {__result.Count - only.Count} 个召唤物，只打真人玩家");
            __result = only;
        }
        catch { /* 绝不影响战斗 */ }
    }
}
