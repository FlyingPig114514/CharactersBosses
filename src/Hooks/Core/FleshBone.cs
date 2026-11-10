using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Monsters;

namespace CharacterBosses.Core;

/// <summary>
/// 第一幕亡灵契约师第二套设计「**血肉骨**」（Key <c>NECROBINDER_FLESH</c>，用户 2026-11-10）的
/// 数值预测：**血肉戏法**的 9 点、**招架盾**的 6 点、以及多人「苦难」传给别人的那几刀。
///
/// 为什么集中在这里：这三笔都**不是牌面变量**，而是「她身上某层能力 / 遗物 + 那份 plan」推导出来的，
/// 而且意图有**三条重算路径**（规划期 / `BladesGrowth.RecomputeAll` / `RefreshPlannedIntents`），
/// 各写一遍必然漏一条（本项目踩过好几次）。所以统一在这里按 `boss.Plan` 现算。
///
/// 反编译依据（`sts2_111.dll`）：
/// <code>
/// // ParryingShield（招架盾）—— 遗物，AfterSideTurnEnd
/// new BlockVar(10m, ValueProp.Unpowered), new DamageVar(6m, ValueProp.Unpowered)
/// if (participants.Contains(Owner.Creature) &amp;&amp; Owner.Creature.Block >= DynamicVars.Block.BaseValue)
///     CreatureCmd.Damage(ctx, HittableEnemies 里随机一个, DynamicVars.Damage, Owner.Creature)
///
/// // IntimidatingHelmet（骇人头盔）—— 遗物，AfterCardPlayed
/// new BlockVar(4m, ValueProp.Unpowered), new EnergyVar(2)
/// if (cardPlay.Card.Owner == Owner &amp;&amp; cardPlay.Resources.EnergyValue >= DynamicVars.Energy.IntValue)
///     CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, null)
///
/// // SleightOfFleshPower（血肉戏法）—— 她身上那层能力，AfterPowerAmountChanged
/// if (amount != 0 &amp;&amp; power.GetTypeForAmount(amount) == PowerType.Debuff
///     &amp;&amp; power.Owner.IsEnemy &amp;&amp; applier == Owner &amp;&amp; !(power is ITemporaryPower))
///     CreatureCmd.Damage(ctx, power.Owner, Amount, ValueProp.Unpowered, Owner)   // ← 无来源伤害
/// </code>
/// ⚠ `power.Owner.IsEnemy` 是**绝对阵营**判断（`Side == CombatSide.Enemy`），而我们这只 boss 给的负面
///   落在**玩家**身上（Side == Player）⇒ 原版那层能力对她是**不会触发**的，见
///   <c>Patches.SleightOfFleshBossPatch</c>（先压掉原版、再照原样替她补一次）。
/// </summary>
public static class FleshBone
{
    public const string Key = "NECROBINDER_FLESH";

    /// <summary>招架盾的格挡门槛与伤害（反编译 `ParryingShield` 的 `BlockVar` / `DamageVar`）。</summary>
    public const int ParryBlockThreshold = 10;
    public const int ParryDamageAmount = 6;

    /// <summary>骇人头盔：耗能 ≥ 这个数的牌打出去给 4 点格挡。</summary>
    public const int HelmetEnergyThreshold = 2;
    public const int HelmetBlock = 4;

    /// <summary>「吞噬生命」每次给她多少格挡（用户设计）。</summary>
    public const int DevourBlock = 4;

    /// <summary>这一只 Boss 是不是「血肉骨」。</summary>
    public static bool IsFlesh(CharBossMonster? boss)
    {
        try { return boss != null && string.Equals(boss.SpawnVariantKey, Key, StringComparison.Ordinal); }
        catch { return false; }
    }

