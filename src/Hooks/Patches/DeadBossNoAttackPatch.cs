using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands.Builders;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// **她已经死了，就不许再出手**（用户 2026-10-24 截图实测：
/// 玩家储君有 3 点「荆棘」、Boss 储君只剩 2 血、君王之剑是「21×2」——
/// 第一剑打出去被荆棘弹死之后，**第二剑还是劈了出来**，跟玩家同归于尽）。
///
/// 为什么会这样：君王之剑的「×2」不是 `WithHitCount`（多段），而是
/// **「剑圣」<c>SwordSagePower</c> 给卡面加了 <c>BaseReplayCount</c>**（反编译：
/// `sovereignBlade.BaseReplayCount += amount`）⇒ 引擎会把**整张牌重放一次**。
/// 多段伤害在 `AttackCommand.Execute` 的循环里每段都会检查 `Attacker.IsDead`，
/// 但"重放"是另一条路径（再一次整牌结算），她死了照样能打出来。
///
/// 修法：给 `AttackCommand.Execute` 挂前缀 —— **攻击者是我们的一只 Boss 且已经死了**时，
/// 直接返回（不结算这次攻击）。只影响我们的 Boss，真人玩家/原版怪物一行不动。
/// </summary>
[HarmonyPatch(typeof(AttackCommand), nameof(AttackCommand.Execute))]
public static class DeadBossNoAttackPatch
{
    private static int _logs;

    [HarmonyPrefix]
    public static bool Prefix(AttackCommand __instance, ref Task<AttackCommand> __result)
    {
        try
        {
            var attacker = __instance?.Attacker;
            if (attacker?.Monster is not CharBossMonster) return true;   // 不是我们的 Boss
            if (!attacker.IsDead) return true;                           // 还活着：照常结算

            if (_logs++ < 20)
            {
                Logger.Log($"[CharBoss] {attacker.Name} 已经死了 → 这次攻击不结算" +
                           "（例如被荆棘弹死后不该再劈出重放的那一刀）");
            }
            __result = Task.FromResult(__instance);   // ⚠ Task<T> 方法必须给结果
            return false;
        }
        catch
        {
            return true;
        }
    }
}
