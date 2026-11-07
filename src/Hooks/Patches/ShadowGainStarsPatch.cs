using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// **所有储君 Boss 共有**的 bug：回合内「获得辉星」（<c>PlayerCmd.GainStars</c>）对影武者不生效。
///
/// 例子：隐秘藏品（HIDDEN_CACHE）原版是
/// <c>PowerCmd.Apply&lt;StarNextTurnPower&gt;(…)</c> + <c>PlayerCmd.GainStars(Stars, Owner)</c> ——
/// 后者对影子玩家什么都不加（用户实测：回合内那 1 星没加），而「下回合 +3 星」那一半
/// 我们自己补过了（见 <c>CharBossMonster.Turn.SettleStarNextTurnPower</c>）。
///
/// 这里直接把影子玩家的星写进 <c>PlayerCombatState.Stars</c>，并跳过原版
/// （原版对影子玩家本来就没效果）。
/// ⚠ Harmony 前缀返回 false 时必须自己给 <c>__result</c> 赋值，否则调用方 await null 会 NRE（踩过）。
/// </summary>
[HarmonyPatch(typeof(PlayerCmd), nameof(PlayerCmd.GainStars))]
public static class ShadowGainStarsPatch
{
    [HarmonyPrefix]
    public static bool Prefix(decimal amount, Player player, ref Task __result)
    {
        try
        {
            if (player?.Creature?.Monster is not CharBossMonster) return true;   // 真人玩家 → 原版

            var pcs = player.PlayerCombatState;
            int applied = 0;
            if (pcs != null && amount != 0m)
            {
                int before = pcs.Stars;
                pcs.Stars = Math.Max(0, before + (int)Math.Round(amount, MidpointRounding.AwayFromZero));
                applied = pcs.Stars - before;
                Logger.Log($"[CharBoss] 影子玩家获得辉星 {amount}：{before} → {pcs.Stars}（原版对影武者无效）");
            }

            // ⚠ 只把数字写进 `pcs.Stars` 不够 ——「**黑洞**」是 `BlackHolePower.AfterStarsGained(amount, gainer)`
            // 派发的（判 `gainer == Owner.Player`，对有星就 3×层数 群伤）。
            // 影子玩家的加星是我们手动记账的，原版那条广播从来没跑过 ⇒ **黑洞一次都不触发**
            //（用户 2026-10-28 实测「黑洞没有生效」）。所以这里替影子玩家补一次广播。
            __result = BroadcastGain(player, applied);
            return false;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 影子玩家加星失败: " + e.Message);
            return true;
        }
    }

    /// <summary>替影子玩家补发一次 `Hook.AfterStarsGained`（黑洞 / 成就等全靠它）。</summary>
    private static async Task BroadcastGain(Player player, int amount)
    {
        try
        {
            if (amount <= 0) return;
            var combat = player?.Creature?.CombatState;
            if (combat == null) return;
            // ⚠ 不能直接广播 Hook.AfterStarsGained：黑洞的 DealDamageToAllEnemies() 用的是
            // CombatState.HittableEnemies，对影子玩家会解析成**敌方阵营** ⇒ 他的黑洞会打到自己
            //（用户 2026-10-31 实测「回合开始/结束时黑洞会攻击到自己」）。这里自己打**真人玩家**。
            if (player?.Creature == null) return;
            int stacks = player.Creature.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.BlackHolePower>();
            if (stacks <= 0) return;
            var victims = new System.Collections.Generic.List<MegaCrit.Sts2.Core.Entities.Creatures.Creature>();
            foreach (var real in CharacterBosses.Core.BossLockOn.RealPlayers(combat))
            {
                if (real?.Creature == null || !real.Creature.IsAlive) continue;
                victims.Add(real.Creature);
            }
            if (victims.Count == 0) return;
            await MegaCrit.Sts2.Core.Commands.CreatureCmd.Damage(
                new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext(),
                victims, stacks, MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered, player.Creature);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 影子玩家加星广播失败: " + e.Message);
        }
    }
}
