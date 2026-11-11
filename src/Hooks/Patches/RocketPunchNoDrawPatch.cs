using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Cards;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// 「**火箭飞拳**」（<c>RocketPunch</c>）对角色 Boss：**拦掉它自带的「抽 1 张」**。
///
/// 用户 2026-11-11：
/// > 「拦掉火箭飞拳的抽牌，因为我已经提前在剧本里写好了。」
///
/// 背景（用户报的「状态机手牌里出现煤灰」）：
///   反编译 `RocketPunch.OnPlay` 是两步 ——
///   <code>
///   ① await DamageCmd.Attack(Damage).FromCard(this, cardPlay).Targeting(cardPlay.Target).Execute(ctx);   // 13 伤害
///   ② await CardPileCmd.Draw(choiceContext, DynamicVars.Cards, Owner);                                   // ★ 再抽 1 张
///   </code>
///   状态机的剧本第 3、8 回合是「火箭飞拳、防御、压缩、疑虑」，牌早摆好了；那一抽只会把她抽牌堆里的
///   状态牌（大抱抱洗牌塞的煤灰、烟囱/污秽攻击生成的 WOUND/SLIMED/DAZED）抽到手上
///   —— mod_log 2026-11-11 21:16:46 实证：`PLAY ROCKET_PUNCH` 之后紧接着
///   `回合开始抽牌 → …（SOOT…）`，手牌随即变成 `[DEFEND_DEFECT,COMPACT,DOUBT,SOOT]`。
///   这些状态牌本该由她第 1/4/9 回合的「散射炮」一次性烧掉（`FlakCannon` 会把
///   `Type == Status &amp;&amp; Pile != Exhaust` 的牌全 `CardCmd.Exhaust`），抽上手只是脏手牌。
///
/// 实现方式与 <see cref="HologramNoReturnPatch"/> 完全同一套（那里拦的是「弃牌堆 → 手牌」）：
///   ① 在 `RocketPunch.OnPlay` 的前/后置开一个窗口（窗口只对**这只 Boss**，且等整张牌的 Task 完成才关，
///      因为抽牌发生在 `await` 之后）；
///   ② 在 `CardPileCmd.Draw(ctx, count, player, fromHandDraw)` 的前置里，命中窗口就直接返回一个
///      已完成的空任务（不搬牌、不触发抽牌钩子）。
///
/// ⚠ `RocketPunch.OnPlay` 是 **protected** ⇒ 不能用 `nameof`，也不能让 Harmony 自己找（`CardModel` 上还有
///   别的 `OnPlay` 重载，参数类型不钉死会 AmbiguousMatchException，整个模组的补丁都挂不上）。
/// ⚠ 只拦「抽牌」这一步，伤害照常结算 —— 不能整条 `OnPlay` 跳过。
/// ⚠ 跳过 async 方法必须给 `ref Task __result` 赋已完成任务，否则下游 `GetAwaiter()` 抛 NRE。
/// </summary>
[HarmonyPatch(typeof(RocketPunch), "OnPlay",
              new[] { typeof(PlayerChoiceContext), typeof(CardPlay) })]
public static class RocketPunchNoDrawWindowPatch
{
    /// <summary>窗口只对**这只 Boss** 开（多只 Boss / 多人局也不会串到别人身上）。</summary>
    internal static CharBossMonster? SuppressFor;

    [HarmonyPrefix]
    public static void Prefix(RocketPunch __instance)
    {
        try
        {
            SuppressFor = null;
            if (__instance?.Owner?.Creature?.Monster is not CharBossMonster boss) return;
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
            // 整张牌（含 await 之后的抽牌）结束之后再关窗口
            __result.ContinueWith(_ => SuppressFor = null, TaskScheduler.Default);
        }
        catch { SuppressFor = null; }
    }
}

/// <summary>真正拦下那一次抽牌（只在火箭飞拳的窗口内、只对她自己）。</summary>
[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.Draw),
              new[] { typeof(PlayerChoiceContext), typeof(decimal), typeof(Player), typeof(bool) })]
public static class RocketPunchNoDrawBlockPatch
{
    [HarmonyPrefix]
    public static bool Prefix(Player player, decimal count, ref Task<IEnumerable<MegaCrit.Sts2.Core.Models.CardModel>> __result)
    {
        try
        {
            var boss = RocketPunchNoDrawWindowPatch.SuppressFor;
            if (boss == null || player == null) return true;

            bool mine;
            try
            {
                mine = ReferenceEquals(player, boss.Shadow)
                       || ReferenceEquals(player.Creature, boss.Creature)
                       || ReferenceEquals(player.Creature?.Monster, boss);
            }
            catch { mine = false; }
            if (!mine) return true;

            // ① 跳过抽牌：剧本早就把这一回合该有的牌摆好了
            Logger.Log($"[CharBoss] 火箭飞拳：拦掉抽牌 {count} 张（剧本已摆好，不再把状态牌抽上手）");

            // ② 返回一个已完成的空任务（下游只是 await，不读返回值）
            __result = Task.FromResult<IEnumerable<MegaCrit.Sts2.Core.Models.CardModel>>(
                Array.Empty<MegaCrit.Sts2.Core.Models.CardModel>());
            return false;
        }
        catch
        {
            return true;    // 任何异常都退回原版行为
        }
    }
}
