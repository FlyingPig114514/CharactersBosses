using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Cards;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// 让**我们的 Boss** 能正常打出「眼部攻击」（`GoForTheEyes`）。
///
/// 原版 `OnPlay`（反编译，逐字）：
/// <code>
/// await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this, cardPlay)
///         .Targeting(cardPlay.Target).WithHitFx("vfx/vfx_attack_blunt", null, "blunt_attack.mp3")
///         .Execute(choiceContext);
/// if (cardPlay.Target.Monster.IntendsToAttack)          // ← 目标没有 Monster 时这里 NRE
///     await PowerCmd.Apply&lt;WeakPower&gt;(...);
/// </code>
/// 玩家的攻击目标是**玩家**（`Target.Monster == null`）⇒ 那一句直接抛 NullReference，
/// 整个 `OnPlay` 的 Task 变成 faulted：伤害其实已经打出去了（我们日志里也有 `done GO_FOR_THE_EYES`），
/// 但原版的**出牌动画链**等不到正常完成 ⇒ **牌停在半空**（用户 2026-10-26 / 10-27 两次实测）。
///
/// 修法：对**我们的 Boss** 接管 `OnPlay` —— 只复刻「伤害」这一半，
/// 「要不要给虚弱」交给 `Powers/GoForTheEyesAimPower` 的判定，
/// 并由 `CharBossMonster.PlayOne` 在牌**结算完之后**补（不在钩子里 await，见那里的注释）。
/// 真人玩家打出这张牌完全走原版。
/// </summary>
// `OnPlay` 是 protected ⇒ 只能用字符串名挂（Harmony 按名字找方法）
[HarmonyPatch(typeof(GoForTheEyes), "OnPlay")]
public static class GoForTheEyesBossPatch
{
    [HarmonyPrefix]
    public static bool Prefix(GoForTheEyes __instance, PlayerChoiceContext choiceContext, CardPlay cardPlay,
                              ref Task __result)
    {
        try
        {
            if (__instance?.Owner?.Creature?.Monster is not CharBossMonster) return true;   // 真人玩家 → 原版

            __result = PlayForBossAsync(__instance, choiceContext, cardPlay);
            return false;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 眼部攻击（Boss 版）接管失败，退回原版: " + e.Message);
            return true;
        }
    }

    /// <summary>只做「造成伤害」这一半（原版的虚弱判定对玩家目标无意义，绝对不要调用）。</summary>
    private static async Task PlayForBossAsync(GoForTheEyes card, PlayerChoiceContext ctx, CardPlay cardPlay)
    {
        var target = cardPlay?.Target;
        if (target == null || !target.IsAlive)
        {
            Logger.Log("[CharBoss] 眼部攻击（Boss 版）：目标已失效 → 只结算空过");
            return;
        }

        try
        {
            await DamageCmd.Attack(card.DynamicVars.Damage.BaseValue)
#if STS2_107
                .FromCard(card)
#else
                .FromCard(card, cardPlay)
#endif
                .Targeting(target)
                .WithHitFx("vfx/vfx_attack_blunt", null, "blunt_attack.mp3")
                .Execute(ctx);
            Logger.Log($"[CharBoss] 眼部攻击（Boss 版）：对 {target.Name} 造成 {card.DynamicVars.Damage.BaseValue} 点伤害" +
                       "（虚弱由「眼部攻击」buff 决定，见 PlayOne）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 眼部攻击（Boss 版）伤害失败: " + e);
        }
    }
}
