using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Potions;
using CharacterBosses.Core;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// 「**瓶中精灵**」对 Boss 生效（用户 2026-10-25 两次实测：炼药猎带着它却不救命）。
///
/// 原版实现（反编译 `FairyInABottle`）：
/// <code>
/// public override bool ShouldDie(Creature creature)   // 死亡判定时不让他死
/// public override Task AfterPreventingDeath(Creature) // 丢弃这瓶药水，回复 30% 最大生命
/// </code>
/// 这两条是 `AbstractModel` 的钩子，靠 `Hook.ShouldDie` 广播给「`CombatState.Players` 里每个玩家
/// 身上的遗物/药水/Power」。**影子 Player 不在 Players 里** ⇒ 她的药水永远收不到。
///
/// ⚠ 第一版写的是 **postfix + `ref preventer`（out 参数）**，实测**完全没生效**
///   （Harmony 对 `out AbstractModel` 的绑定在这个签名上没成，失败信息只会进 `godot.log`，
///    `mod_log` 里一个字都没有）。现在改成 **prefix**：
///   · 我们的 Boss 本该这次死亡、且栏位里真有瓶中精灵 → `__result = false` 并 `return false`
///     （跳过原版那套「找 preventer」的逻辑，我们自己处理掉这瓶药水）；
///   · 其它任何情况 `return true`，原版照常。
///
/// ## ★ v0.2.329：整个复活流程必须被**死亡流程 await**（联机不同步修复）
///
/// 玩家反馈（2026-11-02，联机）：
/// > 三层药水猎人在联机时复活会产生断联机 BUG……复活后的「清除负面状态」没有被出牌流程等待，
/// > 导致两端在不同结算阶段计算校验。
///
/// 原来是 `TaskHelper.RunSafely(SaveAsync(...))` —— **发完就不管**：回血与清负面都是异步的，
/// 可 `Hook.ShouldDie` 是同步方法，返回之后死亡流程立刻往下走 ⇒ 一台机器已经算完、另一台还在等
/// `PowerCmd.Remove`，两端校验自然对不上。
///
/// 反编译死亡流程（`CreatureCmd.KillWithoutCheckingWinCondition`）：
/// <code>
/// preventer = null;
/// if (force || creature.MaxHp &lt;= 0 || Hook.ShouldDie(runState, combatState, creature, out preventer)) { …正常死亡… }
/// if (recursion &gt;= 10) throw …;
/// await Hook.AfterDeath(runState, combatState, creature, wasRemovalPrevented: true, 0f);
/// …
/// await Hook.AfterPreventingDeath(runState, combatState, preventer, creature);   // ← 这里会被 await！
/// </code>
/// 而 `Hook.AfterPreventingDeath` 的本体只是
/// `if (listeners.Contains(preventer)) { await preventer.AfterPreventingDeath(creature); preventer.InvokeExecutionFinished(); }`
/// （`preventer == null` 时什么也不做，也不会 NRE）。
///
/// 所以正确做法是：**把回血 + 清负面挂到 `Hook.AfterPreventingDeath` 的前缀上**，
/// 用 `ref Task __result` 把这个完整的 Task 交回给调用方 ⇒ 死亡流程会 `await` 它，
/// 两端在同一步结算完再继续（与「原版 ShouldDie 登记 preventer + 死亡流程 await AfterPreventingDeath」
/// 完全等价）。
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.ShouldDie))]
public static class BossFairyInABottlePatch
{
    /// <summary>
    /// (Boss → 那瓶药水)：**已经同步丢掉了药水、也已经同步回了血**，只欠「清负面」这一步。
    ///
    /// ★ 2026-11-13（v0.2.617，玩家反馈「『我的小钱钱』把炼药猎打死时，她的瓶中精灵没有复活」）：
    ///   老实现把**整个**复活（丢药水 + 回 30%）都推给了 `Hook.AfterPreventingDeath` 前缀，
    ///   而这一步有两个「可能整段不生效」的入口：
    ///     ① 前缀里要**重新查一遍** `FairyOf(boss)` —— 两次查询之间药水但凡变了一下（多段伤害里
    ///        第二次判死、栏位重排、别的模组动过药水），查不到就 `return true` ⇒ 原版兜底（`preventer`
    ///        是 null ⇒ 什么也不做）⇒ **一滴血都不回**，而 `ShouldDie` 已经说过「不许死」了
    ///        ⇒ 她停在 0 血、`IsDead` 为真 ⇒ 死亡流程**递归**再判一次 ⇒ 这一次没有药水了 ⇒ 真死。
    ///     ② 只要中间任何一个 await/钩子抛异常，那段 Task 就整个不跑。
    ///   现在改成：**判死那一刻同步把药水丢掉、同步把血回满**（`Creature.HealInternal`，
    ///   与 `CreatureCmd.Heal` 同一份底层实现，只是不等动画），`AfterPreventingDeath` 那一段
    ///   只负责「清负面」——它跑不跑都不影响复活本身。多段伤害里第二刀再判死时药水已不在
    ///   ⇒ 走原版（与玩家自己的瓶中精灵完全一致）。
    /// </summary>
    private static readonly Dictionary<CharBossMonster, PotionModel> PendingDebuffClear = new();