    /// <summary>
    /// 这张牌会给**敌人**上几种负面（按种类数，不按层数）。
    ///
    /// 多人「苦难」把被锁定玩家身上的负面**逐种复制**给其他玩家，而每复制一种就触发一次血肉戏法
    /// ⇒ 其他玩家会各自挨 `9 × 种类数`。所以这里按种类数算。
    /// </summary>
    public static int DebuffKinds(CardModel? card)
    {
        if (card == null) return 0;
        int kinds = 0;
        bool selfTargeted = false;
        try
        {
            selfTargeted = card.TargetType == TargetType.Self;
        }
        catch { /* 读不到就当不是 */ }

        try
        {
            foreach (var v in card.DynamicVars.Values)
            {
                var type = v.GetType();
                if (!type.IsGenericType) continue;
                if (type.GetGenericTypeDefinition() != typeof(MegaCrit.Sts2.Core.Localization.DynamicVars.PowerVar<>)) continue;

                var powerType = type.GetGenericArguments()[0];
                var power = ModelDb.GetById<PowerModel>(ModelDb.GetId(powerType));
                if (power == null) continue;

                var amount = v.BaseValue;
                var kind = power.GetTypeForAmount(amount);
                bool harmful = kind == PowerType.Debuff || amount < 0m;
                if (harmful && !selfTargeted) kinds++;
            }
        }
        catch { /* 读不到就当 0 */ }
        return kinds;
    }

    /// <summary>
    /// 她每给一个负面效果，血肉戏法对那个目标造成几点（0 = 她身上没有这层能力）。
    ///
    /// 规划期（她还没打出「血肉戏法」那张牌）也要能算：去 `Plan` 里找排在**这张牌之前**的
    /// 「血肉戏法」，读它的卡面数值（升级版自动算）。与她层数优先，没有再退回计划。
    /// </summary>
    public static int SleightPerHit(CharBossMonster? boss, CardModel? card = null)
    {
        try
        {
            int onHer = boss?.Creature?.GetPowerAmount<SleightOfFleshPower>() ?? 0;
            if (onHer > 0) return onHer;
        }
        catch { /* 继续看计划 */ }

        try
        {
            var plan = boss?.Plan;
            if (plan == null) return 0;
            foreach (var play in plan)
            {
                var planned = play?.Card;
                if (planned == null) continue;
                if (card != null && ReferenceEquals(planned, card)) break;     // 只算排在前面的
                string entry = string.Empty;
                try { entry = planned.Id.Entry; } catch { continue; }
                if (!string.Equals(entry, "SLEIGHT_OF_FLESH", StringComparison.Ordinal)) continue;

                int value = 0;
                try { value = (int)Math.Round((double)planned.DynamicVars["SleightOfFleshPower"].BaseValue); }
                catch
                {
                    try { value = (int)Math.Round((double)planned.DynamicVars["SleightOfFleshPower"].BaseValue); }
                    catch { value = 0; }
                }
                if (value > 0) return value;
            }
        }
        catch { /* 忽略 */ }
        return 0;
    }

