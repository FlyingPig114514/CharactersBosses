using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// **两版通用的 `CreatureCmd.Damage` 重载解析器**。
///
/// 两版的核心重载签名不一样：
/// <code>
/// v0.111: Damage(ctx, IEnumerable&lt;Creature&gt; targets, amount, props, dealer, cardSource, cardPlay)
/// v0.107: Damage(ctx, IEnumerable&lt;Creature&gt; targets, amount, props, dealer)
/// </code>
/// 写死任一版都会在另一版上「挂载失败」（实测日志：`OmnisliceSplashRealPlayersOnlyPatch 挂载失败`）。
/// 这里按「参数个数从多到少」依次尝试，取第一个存在的重载；补丁方法一律用 `object[] __args`
/// 按位置取值，所以不会再被签名差异绊住。
/// </summary>
internal static class DamageOverload
{
    internal static MethodBase? Find()
    {
        try
        {
            var t = typeof(MegaCrit.Sts2.Core.Commands.CreatureCmd);
            // 先试 7 参（111），再 6 参，最后 5 参（107）
            var tries = new[]
            {
                new[]
                {
                    typeof(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext),
                    typeof(IEnumerable<Creature>), typeof(decimal), typeof(ValueProp),
                    typeof(Creature), typeof(CardModel), typeof(MegaCrit.Sts2.Core.Entities.Cards.CardPlay),
                },
                new[]
                {
                    typeof(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext),
                    typeof(IEnumerable<Creature>), typeof(decimal), typeof(ValueProp),
                    typeof(Creature), typeof(CardModel),
                },
                new[]
                {
                    typeof(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext),
                    typeof(IEnumerable<Creature>), typeof(decimal), typeof(ValueProp),
                    typeof(Creature),
                },
            };
            foreach (var sig in tries)
            {
                var m = AccessTools.Method(t, nameof(MegaCrit.Sts2.Core.Commands.CreatureCmd.Damage), sig);
                if (m != null) return m;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    /// <summary>按位置读参数：0=ctx 1=targets 2=amount 3=props 4=dealer 5=cardSource? 6=cardPlay?</summary>
    /// <summary>
    /// **单目标**版本的同名重载（`Damage(ctx, Creature target, amount, props, dealer[, cardSource[, cardPlay]])`）。
    ///
    /// 为什么需要（2026-11-07 用户：「电球/黑球优先打『为你而死』的召唤物」这条其实没生效）：
    /// 闪电球走 `Rng.CombatTargets.NextItem(...)` 拿**一个**目标、暗黑球走 `HittableEnemies.MinBy(...)`
    /// 也是**一个**，两者最终调的都是单目标重载；而 <see cref="Find"/> 只解析 `IEnumerable&lt;Creature&gt;`
    /// 那个（列表）重载 ⇒ 补丁从来没被调用过。
    /// </summary>
    internal static MethodBase? FindSingle()
    {
        try
        {
            var t = typeof(MegaCrit.Sts2.Core.Commands.CreatureCmd);
            var tries = new[]
            {
                new[]
                {
                    typeof(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext),
                    typeof(Creature), typeof(decimal), typeof(ValueProp),
                    typeof(Creature), typeof(CardModel), typeof(MegaCrit.Sts2.Core.Entities.Cards.CardPlay),
                },
                new[]
                {
                    typeof(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext),
                    typeof(Creature), typeof(decimal), typeof(ValueProp),
                    typeof(Creature), typeof(CardModel),
                },
                new[]
                {
                    typeof(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext),
                    typeof(Creature), typeof(decimal), typeof(ValueProp),
                    typeof(Creature),
                },
            };
            foreach (var sig in tries)
            {
                var m = AccessTools.Method(t, nameof(MegaCrit.Sts2.Core.Commands.CreatureCmd.Damage), sig);
                if (m != null) return m;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }
    internal static CardModel? CardSource(object[]? args)
    {
        if (args == null || args.Length <= 5) return null;
        return args[5] as CardModel;
    }

    internal static string PlayingEntryOrCard(object[]? args)
    {
        var cs = CardSource(args);
        if (cs != null)
        {
            try { return cs.Id.Entry; } catch { }
        }
        try { return CharBossMonster.CurrentPlayingEntry ?? ""; } catch { return ""; }
    }
}