    [HarmonyPrefix]
    public static bool Prefix(Creature creature, ref bool __result)
    {
        try
        {
            if (creature?.Monster is not CharBossMonster boss) return true;
            if (creature.CurrentHp > 0) return true;                // 还没死：不干预

            var potion = FairyOf(boss);
            if (potion == null)
            {
                Logger.Log($"[CharBoss] 瓶中精灵：{boss.Def?.Key} 会死，但栏位里没有瓶中精灵");
                return true;
            }

            int maxHp = creature.MaxHp;
            int before = creature.CurrentHp;
            decimal heal = Math.Max(maxHp * 0.3m, 1m);

            Logger.Log($"[CharBoss] 瓶中精灵：{boss.Def?.Key} 本该死亡（HP {before}/{maxHp}）→ 用这瓶药水顶掉" +
                       "（**判死这一刻同步丢药水 + 同步回血**，清负面交给 Hook.AfterPreventingDeath 前缀）");

            // BGM（用户 2026-10-28）：炼药猎触发瓶中精灵 ⇒ 切入女王**二阶段**音乐
            //（原版女王的二阶段触发点是「火炬头死后」，炼药猎这里对应「瓶中精灵救了她一命」）。
            try { BossBgm.OnBrewerFairySaved(); } catch (Exception me) { Logger.Log("[CharBoss] BGM 二阶段调用失败: " + me.Message); }

            // ① 先把「欠清负面」记上（哪怕下面丢药水失败也照清）
            try
            {
                if (PendingDebuffClear.Count > 16) PendingDebuffClear.Clear();   // 兜底：别无限增长
                PendingDebuffClear[boss] = potion;
            }
            catch { /* 忽略 */ }

            // ② 同步丢药水（只丢一次；多段伤害里第二刀再判死时这里已经查不到药水了）
            bool consumed = false;
            try
            {
                var shadow = boss.Shadow;
                if (shadow != null)
                {
                    shadow.RemoveUsedPotionInternal(potion);
                    consumed = true;
                }
            }
            catch (Exception e) { Logger.Log("[CharBoss] 瓶中精灵：丢弃药水失败（继续回血）: " + e.Message); }

            // ③ 同步回血 —— 复活在**这里**就已经完成，后面任何环节跑不跑都不影响
            try { creature.HealInternal(heal); }
            catch (Exception e) { Logger.Log("[CharBoss] 瓶中精灵：同步回血失败: " + e.Message); }

            int after = creature.CurrentHp;
            Logger.Log($"[CharBoss] 瓶中精灵：丢弃药水={consumed} → 回复 {heal:0} 点生命（现在 {after}/{maxHp}）");

            if (after <= 0)
            {
                // 回血真没成 ⇒ 不要拦「不许死」，让原版按它自己的路子处理（宁可她死，也不要卡在 0 血）
                Logger.Log("[CharBoss] ⚠ 瓶中精灵：同步回血没生效（HP 仍是 0）⇒ 放行原版判死");
                try { PendingDebuffClear.Remove(boss); } catch { /* 忽略 */ }
                return true;
            }

            __result = false;                                       // 不死
            return false;                                           // 跳过原版
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 瓶中精灵兜底失败: " + e.Message);
            return true;
        }
    }

    /// <summary>取出（并清掉）这只 Boss 欠着的那次「清负面」。</summary>
    internal static bool TryConsumePending(CharBossMonster boss, out PotionModel? potion)
    {
        potion = null;
        try
        {
            if (boss == null) return false;
            if (!PendingDebuffClear.TryGetValue(boss, out var found)) return false;
            PendingDebuffClear.Remove(boss);
            potion = found;
            return true;
        }
        catch { return false; }
    }

