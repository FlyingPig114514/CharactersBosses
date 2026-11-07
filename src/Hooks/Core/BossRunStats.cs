using System;
using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;

namespace CharacterBosses.Core;

/// <summary>
/// 给影子 Player 补一条「本层统计」记录。
///
/// 为什么必须补：伤害结算（<c>CreatureCmd.Damage</c>）会按**受击方所属玩家**去当前地图点的
/// <c>PlayerStats</c> 里查记录；影子 Player 不在 <c>RunState.Players</c> 里，于是抛
/// <c>"Player with ID ... not found in player stats for this run history! We have 1"</c>。
/// 整次 <c>PlayCardAction</c> 因此异常中断 —— 表现出来就是「玩家打出的牌卡在半空、不进弃牌堆」，
/// 而伤害其实已经结算了（Boss 掉血、意图正常）。
///
/// 修法是最小侵入的：只往列表里塞一条 ID 与影子 Player 相同的空统计，
/// 原版之后按 ID 查得到就没事了，也不会污染真实玩家的统计。
/// </summary>
public static class BossRunStats
{
    public static void Ensure(ICombatState? combatState, Player? shadow)
    {
        if (shadow == null) return;

        try
        {
            var stats = combatState?.RunState?.CurrentMapPointHistoryEntry?.PlayerStats;
            if (stats == null) return;
            if (stats.Any(entry => entry.PlayerId == shadow.NetId)) return;

            stats.Add(new PlayerMapPointHistoryEntry { PlayerId = shadow.NetId });
            Logger.Log($"[CharBoss] 已为影子 Player 补统计条目 id={shadow.NetId}");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 补统计条目失败: " + e.Message);
        }
    }
}
