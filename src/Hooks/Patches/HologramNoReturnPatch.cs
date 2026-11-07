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
/// 「**全息影像**」（<c>Hologram</c>）对**事件机**：**拦掉它把牌从弃牌堆捞回手牌**，
/// 但**照样让「一心化万」记 +1 力量**。
///
/// 用户 2026-11-04：
/// > 「拦掉全息影像的返手，因为我已经算好它要捞的牌了，并且不影响它触发『一心化万』。」
///
/// 反编译 `Hologram.&lt;OnPlay&gt;d__7` 的顺序是：
/// <code>
/// ① await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);   // 3 格挡（升级 +2）
/// ② var picked = await CardSelectCmd.FromCombatPile(… PileType.Discard …, count: 1);
/// ③ await CardPileCmd.Add(picked, PileType.Hand);                              // ← 只拦这一步
/// </code>
/// 所以**不能整条 `OnPlay` 跳过**（那样连 3 点格挡都没了）。这里只拦第 ③ 步：
/// 在她打出全息影像的那一小段时间窗口里，把「弃牌堆 → 手牌」的搬运跳过。
///
/// ⚠ 为什么必须保留「一心化万」：那条特殊效果是「每有一张牌从弃牌堆回到手牌，所有单位 +1 力量」。
///   牌虽然不搬了（剧本早就把它摆在手上），但**效果上算返手了一张** ⇒ 这里在跳过搬运的同时
///   自己补记 +1（写进 `PendingReturnStrength`，真正的加力量仍在 `PlayOne` 的同一个 await 链里结算，
///   不能在这里另起异步链 —— 那是 v0.2.346「她的回合卡死」的教训）。
///
/// ⚠ 只对**带头「一心化万」那层 power 的 Boss**生效。三层还有另一套「能力机」（`DefectScript`，
///   它的第 2 回合也有全息影像），那一套**没有**这个特殊效果 ⇒ 原版行为完全不变。
///
/// ⚠ 跳过 async 方法必须给 `ref Task __result` 赋 `Task.CompletedTask`（否则下游 `GetAwaiter()` 抛 NRE，
///   见 `EmotionChipBossTakeoverPatch` 的注释）。这里跳的是**非 async 的** `CardPileCmd.Add` 的同步重载，
///   它返回 `Task<CardPileAddResult>` ⇒ 用 `__result` 给一个已完成的任务。
/// </summary>
// ⚠ `Hologram.OnPlay` 是 **protected**，不能用 `nameof`（编译期不可访问）⇒ 写死字符串 + 钉死参数类型
//   （CardModel 上还有别的 OnPlay 重载，不钉类型会 AmbiguousMatchException，整个模组的补丁都挂不上）。
[HarmonyPatch(typeof(Hologram), "OnPlay",
              new[] { typeof(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext),
                      typeof(CardPlay) })]
public static class HologramReturnWindowPatch
{
    /// <summary>窗口只对**这只 Boss** 开（多只 Boss / 多人局也不会串到别人身上）。</summary>
    internal static CharBossMonster? SuppressFor;

    [HarmonyPrefix]
    public static void Prefix(Hologram __instance)
    {
        try
        {
            SuppressFor = null;
            if (__instance?.Owner?.Creature?.Monster is not CharBossMonster boss) return;
            // ★ v0.2.412（用户：「拦掉全息影像的捞牌（我已经提前写好了）」）：
            //   以前这里只放行**带「一心化万」**的 boss（三层事件机），「状态机」没有那层 power
            //   ⇒ 它的全息影像还在走原版「从弃牌堆挑 1 张」，而剧本早就把要捞的那张（散射炮 / 高速脱离，
            //   见 `StatusDefectScript` 里 `From = "hologram"`）摆在手上了 ⇒ 会多出一次选牌提示 / 手牌对不上。
            //   现在**所有**角色 Boss 的全息影像都拦掉搬运（它们的剧本一律提前摆牌）。
            SuppressFor = boss;
        }
        catch { SuppressFor = null; }
    }

    [HarmonyPostfix]
    public static void Postfix(ref Task __result)
    {
        try
        {
            if (SuppressFor == null) return;
            if (__result == null) { SuppressFor = null; return; }
            // 整张牌（含它内部的搬运）结束之后再关窗口
            __result.ContinueWith(_ => SuppressFor = null, TaskScheduler.Default);
        }
        catch { SuppressFor = null; }
    }

    internal static bool HasOneForAll(CharBossMonster boss)
    {
        try { return boss.Creature?.GetPower<CharacterBosses.Powers.BossAllForOnePower>() != null; }
        catch { return false; }
    }
}

/// <summary>真正拦下那一步搬运（只在她打出全息影像的窗口内、只对她的牌、只拦「弃牌堆 → 手牌」）。</summary>
[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.Add),
              new[] { typeof(CardModel), typeof(PileType), typeof(CardPilePosition),
                      typeof(AbstractModel), typeof(bool) })]
public static class HologramReturnBlockPatch
{
    [HarmonyPrefix]
    public static bool Prefix(CardModel card, PileType newPileType, ref Task<CardPileAddResult> __result)
    {
        try
        {
            var boss = HologramReturnWindowPatch.SuppressFor;
            if (boss == null) return true;
            if (card == null || newPileType != PileType.Hand) return true;
            if (!ReferenceEquals(card.Owner?.Creature?.Monster, boss)) return true;

            bool fromDiscard;
            try { fromDiscard = card.Pile != null && card.Pile.Type == PileType.Discard; }
            catch { fromDiscard = false; }
            if (!fromDiscard) return true;

            // ① 跳过搬运：剧本早就把她该捞的那张摆在手上了，再搬一次会出现重复 / 手牌对不上
            string entry;
            try { entry = card.Id.Entry; } catch { entry = "?"; }
            Logger.Log($"[CharBoss] 全息影像：拦掉「{entry} 从弃牌堆回到手牌」（剧本已摆好）");

            // ② 但「一心化万」照样算一次返手（所有单位 +1 力量），记账方式与
            //    Patches/BossDiscardToHandStrengthPatch 一致：只记数，真正的加力量在 PlayOne 里结算
            if (HologramReturnWindowPatch.HasOneForAll(boss))
            {
                boss.PendingReturnStrength++;
                Logger.Log($"[CharBoss] 一心化万：全息影像这次返手照样算 ⇒ 待结算 +1 力量" +
                           $"（累计待结算 {boss.PendingReturnStrength}）");
            }

            __result = Task.FromResult(default(CardPileAddResult));
            return false;
        }
        catch
        {
            return true;    // 任何异常都退回原版行为
        }
    }
}
