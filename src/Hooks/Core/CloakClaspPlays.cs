using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.ValueProps;
using CharacterBosses.Monsters;

namespace CharacterBosses.Core;

/// <summary>
/// 遗物「**斗篷扣**」（<c>CloakClasp</c>，用户 2026-11-03 给二层「蛇眼猎」加的）的手动结算：
///
/// > 在你的回合结束时，你每有 1 张手牌，就获得 {Block} 点格挡。
///
/// 原版实现挂在 <c>BeforeSideTurnEnd</c> 上；影子玩家不在 `CombatState.Players` 里，
/// 那条侧回合结束的广播对我们**只在她真的出牌时**才由 `BossRelicHookBridge.FireSideTurnEnd` 补
/// （而且只有 `FireBossRelicHooks = true` 的 Boss 才走那条路）⇒ 蛇眼猎这类默认不补。
///
/// 所以这里在**她回合结束、还没丢手牌之前**照卡面自己结算一次：
///   格挡 = 遗物上的 `Block` 变量 × 手牌张数。
/// 调用点：<c>CharBossMonster.PlayPlannedCards</c> 里「回合结束遗物钩子」那一段之后
/// （必须早于 `BossPiles.DiscardHand`，否则手里已经没牌了）。
/// </summary>
internal static class CloakClaspPlays
{
    private const string RelicEntry = "CLOAK_CLASP";

    /// <summary>她身上有没有「斗篷扣」。</summary>
    internal static bool Has(CharBossMonster? boss)
    {
        try
        {
            var relics = boss?.Shadow?.Relics;
            if (relics == null) return false;
            for (int i = 0; i < relics.Count; i++)
            {
                var relic = relics[i];
                if (relic == null) continue;
                try { if (relic.Id.Entry == RelicEntry) return true; } catch { /* 忽略 */ }
            }
        }
        catch { /* 忽略 */ }
        return false;
    }

    /// <summary>她回合结束时：每张手牌给 N 点格挡（N = 遗物上的 `Block` 变量）。</summary>
    internal static async Task RunAtTurnEnd(CharBossMonster boss, PlayerChoiceContext ctx)
    {
        try
        {
            if (boss?.Creature == null || !boss.Creature.IsAlive) return;
            if (!Has(boss)) return;

            int perCard = 0;
            var relics = boss.Shadow?.Relics;
            for (int i = 0; i < (relics?.Count ?? 0); i++)
            {
                var relic = relics![i];
                if (relic == null) continue;
                string entry;
                try { entry = relic.Id.Entry; } catch { continue; }
                if (entry != RelicEntry) continue;
                try
                {
                    if (relic.DynamicVars.TryGetValue("Block", out var v) && v != null)
                        perCard = Math.Max(0, (int)Math.Round((double)v.BaseValue));
                }
                catch { perCard = 0; }
                break;
            }
            if (perCard <= 0) return;

            int hand = boss.Shadow?.PlayerCombatState?.Hand?.Cards.Count ?? 0;
            if (hand <= 0)
            {
                Logger.Log("[CharBoss] 斗篷扣：手里没有牌 ⇒ 不给格挡");
                return;
            }

            int amount = perCard * hand;
            await CreatureCmd.GainBlock(boss.Creature, amount, ValueProp.Unpowered, null);
            Logger.Log($"[CharBoss] 斗篷扣：回合结束手里 {hand} 张牌 × {perCard} ⇒ 获得 {amount} 点格挡" +
                       "（原版 BeforeSideTurnEnd 对影子玩家不广播，这里照卡面补）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 斗篷扣结算失败: " + e.Message);
        }
    }
}
