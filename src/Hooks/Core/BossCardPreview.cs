using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Monsters;

namespace CharacterBosses.Core;

/// <summary>
/// 用**原版的卡牌预览界面**把几张牌亮给玩家看 —— 就是「能量汲取」那一套
/// （<c>CardCmd.Preview</c> 居中并排显示，配 <see cref="Patches.SuppressPreviewFlyPatch"/> 掐掉飞行动画）。
///
/// 为什么必须用原版这套（用户 2026-11-03）：「你这个羽化的塞牌展示界面怎么还是你自己搞的，
/// 不应该是用原版的吗？目前能量汲取用的就是原版的」。
/// 之前羽化用的是自绘的 <c>UI/BossCardShowcase</c>（在立绘上方摆一排）—— 观感和原版不一致，已弃用。
///
/// 原版这条路线的约束（踩过的坑，见 <c>CharBossMonster.PlayOne</c> 里能量汲取那段注释）：
///   · `CardCmd.Preview` 要求 `card.Pile != null` ⇒ 副本必须先**静默**塞进本地真人玩家的某个牌堆；
///   · 副本必须归**本地真人玩家**（`CombatState.CreateCard(canonical, local)`），
///     因为 `PreviewInternal` 的飞行目标是「牌的拥有者」；
///   · 飞行动画被 `SuppressPreviewFlyPatch` 掐掉 ⇒ 展示完牌原地消失，不会飞进玩家弃牌堆；
///   · 展示完把副本 `RemoveFromCombat` 摘掉 ⇒ **玩家自己的牌堆一件不多**（用户要求）。
/// </summary>
internal static class BossCardPreview
{
    /// <summary>
    /// 把 <paramref name="cards"/> 的**副本**按原版预览界面并排显示 <paramref name="seconds"/> 秒。
    /// <paramref name="makeFree"/> = 展示时把副本的费用显示成 0（羽化印的牌就是 0 费）。
    /// </summary>
    public static async Task ShowAsync(CharBossMonster boss, IReadOnlyList<CardModel> cards,
                                       float seconds = 1.8f, bool makeFree = false)
    {
        if (boss == null || cards == null || cards.Count == 0) return;

        Player? local = null;
        try
        {
            local = LocalRealPlayer(boss);
        }
        catch (Exception e) { Logger.Log("[CharBoss] 预览：找本地玩家失败: " + e.Message); }
        if (local == null)
        {
            Logger.Log("[CharBoss] 预览：取不到本地真人玩家 ⇒ 无法展示（跳过）");
            return;
        }

        var pcsLocal = local.PlayerCombatState;
        var combat = local.Creature?.CombatState;
        if (pcsLocal == null || combat == null) return;

        var copies = new List<CardModel>();
        foreach (var shown in cards)
        {
            try
            {
                var canonical = ModelDb.GetById<CardModel>(shown.Id);
                var copy = combat.CreateCard(canonical, local);
                if (copy == null) continue;

                // 展示的是「现在这张牌的样子」：升级过就显示升级后的
                try { if (shown.IsUpgraded && !copy.IsUpgraded) copy.UpgradeInternal(); } catch { /* 忽略 */ }
                // 羽化印出来的牌本场免费 ⇒ 预览也按 0 费显示
                if (makeFree)
                {
                    try
                    {
                        int now = copy.EnergyCost.GetWithModifiers(
                            MegaCrit.Sts2.Core.Entities.Cards.CostModifiers.All);
                        if (now > 0) copy.EnergyCost.AddThisCombat(-now, true);
                    }
                    catch { /* 忽略 */ }
                }

                pcsLocal.DiscardPile.AddInternal(copy, -1, true);       // 静默进堆（只为过原版的 Pile 检查）
                Patches.SuppressPreviewFlyPatch.Suppress(copy);          // 掐掉飞行动画
                copies.Add(copy);
            }
            catch (Exception e)
            {
                Logger.Log("[CharBoss] 预览：造展示副本失败: " + e.Message);
            }
        }
        if (copies.Count == 0) return;

        try
        {
            CardCmd.Preview(copies, 1.4f, MegaCrit.Sts2.Core.Nodes.CommonUi.CardPreviewStyle.HorizontalLayout);
            Logger.Log($"[CharBoss] 预览：用原版界面并排展示 {copies.Count} 张牌");
            await Cmd.Wait(Math.Max(0.6f, seconds));
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 预览：展示失败: " + e.Message);
        }

        // 收尾：副本不留在玩家牌堆里（用户要求）
        foreach (var copy in copies)
        {
            try
            {
                Patches.SuppressPreviewFlyPatch.Release(copy);
                await CardPileCmd.RemoveFromCombat(copy, true);
            }
            catch (Exception e) { Logger.Log("[CharBoss] 预览：摘掉展示副本失败: " + e.Message); }
        }
    }

    /// <summary>本地真人玩家（单人就是那一位；多人取第一位 —— 只用来「展示」）。</summary>
    private static Player? LocalRealPlayer(CharBossMonster boss)
    {
        try
        {
            var combat = boss.CombatState;
            if (combat == null) return null;
            foreach (var creature in combat.PlayerCreatures)
            {
                var player = creature?.Player;
                if (player == null || creature!.Monster != null) continue;
                return player;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }
}
