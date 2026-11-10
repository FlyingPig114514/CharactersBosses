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

            // ★ 2026-11-10（用户实测：「她打出命运同担的时候对自己也造成了伤害（应该只对玩家造成伤害）」）：
            //   反编译 `SharedFate` 除了让目标掉力量，还**让她自己掉力量**：
            //   `PowerCmd.Apply&lt;StrengthPower&gt;(ctx, Owner.Creature, -PlayerStrengthLoss, Owner.Creature, card)`。
            //   而她自己的生物 `Side == CombatSide.Enemy` ⇒ 原版那句 `power.Owner.IsEnemy` **成立**
            //   ⇒ 她给自己上负面时会被自己的血肉戏法打 9 点。
            //   既然这层能力的持有者是我们的（影子玩家），就**整条接管**：
            //     · 目标**是真人玩家**才打 9 点；
            //     · 她给自己 / 给别的怪上负面 ⇒ 什么都不做（压掉原版）。
            //   其它情况（真玩家自己的血肉戏法）仍然 return true 走原版，零回归。
            var victim = power?.Owner;
            if (victim == null) { __result = Task.CompletedTask; return false; }
            if (victim.Monster != null) { __result = Task.CompletedTask; return false; }   // 她自己/别的怪：不打
            // ★ 2026-11-10（用户：「她现在使用命运同担不会对玩家造成伤害。是不是…拦多了？」）：
            //   **拦多了** —— 原版判据是 `amount != 0`：「失去力量」这种负面是**负数**送进来的
            //   （`PowerCmd.Apply<StrengthPower>(ctx, target, -2, …)` ⇒ amount = -2），
            //   以前写 `amount <= 0` 正好把命运同担这一路挡掉了，于是那 9 点血肉戏法不触发。
            if (amount == 0m) { __result = Task.CompletedTask; return false; }

            PowerType kind;
            try { kind = power!.GetTypeForAmount(amount); } catch { return true; }
            if (kind != PowerType.Debuff) { __result = Task.CompletedTask; return false; }
            if (power is ITemporaryPower) { __result = Task.CompletedTask; return false; }

            var giver = power!.Applier ?? applier;
            if (!ReferenceEquals(giver, owner)) { __result = Task.CompletedTask; return false; }

            // ★ 2026-11-10 第二轮：**这里不再负责结算伤害**。
            //   用户实测「命运同担还是不对玩家造成伤害」时，这条链（原版把这层 power 也派发一遍）
            //   有一次整段没走到 ⇒ 9 点丢了。伤害已经挪进 `BossDevourLifePower`（她必然带着的特殊效果，
            //   同一次派发一定到）。这里只保留一件事：**把她身上这层 power 的原版回调整条压掉**，
            //   免得她给自己上负面（命运同担那一半）时被自己的血肉戏法打 9 点。
            __result = Task.CompletedTask;
            return false;
        }
        catch (Exception e)
        {
            try { Logger.Log("[CharBoss] 血肉戏法（Boss 版）压制失败，放行原版: " + e.Message); } catch { /* 忽略 */ }
            return true;
        }
    }

    private static string SafeName(MegaCrit.Sts2.Core.Entities.Creatures.Creature? creature)
    {
        try { return creature?.Name ?? "?"; } catch { return "?"; }
    }
}