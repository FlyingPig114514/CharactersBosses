using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using CharacterBosses.Core;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// 多人换牌 **A3：斗篷与匕首 → 刀刃交响曲** 的效果（设计见 <c>docs/plans/multiplayer-cards.md</c> §3）。
///
/// ## 原版「刀刃交响曲」做了什么（已反编译 <c>references\sts2.dll</c> 确认）
/// <code>
/// // BladeSymphony（2 费技能，TargetType.AllAllies，MultiplayerOnly，Cards 变量 2；升级：费用 -1）
/// await CreatureCmd.TriggerAnim(owner, "Cast", …);
/// foreach (Creature c in CombatState.GetTeammatesOf(owner.Creature)
///                              .Where(c =&gt; c != null &amp;&amp; c.IsAlive &amp;&amp; c.IsPlayer))
///     for (int i = 0; i &lt; DynamicVars.Cards.IntValue; i++)
///         await Shiv.CreateInHand(c.Player, CombatState);   // ← 给**每个**同阵营玩家塞 2 把小刀
/// </code>
/// <c>GetTeammatesOf</c> = 同阵营全体，所以 Boss 打这张牌时它会把刀塞给**所有** Boss（含她自己）
/// 以及**真人玩家**。用户的设计是：
///   · **只有队友 Boss** 拿到刀，并且**当场朝自己跟踪锁定的那位玩家扔出去**；
///   · 真人玩家一把也不给（总原则：换牌后的效果只作用在 Boss 侧）；
///   · 施法者自己不额外拿刀 —— 她这一手就是「让队友出刀」。
///
/// ## 所以这里整段接管（prefix 返回 false）
/// 复制原版的施法动画 + `Shiv.CreateInHand`，只把「谁拿到刀」换成队友 Boss，
/// 然后逐只把刀打出去（<see cref="ShivPlays.PlayAsync"/> 内部走
/// <c>BossPlanner.ChooseTarget</c> ⇒ **优先打她自己的跟踪锁定者**）。
/// 意图由 <c>BossIntentBuilder</c> 的 BLADE_SYMPHONY 分支提前画出来（Raw 意图，按队友自己的力量算）。
///
/// ## 升级
/// 沿用原进阶规则：剧本里 `(9, 3, CloakAndDagger)` 那条按**原牌类型**匹配
/// （<c>BossScriptRunner.BuildHand</c> 用 `entry.Card` 判升级、造的是换上的牌），
/// 所以进阶 9 时换上的这张自然就是 1 费升级版，低进阶是 2 费 —— 能量预算照原样，不用特殊处理。
///
/// 单人零回归：单人局里「刀刃交响曲」不会进卡组（换牌只在多人生效），
/// 而且施法者不是我们的 Boss 时这个前缀直接放行 → 走原版。
/// </summary>
[HarmonyPatch]
public static class BladeSymphonyPatch
{
    /// <summary>
    /// ★ 双版本：这张卡是**新版（v0.111）才有的**，旧版（v0.107）整个类型不存在
    ///   ⇒ 目标方法按字符串解析，旧版探测不到就整类跳过（换牌功能在旧版也用不到它）。
    /// </summary>
    [HarmonyPrepare]
    public static bool Prepare() => Core.GameCompat.HasBladeSymphony;

    internal static System.Reflection.MethodBase? TargetMethod()
        => Core.GameCompat.DeclaredMethod(Core.GameCompat.TypeOrNull("MegaCrit.Sts2.Core.Models.Cards.BladeSymphony"), "OnPlay");
    [HarmonyPrefix]
    public static bool Prefix(CardModel __instance, PlayerChoiceContext choiceContext, CardPlay cardPlay,
                              ref Task __result)
    {
        try
        {
            var owner = __instance.Owner;
            if (owner?.Creature?.Monster is not CharBossMonster caster) return true;   // 真人玩家施法 → 原版

            __result = RunAsync(__instance, owner, caster, choiceContext);
            return false;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 刀刃交响曲接管失败（按原版放行）: " + e);
            return true;
        }
    }

    private static async Task RunAsync(CardModel card, Player owner, CharBossMonster caster,
                                       PlayerChoiceContext ctx)
    {
        // ① 原版的施法动画（照抄，缺了会少了起手动作）
        try
        {
            await CreatureCmd.TriggerAnim(owner.Creature, "Cast", owner.Character.CastAnimDelay);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 刀刃交响曲施法动画失败: " + e.Message);
        }

        int perTeammate = 2;
        try { perTeammate = Math.Max(1, (int)card.DynamicVars.Cards.IntValue); } catch { /* 用默认 2 */ }

        var combat = card.CombatState;
        if (combat == null) return;

        // ② 给「她自己 + 每只队友 Boss」各塞刀（真人玩家一律不给）
        //    用户 2026-10-22 订正：**她自己也要拿刀**（原版这张牌给的是「每个队友（含自己）」）。
        var mates = BossIntentBuilder.VolleyBossesOf(card);
        var got = new List<(CharBossMonster Boss, PlayerCombatState Pcs, int Created)>();
        foreach (var mate in mates)
        {
            var pcs = mate.Shadow?.PlayerCombatState;
            if (pcs == null) continue;

            int created = 0;
            for (int i = 0; i < perTeammate; i++)
            {
                try
                {
                    await CharacterBosses.Core.GameCompatExtra.CreateShivInHandOne(mate.Shadow!, combat, owner);
                    created++;
                    await Cmd.Wait(0.1f);
                }
                catch (Exception e)
                {
                    Logger.Log($"[CharBoss] 刀刃交响曲：给 {mate.Id.Entry} 造小刀失败: {e.Message}");
                    break;
                }
            }
            got.Add((mate, pcs, created));
        }

        // ③ 队友当场把刀朝「自己跟踪锁定的玩家」扔出去（ShivPlays 内部按锁定优先选靶）
        //    注：意图那边（BossIntentBuilder 的 BLADE_SYMPHONY 分支）用的是 Raw 意图、
        //    按**队友自己的力量**算，不含「目标的易伤」—— 因为意图节点的 Modifier 用的是
        //    施法者（她）而不是真正出刀的队友；这里只保证「力量那一半」准确。
        foreach (var (mate, pcs, created) in got)
        {
            try
            {
                if (mate.Creature == null || !mate.Creature.IsAlive) continue;
                int played = await ShivPlays.PlayAsync(mate, pcs, ctx, perTeammate, $"刀刃交响曲（{caster.Id.Entry} 打的）");
                Logger.Log($"[CharBoss] 刀刃交响曲：{mate.Id.Entry} 打出 {played} 把小刀");
                if (played < created)
                {
                    // 意图是按 perTeammate 画的，实际少了 → 明确记一条，方便下一轮核对意图
                    Logger.Log($"[CharBoss] 刀刃交响曲：{mate.Id.Entry} 只打出了 {played}/{created} 把" +
                               $"（意图按 {perTeammate} 画，偏差请对照手牌是否已满）");
                }
            }
            catch (Exception e)
            {
                Logger.Log($"[CharBoss] 刀刃交响曲：{mate.Id.Entry} 出刀失败: {e.Message}");
            }
        }

        if (got.Count == 0)
        {
            Logger.Log("[CharBoss] 刀刃交响曲：场上没有队友 Boss（单人？）→ 这把牌不产生任何效果");
        }
    }
}
