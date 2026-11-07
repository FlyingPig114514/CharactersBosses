using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Patches;

/// <summary>
/// 「麻痹」（<see cref="Powers.ParalysisPower"/>）的减伤挂点（用户 2026-11-05 双版本改造）。
///
/// 为什么不写在 power 里当 `override`：`ModifyDamageMultiplicative` 的签名在两版之间变了
/// <code>
/// v0.107: decimal ModifyDamageMultiplicative(Creature target, decimal amount, ValueProp props,
///                                             Creature dealer, CardModel cardSource)
/// v0.111: … 同上 + CardPlay cardPlay          // ← 末尾多一个参数
/// </code>
/// 一个 `override` 不可能同时对上两版（旧版会报「没有找到适合的方法来重写」）。
/// 所以改挂在**声明它的基类** `AbstractModel` 上，用 `object[] __args` 按**位置**取
/// —— 两版的位置完全一致：`[0]=target [1]=amount [2]=props [3]=dealer [4]=cardSource [5]=cardPlay(仅新版)`。
///
/// `ParalysisPower` 不再覆写 ⇒ 调用会落到基类的默认实现（返回 1）⇒ 本后置补丁改这个返回值。
/// 代价是每次伤害结算都会被叫一次，所以第一行就按 `__instance` 早退。
/// </summary>
[HarmonyPatch(typeof(AbstractModel), "ModifyDamageMultiplicative")]
public static class ParalysisDamagePatch
{
    [HarmonyPostfix]
    public static void Postfix(object __instance, object[] __args, ref decimal __result)
    {
        try
        {
            if (__instance is not Powers.ParalysisPower paralysis) return;
            if (__args == null || __args.Length < 5) return;

            var dealer = __args[3] as Creature;
            var cardSource = __args[4] as CardModel;
            decimal m = paralysis.MultiplierFor(dealer, cardSource);
            if (m == 1m) return;
            __result *= m;
        }
        catch { /* 任何异常都不影响原伤害 */ }
    }
}
