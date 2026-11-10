using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// **群体减力量**（「尖啸」<c>PiercingWail</c>：所有敌人在本回合失去 N 点力量，升级 +2；
/// 以及同类「黑暗镣铐」那种 `TemporaryStrengthPower`）**不作用于 Boss 的召唤物**（用户的奥斯提）。
///
/// 用户 2026-10-23 要求：「让尖啸这种群体减力量不会影响奥斯提」。
/// 为什么要改：那些牌是「所有敌人」——而奥斯提是我们的 Boss 侧的随从，同样在敌人列表里，
/// 于是玩家一张尖啸把奥斯提的力量也减了，奥斯提的伤害（以及它的意图预览）就平白变低，
/// 而设计上奥斯提只该吃「它的主人」那套数值。
///
/// 做法：切 <c>PowerCmd.Apply(ctx, PowerModel, Creature, decimal, Creature, CardModel, bool)</c>
/// 这个**非泛型**重载（泛型 <c>Apply&lt;T&gt;</c> 最终都走它）：
/// 只要「施加的是减力量类的能力」+「目标是某只 CharacterBosses Boss 的随从」+「施加者是真人玩家」
/// ⇒ 直接跳过。
/// </summary>
[HarmonyPatch(typeof(PowerCmd), nameof(PowerCmd.Apply),
              new[] { typeof(PlayerChoiceContext), typeof(PowerModel), typeof(Creature), typeof(decimal),
                      typeof(Creature), typeof(CardModel), typeof(bool) })]
public static class BossPetNoStrengthLossPatch
{
    [HarmonyPrefix]
    public static bool Prefix(PowerModel power, Creature target, decimal amount, Creature applier,
                             ref Task __result)
    {
        try
        {
            if (!IsBossPet(target)) return true;

            // ★ 2026-11-10（用户：「奥斯提不应该会获得负面效果」——实测王储的**临时减力量**能落到她那只
            //   奥斯提身上）：以前只在「施加者是真人玩家」时才拦，于是**她自己/她的牌**给的负面
            //   （王储系的减力量、群体减益……施加者是 Boss 本人）照样落到了随从身上。
            //   现在改成两条独立判据，任一命中就跳过：
            //     ① **任何来源的负面效果**都不给随从上（`GetTypeForAmount` 判 Debuff，负数力量也算）；
            //     ② 施加者是**真人玩家**时，正面效果也不给（用户 2026-10-23 的原始要求：
            //        「奥斯提不受任何其它正面/负面效果影响」）。
            bool fromPlayer = applier != null && applier.Monster == null;
            bool debuff = false;
            try { if (power != null && amount != 0m) debuff = power.GetTypeForAmount(amount) == PowerType.Debuff; }
            catch { debuff = false; }

            if (!debuff && !fromPlayer) return true;      // 她自己给的正面效果 → 照常

            Logger.Log($"[CharBoss] Boss 随从免疫{(debuff ? "负面" : "外部")}效果：跳过 {SafeEntry(power)} " +
                       $"{amount:+0;-0} → {target.Name}（施加者 {(applier == null ? "null" : applier.Name)}）");
            __result = Task.CompletedTask;                  // ⚠ 返回 false 必须给 Task，否则 await null 崩
            return false;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>这是不是「减力量」类的能力（临时减力量 / 直接负力量）。</summary>
    private static bool IsStrengthLoss(PowerModel? power, decimal amount)
    {
        if (power == null) return false;
        if (power is TemporaryStrengthPower) return true;                 // 尖啸 / 黑暗镣铐
        if (power is StrengthPower && amount < 0) return true;            // 直接扣力量
        return false;
    }

    /// <summary>这个生物是不是「我们模组某只 Boss」的随从（奥斯提等）。</summary>
    private static bool IsBossPet(Creature? creature)
    {
        try
        {
            if (creature == null) return false;
            var owner = creature.PetOwner;
            if (owner == null) return false;
            return owner.Creature?.Monster is CharBossMonster;
        }
        catch { return false; }
    }

    private static string SafeEntry(AbstractModel? model)
    {
        try { return model?.Id.Entry ?? "?"; } catch { return "?"; }
    }
}
