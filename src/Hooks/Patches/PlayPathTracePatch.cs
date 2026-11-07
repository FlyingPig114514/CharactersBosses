#if STS2_107
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// **出牌路径探针（只在 v0.107 上编译）** —— 用户 2026-11-05 报「羽化一印牌就卡死」：
/// 日志停在 `PLAY METAMORPHOSIS` 之后什么都没有，说明她这一手在**某个 await 上永远不返回**。
/// 这些探针只打印「进入了哪个调用」，最后一条没有配套后续的日志就是卡住的位置。
///
/// 全部用**字符串寻址 + object[] __args**：不写死签名，所以不会被两版签名差异绊住；
/// 目标解析不到就整类跳过（Prepare 守卫）。只看**我们的 Boss** 相关的调用，避免刷屏。
/// </summary>
internal static class PlayTrace
{
    private static int _n;

    internal static void Log(string what, object[]? args)
    {
        try
        {
            if (_n++ > 400) return;                       // 防止刷爆日志
            if (!IsOurs(args)) return;
            var sb = new StringBuilder();
            sb.Append("[TRACE] ").Append(what);
            if (args != null)
            {
                for (int i = 0; i < args.Length && i < 4; i++)
                {
                    sb.Append(" | ").Append(Describe(args[i]));
                }
            }
            Logger.Log(sb.ToString());
        }
        catch { /* 忽略 */ }
    }

    private static bool IsOurs(object[]? args)
    {
        if (args == null) return false;
        foreach (var a in args)
        {
            switch (a)
            {
                case CardModel c when c.Owner?.Creature?.Monster is CharBossMonster:
                    return true;
                case Creature cr when cr.Monster is CharBossMonster:
                    return true;
                case MegaCrit.Sts2.Core.Entities.Cards.CardPlay p
                    when p.Card?.Owner?.Creature?.Monster is CharBossMonster:
                    return true;
            }
        }
        return false;
    }

    private static string Describe(object? a)
    {
        try
        {
            return a switch
            {
                null => "null",
                CardModel c => "card=" + c.Id.Entry,
                Creature cr => "creature=" + (cr.Monster?.Id.Entry ?? cr.Player?.NetId.ToString() ?? "?"),
                MegaCrit.Sts2.Core.Entities.Cards.CardPlay p => "play=" + p.Card?.Id.Entry,
                _ => a.GetType().Name,
            };
        }
        catch { return "?"; }
    }

    internal static MethodBase? Find(string typeName, string method) =>
        AccessTools.Method(AccessTools.TypeByName(typeName), method);
}

[HarmonyPatch] internal static class TraceAutoPlay
{
    [HarmonyPrepare] public static bool Prepare() => TargetMethod() != null;
    internal static MethodBase? TargetMethod() => PlayTrace.Find("MegaCrit.Sts2.Core.Commands.CardCmd", "AutoPlay");
    [HarmonyPrefix] public static void Prefix(object[] __args) => PlayTrace.Log("CardCmd.AutoPlay", __args);
}

[HarmonyPatch] internal static class TraceAddGen
{
    [HarmonyPrepare] public static bool Prepare() => TargetMethod() != null;
    internal static MethodBase? TargetMethod() => PlayTrace.Find("MegaCrit.Sts2.Core.Commands.CardPileCmd", "AddGeneratedCardToCombat");
    [HarmonyPrefix] public static void Prefix(object[] __args) => PlayTrace.Log("CardPileCmd.AddGeneratedCardToCombat", __args);
}

[HarmonyPatch] internal static class TraceAddGenMany
{
    [HarmonyPrepare] public static bool Prepare() => TargetMethod() != null;
    internal static MethodBase? TargetMethod() => PlayTrace.Find("MegaCrit.Sts2.Core.Commands.CardPileCmd", "AddGeneratedCardsToCombat");
    [HarmonyPrefix] public static void Prefix(object[] __args) => PlayTrace.Log("CardPileCmd.AddGeneratedCardsToCombat", __args);
}

[HarmonyPatch] internal static class TracePileAdd
{
    [HarmonyPrepare] public static bool Prepare() => TargetMethod() != null;
    internal static MethodBase? TargetMethod() => AccessTools.Method(
        AccessTools.TypeByName("MegaCrit.Sts2.Core.Commands.CardPileCmd"), "Add",
        new[]
        {
            typeof(CardModel), typeof(MegaCrit.Sts2.Core.Entities.Cards.PileType),
            typeof(MegaCrit.Sts2.Core.Entities.Cards.CardPilePosition),
            typeof(MegaCrit.Sts2.Core.Models.AbstractModel), typeof(bool),
        });
    [HarmonyPrefix] public static void Prefix(object[] __args) => PlayTrace.Log("CardPileCmd.Add", __args);
}

[HarmonyPatch] internal static class TraceGetForCombat
{
    [HarmonyPrepare] public static bool Prepare() => TargetMethod() != null;
    internal static MethodBase? TargetMethod() => PlayTrace.Find("MegaCrit.Sts2.Core.Factories.CardFactory", "GetForCombat");
    [HarmonyPrefix] public static void Prefix(object[] __args) => PlayTrace.Log("CardFactory.GetForCombat", __args);
}

[HarmonyPatch] internal static class TracePreviewAdd
{
    [HarmonyPrepare] public static bool Prepare() => TargetMethod() != null;
    internal static MethodBase? TargetMethod() => PlayTrace.Find("MegaCrit.Sts2.Core.Commands.CardCmd", "PreviewCardPileAdd");
    [HarmonyPrefix] public static void Prefix(object[] __args) => PlayTrace.Log("CardCmd.PreviewCardPileAdd", __args);
}

[HarmonyPatch] internal static class TraceBeforeCardPlayed
{
    [HarmonyPrepare] public static bool Prepare() => TargetMethod() != null;
    internal static MethodBase? TargetMethod() => PlayTrace.Find("MegaCrit.Sts2.Core.Hooks.Hook", "BeforeCardPlayed");
    [HarmonyPrefix] public static void Prefix(object[] __args) => PlayTrace.Log("Hook.BeforeCardPlayed", __args);
}

[HarmonyPatch] internal static class TraceAfterCardPlayed
{
    [HarmonyPrepare] public static bool Prepare() => TargetMethod() != null;
    internal static MethodBase? TargetMethod() => PlayTrace.Find("MegaCrit.Sts2.Core.Hooks.Hook", "AfterCardPlayed");
    [HarmonyPrefix] public static void Prefix(object[] __args) => PlayTrace.Log("Hook.AfterCardPlayed", __args);
}

[HarmonyPatch] internal static class TraceCmdWait
{
    [HarmonyPrepare] public static bool Prepare() => TargetMethod() != null;
    internal static MethodBase? TargetMethod() => PlayTrace.Find("MegaCrit.Sts2.Core.Commands.Cmd", "Wait");
    [HarmonyPrefix] public static void Prefix(object[] __args) => PlayTrace.Log("Cmd.Wait", __args);
}
#endif
