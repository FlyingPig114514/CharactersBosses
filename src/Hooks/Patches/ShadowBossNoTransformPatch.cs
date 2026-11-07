using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// **拦掉「原始力量」的变牌**（用户 2026-10-28）。
///
/// 反编译：`PrimalForce.OnPlay` 会把手牌里所有可变换的攻击牌逐个
/// `CardCmd.Transform(original, replacement, style)` 变换掉。
/// 而我们的剧本手牌是**写死**的 —— 变换之后剧本里那几张巨石就不在手上了，
/// 于是「意图里有、实际不打」甚至「回合直接结束」（实测：变完手牌变成 6 张巨石、计划归 0）。
///
/// 这里对**影子 Boss** 的变换请求直接跳过（返回 null 结果，等价于没变），
/// 真人玩家的变换一律原样执行。
/// </summary>
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Commands.CardCmd), "Transform", new[]
{
    typeof(CardModel), typeof(CardModel), typeof(CardPreviewStyle),
})]
internal static class ShadowBossNoTransformPatch
{
    [HarmonyPrefix]
    public static bool Prefix(CardModel original, ref Task __result)
    {
        try
        {
            var owner = original?.Owner;
            if (owner?.Creature?.Monster is not CharBossMonster) return true;

            Logger.Log($"[CharBoss] 变牌被拦：{original!.Id.Entry} 是剧本牌，不参与变换");
            var tcs = new TaskCompletionSource<MegaCrit.Sts2.Core.Entities.Cards.CardPileAddResult?>();
            tcs.SetResult(null);
            __result = tcs.Task;
            return false;
        }
        catch { return true; }
    }
}