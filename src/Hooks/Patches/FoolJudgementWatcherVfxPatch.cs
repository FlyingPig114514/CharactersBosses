#if STS2_113   // 愚者 / 塔罗牌 只在 0.113 存在
using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Cards;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// **审判**打出时借用观者 mod 的处决演出（用户 2026-11-14）：
/// > 「审判既是原版衍生牌塔罗牌，也是一张观者的稀有技能牌，除了塔罗牌版本费用为零且具有消耗外两张牌的效果相同，
/// >   如果装载了观者模组，就让这张审判也使用观者模组中审判的特效和动画」
///
/// 判据：这张牌是**我们 Boss** 打出的 `Judgement`（原版塔罗牌）。玩家自己打的那张不受影响。
/// 演出本体在 `Core.WatcherVfx.PlayJudgment`（反射调 `WatcherMod.WatcherAttackVfxHelper.PlayJudgment`），
/// 观者 mod 没装就什么都不做。
/// </summary>
[HarmonyPatch(typeof(Judgement), "OnPlay")]
public static class FoolJudgementWatcherVfxPatch
{
    [HarmonyPostfix]
    public static void Postfix(Judgement __instance, ref Task __result)
    {
        try
        {
            if (__instance?.Owner?.Creature?.Monster is not CharBossMonster) return;
        }
        catch { return; }

        __result = Chain(__result, __instance);
    }

    private static async Task Chain(Task inner, Judgement card)
    {
        try { await inner; } catch { /* 原版出错也照样播 */ }
        try
        {
            var caster = card.Owner?.Creature;
            var target = caster?.CombatState?.HittableEnemies is { Count: > 0 } list && list.Count > 0 ? list[0] : null;
            string title = string.Empty;
            try { title = card.Title ?? string.Empty; } catch { title = string.Empty; }
            Core.WatcherVfx.PlayJudgment(caster, target, title);
        }
        catch { /* 纯表现 */ }
    }
}
#endif