using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace CharacterBosses.Patches;

/// <summary>
/// 让「**血肉戏法**」（<c>SleightOfFleshPower</c>）对我们这只 Boss 也生效。
///
/// 反编译原文（`sts2_111.dll`）：
/// <code>
/// public override Task AfterPowerAmountChanged(PlayerChoiceContext ctx, PowerModel power,
///                                              decimal amount, Creature? applier, CardModel? cardSource)
/// {
///     if (amount != 0 &amp;&amp; power.GetTypeForAmount(amount) == PowerType.Debuff
///         &amp;&amp; power.Owner.IsEnemy        // ← ★ 绝对阵营：Side == CombatSide.Enemy
///         &amp;&amp; applier == Owner &amp;&amp; !(power is ITemporaryPower))
///     {
///         await CreatureCmd.Damage(ctx, power.Owner, Amount, ValueProp.Unpowered, Owner);
///     }
/// }
/// </code>
/// `Creature.IsEnemy => Side == CombatSide.Enemy`，而玩家的 `Side` 是 `CombatSide.Player`
/// ⇒ 她（影子玩家）给**玩家**上的负面一条都不满足 ⇒ 原版这层能力对我们**永远不触发**。
///
/// 做法（按项目规矩：能走原版就走原版，必须接管的先压掉原版、再照原样补一次）：
///   · 只有「持有者是我们的 Boss，且目标确实是真人玩家」这一种情况接管；
///   · 接管时按原版同一条件（debuff / applier 是她 / 不是 ITemporaryPower）结算
///     同样的 `CreatureCmd.Damage(..., Amount, ValueProp.Unpowered, Owner)`；
///   · 其它情况（真玩家自己的血肉戏法）一律 `return true` 走原版，零回归。
///
/// ⚠ 这是 **async 方法** ⇒ Prefix 返回 false 时**必须**给出 `ref Task __result`，
///   否则游戏侧拿到 null 直接 NRE（本项目 v0.2.356 踩过这个坑，整条钩子链会断）。
/// </summary>
[HarmonyPatch(typeof(SleightOfFleshPower), "AfterPowerAmountChanged",
    new[]
    {
        typeof(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext),
        typeof(PowerModel), typeof(decimal), typeof(MegaCrit.Sts2.Core.Entities.Creatures.Creature),
        typeof(MegaCrit.Sts2.Core.Models.CardModel),
    })]
internal static class SleightOfFleshBossPatch
{
    private static bool Prefix(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext choiceContext,
                               PowerModel power, decimal amount,
                               MegaCrit.Sts2.Core.Entities.Creatures.Creature? applier,
                               SleightOfFleshPower __instance, ref Task __result)
    {
        try
        {
            var owner = __instance?.Owner;
            if (owner == null) return true;
            if (owner.Monster is not Monsters.CharBossMonster boss) return true;   // 只接管我们的 Boss

            // 目标必须是真人玩家（她视角的敌人）；别的 Boss 的影子玩家不算
            var victim = power?.Owner;
            if (victim == null || victim.Monster != null) return true;
            if (amount <= 0m) return true;

            PowerType kind;
            try { kind = power!.GetTypeForAmount(amount); } catch { return true; }
            if (kind != PowerType.Debuff) return true;
            if (power is ITemporaryPower) return true;

            var giver = power!.Applier ?? applier;
            if (!ReferenceEquals(giver, owner)) return true;

            // —— 到这里才接管：照原版那条 Damage 原样补一次（Amount 就是血肉戏法的 9/13）
            int per = 0;
            try { per = Math.Max(0, (int)Math.Round((double)__instance!.Amount)); } catch { per = 0; }
            if (per <= 0) { __result = Task.CompletedTask; return false; }

            __result = DealAsync(choiceContext, boss, victim, per);
            return false;
        }
        catch (Exception e)
        {
            try { Logger.Log("[CharBoss] 血肉戏法（Boss 版）判定失败，放行原版: " + e.Message); } catch { /* 忽略 */ }
            return true;
        }
    }

    private static async Task DealAsync(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext choiceContext,
                                        Monsters.CharBossMonster boss,
                                        MegaCrit.Sts2.Core.Entities.Creatures.Creature victim, int per)
    {
        try
        {
            // 原版就是 Unpowered（无来源伤害）⇒ 带「为你而死」的召唤物不会替玩家承受，
            // 正好符合用户写在特殊效果里的那句警告。
            await MegaCrit.Sts2.Core.Commands.CreatureCmd.Damage(
                choiceContext, victim, per, MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered, boss.Creature);
            Logger.Log($"[CharBoss] 血肉戏法：她给 {victim.Name} 上了负面 ⇒ 造成 {per} 点无来源伤害");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 血肉戏法（Boss 版）结算失败: " + e.Message);
        }
    }
}