    /// <summary>
    /// **回合结束时她会有多少格挡**（招架盾的门槛用它）。
    ///
    /// 组成（用户口径：「记得考虑她特殊效果和骇人头盔为她带来的格挡」）：
    ///   · 她**现在**的格挡（她自己回合开始会清零，所以这份只在她的回合里、以及玩家回合的
    ///     "上回合留下的" 情况下有意义 —— 为了保守，只算 0 起算的计划部分 + 当前格挡里未被清掉的部分）；
    ///   · plan 里剩下的牌各自的 `Block` 变量（防御 5 / 违逆 6 / 吸引仇恨 7 …）；
    ///   · 骇人头盔：plan 里耗能 ≥2 的牌，每张 +4；
    ///   · 吞噬生命：plan 里会施加负面的**种类数** ×4。
    /// </summary>
    public static int EndOfTurnBlock(CharBossMonster? boss)
    {
        if (boss == null) return 0;
        int block = 0;

        // ★ 2026-11-10（用户实测：「招架盾预测有误，即使她会获得的格挡不足也会显示要造成伤害」）：
        //   以前无条件把她**当前**格挡也算进去 —— 但格挡是在**她自己回合开始时清空**的，
        //   所以在玩家回合（= 意图显示的时刻）她身上那份是**上回合剩下的、下一回合开始就没了**，
        //   算进去就会虚高。只有「现在正轮到她出手（她自己回合内、每帧重算）」时，当前格挡才是真账。
        bool herTurn = false;
        try
        {
            herTurn = boss.CombatState?.CurrentSide == MegaCrit.Sts2.Core.Combat.CombatSide.Enemy;
        }
        catch { herTurn = false; }

        if (herTurn)
        {
            try { block += Math.Max(0, boss.Creature?.Block ?? 0); } catch { /* 忽略 */ }
        }

        bool helmet = HasRelic(boss, "INTIMIDATING_HELMET");
        int fromCards = 0, fromHelmet = 0, fromDevour = 0;
        try
        {
            foreach (var play in boss.Plan)
            {
                var card = play?.Card;
                if (card == null) continue;
                int cardBlock = BossScriptPlanner.BlockGainOf(card);
                fromCards += cardBlock;
                block += cardBlock;

                if (helmet && play.Cost >= HelmetEnergyThreshold)
                {
                    fromHelmet += HelmetBlock;
                    block += HelmetBlock;
                }

                // 吞噬生命：每施加一种负面 = 4 点格挡
                int kinds = DebuffKinds(card);
                if (kinds > 0)
                {
                    fromDevour += DevourBlock * kinds;
                    block += DevourBlock * kinds;
                }
            }
        }
        catch { /* 忽略 */ }

        if (_blockLogs++ < 30)
        {
            Logger.Log($"[CharBoss] 招架盾预测：{(herTurn ? "她回合内" : "玩家回合")} 当前格挡{(herTurn ? (boss.Creature?.Block ?? 0).ToString() : "不计")}" +
                       $" + 牌 {fromCards} + 骇人头盔 {fromHelmet} + 吞噬生命 {fromDevour} = {block}（门槛 {ParryBlockThreshold}）");
        }
        return block;
    }

    /// <summary>招架盾预测日志限流。</summary>
    private static int _blockLogs;

    /// <summary>
    /// 招架盾这一手会造成几点伤害（0 = 不显示）。**无来源伤害**（`ValueProp.Unpowered`），
    /// 所以意图用 Raw（不吃力量/易伤，也不是「攻击牌」）。
    /// </summary>
    public static int ParryDamage(CharBossMonster? boss)
    {
        try
        {
            if (!HasRelic(boss, "PARRYING_SHIELD")) return 0;
            return EndOfTurnBlock(boss) >= ParryBlockThreshold ? ParryDamageAmount : 0;
        }
        catch { return 0; }
    }

    /// <summary>
    /// 多人：**被这只 Boss 锁定的那位玩家**身上有几种负面（苦难会把它逐种传给其他玩家 ⇒
    /// 每个其他玩家会挨 `血肉戏法 × 种类数`）。
    /// </summary>
    public static int LockedPlayerDebuffKinds(CharBossMonster? boss)
    {
        try
        {
            var locked = BossLockOn.TargetFor(boss!);
            var creature = locked?.Creature;
            if (creature == null) return 0;

            int kinds = 0;
            foreach (var power in creature.Powers)
            {
                if (power == null) continue;
                try
                {
                    if (power.TypeForCurrentAmount == PowerType.Debuff) kinds++;
                }
                catch { /* 忽略这一层 */ }
            }
            return kinds;
        }
        catch { return 0; }
    }

    /// <summary>她身上有没有某件遗物（按 entry 判）。</summary>
    public static bool HasRelic(CharBossMonster? boss, string entry)
    {
        try
        {
            var relics = boss?.Shadow?.Relics;
            if (relics == null) return false;
            for (int i = 0; i < relics.Count; i++)
            {
                var relic = relics[i];
                if (relic == null) continue;
                try { if (string.Equals(relic.Id.Entry, entry, StringComparison.Ordinal)) return true; }
                catch { /* 忽略这一件 */ }
            }
        }
        catch { /* 忽略 */ }
        return false;
    }
}
