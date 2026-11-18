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
/// **随从（奥斯提）不吃任何负面效果**。
///
/// 用户 2026-10-23：「让尖啸这种群体减力量不会影响奥斯提」——
/// 那些牌是「所有敌人」，而奥斯提是 Boss 侧随从、同样在敌人列表里，
/// 玩家一张尖啸就把奥斯提的力量也减了，它的伤害（以及意图预览）就平白变低。
///
/// 用户 2026-11-19（本轮）：「**奥斯提不应该被施加任何减益**」——
/// 实测：带「储君的支配」（王之凝视）的普通小怪攻击亡灵契约师时，
/// 奥斯提替主人挨了这一下 ⇒ `MonarchsGazeStrengthDownPower`（`TemporaryStrengthPower`，`IsPositive=false`
/// ⇒ `Type == Debuff`）落到了**玩家自己那只**奥斯提身上。
/// 以前这里只认 `IsBossPet`（我们 Boss 那侧的随从）⇒ 玩家那只完全没被管到。
/// 现在两只都管（判据复用 `Core.BossOsty.IsAnyOsty`，与「奥斯提不吃毒」同一套）：
///   · **Boss 侧随从**：任何来源的负面都不给；施加者是真人玩家时连正面也不给（2026-10-23 口径）。
///   · **玩家的奥斯提**：**只拦减益**（正面效果照原版），避免扩大改动面。
///
/// 做法：切 <c>PowerCmd.Apply(ctx, PowerModel, Creature, decimal, Creature, CardModel, bool)</c>
/// 这个**非泛型**重载（泛型 <c>Apply&lt;T&gt;</c> 最终都走它）。
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
            bool bossPet = IsBossPet(target);

            // ★ v0.2.790（用户 2026-11-19）：「奥斯提不应该被施加任何减益」——
            //   以前这里只管**我们 Boss 那边**的随从（`IsBossPet`），于是玩家自己那只奥斯提
            //   照样吃到「王之凝视」这类减益（实测：带储君的支配的小怪打亡灵契约师，
            //   奥斯提替主人挨这一下 ⇒ `MonarchsGazeStrengthDownPower` 落到奥斯提身上）。
            //   现在**两只奥斯提都免疫减益**（判据复用 `BossOsty.IsAnyOsty`，与「奥斯提不吃毒」同一个）。
            bool anyOsty = !bossPet && Core.BossOsty.IsAnyOsty(target);
            if (!bossPet && !anyOsty) return true;

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
            bool harmful = debuff || amount < 0m;         // 负数力量（直接扣力量）也算负面

            if (bossPet)
            {
                if (!harmful && !fromPlayer) return true;  // 她自己给的正面效果 → 照常
            }
            else
            {
                // 玩家自己那只奥斯提：**只**拦减益，正面效果照原版走（不扩大改动面）
                if (!harmful) return true;
            }

            Logger.Log($"[CharBoss] {(bossPet ? "Boss 随从" : "玩家的奥斯提")}免疫" +
                       $"{(harmful ? "负面" : "外部")}效果：跳过 {SafeEntry(power)} " +
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
