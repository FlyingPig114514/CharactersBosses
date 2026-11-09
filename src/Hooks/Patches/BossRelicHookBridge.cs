using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.ValueProps;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// 让 Boss 的**遗物**也能真正生效 —— 目前只服务一层「愤怒战」的两件遗物：
///
/// · **卡戎之灰** `CharonsAshes`：每消耗一张牌 → 对所有敌人 3 点伤害（`Unpowered`，可被格挡）。
///   她的「烙印 / 坚毅」每回合消耗一张手牌，按用户设计这 3 点是要打出来的。
/// · **奥利哈钢** `Orichalcum`：回合结束时若没有格挡 → 获得 6 格挡。
///
/// ## 为什么要「先压制原版、再由我们自己做一次」（2026-10-23 实测教训）
/// 第一版是「只在旁边补跑一次遗物钩子」，结果**玩家吃了两次 3 点伤害**（用户实测：
/// 「卡戎之灰会对玩家造成 3 点伤害 2 次」）—— 说明这两条钩子里至少有一条对影子 Player 的遗物
/// **本来就会派发一次**，我们再补一次就成了两倍。
/// 于是改成**确定性写法**：
///   1. 原版对「我们的 Boss」的这两条遗物钩子一律**压掉**（前缀返回 false）；
///   2. 由本文件**自己实现一次**同样的效果（数值都从遗物自己的 DynamicVar 读）。
/// 这样无论原版派不派发，最终都**恰好一次**。
///
/// ⚠ 只有 `BossDefinition.FireBossRelicHooks = true` 的 Boss（目前只有愤怒战）走这条路
/// ⇒ 真人玩家、其它 Boss 的行为一点不变。
/// </summary>
public static class BossRelicHookBridge
{
    /// <summary>这个遗物的主人是不是「要我们接管遗物钩子」的 Boss（给压制补丁用）。</summary>
    public static bool IsOurBossRelicPublic(RelicModel? relic) => IsOurBossRelic(relic);

    /// <summary>
    /// 这件遗物的主人是不是**任意一只我们的 Boss**（不要求 FireBossRelicHooks）。
    /// 用于「原版钩子必须压掉、效果由我们统一补」的遗物（目前是弹珠袋）。
    /// </summary>
    public static bool IsOwnedByOurBoss(RelicModel? relic)
    {
        try { return relic?.Owner?.Creature?.Monster is CharBossMonster; }
        catch { return false; }
    }

    /// <summary>这个遗物的主人是不是「要我们接管遗物钩子」的 Boss。</summary>
    private static bool IsOurBossRelic(RelicModel? relic)
    {
        try
        {
            if (relic?.Owner?.Creature?.Monster is not CharBossMonster boss) return false;
            return boss.Def != null && boss.Def.FireBossRelicHooks;
        }
        catch { return false; }
    }

    private static CharBossMonster? BossOf(RelicModel? relic)
    {
        try
        {
            if (relic?.Owner?.Creature?.Monster is not CharBossMonster boss) return null;
            return boss.Def != null && boss.Def.FireBossRelicHooks ? boss : null;
        }
        catch { return null; }
    }

