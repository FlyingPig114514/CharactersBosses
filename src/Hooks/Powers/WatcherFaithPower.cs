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
/// 「**虔信**」buff（用户 2026-11-15；2026-11-19 订正挂载对象）—— 回合开始时给观者真言。
///
/// 这个 power 有两个来源，语义是同一个：
///   · **斗蛐蛐模式**（对手不是角色 Boss 时）：「观者在战斗开始时获得 3 层虔信
///     （buff，**回合开始时获得 3 真言**）」⇒ 挂在观者自己身上；
///   · **恐惧模式的「观者的支配」**：「所有敌人获得 **3** 层虔信」⇒ 挂在**每个敌人**身上
///     （★ v0.2.781 修正：以前错挂到**玩家**身上，既是给玩家平白上了一个 debuff 图标，
///      也不符合「所有敌人获得虔信」这句口径 —— 用户 2026-11-19 截图实证 + 订正）。
///
/// 所以这里不区分来源：**谁身上有虔信，谁的回合开始就给观者 `Amount` 层真言**。
/// （⚠ 普通房间里场上没有观者 ⇒ 挂了也不产生真言；只有观者上场那场才吃得到。）
/// </summary>
public sealed class WatcherFaithPower : PowerModel
{
    /// <summary>
    /// ★ 2026-11-19（用户）：「这里的虔信不应该是 debuff，不应该显示成红的框」
    ///   ⇒ 与恐惧模式支配那层仿制版（`DominionPowers.WatcherDominionDevotionPower`）保持一致，改成 `Buff`。
    /// </summary>
    public override PowerType Type => PowerType.Buff;

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

            if (boss.Creature?.GetPower<BossDevotionPower>() == null) return;
            var shadow = boss.Shadow;
            if (shadow == null) return;

            // ⚠ v0.2.681：必须走「加真言 + 满 10 层结算」这一条唯一入口，
            //   否则这条路径上永远进不了神格（结算逻辑在 BossDevotionPower 里）。
            await BossDevotionPower.GainMantraAndResolveAsync(boss, shadow, amount, null, null);
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
