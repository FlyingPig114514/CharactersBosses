using System;
using System.Reflection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Monsters;

namespace CharacterBosses.Core;

/// <summary>
/// 「**开心小花**」（<c>HappyFlower</c>）对角色 Boss 的正确计数（用户 2026-11-02 定的口径）：
///
/// > 战斗开始时计数 0；她的第 1、4、7…（3n+1）回合 → 1；第 2、5、8…（3n+2）回合 → 2；
/// > 第 3、6、9…（3n）回合 → **+1 能量**，计数归 0。
///
/// 为什么要自己算：原版遗物的钩子（`AfterSideTurnStart`）对她会被**广播多次**
/// （原版广播 + `BossHookBridge` 的补触发），计数跑得比回合快 ⇒ 加费时点全错。
/// 所以原版那条对她已由 <see cref="Patches.HappyFlowerNoDoublePatch"/> 拦掉，
/// 这里在**她自己的回合开始**（`BossHookBridge.FireSideTurnStart`）按回合号统一结算。
///
/// ⚠ 顺带把遗物自己的计数字段（`TurnsSeen`）也摆到对应位置，省得图标上的进度和实际对不上。
/// </summary>
internal static class HappyFlowerPlays
{
    private const string Entry = "HAPPY_FLOWER";
    private const int TurnsPerCharge = 3;      // 开心小花：每 3 回合 +1 能量

    private static FieldInfo? _turnsSeenField;
    private static bool _reflected;

    /// <summary>她回合开始时调一次（<see cref="BossHookBridge.FireSideTurnStart"/>）。</summary>
    internal static void OnBossTurnStart(CharBossMonster boss, PlayerChoiceContext? ctx)
    {
        try
        {
            var shadow = boss?.Shadow;
            if (shadow == null) return;

            var relic = Find(shadow);
            if (relic == null) return;

            int turn = Math.Max(1, boss!.TurnNumber);          // 1 起的回合号
            int progress = turn % TurnsPerCharge;              // 1,2,0,1,2,0…
            SetTurnsSeen(relic, progress);

            if (progress != 0)
            {
                Logger.Log($"[CharBoss] 开心小花：第 {turn} 回合 → 计数 {progress}/{TurnsPerCharge}（下次加费在下一次 3n 回合）");
                return;
            }

            var energyCmd = PlayerCmd.GainEnergy(1, shadow);
            Logger.Log($"[CharBoss] 开心小花：第 {turn} 回合（3 的倍数）→ +1 能量（计数归 0）");
            if (energyCmd != null) _ = energyCmd;              // 不 await：能量到账即可（同步生效）
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 开心小花结算失败: " + e.Message);
        }
    }

    private static RelicModel? Find(Player shadow)
    {
        try
        {
            var relics = shadow.Relics;
            if (relics == null) return null;
            for (int i = 0; i < relics.Count; i++)
            {
                var r = relics[i];
                if (r == null) continue;
                string entry;
                try { entry = r.Id.Entry; } catch { continue; }
                if (entry == Entry) return r;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    /// <summary>
    /// 把遗物自己的计数摆到 <paramref name="value"/>。
    ///
    /// ⚠ v0.2.300（用户 2026-11-02：「你这开心小花的计数怎么不变了」）：`TurnsSeen` 是
    /// **public 属性**（`P System.Int32 TurnsSeen { get; set; }`），不是字段 ——
    /// 上一版用 `GetField("TurnsSeen")` 拿不到、又不报错 ⇒ 图标计数一直停在 0。
    /// 现在直接按类型转换写属性，读不到才退回反射（`_turnsSeen` 是它的后备字段）。
    /// </summary>
    private static void SetTurnsSeen(RelicModel relic, int value)
    {
        try
        {
            if (relic is MegaCrit.Sts2.Core.Models.Relics.HappyFlower flower)
            {
                flower.TurnsSeen = value;
                return;
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 开心小花：写 TurnsSeen 失败（忽略）: " + e.Message);
        }
    }
}
