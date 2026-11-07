using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// 「**幻影之刃**」（`PhantomBladesPower`：小刀获得保留；**每回合你打出的第一把小刀**额外造成 N 点伤害）
/// 在**意图预览**上的补正 —— 玩家 2026-11-03 反馈：
/// 「刀猎存在显示问题，**幻刃的增伤会错误的显示到后续小刀上**导致预计伤害存在误差」。
///
/// 反编译 `PhantomBladesPower.ModifyDamageAdditive`：
/// <code>
/// if (!props.IsPoweredAttack()) return 0;
/// if (cardSource == null || !cardSource.Tags.Contains(CardTag.Shiv)) return 0;
/// if (dealer != Owner) return 0;
/// int played = History.CardPlaysFinished.Count(e =&gt; e.HappenedThisTurn(CombatState)
///                 &amp;&amp; e.CardPlay.Card.Tags.Contains(Shiv) &amp;&amp; e.CardPlay.Player == Owner.Player);
/// if (played &gt; 0) return 0;      // ← 已经打过刀 ⇒ 不加
/// return Amount;                  // = 9（升级 12）
/// </code>
/// 问题：意图是在她**出手之前**算的，那时历史里一把刀都没有 ⇒ 「played &gt; 0」永远不成立
/// ⇒ 它会给**每一把刀**都加 N（实打没问题，只有预览虚高）。
///
/// 补正（只对**我们的影子 Boss**、只针对**计划里排在后面的真刀**）：
///   · 计划里第一把刀 ⇒ 保留这 N 点（`CharBossMonster.IsFirstPlannedShivCard`）；
///   · 计划里后面的刀 ⇒ 压回 0；
///   · **不在计划里**的刀（例如给「生成小刀」预览用的那张合成刀）⇒ 原样返回，
///     那条路径的加减账由 `BossIntentBuilder` 的 `shivBonus` / `shivBonusBakedIn` 自己算，别在这里动。
/// 真人玩家/其它怪物完全不受影响。
/// </summary>
[HarmonyPatch]
public static class PhantomBladesFirstShivPatch
{
    /// <summary>
    /// ★ 双版本：`ModifyDamageAdditive` 在 v0.107 只有 5 个参数（没有末尾的 `CardPlay`）
    ///   ⇒ 原来写死 6 参的 Postfix 在旧版「挂载失败」。改成 `object[] __args` 按位置取：
    ///   (0)=target (1)=amount (2)=props (3)=dealer (4)=cardSource (5)=cardPlay（仅新版）。
    /// </summary>
    [HarmonyPrepare]
    public static bool Prepare() => TargetMethod() != null;

    internal static System.Reflection.MethodBase? TargetMethod()
        => Core.GameCompat.DeclaredMethod(typeof(PhantomBladesPower), "ModifyDamageAdditive");

    [HarmonyPostfix]
    public static void Postfix(object[] __args, ref decimal __result)
    {
        if (__args == null || __args.Length < 5) return;
        decimal amount = (decimal)__args[1];
        ValueProp props = (ValueProp)__args[2];
        Creature? dealer = __args[3] as Creature;
        CardModel? cardSource = __args[4] as CardModel;
        CardPlay? cardPlay = __args.Length > 5 ? __args[5] as CardPlay : null;
        try
        {
            _ = amount; _ = props; _ = dealer; _ = cardPlay;
            if (__result == 0m) return;

            var boss = cardSource?.Owner?.Creature?.Monster as CharBossMonster;
            if (boss == null) return;                       // 不是我们的 Boss：照原版

            if (!boss.PlanContainsShivCard(cardSource)) return;   // 合成刀（预览用）：交给 shivBonus 那套

            if (boss.IsFirstPlannedShivCard(cardSource)) return;  // 第一把刀：保留这 N 点

            Logger.Log($"[CharBoss] 幻影之刃：{SafeEntry(cardSource)} 不是本回合第一把刀 ⇒ 意图里去掉这 {__result} 点");
            __result = 0m;
        }
        catch { /* 出问题就维持原版结果 */ }
    }

    private static string SafeEntry(CardModel? card)
    {
        try { return card?.Id.Entry ?? "?"; } catch { return "?"; }
    }
}
