#if STS2_113   // ⚠ 「愚者 / 塔罗牌」是 0.113.0 才有的 ⇒ 用户 2026-11-14：107 版本不加这个 Boss
using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// **恐惧模式下「新生之喜」和「教皇」多印 1 张「非凡技艺」**（用户 2026-11-14，见
/// <c>docs/plans/boss-fool-tarot.md</c> §3.8）：
///
/// > 「新生之喜 与 教皇 额外多印一张 **非凡技艺**」
///
/// 这两张牌原版都是「把随机无色牌加入你的手牌」：
///   · `BundleOfJoy`（新生之喜）= `Cards` 张随机无色牌进手牌；
///   · `TheHierophant`（教皇）= 3 张随机无色牌进手牌，且这些牌本回合耗能 -1。
/// 恐惧模式额外多给的那一张是**固定牌**「非凡技艺」（`Prowess`），不是随机的 ⇒
/// 用**后置补一张**（`OnPlay` 返回的 Task 之后再追加），不去动原版那套随机逻辑。
///
/// ⚠ 只在「我们自己的 Boss + 恐惧模式」生效；玩家的愚者在恐惧模式里打出这两张牌完全不受影响。
/// </summary>
internal static class FoolDreadExtraProwess
{
    internal static bool Applies(CardModel? card)
    {
        try
        {
            if (card?.Owner?.Creature?.Monster is not CharBossMonster) return false;
            return Core.DreadMode.IsActive;
        }
        catch { return false; }
    }

    /// <summary>等原版结算完，再往手牌里补 1 张「非凡技艺」。</summary>
    internal static async Task After(Task inner, CardModel card)
    {
        try { await inner; }
        catch (Exception e) { Logger.Log("[CharBoss] 恐惧加印：原版结算出错（继续补印）: " + e.Message); }

        try
        {
            var owner = card.Owner;
            var combat = owner?.Creature?.CombatState;
            if (owner == null || combat == null) return;

            var id = ModelDb.GetId(typeof(Prowess));
            var canonical = id == null ? null : ModelDb.GetById<CardModel>(id);
            if (canonical == null)
            {
                Logger.Log("[CharBoss] 恐惧加印：取不到「非凡技艺」的卡模型");
                return;
            }

            var copy = combat.CreateCard(canonical, owner);
            if (copy == null) return;
            await CardPileCmd.AddGeneratedCardToCombat(copy, PileType.Hand, owner);
            Logger.Log($"[CharBoss] 恐惧模式：{card.Id.Entry} 额外印 1 张「非凡技艺」进手牌");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 恐惧加印「非凡技艺」失败: " + e.Message);
        }
    }
}

/// <summary>新生之喜（`BundleOfJoy`）：恐惧模式下多印 1 张「非凡技艺」。</summary>
[HarmonyPatch(typeof(BundleOfJoy), "OnPlay")]
public static class FoolDreadBundleOfJoyPatch
{
    [HarmonyPostfix]
    public static void Postfix(BundleOfJoy __instance, ref Task __result)
    {
        if (!FoolDreadExtraProwess.Applies(__instance)) return;
        __result = FoolDreadExtraProwess.After(__result, __instance);
    }
}

/// <summary>教皇（`TheHierophant`）：恐惧模式下多印 1 张「非凡技艺」。</summary>
[HarmonyPatch(typeof(TheHierophant), "OnPlay")]
public static class FoolDreadHierophantPatch
{
    [HarmonyPostfix]
    public static void Postfix(TheHierophant __instance, ref Task __result)
    {
        if (!FoolDreadExtraProwess.Applies(__instance)) return;
        __result = FoolDreadExtraProwess.After(__result, __instance);
    }
}
#endif
