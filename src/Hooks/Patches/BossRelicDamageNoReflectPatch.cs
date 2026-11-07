using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// **遗物/被动造成的伤害不触发玩家的「荆棘」反伤**（用户 2026-10-24：
/// 「愤怒战的卡戎之灰会触发玩家的荆棘反伤」）。
///
/// 反编译 `ThornsPower.BeforeDamageReceived`：
/// <code>
/// if (target == Owner &amp;&amp; dealer != null &amp;&amp; (props.IsPoweredAttack() || cardSource is Omnislice))
///     CreatureCmd.Damage(…, dealer, Amount, Unpowered | SkipHurtAnim, Owner, …);
/// </code>
/// 判据是「**可加成的攻击**」（`props.IsPoweredAttack()` = 带 Move 且不带 Unpowered）。
///   卡戎之灰我们本来是照抄原版、用 `ValueProp.Unpowered` 打的（`BossRelicHookBridge.RunCharonsAshesAsync`），
///   按这条判据本来就不该反弹；但实测弹了 ⇒ 这里加一道**只针对我们 Boss 的遗物伤害**的保险：
///   当「伤害来源不是任何卡牌（`cardSource == null`）」而 `dealer` 是我们的一只 Boss 时，直接跳过反伤。
///
/// 为什么这条保险是安全的：
///   · 她打出的**攻击牌**走 `DamageCmd…FromCard(...)`，`cardSource` 非空 ⇒ 荆棘照常反弹（原版行为）；
///   · 只有遗物/被动（卡戎之灰这类）与「无牌伤害」被排除 —— 那本来就不该被反弹。
/// </summary>
[HarmonyPatch(typeof(ThornsPower), nameof(ThornsPower.BeforeDamageReceived))]
public static class BossRelicDamageNoReflectPatch
{
    private static int _logs;

    [HarmonyPrefix]
    public static bool Prefix(Creature? dealer, CardModel? cardSource, ref Task __result)
    {
        try
        {
            if (cardSource != null) return true;                       // 卡牌造成的伤害：原版说了算
            if (dealer?.Monster is not CharBossMonster) return true;   // 不是我们的 Boss：不插手

            if (_logs++ < 20)
            {
                Logger.Log($"[CharBoss] {dealer.Name} 的遗物/被动伤害不触发荆棘反伤（无卡牌来源）");
            }
            __result = Task.CompletedTask;                             // ⚠ Task 方法必须给结果
            return false;
        }
        catch
        {
            return true;
        }
    }
}
