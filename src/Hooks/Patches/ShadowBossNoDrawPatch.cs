using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Monsters;
using CharacterBosses.Powers;

namespace CharacterBosses.Patches;

/// <summary>
/// **拦掉二层「灵魂骨」的原版抽牌**（用户 2026-10-23）：
/// 「目前我的意图里已经把抽牌提前算好了，再触发原版抽牌会乱。不过触发抽牌时仍然要增加自动化计数
///   和给死亡行军增伤，实现方法你可以去看看无色储君，我也是把他的印牌给拦了。目前就这一个 boss 这么改其它别动。」
///
/// 做法与「无色储君拦印牌」（<see cref="ShadowBossNoRandomCardPatch"/> 拦
/// <c>CardFactory.GetDistinctForCombat</c>）完全同源：**在真正的入口上一刀切掉**，
/// 但这次要「拦了还记账」——牌一张都不搬，计数照加。
///
/// 为什么必须拦：她的手牌是剧本每回合**重新造**的（`BossScriptRunner.BuildHand`），
/// 意图里的伤害/格挡也是按那份手牌提前算好的；真抽一张进来，手牌多一张、牌堆少一张，
/// 意图就和实际打出的东西对不上了。
///
/// 拦哪两个入口（反编译确认）：
///   · <c>CardPileCmd.DrawInternal(ctx, count, player, fromHandDraw)</c> ——
///     `Draw(ctx, player)` / `Draw(ctx, count, player, …)` 全都落到它；
///   · <c>CardPileCmd.DrawWithoutBlockingOnOtherPlayers(…)</c> —— 它自己另写了一遍搬牌 + 记历史的循环。
///
/// ⚠ 生效范围（两条）：
///   · **身上有「灵魂绑定」的 Boss**（二层灵魂骨）；
///   · **`BossDefinition.BlocksVanillaDraw` 打开的职业**（二层「物理机」：它的「超越光速」会抽牌，
///     而剧本已经把「抽上来的那张」写好了 —— 用户 2026-10-24：「你把原版的抽牌和野性的回手都拦了吧」）。
/// 其它职业、真人玩家、原版怪物一律不动。
/// </summary>
internal static class ShadowBossNoDraw
{
    /// <summary>这个 Player 是不是「要拦抽牌的那只 Boss」（灵魂绑定 / 定义里开了开关）。</summary>
    internal static bool ShouldBlock(Player? player, out CharBossMonster boss, out ICombatState combat)
    {
        boss = null!;
        combat = null!;
        try
        {
            if (player == null) return false;
            if (player.Creature?.Monster is not CharBossMonster b) return false;
            // 「战斗专注」这类**逐张牌**的拦截（DrawBlockEntry 由 CharBossMonster.PlayOne 在出牌期间置位）
            if (!string.IsNullOrEmpty(b.DrawBlockEntry))
            {
                var blockState = player.Creature!.CombatState;
                if (blockState == null) return false;
                boss = b;
                combat = blockState;
                return true;
            }

            bool soulBound = b.Creature?.GetPower<BossSoulBoundPower>() != null;
            if (!soulBound && b.Def?.BlocksVanillaDraw != true) return false;
            var state = player.Creature!.CombatState;
            if (state == null) return false;

            boss = b;
            combat = state;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>拦掉一次抽牌请求；灵魂骨那边还要把「她抽到了」记进账（自动化计数 / 死亡行军伤害）。</summary>
    internal static void BlockAndCount(CharBossMonster boss, ICombatState combat,
                                       PlayerChoiceContext ctx, bool fromHandDraw, decimal count)
    {
        int n;
        try { n = (int)Math.Round((double)count); }
        catch { return; }
        if (n <= 0) return;

        bool soulBound;
        try { soulBound = boss.Creature?.GetPower<BossSoulBoundPower>() != null; }
        catch { soulBound = false; }

        if (soulBound)
        {
            SoulBoundDrawPatch.CreditFakeDraw(boss, combat, ctx, fromHandDraw, n,
                                              $"拦掉她的原版抽牌 {n} 张（手牌由剧本给，牌堆不动）");
            // 意图里「本回合还会抽的牌」是提前补的 → 这里真的抽了，就要把预补的那份收回去
            boss.NoteHerOwnInTurnDraw(n);
            return;
        }

        // 剧本已经把「抽上来的牌」写好了（物理机的超越光速）⇒ 只拦、不记账
        Logger.Log($"[CharBoss] 拦掉她自己的原版抽牌 {n} 张（手牌由剧本给，牌堆不动）");
    }
}

/// <summary>入口①：<c>CardPileCmd.DrawInternal</c>（所有 <c>Draw(…)</c> 的最终实现）。</summary>
[HarmonyPatch]
public static class ShadowBossNoDrawInternalPatch
{
    /// <summary>新版（v0.111）的抽牌最终实现叫 `DrawInternal`；旧版没有 ⇒ 旧版走下面的 Legacy 那一支。</summary>
    [HarmonyPrepare]
    public static bool Prepare() => Core.GameCompat.HasDrawInternal;

    internal static System.Reflection.MethodBase? TargetMethod()
        => AccessTools.Method(typeof(CardPileCmd), "DrawInternal");

    [HarmonyPrefix]
    public static bool Prefix(PlayerChoiceContext choiceContext, decimal count, Player player,
                             bool fromHandDraw, ref Task<IEnumerable<CardModel>> __result)
    {
        if (!ShadowBossNoDraw.ShouldBlock(player, out var boss, out var combat)) return true;

        try
        {
            ShadowBossNoDraw.BlockAndCount(boss, combat, choiceContext, fromHandDraw, count);
            __result = Task.FromResult<IEnumerable<CardModel>>(Array.Empty<CardModel>());
            return false;   // 一张都不搬
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 灵魂绑定：拦抽牌失败（按原版放行）: " + e.Message);
            return true;
        }
    }
}

/// <summary>入口②：<c>CardPileCmd.DrawWithoutBlockingOnOtherPlayers</c>（自己另写了一遍搬牌循环）。</summary>

/// <summary>
/// **旧版（v0.107）那一支**：旧版没有 private 的 `DrawInternal`，抽牌入口直接是
/// <c>CardPileCmd.Draw(ctx, count, player, fromHandDraw)</c> 和 <c>Draw(ctx, player)</c>。
/// 新版上这两个方法还在（但只是转发到 `DrawInternal`），所以这里用 `!HasDrawInternal` 开关
/// 保证**两版各挂一支、不会重复拦**。
/// </summary>
[HarmonyPatch]
public static class ShadowBossNoDrawLegacyPatch
{
    [HarmonyPrepare]
    public static bool Prepare() => Core.GameCompat.HasLegacyDraw;

    internal static System.Reflection.MethodBase? TargetMethod()
        => AccessTools.Method(typeof(CardPileCmd), "Draw",
                               new[] { typeof(PlayerChoiceContext), typeof(decimal), typeof(Player), typeof(bool) });

    [HarmonyPrefix]
    public static bool Prefix(PlayerChoiceContext choiceContext, decimal count, Player player,
                             bool fromHandDraw, ref Task<IEnumerable<CardModel>> __result)
    {
        if (!ShadowBossNoDraw.ShouldBlock(player, out var boss, out var combat)) return true;

        try
        {
            ShadowBossNoDraw.BlockAndCount(boss, combat, choiceContext, fromHandDraw, count);
            __result = Task.FromResult<IEnumerable<CardModel>>(Array.Empty<CardModel>());
            return false;   // 一张都不搬
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 灵魂绑定：拦抽牌失败（按原版放行）: " + e.Message);
            return true;
        }
    }
}

/// <summary>旧版那一支的单抽入口：<c>CardPileCmd.Draw(ctx, player)</c>（返回抽到的那张，拦就返回 null）。</summary>
[HarmonyPatch]
public static class ShadowBossNoDrawLegacySinglePatch
{
    [HarmonyPrepare]
    public static bool Prepare() => Core.GameCompat.HasLegacyDraw;

    internal static System.Reflection.MethodBase? TargetMethod()
        => AccessTools.Method(typeof(CardPileCmd), "Draw",
                               new[] { typeof(PlayerChoiceContext), typeof(Player) });

    [HarmonyPrefix]
    public static bool Prefix(PlayerChoiceContext choiceContext, Player player, ref Task<CardModel> __result)
    {
        if (!ShadowBossNoDraw.ShouldBlock(player, out var boss, out var combat)) return true;

        try
        {
            ShadowBossNoDraw.BlockAndCount(boss, combat, choiceContext, false, 1m);
            __result = Task.FromResult<CardModel>(null!);
            return false;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 灵魂绑定：拦单抽失败（按原版放行）: " + e.Message);
            return true;
        }
    }
}