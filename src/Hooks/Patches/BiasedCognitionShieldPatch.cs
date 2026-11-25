using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Powers;

namespace CharacterBosses.Patches;

/// <summary>
/// 能力机（`DEFECT_BOSS` 第三幕）**普通模式**的新特殊效果：
/// 「如果你在一回合打出一张能力牌，故障机器人当回合的偏差认知不会生效。」
///
/// ## 反编译依据（原版 `BiasedCognitionPower`）
/// <code>
/// public override async Task AfterSideTurnStart(CombatSide side, IReadOnlyList&lt;Creature&gt; participants,
///                                               ICombatState combatState)
/// {
///     if (participants.Contains(base.Owner))
///     {
///         Flash();
///         await PowerCmd.Apply&lt;FocusPower&gt;(new ThrowingPlayerChoiceContext(), base.Owner, -base.Amount, base.Owner, null);
///     }
/// }
/// </code>
/// 也就是「**持有者自己那一侧回合开始**时掉 <c>Amount</c> 点集中」。她（能力机）身上那层是
/// 剧本里她自己打的「偏差认知」卡给的，掉集中时力量会跟着镜像掉（见 `DeviationPower.Mirror`）。
///
/// ## 做法
/// 前缀拦这一个方法：如果
///   · 不是恐惧模式（恐惧模式沿用原版：每回合照掉），
///   · 持有者是**我们**的 Boss，且身上挂着 <see cref="DeviationPower"/>（= 能力机），
///   · 玩家这一回合打出过能力牌（<see cref="DeviationPower.TryConsumeBiasShield"/> 返回 true），
/// 就**整段跳过**原版（这一次不掉集中、不闪），并消费掉那个标记。
///
/// 伤害预览侧同步：`BossIntentBuilder.PendingDrop` 在标记挂着时返回 0，
/// 于是意图/薄荷尖塔不再显示「被那一笔衰减削过」的数字。
/// </summary>
[HarmonyPatch(typeof(BiasedCognitionPower), nameof(BiasedCognitionPower.AfterSideTurnStart))]
internal static class BiasedCognitionShieldPatch
{
    [HarmonyPrefix]
    private static bool Prefix(BiasedCognitionPower __instance, CombatSide side,
                               IReadOnlyList<Creature> participants, ref Task __result)
    {
        try
        {
            var owner = __instance?.Owner;
            if (owner == null) return true;
            if (Core.DreadMode.IsActive) return true;              // 恐惧模式没有这个机制

            var dev = owner.GetPower<DeviationPower>();
            if (dev == null) return true;                          // 不是能力机
            if (!dev.TryConsumeBiasShield()) return true;           // 本回合玩家没打能力牌

            Logger.Log("[CharBoss] 偏差认知（普通模式）：你本回合打出过能力牌 ⇒ " +
                       "她这一次的「偏差认知」不生效（不扣集中、力量也不掉）");
            __result = Task.CompletedTask;
            return false;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 偏差认知屏蔽判断失败（按原版继续）: " + e.Message);
            return true;
        }
    }
}