    private static RelicModel? FindRelic(CharBossMonster boss, string entry)
    {
        try
        {
            var relics = boss.Shadow?.Relics;
            for (int i = 0; i < (relics?.Count ?? 0); i++)
            {
                var relic = relics![i];
                if (relic == null) continue;
                string id;
                try { id = relic.Id.Entry; } catch { continue; }
                if (id == entry) return relic;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    // ── 卡戎之灰：消耗一张牌 → 对所有敌人 N 点伤害 ───────────────────────────

    /// <summary>
    /// **待结算的「卡戎之灰」**（v0.2.450）。
    ///
    /// 为什么要改成「登记 + 稍后 await」而不是当场 `_ = …`：
    /// `Hook.AfterCardExhausted` 是 **void** 钩子，想 await 也 await 不了，于是以前只能 fire-and-forget。
    /// 那个任务会**跨过回合结束的校验和** —— 房主赶在 `After enemy turn end` 之前落地、客人没有，
    /// 直接联机不同步（玩家 2026-11-06 的诊断包：两名玩家恰好各差 7 点 = 速行者 4 + 卡戎之灰 3，
    /// 见 docs/plans/desync-2026-11-06-review.md）。
    /// 现在钩子里**只登记**，由 <see cref="FlushPendingAsync"/> 在
    /// 「她这一张牌打完」/「她回合收尾」这两个**两端都会被 await 的**同步点上结算。
    /// </summary>
    private static readonly List<(CharBossMonster Boss, PlayerChoiceContext Ctx, string Card)> Pending = new();

    /// <summary>消耗一张牌之后，替她**登记**一次「卡戎之灰」（真正的伤害见 <see cref="FlushPendingAsync"/>）。</summary>
    internal static void FireAfterCardExhausted(CardModel? card, PlayerChoiceContext? ctx, bool causedByEthereal)
    {
        try
        {
            if (card?.Owner?.Creature?.Monster is not CharBossMonster boss) return;
            if (boss.Def == null || !boss.Def.FireBossRelicHooks || ctx == null) return;
            if (FindRelic(boss, "CHARONS_ASHES") == null) return;      // 没这件遗物就不登记

            string entry;
            try { entry = card.Id.Entry; } catch { entry = card.GetType().Name; }

            lock (Pending)
            {
                if (Pending.Count >= 16) Pending.RemoveAt(0);          // 极端情况别撑爆
                Pending.Add((boss, ctx, entry));
            }
            Logger.Log($"[CharBoss] 卡戎之灰：{entry} 被消耗 → **登记**一次（等她这一张牌打完再结算，" +
                       "避免 fire-and-forget 跨过回合结束的校验和）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 卡戎之灰兜底异常: " + e.Message);
        }
    }

    /// <summary>
    /// 把登记的「卡戎之灰」全部结算掉（**必须被 await**）。
    /// <paramref name="onlyFor"/> 非空时只结算这只 Boss 的（别把别人的抢到这个时点）。
    /// </summary>
    internal static async Task FlushPendingAsync(CharBossMonster? onlyFor = null)
    {
        while (true)
        {
            (CharBossMonster Boss, PlayerChoiceContext Ctx, string Card) item;
            lock (Pending)
            {
                if (Pending.Count == 0) return;
                int idx = onlyFor == null ? 0 : Pending.FindIndex(p => ReferenceEquals(p.Boss, onlyFor));
                if (idx < 0) return;
                item = Pending[idx];
                Pending.RemoveAt(idx);
            }
            await RunCharonsAshesAsync(item.Boss, item.Card, item.Ctx);
        }
    }

    private static async Task RunCharonsAshesAsync(CharBossMonster boss, string cardEntry, PlayerChoiceContext ctx)
    {
        try
        {
            var relic = FindRelic(boss, "CHARONS_ASHES");
            var me = boss.Creature;
            var combat = me?.CombatState;
            if (relic == null || me == null || combat == null || !me.IsAlive) return;

            decimal damage = 3m;
            try { damage = relic.DynamicVars.Damage.BaseValue; } catch { /* 用 3 */ }
            if (damage <= 0) return;

            // 照抄原版：对所有「可命中的敌人」造成 Unpowered 伤害（不吃力量、可被格挡）
            var targets = new List<Creature>();
            try
            {
                foreach (var c in combat.HittableEnemies)
                    if (c != null && c.IsAlive && !ReferenceEquals(c, me)) targets.Add(c);
            }
            catch { /* 取不到就空 */ }
            if (targets.Count == 0) return;

            Logger.Log($"[CharBoss] 卡戎之灰：{cardEntry} 被消耗 → 对 " +
                       $"{string.Join(",", targets.ConvertAll(t => t.Name))} 造成 {damage} 点伤害");
            await CreatureCmd.Damage(ctx, targets, damage, ValueProp.Unpowered, me);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 卡戎之灰兜底失败: " + e.Message);
        }
    }

    // ── 奥利哈钢：回合结束没有格挡 → 获得 N 格挡 ─────────────────────────────

    /// <summary>她回合结束：替她结算「奥利哈钢」（原版那条已被本文件压掉）。</summary>
    internal static async Task FireSideTurnEnd(CharBossMonster boss, PlayerChoiceContext ctx)
    {
        // ★ v0.2.450：**先把她这一回合登记过、还没结算的「卡戎之灰」结清**
        //   —— 这是她回合收尾前的最后一个同步点，保证那 3 点群伤一定算在回合结束校验和之内。
        try { await FlushPendingAsync(boss); }
        catch (Exception e) { Logger.Log("[CharBoss] 卡戎之灰收尾结算失败（忽略）: " + e.Message); }

        try
        {
            if (boss?.Def == null || !boss.Def.FireBossRelicHooks) return;
            var me = boss.Creature;
            if (me == null || !me.IsAlive) return;

            var relic = FindRelic(boss, "ORICHALCUM");
            if (relic == null) return;
            if (me.Block > 0) return;      // 原版条件：回合结束时**没有**格挡才给

            decimal block = 6m;
            try { block = relic.DynamicVars.Block.BaseValue; } catch { /* 用 6 */ }
            if (block <= 0) return;

            await CreatureCmd.GainBlock(me, relic.DynamicVars.Block, null);
            Logger.Log($"[CharBoss] 奥利哈钢：她回合结束没有格挡 → 获得 {block} 点格挡（现在 {me.Block}）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 奥利哈钢兜底失败: " + e.Message);
        }
    }
}

/// <summary>压掉原版的「卡戎之灰」——由 <see cref="BossRelicHookBridge"/> 自己做一次。</summary>
[HarmonyPatch(typeof(CharonsAshes), nameof(CharonsAshes.AfterCardExhausted))]
public static class BossRelicSuppressCharonsAshesPatch
{
    [HarmonyPrefix]
    public static bool Prefix(CharonsAshes __instance, ref Task __result)
    {
        if (!BossRelicHookBridge.IsOurBossRelicPublic(__instance)) return true;
        __result = Task.CompletedTask;   // ⚠ 必须给：目标返回 Task，留 null 会让调用方 await null 崩
        return false;
    }
}

/// <summary>压掉原版的「奥利哈钢」——由 <see cref="BossRelicHookBridge"/> 自己做一次。</summary>
[HarmonyPatch(typeof(Orichalcum), nameof(Orichalcum.BeforeSideTurnEndVeryEarly))]
public static class BossRelicSuppressOrichalcumVeryEarlyPatch
{
    [HarmonyPrefix]
    public static bool Prefix(Orichalcum __instance, ref Task __result)
    {
        if (!BossRelicHookBridge.IsOurBossRelicPublic(__instance)) return true;
        __result = Task.CompletedTask;
        return false;
    }
}

/// <summary>压掉原版的「奥利哈钢」（真正加格挡的那一条）。</summary>
[HarmonyPatch(typeof(Orichalcum), nameof(Orichalcum.BeforeSideTurnEnd))]
public static class BossRelicSuppressOrichalcumPatch
{
    [HarmonyPrefix]
    public static bool Prefix(Orichalcum __instance, ref Task __result)
    {
        if (!BossRelicHookBridge.IsOurBossRelicPublic(__instance)) return true;
        __result = Task.CompletedTask;
        return false;
    }
}

/// <summary>压掉原版的「吊灯」——由 <see cref="BossHookBridge.ChandelierEnergyBonusAtTurnStart"/>
/// 在 `PrepareTurn` 里按卡面加进本回合预算（否则可能双份，或者被 `pcs.Energy = …` 覆盖成 0）。</summary>
[HarmonyPatch(typeof(Chandelier), nameof(Chandelier.AfterSideTurnStart))]
public static class BossRelicSuppressChandelierPatch
{
    [HarmonyPrefix]
    public static bool Prefix(Chandelier __instance, ref Task __result)
    {
        if (!BossRelicHookBridge.IsOurBossRelicPublic(__instance)) return true;
        __result = Task.CompletedTask;
        return false;
    }
}

/// <summary>压掉原版的「弹珠袋」——由 <see cref="BossRelicHookBridge"/> 在战斗开始时自己做一次
/// （否则可能和她身上那件一起触发两次易伤）。</summary>
[HarmonyPatch(typeof(BagOfMarbles), nameof(BagOfMarbles.BeforeSideTurnStart))]
public static class BossRelicSuppressBagOfMarblesPatch
{
    [HarmonyPrefix]
    public static bool Prefix(BagOfMarbles __instance, ref Task __result)
    {
        // ★ v0.2.525（用户 2026-11-09 第三次口径：「**你拦掉原版的，然后仿照原版的在战斗开始时触发一次**。
        //   而且你这易伤又持续三回合了」）：
        //   v0.2.524 放行原版是错的 —— 反编译 `BagOfMarbles.BeforeSideTurnStart`：
        //     `participants.Contains(Owner.Creature) && Owner.PlayerCombatState.TurnNumber <= 1`
        //       → 对 `HittableEnemies` 施加易伤
        //   它的判定是「可命中敌人」而不是阵营，而她把影子玩家的遗物也算成监听者
        //   ⇒ 这条在我们**已经在战斗开始补过一次**之后又给玩家加了一层（实测日志同一次战斗里
        //   「施加后 = 1」紧接着「玩家侧快照 = VULNERABLE:2」）。
        //   所以这里必须**压掉**，只留我们自己那一次（见
        //   `BossHookBridge.SideTurn.GrantBagOfMarblesVulnerableAtCombatStart`）。
        if (!BossRelicHookBridge.IsOwnedByOurBoss(__instance)) return true;
        __result = Task.CompletedTask;   // ⚠ 必须给：目标返回 Task，留 null 会让调用方 await null 崩
        return false;
    }
}

/// <summary>
/// 压掉原版的「覆甲在第 1 回合玩家回合开始给格挡」（`PlatingPower.BeforeSideTurnStart`）。
///
/// 反编译：`if (side != Player) return; if (Owner.IsPlayer) return; if (RoundNumber &gt; 1) return;
///          return CreatureCmd.GainBlock(Owner, Amount, Unpowered, null);`
/// 也就是「敌人身上有覆甲 ⇒ 第 1 回合**玩家**回合开始时给等量格挡」。
///
/// 我们给 Boss 补覆甲（护喉甲）是**异步 fire-and-forget**（`TaskHelper.RunSafely`），
/// 可能晚于这个时点 ⇒ 多人实测「一端 +12 格挡、另一端没有」（10 vs 22）。
/// 改成确定性写法：这条对**我们的 Boss**一律压掉，由
/// <c>BossHookBridge.GrantGorgetPlatingAtCombatStart</c> 在挂上覆甲之后自己补一次等量格挡。
///
/// 对我们 Boss 的其它覆甲来源没有影响：玩家回合永远在第 1 回合**先**于她的回合，
/// 所以「她打出岩石铠甲拿到覆甲」时 `RoundNumber` 早就不满足 `&lt;= 1`，原版本来也不会给这笔格挡。
/// </summary>
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Models.Powers.PlatingPower),
              nameof(MegaCrit.Sts2.Core.Models.Powers.PlatingPower.BeforeSideTurnStart))]
