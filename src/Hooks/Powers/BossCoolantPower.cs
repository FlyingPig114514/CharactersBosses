using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Core;
using CharacterBosses.Monsters;

namespace CharacterBosses.Powers;

/// <summary>
/// 一层「黑球机」的特殊效果「**冷却剂**」—— 挂在 **Boss 身上**（状态栏可见），效果分两半：
///
/// ⚠ **类名必须带 Boss 前缀**：原版已经有一个 `MegaCrit.Sts2.Core.Models.Powers.CoolantPower`
/// （故障机器人的「冷却剂」），模型 ID 是按类名算的 ⇒ 撞名会让 `ModelDb` 初始化直接抛
/// `DuplicateModelException`，**整个游戏起不来**（2026-10-27 实测）。所以这里叫 `BossCoolantPower`，
/// 本地化 key 也随之变成 `BOSS_COOLANT_POWER`。
///
/// ① **战斗开始时**：给被她锁定的那位玩家 **2 张「冷光」**，**各 1 张**分别进**手牌**和**抽牌堆**
///    （用户 2026-11-05「削弱/回调黑球机」：从「1 张、只进手牌」回调成「2 张、手牌 + 抽牌堆各 1」；
///     冷光是 0 费 Token 技能「获得能量」）；
/// ② **玩家回合结束时手里还留着冷光**：故障机器人的**所有暗黑充能球本回合不使用被动**
///    （= 该回合她的黑暗球不会按 `DarkOrb.BeforeTurnEndOrbTrigger` 打人，
///      patch 见 <c>Patches/DarkOrbPassiveSuppressPatch</c>）。
///
/// 为什么用「她身上的 Power」来当载体：和袖子乾坤一样，原版
/// <c>Hook.AfterCardPlayed</c> 是广播给 `CombatState.IterateHookListeners()` 的，
/// 会把场上每只生物的 Powers 都算进去 ⇒ 挂在她身上的 Power 天然看得到玩家出牌。
/// </summary>
public sealed class BossCoolantPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    private int _logs;

    /// <summary>
    /// 「**在你的回合结束时，若你手牌中保留着冷光**，本回合她的暗黑充能球不触发被动」（用户 2026-10-27 改口径）。
    ///
    /// 挂点用 `BeforeFlush`：它在**玩家的手牌被丢弃之前**按玩家逐个派发，
    /// 正好是「回合结束时手牌里还剩什么」那一刻（`AfterSideTurnEnd` 可能已经在清手牌之后了）。
    ///
    /// ⇒ 打掉冷光（拿去换能量）就拿不到保护；想保护就得**把它留在手里**。单体效果：
    /// 只有**被她锁定的那位玩家**手牌里还剩冷光才算。
    /// </summary>
    public override Task BeforeFlush(PlayerChoiceContext choiceContext, Player player)
    {
        try
        {
            var boss = Owner?.Monster as CharBossMonster;
            var shadow = Owner?.Player;
            if (boss == null || shadow == null || player == null) return Task.CompletedTask;
            if (ReferenceEquals(player, shadow)) return Task.CompletedTask;          // 她自己不算
            if (player.Creature?.Monster != null) return Task.CompletedTask;        // 玩家的召唤物不算
            if (!Core.BossLockOn.IsLocked(boss, player)) return Task.CompletedTask; // 只认被她锁定的那位

            var hand = player.PlayerCombatState?.Hand?.Cards;
            if (hand == null) return Task.CompletedTask;

            bool kept = false;
            foreach (var card in hand)
            {
                if (card == null) continue;
                string entry;
                try { entry = card.Id.Entry; } catch { continue; }
                if (entry == "LUMINESCE") { kept = true; break; }
            }
            if (!kept) return Task.CompletedTask;

            boss.DarkPassiveSuppressed = true;
            if (_logs++ < 12)
            {
                Logger.Log("[CharBoss] 冷却剂：被锁定的玩家回合结束时**手里留着冷光**" +
                           "→ 故障机器人本回合的**暗黑充能球不使用被动**" +
                           $"（{boss.Def.Key}，现有球 {CharacterBosses.UI.BossOrbDisplay.DescribeOrbs(boss)}）");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 冷却剂判定失败: " + e.Message);
        }
        return Task.CompletedTask;
    }

    /// <summary>她的回合结束时把「本回合不打被动」收掉（下一回合照常）。</summary>
    public override Task AfterSideTurnEnd(PlayerChoiceContext choiceContext,
                                          MegaCrit.Sts2.Core.Combat.CombatSide side,
                                          System.Collections.Generic.IEnumerable<Creature> participants)
    {
        try
        {
            if (side != MegaCrit.Sts2.Core.Combat.CombatSide.Enemy) return Task.CompletedTask;
            var boss = Owner?.Monster as CharBossMonster;
            if (boss != null && boss.DarkPassiveSuppressed)
            {
                boss.DarkPassiveSuppressed = false;
                Logger.Log("[CharBoss] 冷却剂：她这回合结束了，黑球被动恢复正常");
            }
        }
        catch { /* 忽略 */ }
        return Task.CompletedTask;
    }
}
