#if STS2_111   // 观者 Boss 只在 0.113 构建里（用户：107 不加这个 Boss）
using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using CharacterBosses.Powers;

namespace CharacterBosses.Core;

/// <summary>
/// **斗蛐蛐（CricketFight）模式下的虔信规则**（用户 2026-11-15）：
/// > 斗蛐蛐模式中，对于神格观的特殊效果：
/// >   · 如果对手是**角色 Boss**，其每打出一张牌，观者获得一层真言；
/// >   · 否则，观者在战斗开始时获得 3 层**虔信**（buff，回合开始时获得 3 真言）。
///
/// 两种情况其实共用同一套机制，区别只在「真言从哪来」：
///   · **对手是角色 Boss**：`BossDevotionPower.AfterCardPlayed` 已经在算「任意真人玩家/影子玩家出的牌」 ⇒
///     对方 Boss 的牌`Owner` 是**对方自己的影子玩家**（不是我们的），天然被计入 ⇒ **不需要额外做什么**；
///   · **对手不是角色 Boss**（普通怪物 / 观者自己单挑）：没人出牌可算 ⇒ 开局给她挂 3 层
///     <see cref="WatcherFaithPower"/>，她自己回合开始就 +3 真言（同一条链：`AddMantraAsync`）。
///
/// 判据「对手是不是角色 Boss」：看战斗里**除自己以外**还有没有第二只 `CharBossMonster`。
/// </summary>
internal static class WatcherCricketRule
{
    private const int FaithOnStart = 3;

    /// <summary>战斗开始时调一次（幂等：身上已经有虔信就不再叠加）。</summary>
    internal static async Task ApplyAtCombatStart(Monsters.CharBossMonster boss)
    {
        try
        {
            if (boss == null) return;
            if (!CricketCompat.Installed) return;          // 只有斗蛐蛐模式需要这条规则
            if (boss.Creature == null) return;

            if (HasCharacterBossOpponent(boss))
            {
                Logger.Log("[CharBoss] 斗蛐蛐·虔信：对手是角色 Boss ⇒ 他每打出一张牌观者获得 1 层真言" +
                           "（由 BossDevotionPower 计）");
                return;
            }

            var existing = boss.Creature.GetPower<WatcherFaithPower>();
            if (existing != null)
            {
                Logger.Log("[CharBoss] 斗蛐蛐·虔信：她已经带着虔信，跳过");
                return;
            }

            await MegaCrit.Sts2.Core.Commands.PowerCmd.Apply<WatcherFaithPower>(
                new ThrowingPlayerChoiceContext(), boss.Creature, FaithOnStart, boss.Creature, null, false);
            Logger.Log($"[CharBoss] 斗蛐蛐·虔信：对手不是角色 Boss ⇒ 开战给她 {FaithOnStart} 层虔信" +
                       $"（她每回合开始获得 {FaithOnStart} 层真言）");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 斗蛐蛐·虔信规则失败: " + e.Message); }
    }

    /// <summary>场上除自己以外还有没有第二只我们的 Boss（= 对手是角色 Boss）。</summary>
    private static bool HasCharacterBossOpponent(Monsters.CharBossMonster self)
    {
        try
        {
            var combat = self.CombatState;
            if (combat == null) return false;

            foreach (var c in combat.Enemies)
            {
                if (c?.Monster is Monsters.CharBossMonster other && !ReferenceEquals(other, self)) return true;
            }
            var players = combat.Players;
            if (players != null)
            {
                foreach (var p in players)
                {
                    if (p?.Creature?.Monster is Monsters.CharBossMonster other && !ReferenceEquals(other, self)) return true;
                }
            }
        }
        catch { /* 忽略 */ }
        return false;
    }
}
#endif
