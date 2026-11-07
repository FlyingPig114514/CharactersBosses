using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// 「**王室猛毒**」（<c>RoyalPoison</c>）对**我们的影子 Boss** 只许在**战斗开始**掉一次血。
///
/// 反编译 `RoyalPoison.&lt;AfterPlayerTurnStart&gt;d__4`：
/// <code>
/// if (player == Owner &amp;&amp; Owner.PlayerCombatState.TurnNumber &lt;= 1) {
///     Flash();
///     await CreatureCmd.Damage(choiceContext, Owner.Creature, DynamicVars.Damage, null, null, null);
/// }
/// </code>
/// 也就是说它挂在 **`Hook.AfterPlayerTurnStart`** 上、条件是「钩子广播的 player 就是遗物主人，
/// 且她的回合数 ≤ 1」。
///
/// <para>
/// ⚠ 历史：v0.2.352 之前的注释一直写着「影子玩家**收不到**那条钩子」——那个前提是**错的**。
/// `BossHookBridge` 在她的回合开始会替影子玩家广播 `Hook.AfterPlayerTurnStart`，
/// 所以她其实是**收得到**的；只是 v0.2.351～355 期间那条链在情感芯片那里抛 NRE，
/// **整条链在到她之前就断了**，所以看起来像「收不到」（用户 2026-11-04 修好 NRE 之后
/// 立刻报「王室猛毒触发了两次」——战斗开始一次（`BossHookBridge.ApplyRoyalPoisonAtCombatStart`）
/// + 她第 1 回合开始一次（原版这条））。
/// </para>
///
/// 用户口径（2026-11-03 定、2026-11-04 再次确认）：**只在战斗开始时掉一次，她第 1 回合开始不掉**。
/// 所以这里对她把原版那条钩子整条跳过。
///
/// ⚠ 跳过 async 方法必须给 `ref Task __result` 赋 `Task.CompletedTask`——
/// 少这一句就是 null Task，下游 `GetAwaiter()` 会抛 NRE 把整条钩子链炸掉
/// （2026-11-04「事件机没有结束回合按钮」就是这么来的，见 `EmotionChipBossTakeoverPatch` 的注释）。
/// </summary>
[HarmonyPatch(typeof(RoyalPoison), nameof(RoyalPoison.AfterPlayerTurnStart))]
public static class RoyalPoisonBossSkipPatch
{
    /// <summary>整场只记一行（这条链每个玩家回合都会广播一次，不节流会刷爆日志）。</summary>
    private static bool _logged;

    [HarmonyPrefix]
    public static bool Prefix(RoyalPoison __instance, MegaCrit.Sts2.Core.Entities.Players.Player player,
                              ref Task __result)
    {
        try
        {
            if (__instance?.Owner?.Creature?.Monster is not CharBossMonster) return true;   // 别人的王室猛毒：照原版

            // 只有「广播的 player 就是遗物主人」那一次才会真扣血（反编译：`player == Owner`）——
            // 其它人的回合开始原版自己也是直接 return，我们不必插手（也就不刷日志）。
            if (!ReferenceEquals(player, __instance.Owner)) return true;

            __result = Task.CompletedTask;   // ← 关键：不赋值就是 null Task
            if (!_logged)
            {
                _logged = true;
                Logger.Log("[CharBoss] 王室猛毒：跳过原版「她第 1 回合开始」那次掉血" +
                           "（战斗开始时已由 BossHookBridge 扣过一次；用户要求整场只掉这一次）");
            }
            return false;
        }
        catch
        {
            return true;
        }
    }
}
