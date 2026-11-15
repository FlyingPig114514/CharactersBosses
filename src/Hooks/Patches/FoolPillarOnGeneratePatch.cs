#if STS2_111   // 愚者 / 塔罗牌 只在 0.113 存在（用户 2026-11-14：107 不加这个 Boss）
using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// 创世之柱在「印牌」那一刻触发（用户 2026-11-14 订正）。
/// 时机：恋人印 10 张 / 新生之喜印 Cards 张 / 教皇印 3 张（恐惧升级 4）；月亮原版就会触发，不补。
/// 补法：PillarOfCreationPower.AfterCardGeneratedForCombat(card, creator) 判据是 creator == 我。
/// </summary>
internal static class FoolPillarPrints
{
    internal static async Task FireAsync(CardModel? sourceCard, int count, string reason)
    {
        if (count <= 0) return;
        try
        {
            if (sourceCard?.Owner?.Creature?.Monster is not CharBossMonster boss) return;
            var shadow = boss.Shadow;
            var pillar = boss.Creature?.GetPower<PillarOfCreationPower>();
            if (shadow == null || pillar == null) return;

            for (int i = 0; i < count; i++)
            {
                try { await pillar.AfterCardGeneratedForCombat(sourceCard, shadow); }
                catch (Exception e) { Logger.Log("[CharBoss] 创世之柱（" + reason + "）结算失败: " + e.Message); return; }
            }
            Logger.Log($"[CharBoss] 创世之柱：{reason} 印了 {count} 张 => 触发 {count} 次");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 创世之柱（" + reason + "）失败: " + e.Message);
        }
    }

    internal static int PrintedCount(CardModel? card, int fallback)
    {
        try
        {
            if (card == null) return fallback;
            int n = (int)Math.Round((double)card.DynamicVars.Cards.BaseValue);
            return n > 0 ? n : fallback;
        }
        catch { return fallback; }
    }
}

[HarmonyPatch(typeof(TheLovers), "OnPlay")]
public static class FoolPillarLoversPatch
{
    [HarmonyPostfix]
    public static void Postfix(TheLovers __instance, ref Task __result)
        => __result = Chain(__result, __instance);

    private static async Task Chain(Task inner, TheLovers card)
    {
        try { await inner; } catch { }
        await FoolPillarPrints.FireAsync(card, 10, "恋人");
    }
}

[HarmonyPatch(typeof(BundleOfJoy), "OnPlay")]
public static class FoolPillarBundleOfJoyPatch
{
    [HarmonyPostfix]
    public static void Postfix(BundleOfJoy __instance, ref Task __result)
        => __result = Chain(__result, __instance);

    private static async Task Chain(Task inner, BundleOfJoy card)
    {
        int n = FoolPillarPrints.PrintedCount(card, 3);
        try { await inner; } catch { }
        await FoolPillarPrints.FireAsync(card, n, "新生之喜");
    }
}

[HarmonyPatch(typeof(TheHierophant), "OnPlay")]
public static class FoolPillarHierophantPatch
{
    [HarmonyPostfix]
    public static void Postfix(TheHierophant __instance, ref Task __result)
        => __result = Chain(__result, __instance);

    private static async Task Chain(Task inner, TheHierophant card)
    {
        int n = FoolPillarPrints.PrintedCount(card, 3);
        try { await inner; } catch { }
        await FoolPillarPrints.FireAsync(card, n, "教皇");
        DiscountPrintedCards(card);
    }

    /// <summary>
    /// **教皇印出来的牌本回合耗能 -1**（用户 2026-11-14：「教皇印的滚石、永恒铠甲、震荡波
    /// （恐惧模式还有一张非凡技艺）要 -1 费」）。
    ///
    /// 原版 `TheHierophant.OnPlay` 是对**它自己生成的那几张**逐张
    /// `current.EnergyCost.AddThisTurnOrUntilPlayed(-1)`；而在我们的世界里「教皇印出来的牌」
    /// 是剧本紧接着打出的 滚石 / 永恒铠甲 / 震荡波（恐惧模式再加一张「非凡技艺」）——
    /// 它们是剧本造的新卡实例，原版那条减费当然没落到它们身上 ⇒ 这里自己补。
    /// </summary>
    private static void DiscountPrintedCards(TheHierophant card)
    {
        try
        {
            var owner = card?.Owner;
            var hand = owner?.Creature?.CombatState == null ? null : owner.PlayerCombatState?.Hand;
            if (hand == null) return;

            int touched = 0;
            foreach (var c in hand.Cards)
            {
                string entry = string.Empty;
                try { entry = c?.Id.Entry ?? string.Empty; } catch { continue; }
                if (entry != "ROLLING_BOULDER" && entry != "ETERNAL_ARMOR" && entry != "SHOCKWAVE" && entry != "PROWESS") continue;
                try
                {
                    c!.EnergyCost.AddThisTurnOrUntilPlayed(-1);
                    touched++;
                    Logger.Log($"[CharBoss] 教皇：{entry} 本回合耗能 -1");
                }
                catch (Exception e) { Logger.Log("[CharBoss] 教皇减费失败: " + e.Message); }
            }
            if (touched == 0) Logger.Log("[CharBoss] 教皇：手牌里没找到滚石/永恒铠甲/震荡波/非凡技艺（减费没落地）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 教皇减费出错: " + e.Message);
        }
    }
}
#endif