using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// 「**无界命匣**」（<c>PhylacteryUnbound</c>）的**每回合召唤 2** —— 对我们这只 Boss **只许发生一次**。
///
/// 用户 2026-11-04：「每回合她都召唤 2 两次。」
///
/// 反编译 `PhylacteryUnbound`：
/// <code>
/// BeforeCombatStart()   → OstyCmd.Summon(Owner, DynamicVars["StartOfCombat"] = 5)
/// AfterSideTurnStart(…) → OstyCmd.Summon(Owner, DynamicVars["StartOfTurn"] = 2)   // ← 这条
/// </code>
/// 我们本来就有两条来源：
///   1. `Core/BossOsty.SummonAndRegister`：她**回合开始**按 `TurnStartSummonOf`（无界命匣 = 2）自己召唤一次；
///   2. **原版这条 `AfterSideTurnStart`** —— 而影子玩家是**收得到**原版广播的
///      （v0.2.356 修好钩子链那个 NRE 之后才真正跑起来，和「王室猛毒掉两次」是**同一个根因**），
///      于是每回合再多一次 ⇒ **2 变 4**。
///
/// 所以这里对「遗物主人是我们的 Boss」整条跳过 —— 她的召唤量由 `BossOsty` 统一结算。
/// 「战斗开始召唤 5」那条（`BeforeCombatStart`）早已被 `Patches/OstyNoCombatStartSummonPatch` 拦掉，
/// 现在改由 `BossOsty.TryCombatStartSummon` 在战斗真的开始的第一帧补一次 ⇒ 战斗开始 5、之后每回合 2。
///
/// ⚠ 跳过 async 方法必须 `ref Task __result` 赋 `Task.CompletedTask`
/// （少这一句 Prefix 就返回 null Task，下游 `GetAwaiter()` 抛 NRE 把整条钩子链炸掉）。
/// 真人玩家的无界命匣、其它怪物的完全不受影响。
/// </summary>
[HarmonyPatch(typeof(PhylacteryUnbound), nameof(PhylacteryUnbound.AfterSideTurnStart))]
public static class PhylacteryNoDoubleSummonPatch
{
    /// <summary>整场只记一行（这条钩子每方回合开始都会广播一次，不节流会刷屏）。</summary>
    private static bool _logged;

    [HarmonyPrefix]
    public static bool Prefix(PhylacteryUnbound __instance, CombatSide side,
                              System.Collections.Generic.IReadOnlyList<Creature> participants,
                              ICombatState combatState, ref Task __result)
    {
        try
        {
            if (__instance?.Owner?.Creature?.Monster is not CharBossMonster) return true;   // 别人的无界命匣：照原版

            __result = Task.CompletedTask;   // ← 关键：不赋值就是 null Task
            if (!_logged)
            {
                _logged = true;
                Logger.Log("[CharBoss] 无界命匣：跳过原版 AfterSideTurnStart 的那次召唤" +
                           "（每回合的 2 由 BossOsty 统一结算；原因是影子玩家收得到原版广播 ⇒ 否则每回合召唤两次）");
            }
            return false;
        }
        catch
        {
            return true;
        }
    }
}
