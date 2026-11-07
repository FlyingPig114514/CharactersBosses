using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace CharacterBosses.Powers;

/// <summary>
/// 第三幕「无色储君」的特殊效果「**共鸣**」。
///
/// 用户设计（最终版）：下面三个时机各算一次「印牌」——
///   1. **每回合开始时 1 次**
///   2. **打出君权自授时**
///   3. **打出发现时**
/// 每次「印牌」要做两件事：
///   · **触发储君的「创世之柱」**（按它的层数补格挡）；
///   · **让「超质量体」的伤害涨一档**（它按"自己生成过几张牌"加伤，升级后每张 +4）。
///
/// 实现：不真的造牌（用户要求把这些印牌拦掉），而是**只补「生成事件」**——
/// 原版 <c>AddGeneratedCardsToCombat</c> 每张牌就是做这两步：
/// <code>
/// CombatManager.Instance.History.CardGenerated(combatState, card, creator);   // 超质量体靠这条计数
/// await Hook.AfterCardGeneratedForCombat(combatState, card, creator);        // 创世之柱靠这条结算
/// </code>
/// 我们把这两步直接跑一遍，等于"发生了一次印牌但没往手里塞牌"。
/// </summary>
public sealed class ResonancePower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    /// <summary>
    /// 1. **她每回合开始时** 1 次（用户订正：不是玩家回合）。
    ///
    /// ⚠ 必须在 <c>AfterSideTurnStart</c>，不能用 <c>BeforeSideTurnStart</c>：
    /// 原版「回合开始会清空格挡」那一步正好夹在这两个钩子之间，
    /// 起早了这回合刚加的格挡（创世之柱 + 君王矿石）会立刻被清掉 ——
    /// 用户实测「起的防马上就消失了」。
    /// </summary>
    public override async Task AfterSideTurnStart(CombatSide side,
                                                  System.Collections.Generic.IReadOnlyList<Creature> participants,
                                                  ICombatState combatState)
    {
        if (Owner == null || side != Owner.Side) return;
        await FireGeneration("她回合开始", creditHistory: true, withdrawPending: true);
    }

    /// <summary>
    /// 2/3. **玩家生成卡牌时** 各一次 —— 用户设计「共鸣」的核心。
    ///
    /// 之前的写法是盯着玩家打「君权自授／发现」这两张牌（<c>BeforeCardPlayed</c>），
    /// 但玩家生成牌的来源远不止这两张（机关盒、光谱偏移、药水、遗物…），所以经常不触发（用户实测）。
    /// 现在改成挂原版的**生成钩子** <c>AbstractModel.AfterCardGeneratedForCombat</c>
    /// （创世之柱 <c>PillarOfCreationPower</c>、君王矿石 <c>Regalite</c> 都挂在这条上），
    /// 凡是玩家（不是她自己）生成的牌，就替她把一次「印牌事件」补出来。
    ///
    /// ⚠ 不会递归：<see cref="FireGeneration"/> 里是用 <c>bossPlayer</c> 当 creator 去广播的，
    /// 本方法开头就把「她自己生成的」挡掉了。
    /// </summary>
    public override async Task AfterCardGeneratedForCombat(CardModel card, MegaCrit.Sts2.Core.Entities.Players.Player creator)
    {
        try
        {
            if (Owner == null || creator == null) return;
            if (ReferenceEquals(creator.Creature, Owner)) return;   // 她自己印的由她自己的创世之柱结算

            // **对应型**（多人）：只认「锁定这只 Boss」的真人玩家生成的牌
            //（单人恒为 true；其它 Boss 影子 Player 生成的牌一律不算）
            if (!Core.BossLockOn.SpecialEffectAppliesTo(Owner.Monster as Monsters.CharBossMonster, creator)) return;

            await FireGeneration("玩家生成卡牌", creditHistory: false, withdrawPending: false);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 共鸣（生成触发）失败: " + e.Message);
        }
    }

    /// <summary>
    /// 补一次「印牌事件」：不造牌，只跑历史记账 + 创世之柱那条钩子。
    /// </summary>
    /// <param name="creditHistory">
    /// 要不要往战斗历史里记一条「她生成的牌」。
    ///
    /// **玩家触发的那次不记**（用户 2026-10-24 实测截图：玩家印牌时「超质量体」的伤害也跟着涨）。
    /// 原因（反编译）：
    /// <code>
    /// // Supermassive 的伤害倍率
    /// CalculatedDamage.WithMultiplier((card, _) => History.Entries.OfType&lt;CardGeneratedEntry&gt;()
    ///     .Count(c => c.Creator == card.Owner));
    /// </code>
    /// 而 <c>PillarOfCreationPower.AfterCardGeneratedForCombat</c> 的判定是
    /// <c>creator.Creature == Owner</c>（只认「她自己的生物当生成者」）。
    /// 所以这两件事必须拆开：**创世之柱**照旧用 `bossPlayer` 当 creator 去广播（否则不触发），
    /// **历史那条**玩家触发时不写（否则超质量体把这笔算成她自己的印牌）。
    /// </param>
    /// <param name="withdrawPending">要不要回收意图里预补的「超质量体 +N」（只有她自己的印牌才回收）。</param>
    private async Task FireGeneration(string why, bool creditHistory, bool withdrawPending)
    {
        try
        {
            var combat = Owner?.CombatState;
            var bossPlayer = Owner?.Player;
            if (combat == null || bossPlayer == null) return;

            var history = CombatManager.Instance?.History;
            // 传哪张卡不影响计数，但**必须是战斗内的可变实例**：
            // 传 canonical（ModelDb 里那份原件）会被原版拒绝 ——
            // 实测报错「Canonical model of type PillarOfCreation used in incorrect place」。
            var canonical = ModelDb.GetById<CardModel>(ModelDb.GetId(typeof(MegaCrit.Sts2.Core.Models.Cards.PillarOfCreation)));
            if (canonical == null) return;
            var proof = combat.CreateCard(canonical, bossPlayer);
            if (proof == null) return;

            if (creditHistory) history?.CardGenerated(combat, proof, bossPlayer);
            await Hook.AfterCardGeneratedForCombat(combat, proof, bossPlayer);

            // 把意图里提前补的那份「超质量体 +4」收回去（一回合几次就收几次）
            if (withdrawPending && Owner.Monster is Monsters.CharBossMonster boss) boss.NoteGenerationFired();

            Flash();
            string extra = creditHistory ? " + 超质量体加伤" : "，不计入超质量体";
            Logger.Log($"[CharBoss] 共鸣：{why} → 触发一次「印牌」（创世之柱{extra}）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 共鸣触发印牌失败: " + e.Message);
        }
    }
}
