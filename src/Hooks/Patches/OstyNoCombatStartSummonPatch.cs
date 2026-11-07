using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// 缚魂命匣：**跳过影子玩家在「战斗开始」那一次召唤**。
///
/// 现象（用户报）：刚进战斗、她的第一个回合还没开始，她脚下就已经站着一只奥斯提，
/// 而且没有血条（原版把「非本地玩家的随从」状态栏整块压掉）—— 看着像个 bug。
///
/// 机制（反编译 + 日志双证）：
///   · <c>BoundPhylactery.BeforeCombatStart()</c> 里就是 <c>SummonPet()</c>；
///   · <c>BoundPhylactery.AfterEnergyResetLate(player)</c> 只在
///     <c>Owner.PlayerCombatState.TurnNumber != 1</c> 时补召唤（第 2 回合起加血）；
///   · 也就是说**原版是"战斗开始就召一只"**，好让玩家第 1 回合手上就有奥斯提可用 ——
///     遗物描述写的"在你的回合开始时召唤1"只是文字。
///   · 而 <c>CombatState.IterateHookListeners()</c> 是遍历 <c>_allies/_enemies</c> 生物，
///     对**带 Player 的生物**把该玩家的遗物全部当钩子监听者 ⇒ 我们挂在 Boss 生物上的
///     影子 Player 的遗物**会被原版正常广播**（日志里的「涅奥的祝福：战斗开始特殊效果」
///     就是同一个广播打出来的）。所以这一只躲不掉，只能在遗物本体上拦。
///
/// 做法：只拦**影子玩家**（其生物在敌方侧）的那个实例。
/// 真玩家自己的缚魂命匣走原版逻辑，完全不受影响（他本来就应该在战斗开始就有一只）。
/// 她自己的第一只奥斯提由 <c>BossOsty</c> 在她的回合开始时召唤 —— 与描述一致。
/// </summary>
[HarmonyPatch(typeof(BoundPhylactery), nameof(BoundPhylactery.BeforeCombatStart))]
public static class OstyNoCombatStartSummonPatch
{
    [HarmonyPrefix]
    public static bool Prefix(BoundPhylactery __instance, ref Task __result)
    {
        try
        {
            if (!IsShadowOwner(__instance)) return true;   // 真玩家：照原版

            __result = Task.CompletedTask;
            Logger.Log("[CharBoss] 缚魂命匣：跳过影子（Boss）的「战斗开始」召唤 —— " +
                       "她的第一只奥斯提等她的回合开始再出");
            return false;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 缚魂命匣战斗开始拦截失败，按原版执行: " + e.Message);
            return true;
        }
    }

    /// <summary>真玩家的生物永远在 Player 侧；影子 Player 挂的是敌方侧那只 Boss 生物。</summary>
    private static bool IsShadowOwner(BoundPhylactery relic)
    {
        try
        {
            var creature = relic.Owner?.Creature;
            return creature != null && creature.Side == CombatSide.Enemy;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// 「**无界命匣**」（<c>PhylacteryUnbound</c>，欧洛巴斯之触把缚魂命匣升级成它）的战斗开始召唤 ——
/// 同样对**影子玩家**拦掉，改由 <c>BossHookBridge.FireCombatStart</c> 里自己结算一次
/// （`BossOsty.CombatStartSummonOf` → `StartOfCombat` = 5）。
///
/// 为什么也要拦：影子玩家的**遗物钩子其实会被原版广播**（`IterateHookListeners` 会把带 Player 的
/// 生物身上的遗物都算进监听者），不拦就会「原版一次 + 我们自己一次」= 召唤双份。
/// 真人玩家自己的无界命匣完全不受影响。
/// </summary>
[HarmonyPatch(typeof(PhylacteryUnbound), nameof(PhylacteryUnbound.BeforeCombatStart))]
public static class OstyUnboundNoCombatStartSummonPatch
{
    [HarmonyPrefix]
    public static bool Prefix(PhylacteryUnbound __instance, ref Task __result)
    {
        try
        {
            if (__instance?.Owner?.Creature?.Monster is not CharBossMonster) return true;   // 真玩家：照原版

            __result = Task.CompletedTask;
            Logger.Log("[CharBoss] 无界命匣：跳过影子（Boss）原版的「战斗开始」召唤 —— 由我们统一结算一次");
            return false;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 无界命匣战斗开始拦截失败，按原版执行: " + e.Message);
            return true;
        }
    }
}

/// <summary>
/// 「**欧洛巴斯之触**」（<c>TouchOfOrobas</c>，用户 2026-11-03 用它替换了「放电异虾」）：
/// 卡面是「拾起时把你的起始遗物换成它的远古版本」。对三层「养手骨」来说这件事**已经由设计本身完成**
/// （她的遗物栏里直接就是升级后的「无界命匣」）⇒ 把它对我们这只 Boss 的拾起钩子关掉，
/// 免得它去翻「起始遗物」时把别的东西换掉 / 报错。真人玩家不受影响。
/// </summary>
[HarmonyPatch(typeof(TouchOfOrobas), nameof(TouchOfOrobas.AfterObtained))]
public static class OstyTouchOfOrobasNoPickupPatch
{
    [HarmonyPrefix]
    public static bool Prefix(TouchOfOrobas __instance, ref Task __result)
    {
        try
        {
            if (__instance?.Owner?.Creature?.Monster is not CharBossMonster) return true;

            __result = Task.CompletedTask;
            Logger.Log("[CharBoss] 欧洛巴斯之触：她的起始遗物已经是「无界命匣」⇒ 跳过原版换遗物逻辑");
            return false;
        }
        catch
        {
            return true;
        }
    }
}