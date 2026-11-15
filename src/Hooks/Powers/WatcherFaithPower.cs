#if STS2_111   // 观者 Boss 只在 0.113 构建里（用户：107 不加这个 Boss）
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Core;

namespace CharacterBosses.Powers;

/// <summary>
/// 「**虔信**」buff（用户 2026-11-15）—— 挂在**玩家**身上，回合开始时给观者真言。
///
/// 这个 power 有两个来源，语义是同一个（用户两条需求合起来才看得懂）：
///   · **斗蛐蛐模式**（对手不是角色 Boss 时）：「观者在战斗开始时获得 3 层虔信
///     （buff，**回合开始时获得 3 真言**）」⇒ 直接挂在观者自己身上，谁身上有就谁产生真言；
///   · **恐惧模式的「观者的支配」**：「所有敌人获得 2 层虔信」⇒ 挂在每位玩家身上，
///     他们回合开始 ⇒ 观者拿真言（支配是给 Boss 加码的，和愚者「塞 2 张悲伤」同向）。
///
/// 所以这里不区分来源：**谁身上有虔信，谁的回合开始就给观者 `Amount` 层真言**。
/// </summary>
public sealed class WatcherFaithPower : PowerModel
{
    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldPlayVfx => false;

    public override async Task AfterSideTurnStart(CombatSide side,
                                                 IReadOnlyList<Creature> participants,
                                                 ICombatState combatState)
    {
        try
        {
            int amount = (int)Math.Round((double)Amount);
            if (amount <= 0) return;

            // 谁的回合开始？（这个 power 挂在谁身上就算谁）
            var ownerSide = Owner?.Side;
            if (ownerSide != side) return;

            var boss = WatcherBossOwning();
            if (boss == null) return;

            var devotion = boss.Creature?.GetPower<BossDevotionPower>();
            if (devotion == null) return;

            await devotion.AddMantraAsync(new ThrowingPlayerChoiceContext(), amount);
            Logger.Log($"[CharBoss] 虔信：{Owner?.Name} 的回合开始 ⇒ 观者获得 {amount} 层真言");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 虔信 buff 结算失败: " + e.Message); }
    }

    /// <summary>场上那只带「虔信」特殊效果的我们的 Boss（她是真言唯一的收取方）。</summary>
    private Monsters.CharBossMonster? WatcherBossOwning()
    {
        try
        {
            var enemies = Owner?.CombatState?.Enemies;
            if (enemies == null) return null;
            foreach (var c in enemies)
            {
                if (c?.Monster is Monsters.CharBossMonster boss
                    && boss.Creature?.GetPower<BossDevotionPower>() != null) return boss;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }
}
#endif
