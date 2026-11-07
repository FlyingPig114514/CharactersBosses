using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Core;

namespace CharacterBosses.Patches;

/// <summary>
/// 「击剑指南」只允许铸造**一次**（战斗开始时）。
///
/// 用户实测：「战斗开始时触发了一次，他第一回合开始时又触发了一次」。
/// 原因是两条路径都在铸：
///   ① 我们自己：<see cref="BossHookBridge"/> 在战斗开始时替影子玩家铸造 10（原版
///      遗物钩子对影子玩家不生效，所以必须自己来）；
///   ② 原版遗物自己：<c>FencingManual.AfterSideTurnStart</c> 的条件是
///      <code>
///      participants.Contains(Owner.Creature) &amp;&amp; Owner.PlayerCombatState.TurnNumber &lt;= 1
///        → ForgeCmd.Forge(DynamicVars.Forge.BaseValue /* = 10 */, Owner, this)
///      </code>
///      影子玩家的 <c>TurnNumber</c> 在他自己的第 1 回合正好是 1 ⇒ 又铸一次
///      （日志实测：剑伤 20 → 31）。
///
/// 所以：只要我们这场战斗已经铸过了，就把原版这条钩子对影子玩家跳过。
/// 真人玩家的这条路完全不受影响（判断依据是「这只生物是不是本 mod 的 Boss」）。
/// </summary>
[HarmonyPatch(typeof(FencingManual), nameof(FencingManual.AfterSideTurnStart))]
public static class FencingManualForgeOncePatch
{
    [HarmonyPrefix]
    public static bool Prefix(FencingManual __instance, ref System.Threading.Tasks.Task __result)
    {
        try
        {
            var owner = __instance.Owner;
            bool isShadowOwner = owner?.Creature?.Monster is Monsters.CharBossMonster;
            if (!isShadowOwner) return true;              // 真人玩家 → 原版逻辑
            if (!BossHookBridge.FencingManualForgedThisFight) return true;   // 我们还没铸过 → 交给原版

            Logger.Log("[CharBoss] 击剑指南：跳过原版遗物在他回合开始的第二次铸造（战斗开始时已铸过一次）");
            // ⚠ 跳过一个返回 Task 的方法时**必须**给它一个已完成的 Task。
            // 上一版只 `return false`，__result 保持 null → 调用方 await null → NRE
            // （日志：`遗物 FENCING_MANUAL 的 AfterSideTurnStart 失败: Object reference not set…`），
            // 而这条 NRE 是落在游戏自己的回合开始广播里的，会把玩家回合卡住（回合结束按钮消失）。
            __result = System.Threading.Tasks.Task.CompletedTask;
            return false;
        }
        catch
        {
            return true;   // 判断失败就按原版走，绝不影响流程
        }
    }
}