public static class BossRelicSuppressPlatingTurnStartPatch
{
    [HarmonyPrefix]
    public static bool Prefix(MegaCrit.Sts2.Core.Models.Powers.PlatingPower __instance, ref Task __result)
    {
        try
        {
            if (__instance?.Owner?.Monster is not CharBossMonster) return true;
            __result = Task.CompletedTask;   // ⚠ 必须给：目标返回 Task
            return false;
        }
        catch { return true; }
    }
}

/// <summary>
/// 压掉原版「明耀酊剂」留下的 `RadiancePower.AfterEnergyReset`（每个回合开始 +1 能量）。
///
/// 炼药猎喝「明耀酊剂」后，那 3 个回合每回合 +1 能量。我们是在 `PrepareTurn` 里**主动算进规划**的
/// （否则规划期按 2 费算，第二回合的「夜魇」会被判成打不起 —— 用户 2026-10-25 实测）。
/// 既然规划期已经给了，这里就把原版那条压掉，免得同一回合加两次。
/// 只压**我们的 Boss**，真人玩家喝明耀酊剂一点不变。
/// </summary>
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Models.Powers.RadiancePower),
              nameof(MegaCrit.Sts2.Core.Models.Powers.RadiancePower.AfterEnergyReset))]
public static class BossRadianceSuppressPatch
{
    [HarmonyPrefix]
    public static bool Prefix(MegaCrit.Sts2.Core.Models.Powers.RadiancePower __instance, ref Task __result)
    {
        try
        {
            if (__instance?.Owner?.Monster is not CharBossMonster) return true;
            __result = Task.CompletedTask;
            return false;
        }
        catch { return true; }
    }
}

/// <summary>
/// 把 `Hook.AfterCardExhausted` 的最后一步接上：每张牌被消耗后，替「我们的 Boss」结算一次卡戎之灰。
/// （原版对遗物的那条已经在上面的前缀里压掉了，所以最终只会结算一次。）
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardExhausted))]
public static class BossRelicExhaustPatch
{
    [HarmonyPostfix]
    public static void Postfix(PlayerChoiceContext choiceContext, CardModel card, bool causedByEthereal)
    {
        try { BossRelicHookBridge.FireAfterCardExhausted(card, choiceContext, causedByEthereal); }
        catch (Exception e) { Logger.Log("[CharBoss] 消耗钩子桥接异常: " + e.Message); }
    }
}
