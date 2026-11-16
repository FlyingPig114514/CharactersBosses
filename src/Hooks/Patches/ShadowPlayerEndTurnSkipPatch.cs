using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// 把「让她结束回合」这条命令对我们的影子玩家**整条吞掉**（用户 2026-11-16：
/// 「观者出冥想的下个玩家回合开始时，玩家的回合结束按钮会消失」）。
///
/// 反编译 `Watcher.Code.Cards.Uncommon.Meditate.OnPlay` 最后一句是
/// <code>
/// PlayerCmd.EndTurn(Owner, false, null);
/// </code>
/// 也就是「冥想」这张牌本身会替**出牌者**结束本回合。我们的影子玩家本来就是由
/// <c>CharBossMonster</c> 的回合流程驱动的 —— 她在剧本里打完这一手之后我们会自己收尾，
/// 中途再被 `PlayerCmd.EndTurn` 结束一次，就把原版的回合状态机推成了「已结束但没人重置」：
/// 下一个**玩家**回合开始时 `NEndTurnButton.OnTurnStarted` 拿不到正确的状态 ⇒
/// 结束回合按钮一直是 Hidden（和 `EmotionChipBossTakeoverPatch` 里记的那次事故同一类）。
///
/// ⇒ 这里只对「她自己的影子玩家」跳过这条命令；真人玩家（含她当玩家的那套原版冥想）完全不受影响。
/// 返回类型是 <c>void</c>，所以不需要 <c>ref __result</c>。
/// </summary>
[HarmonyPatch(typeof(PlayerCmd), nameof(PlayerCmd.EndTurn))]
public static class ShadowPlayerEndTurnSkipPatch
{
    private static int _logs;

    [HarmonyPrefix]
    public static bool Prefix(Player player)
    {
        try
        {
            if (player?.Creature?.Monster is not CharBossMonster boss) return true;   // 真人玩家：照常

            if (_logs++ < 8)
            {
                string name = "?";
                try { name = boss.Def?.Key ?? name; } catch { /* 忽略 */ }
                Logger.Log($"[CharBoss] 影子玩家的 PlayerCmd.EndTurn 被吞掉（{name}；" +
                           "她的回合由我们自己的流程收尾 —— 否则玩家的结束回合按钮会消失）");
            }
            return false;
        }
        catch
        {
            return true;
        }
    }
}
