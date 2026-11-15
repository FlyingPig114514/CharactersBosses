using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Patches;

/// <summary>
/// **临时诊断（2026-11-14，v0.2.653）**：追踪「玩家身上的负面」是**谁**在什么时候加的/减的。
///
/// 起因：用户反馈「恐惧模式·静默猎手的支配」给的 1 层虚弱，在第一回合结束时会**闪到 2 再变回 1**。
/// 支配本身每场只施加一次（日志可证），所以那「多出来的一层」另有来源 ——
/// 最可能是**这场战斗的敌人自己**在它的回合里给玩家上了 1 层虚弱（例如 `SLUDGE_SPINNER_WEAK`
/// 这种遭遇名里带 WEAK 的怪），随后原版的时长结算又扣掉 1 层。
///
/// 为了不靠猜，这里把两条关键路径都打出来（只记**真人玩家**身上的
/// `WEAK` / `VULNERABLE` / `STRENGTH` / `DEBILITATE`，上限 <see cref="MaxLines"/> 行）：
/// <list type="bullet">
///   <item>`PowerCmd.Apply` —— 谁加的、加多少、加之前多少、当时是哪个阵营的回合、有没有置「跳过下次结算」；</item>
///   <item>`PowerCmd.Decrement` —— 谁被扣了、扣之前多少（结算发生在「敌方回合结束」）。</item>
/// </list>
/// 定位完这一批就可以把本文件删掉（它只写日志，不改任何行为）。
/// </summary>
[HarmonyPatch]
internal static class PowerDurationTrace
{
    private const int MaxLines = 80;

    private static readonly HashSet<string> Watched = new(StringComparer.Ordinal)
    {
        "WEAK_POWER", "VULNERABLE_POWER", "STRENGTH_POWER", "DEBILITATE_POWER", "FRAIL_POWER",
    };

    private static int _lines;

    private static bool Wanted(PowerModel? power, Creature? target, out string entry)
    {
        entry = "?";
        try
        {
            if (power == null || target == null) return false;
            entry = power.Id.Entry ?? "?";
            if (!Watched.Contains(entry)) return false;
            // 只关心真人玩家（影子玩家/怪物不看）
            if (target.Monster != null) return false;
            return target.Player != null;
        }
        catch { return false; }
    }

    private static string SideNow()
    {
        try { return MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.DebugOnlyGetState()?.CurrentSide.ToString() ?? "?"; }
        catch { return "?"; }
    }

    private static string AmountOf(PowerModel? power)
    {
        try { return power == null ? "?" : power.Amount.ToString(); }
        catch { return "?"; }
    }

    private static bool SkipFlagOf(PowerModel? power)
    {
        try { return power != null && power.SkipNextDurationTick; }
        catch { return false; }
    }

    private static bool BeforeApply(PowerModel power, Creature target, decimal amount)
    {
        try
        {
            if (_lines >= MaxLines) return true;
            if (!Wanted(power, target, out string entry)) return true;
            _lines++;
            Logger.Log($"[CharBoss] 负面追踪 Apply：{entry} {amount:+0;-0} → {Name(target)}" +
                       $"（加前 {AmountOf(power)}，当前阵营={SideNow()}，施加者=…）");
        }
        catch { /* 忽略 */ }
        return true;
    }

    private static bool BeforeDecrement(PowerModel power)
    {
        try
        {
            if (_lines >= MaxLines) return true;
            Creature? owner = null;
            try { owner = power.Owner; } catch { owner = null; }
            if (!Wanted(power, owner, out string entry)) return true;
            _lines++;
            Logger.Log($"[CharBoss] 负面追踪 结算：{entry} 被扣 1（扣前 {AmountOf(power)}，" +
                       $"跳过标记={SkipFlagOf(power)}，当前阵营={SideNow()}）→ {Name(owner)}");
        }
        catch { /* 忽略 */ }
        return true;
    }

    private static string Name(Creature? c)
    {
        try { return c?.Name ?? "?"; } catch { return "?"; }
    }

    /// <summary>非泛型那条 `Apply(ctx, PowerModel, Creature, amount, applier, cardSource, silent)`。</summary>
    [HarmonyPatch(typeof(PowerCmd), nameof(PowerCmd.Apply),
        new[]
        {
            typeof(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext),
            typeof(PowerModel), typeof(Creature), typeof(decimal),
            typeof(Creature), typeof(CardModel), typeof(bool),
        })]
    [HarmonyPrefix]
    public static bool ApplyPrefix(PowerModel power, Creature target, decimal amount)
        => BeforeApply(power, target, amount);

    /// <summary>`PowerCmd.Decrement(PowerModel)` —— 时长结算真正落地的地方。</summary>
    [HarmonyPatch(typeof(PowerCmd), nameof(PowerCmd.Decrement), new[] { typeof(PowerModel) })]
    [HarmonyPrefix]
    public static bool DecrementPrefix(PowerModel power) => BeforeDecrement(power);
}