    /// <summary>她栏位里那瓶「瓶中精灵」（没有就 null）。</summary>
    internal static PotionModel? FairyOf(CharBossMonster boss)
    {
        foreach (var potion in BossPotions.Held(boss.Shadow))
        {
            try { if (potion is FairyInABottle) return potion; }
            catch { /* 忽略 */ }
        }
        return null;
    }

    /// <summary>
    /// **只清负面**（v0.2.617 起的主力路径）：药水与回血已经在 `Hook.ShouldDie` 前缀里同步做完了，
    /// 这一段只把「调制：瓶中精灵触发时清除她的所有负面效果」这件事接回死亡流程去 await
    /// （联机两端在同一步结算完再继续，与 v0.2.329 的口径一致）。
    /// </summary>
    internal static async Task ClearDebuffsOnlyAsync(Creature me, PotionModel? potion)
    {
        try
        {
            Logger.Log($"[CharBoss] 瓶中精灵：复活已同步完成（{potion?.Id.Entry ?? "?"}）⇒ 现在清负面");
            await ClearDebuffsAsync(me);
            Logger.Log("[CharBoss] 瓶中精灵：复活流程结束（回血 + 清负面都在死亡流程的 await 之内）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 瓶中精灵清负面失败: " + e.Message);
        }
    }

    /// <summary>
    /// 丢弃药水 + 回复 30% 最大生命（照抄原版 `AfterPreventingDeath` 的数值），然后清掉全部负面。
    /// 这个 Task 会被 `CreatureCmd.Kill` **await**（联机两端在同一步结算完再继续）。
    ///
    /// ⚠ v0.2.617 起它只是**兜底老路径**（判死时没走到同步那条、但药水还在）。
    /// </summary>
    internal static async Task SaveAsync(Player? shadow, PotionModel potion)
    {
        try
        {
            var me = shadow?.Creature;
            if (shadow == null || me == null || potion == null) return;

            bool held = BossPotions.Held(shadow).Contains(potion);
            if (!held)
            {
                Logger.Log("[CharBoss] 瓶中精灵：药水已经被处理过，跳过（避免打两次）");
                return;
            }

            decimal heal = Math.Max(me.MaxHp * 0.3m, 1m);
            try { shadow.RemoveUsedPotionInternal(potion); } catch { /* 忽略 */ }
            await CreatureCmd.Heal(me, heal);
            Logger.Log($"[CharBoss] 瓶中精灵：丢弃药水 → 回复 {heal:0} 点生命（现在 {me.CurrentHp}/{me.MaxHp}）");

            // 用户 2026-10-25 追加的「调制」补充条款：**瓶中精灵触发时清除她的所有负面效果**。
            await ClearDebuffsAsync(me);

            Logger.Log("[CharBoss] 瓶中精灵：复活流程结束（回血 + 清负面都在死亡流程的 await 之内）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 瓶中精灵回血失败: " + e.Message);
        }
    }

