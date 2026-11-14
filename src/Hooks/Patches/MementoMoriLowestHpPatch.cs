#if STS2_111
using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Cards;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// **「铭记死亡」改打「生命值最少的玩家」**（用户 2026-11-13，针对 0.113.0 的卡牌重做）。
///
/// 0.113.0 把这张牌重做成：3 费、**奇巧**、`TargetType.RandomEnemy`，
/// 「对**生命值最低的敌人**造成 19(23) 点伤害」，反编译实现是：
///
/// <code>
/// protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
/// {
///     IReadOnlyList&lt;Creature&gt; hittableEnemies = base.CombatState.HittableEnemies;
///     if (hittableEnemies.Count != 0)
///     {
///         Creature target = hittableEnemies.MinBy((Creature c) =&gt; c.CurrentHp);
///         await DamageCmd.Attack(base.DynamicVars.Damage.IntValue).FromCard(this, cardPlay)
///             .Targeting(target).WithHitFx("vfx/vfx_attack_slash").Execute(choiceContext);
///     }
/// }
/// </code>
///
/// 问题：`HittableEnemies` 是**从卡牌拥有者（她的影子玩家）视角**数的，会把玩家侧的召唤物
/// （奥斯提、「为你而死」的随从…）一起算进去 ⇒ 可能打到一个生命值更低的随从身上，
/// 而不是玩家。用户要求：**只数玩家**，忽略召唤物。
///
/// 做法：只对**我们自己的 Boss** 接管这一个方法（前缀返回 false + 自己走同一条原版管线
/// `DamageCmd.Attack(...).FromCard(...).Targeting(...)`，只是目标换成
/// <see cref="Core.BossLockOn.LowestHpRealPlayer"/>）—— 玩家/原版怪打这张牌完全不受影响，
/// 动画、伤害管道、命中特效仍然是原版那一套。
/// </summary>
[HarmonyPatch(typeof(MementoMori), "OnPlay")]
internal static class MementoMoriLowestHpPatch
{
    private static int _logs;

    [HarmonyPrefix]
    private static bool Prefix(MementoMori __instance, PlayerChoiceContext choiceContext,
                               CardPlay cardPlay, ref Task __result)
    {
        try
        {
            if (__instance == null) return true;
            // 只拦「她的影子玩家」打出的那张；玩家自己 / 其它来源不接管
            if (__instance.Owner?.Creature?.Monster is not CharBossMonster) return true;

            var combat = __instance.CombatState;
            var target = Core.BossLockOn.LowestHpRealPlayer(combat)?.Creature;
            if (target == null) return true;      // 找不到玩家 ⇒ 交回原版处理

            __result = PlayAtLowestHpPlayer(__instance, choiceContext, cardPlay, target);
            return false;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 铭记死亡改打最低血玩家失败（交回原版）: " + e.Message);
            return true;
        }
    }

    private static async Task PlayAtLowestHpPlayer(MementoMori card, PlayerChoiceContext choiceContext,
                                                  CardPlay cardPlay, Creature target)
    {
        try
        {
            if (_logs++ < 20)
            {
                Logger.Log($"[CharBoss] 铭记死亡：改为攻击生命值最低的玩家 {target.Name}（{target.CurrentHp}）" +
                           "（忽略召唤物）");
            }

            await DamageCmd.Attack(card.DynamicVars.Damage.IntValue)
                .FromCard(card, cardPlay)
                .Targeting(target)
                .WithHitFx("vfx/vfx_attack_slash")
                .Execute(choiceContext);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 铭记死亡（最低血玩家版）结算失败: " + e.Message);
        }
    }
}
#endif
