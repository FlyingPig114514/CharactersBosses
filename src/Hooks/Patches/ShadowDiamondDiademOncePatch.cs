using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Relics;

namespace CharacterBosses.Patches;

/// <summary>
/// 钻石头冠（<c>DiamondDiadem</c>）：**只让它在战斗开始生效一次**，别再在她第 1 回合开始重复触发。
///
/// 反编译的钻石头冠实现：
/// <code>
/// AfterSideTurnStart(side, participants, combatState):
///     if (participants.Contains(Owner.Creature) &amp;&amp; Owner.PlayerCombatState.TurnNumber &lt;= 1)
///     {
///         await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block /* 20 */, null);
///         await PowerCmd.Apply&lt;BlurPower&gt;(Owner.Creature, 1, …);
///     }
/// </code>
/// 而 <c>CombatState.IterateHookListeners()</c> 会把挂在 Boss 生物上的影子 Player 的遗物也算成监听者
/// ⇒ 它会在**她自己的第 1 回合开始**被原版广播一次；我们又在战斗开始（<c>BossHookBridge.FireCombatStart</c>）
/// 逐件遗物手动调了一次 ⇒ **20 格挡 + 1 模糊 给了两遍**（用户 2026-10-27 实测：三层刀猎的钻石头冠触发了 2 次）。
///
/// 修法同红面具（<see cref="ShadowRedMaskTurnStartPatch"/>）：把**原版那次**（在她回合开始）拦掉，
/// 保留战斗开始那次。补丁必须打在**具体类型**上 —— 基类虚方法拦不住派生 override（踩过）。
/// 真人玩家自己的钻石头冠完全不受影响（按 owner 阵营判断）。
/// </summary>
[HarmonyPatch]
public static class ShadowDiamondDiademOncePatch
{
    private static int _logs;

    /// <summary>
    /// ★ 双版本：`DiamondDiadem.AfterSideTurnStart` **只有 v0.111 声明**（v0.107 的钻石头冠走的是
    ///   `AfterCardPlayed` / `BeforeSideTurnEnd`）⇒ 旧版解析不到目标，整类跳过。
    ///   旧版上不存在「战斗开始手动补一次 + 原版回合开始再触发一次」这个双触发，
    ///   所以跳过不会造成重复结算（实测日志里它曾是 5 个挂载失败的补丁类之一）。
    /// </summary>
    [HarmonyPrepare]
    public static bool Prepare() => TargetMethod() != null;

    internal static System.Reflection.MethodBase? TargetMethod()
        // ⚠ 只查「钻石头冠自己声明」的（125 事故：AccessTools.Method 会找到基类 ⇒ 补丁挂到 AbstractModel 上，
    //   把所有模型的这个钩子都拦掉 ⇒ 崩溃）
        => Core.GameCompat.DeclaredMethod(typeof(DiamondDiadem), "AfterSideTurnStart");

    [HarmonyPrefix]
    public static bool Prefix(DiamondDiadem __instance, ref Task __result)
    {
        try
        {
            var owner = __instance.Owner;
            if (owner?.Creature == null) return true;
            if (owner.Creature.Side != CombatSide.Enemy) return true;   // 真人玩家：走原版
            if (owner.Creature.Monster == null) return true;            // 不是我们的 Boss 生物

            __result = Task.CompletedTask;
            if (_logs++ < 8)
            {
                Logger.Log("[CharBoss] 钻石头冠：跳过她回合开始的重复触发" +
                           "（20 格挡 + 1 模糊已在战斗开始时给过）");
            }
            return false;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 钻石头冠拦截失败，按原版执行: " + e.Message);
            return true;
        }
    }
}

/// <summary>
/// 钻石头冠那 20 点格挡的**唯一**发放点（v0.2.241）。
///
/// 为什么改成挂在这里（用户 2026-10-30 联机不同步，RitsuLib 诊断包直接点出
/// `creatures[6].block: local=20; remote=0`，当地怪物是 `MONSTER.SILENT_BOSS`）：
///   · 以前的做法是「战斗开始 `CreatureCmd.GainBlock(20)`」+「每帧驱动 `EnsureDiamondDiademBlock`
///     发现格挡被清零就异步补一次（`Task.Delay(120)`）」；
///   · 而「战斗开始清零」这一步**两端的相对时序并不一致** ⇒ 有的客户端那 20 点活下来了、
///     有的被清掉后靠帧驱动补回、还有的（那位 Hermit 玩家）帧驱动那几帧没跑到 ⇒ 剩下 0 点
///     ⇒ **同一场战斗两端格挡不同 ⇒ 校验和必然分叉**。
///   · 结论：**战斗状态绝不能在帧回调/UI tick 里改**。现在改成：
///     1. 战斗开始只施加「残影 2 层」（走同一套同步流程）；
///     2. 格挡在「玩家回合开始」这条**同步的、两端都会被 await 的钩子**里一次性补齐
///        （`AfterSideTurnStart(Player)` → `Creature.GainBlockInternal`，同步写，写完才继续），
///        时机对玩家来说等价于「一进战斗就有」，而两端结果必然相同。
/// </summary>
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), nameof(MegaCrit.Sts2.Core.Hooks.Hook.AfterSideTurnStart),
              new[] { typeof(ICombatState), typeof(CombatSide), typeof(IReadOnlyList<Creature>) })]
public static class DiademBlockSyncPatch
{
    [HarmonyPostfix]
    public static void Postfix(ICombatState combatState, CombatSide side)
    {
        if (side != CombatSide.Player) return;
        try { CharacterBosses.Core.BossHookBridge.GrantDiademBlockSync(combatState); }
        catch (Exception e) { Logger.Log("[CharBoss] 钻石头冠补格挡失败: " + e.Message); }
    }
}