    /// <summary>
    /// 清掉她身上的负面 power（`PowerType.Debuff`），并且**额外按名字清「灾厄」**。
    ///
    /// 用户 2026-11-02：「炼药猎使用瓶中精灵复活后没有清除掉自身的灾厄」。
    /// 灾厄有两套实现：原版亡灵契约师的 `DoomPower`，以及崩坠收藏家 mod 的
    /// `Collector.CollectorCode.Powers.CollectorDoomPower`（id 形如 `COLLECTOR-COLLECTOR_DOOM_POWER`）。
    /// 后者是别的模组自定义的 power —— 它的 `PowerType` 未必是我们按 `Debuff` 过滤时能命中的那一档，
    /// 而且不同模组还可能改过移除流程 ⇒ 光按 Type 过滤会漏。
    /// 所以这里再加一条**按名字**的判据（`DOOM`），并且把清理前后的 power 名单打进日志，方便复验。
    /// </summary>
    private static async Task ClearDebuffsAsync(Creature me)
    {
        try
        {
            var before = new System.Collections.Generic.List<string>();
            foreach (var power in me.Powers)
            {
                if (power == null) continue;
                try { before.Add($"{power.Id.Entry}×{power.Amount}"); } catch { /* 忽略 */ }
            }
            Logger.Log($"[CharBoss] 瓶中精灵：复活前她身上的 power [{string.Join(",", before)}]");

            var debuffs = new System.Collections.Generic.List<PowerModel>();
            foreach (var power in me.Powers)
            {
                if (power == null) continue;
                bool debuff = false;
                try { debuff = power.Type == MegaCrit.Sts2.Core.Entities.Powers.PowerType.Debuff; }
                catch { /* 忽略 */ }
                // 灾厄：不管它是哪个模组实现的、标成什么 Type，一律清掉（用户明确要求）
                if (!debuff && IsDoomPower(power)) debuff = true;
                if (debuff) debuffs.Add(power);
            }
            foreach (var power in debuffs)
            {
                try
                {
                    Logger.Log($"[CharBoss] 瓶中精灵：清除{(IsDoomPower(power) ? "灾厄" : "负面效果")} " +
                               $"{power.Id.Entry}（{power.Amount}）");
                    await PowerCmd.Remove(power);
                }
                catch (Exception e) { Logger.Log("[CharBoss] 清除负面效果失败: " + e.Message); }
            }

            var after = new System.Collections.Generic.List<string>();
            foreach (var power in me.Powers)
            {
                if (power == null) continue;
                try { after.Add($"{power.Id.Entry}×{power.Amount}"); } catch { /* 忽略 */ }
            }
            Logger.Log($"[CharBoss] 瓶中精灵：复活后她身上的 power [{string.Join(",", after)}]");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 清负面效果失败: " + e.Message);
        }
    }

    /// <summary>是不是「灾厄」类 power（原版 `DoomPower` / 崩坠收藏家的 `CollectorDoomPower` / 别的模组同类）。</summary>
    private static bool IsDoomPower(PowerModel? power)
    {
        try
        {
            if (power == null) return false;
            string entry = power.Id.Entry ?? string.Empty;
            string type = power.GetType().Name;
            return entry.IndexOf("DOOM", StringComparison.OrdinalIgnoreCase) >= 0
                || type.IndexOf("Doom", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch { return false; }
    }
}

/// <summary>
/// 「瓶中精灵」复活的**可等待**入口（v0.2.329，联机不同步修复）。
///
/// 死亡流程（反编译 `CreatureCmd.KillWithoutCheckingWinCondition`）在「死亡被防止」之后会
/// <c>await Hook.AfterPreventingDeath(runState, combatState, preventer, creature)</c>，
/// 而那个方法的本体只是「preventer 是监听者就叫它自己的 AfterPreventingDeath」。
/// 这里用前缀把 <c>__result</c> 换成我们自己的 Task（丢药水 + 回 30% + 清全部负面），
/// 于是**整段复活都被死亡流程 await**，联机两端在同一步结算完再继续
/// —— 等价于原版「ShouldDie 登记 preventer + 死亡流程 await AfterPreventingDeath」的生命周期。
/// （原来是 `TaskHelper.RunSafe(...)` 发完就不管，清负面还没结束校验就过去了 ⇒ 断联机。）
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPreventingDeath))]
public static class BossFairyAfterPreventingDeathPatch
{
    [HarmonyPrefix]
    public static bool Prefix(Creature? creature, ref Task __result)
    {
        try
        {
            if (creature?.Monster is not CharBossMonster boss) return true;

            // ① 主力路径（v0.2.617）：判死那一刻已经同步丢了药水 + 回了血，这里只清负面。
            if (BossFairyInABottlePatch.TryConsumePending(boss, out var pending))
            {
                __result = BossFairyInABottlePatch.ClearDebuffsOnlyAsync(creature, pending);
                Logger.Log("[CharBoss] 瓶中精灵：把「清负面」挂成死亡流程要 await 的 Task（复活本身已同步完成）");
                return false;
            }

            // ② 兜底老路径：药水还在、但没有同步记账（判死不是我们拦的）。
            var potion = BossFairyInABottlePatch.FairyOf(boss);
            if (potion == null) return true;          // 不是这条路径 ⇒ 交给原版

            __result = BossFairyInABottlePatch.SaveAsync(boss.Shadow, potion);
            Logger.Log("[CharBoss] 瓶中精灵：把「回血 + 清负面」挂成死亡流程要 await 的 Task（兜底老路径）");
            return false;                              // 原版本体无需再跑
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 瓶中精灵（await 版）接管失败，按原版执行: " + e.Message);
            return true;
        }
    }
}