#if STS2_111
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace CharacterBosses.Core;

/// <summary>
/// **接管观者模组的「勤学精进（Lesson Learned）」** —— 它在新版游戏上会把整场卡死（用户 2026-11-27）。
///
/// ## 真因（这次是抓到了异常原文，不是猜）
/// 本机 `mod_log.txt`（v0.2.983 实机，卡死发生在 23:13:53）：
/// <code>
/// ⚠ 启动期异常#39: MissingMethodException: Method not found:
///    'Void MegaCrit.Sts2.Core.Models.CardModel.UpgradeInternal()'.
/// ⚠ 启动期异常#40: （同上，第二次）
/// </code>
/// 反编译两边的 `CardModel`：
/// <code>
/// 观者模组编译时（旧版游戏）： public void UpgradeInternal()                    ← 无参
/// 本机游戏（现版本）：         public void UpgradeInternal(System.Boolean silent)   ← 多了一个参数
/// </code>
/// 所以这张牌的 `OnPlay` 状态机里那条 `call UpgradeInternal()` **在 JIT 编译 MoveNext 时就解析不到**
/// ⇒ `MissingMethodException` 直接从 `OnPlay` **同步抛出**（不是塞进 Task）。
/// 而 STS2 的出牌是 `PlayCardAction` 里 `await card.OnPlay(...)`：这条调用抛出来，动作就永远完不成，
/// 动作队列一直排在它前面 ⇒「结束回合按了没反应、整场卡住」（实测 `AwaitTurnEndAndSwitchSides` 卡 249 秒）。
///
/// ★ 这也解释了 v0.2.982 那版为什么没用：那版是**Postfix 把返回的 Task 包一层守卫**，
///   可这里是「连 Task 都没生成就抛了」⇒ Postfix 压根不执行。必须**整段接管**。
///
/// ## 现在的做法：前缀直接接管 `OnPlay`（原方法不跑），用现版本 API 重做同样的效果
/// 1. 目标：`cardPlay.Target`；为空就兜底取「血最少的可命中敌人」（空目标在多人/切视角/目标刚死时都可能出现）；
/// 2. 致命判定：目标身上所有 Power 的 `ShouldOwnerDeathTriggerFatal()` 都为真（与观者原实现逐字一致）；
/// 3. 伤害：`DamageCmd.Attack(牌面伤害).FromCard(...).Targeting(...).WithHitFx("vfx/vfx_starry_impact")`
///    —— 和观者原实现同一个调用链，所以力量/易伤/虚弱/格挡全都照常生效；
/// 4. 斩杀：从手里挑**可升级**的牌，用 `RunState.Rng.Niche`（两端同种子 ⇒ 多人也一致）随机取一张，
///    再用**现版本**的 `CardCmd.Upgrade(card, CardPreviewStyle.None)` 升级（它自带升级弹窗，替代观者原来那套 VFX）。
/// 全程 try/catch：任何一步出意外都只记一行日志，**这个 Task 一定会完成** ⇒ 再也不会卡死。
/// 另加一个 `Finalizer` 作为最后一道网：万一原方法还是跑了（比如我们前缀提前退出），异常也会被吞掉。
///
/// ⚠ 战斗数值一个字节没改：伤害取牌面 `DynamicVars.Damage.BaseValue`，升级仍然只升「**卡组**里随机一张可升级的牌」（观者原实现的 `(PileType)6` = Deck）。
/// </summary>
internal static class WatcherLessonLearnedSafePatch
{
    /// <summary>两个观者实现里的同名卡（A 版 / B 版）。</summary>
    private static readonly string[] TypeNames =
    {
        "WatcherMod.WatcherLessonLearned",            // A：Boninall（3747526116）
        "Watcher.Code.Cards.Rare.LessonLearned",      // B：lamali（3747492505）
    };

    private static readonly FieldInfo? TargetField =
        typeof(CardPlay).GetField("<Target>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

    private static long _nextTryMs = long.MinValue / 4;
    private static int _logs;
    private static bool _installedAny;
    private static bool _hooked;

    /// <summary>挂 `SceneTree.process_frame`（主菜单也在跳）+ 启动第一帧各兜一次，不依赖战斗 HUD。</summary>
    internal static void EnsureHooked()
    {
        if (_hooked) return;
        try
        {
            if (Godot.Engine.GetMainLoop() is not Godot.SceneTree tree) return;
            tree.ProcessFrame += TryInstall;
            _hooked = true;
            TryInstall();
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 观者补丁：勤学精进 心跳挂载失败（忽略）: " + e.Message);
        }
    }

    /// <summary>幂等；没找到类型时 5 秒后再试一次（观者模组可能比我们晚加载）。</summary>
    internal static void TryInstall()
    {
        if (_installedAny) return;
        long now = Environment.TickCount64;
        if (now < _nextTryMs) return;
        _nextTryMs = now + 5000;

        foreach (var name in TypeNames)
        {
            try
            {
                var type = FindType(name);
                if (type == null) continue;

                // ⚠ 写死参数类型：卡片基类里 OnPlay 有多个重载形态，按名字挑会撞（本仓库踩过）
                var onPlay = type.GetMethod("OnPlay",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(PlayerChoiceContext), typeof(CardPlay) }, null);
                if (onPlay == null) continue;

                var prefix = new HarmonyMethod(typeof(WatcherLessonLearnedSafePatch)
                    .GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic)!);
                var finalizer = new HarmonyMethod(typeof(WatcherLessonLearnedSafePatch)
                    .GetMethod(nameof(Finalizer), BindingFlags.Static | BindingFlags.NonPublic)!);

                new Harmony("CharacterBosses.watcher.lessonlearned." + type.Name)
                    .Patch(onPlay, prefix: prefix, finalizer: finalizer);

                _installedAny = true;
                Logger.Log($"[CharBoss] 观者补丁：勤学精进（{type.FullName}）**已整段接管** —— " +
                           "原因：观者模组调的 `CardModel.UpgradeInternal()` 在现版本变成了 `UpgradeInternal(bool)`，" +
                           "那张牌一跑就 MissingMethodException 并把整场卡死；现在改用它自己的伤害链重做");
            }
            catch (Exception e)
            {
                Logger.Log($"[CharBoss] 观者补丁：勤学精进 {name} 挂载失败（忽略）: {e.Message}");
            }
        }
    }

    private static Type? FindType(string fullName)
    {
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (asm.IsDynamic) continue;
                    var t = asm.GetType(fullName, false);
                    if (t != null) return t;
                }
                catch { /* 单个程序集读不到就算了 */ }
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    // ── 接管 ───────────────────────────────────────────────────────────────

    /// <summary>原方法**不跑**（它 JIT 就炸）；效果由 <see cref="SafePlayAsync"/> 重做。</summary>
    private static bool Prefix(CardModel __instance, PlayerChoiceContext choiceContext, CardPlay cardPlay,
                               ref Task __result)
    {
        try
        {
            __result = SafePlayAsync(__instance, choiceContext, cardPlay);
        }
        catch (Exception e)
        {
            Log($"[CharBoss] ⚠ 勤学精进：接管入口异常（已吞掉，出牌照常结束）: {e.GetType().Name}: {e.Message}");
            __result = Task.CompletedTask;
        }
        return false;
    }

    /// <summary>最后一道网：万一原方法还是跑了并抛异常，也吞掉并保证返回一个已完成的 Task。</summary>
    private static Exception? Finalizer(Exception? __exception, ref Task __result)
    {
        if (__exception != null)
        {
            Log($"[CharBoss] ⚠ 勤学精进：原实现抛异常（已吞掉，避免整场卡死）: " +
                $"{__exception.GetType().Name}: {__exception.Message}");
        }
        if (__result == null) __result = Task.CompletedTask;
        return null;
    }

    /// <summary>照观者原逻辑重做：打伤害 → 若斩杀则从**卡组**里随机升级一张可升级的牌。任何失败都只记日志。</summary>
    private static async Task SafePlayAsync(CardModel? card, PlayerChoiceContext? ctx, CardPlay? play)
    {
        try
        {
            if (card == null || play == null) return;

            // ① 目标
            var target = play.Target;
            if (target == null)
            {
                target = FallbackTarget(play);
                if (target == null)
                {
                    Log("[CharBoss] 勤学精进：没有可打的目标 ⇒ 这一手只结束（原版在这里会抛异常并卡死）");
                    return;
                }
                Log($"[CharBoss] 勤学精进：这张牌没有出牌目标 ⇒ 兜底选血最少的可命中敌人 {SafeName(target)}");
            }

            // ★ 观者原实现：只有「本机操控的就是出牌者」时才播 Lesson 起手特效（表现层，失败不影响逻辑）
            try { if (LocalContext.IsMe(card.Owner)) InvokeVfx("BeginLesson", new object?[] { target }); } catch { /* 忽略 */ }

            // ② 致命判定（与观者原实现一致：目标身上所有 Power 都允许死亡触发致命）
            bool fatalAllowed = true;
            try
            {
                foreach (var p in target.Powers)
                {
                    if (p != null && !p.ShouldOwnerDeathTriggerFatal()) { fatalAllowed = false; break; }
                }
            }
            catch { fatalAllowed = true; }

            // ③ 伤害（同一条调用链：力量 / 易伤 / 虚弱 / 格挡都照常生效）
            decimal dmg = 0m;
            try { dmg = card.DynamicVars.Damage.BaseValue; } catch { /* 读不到就按 0 走 */ }

            MegaCrit.Sts2.Core.Commands.Builders.AttackCommand? atk = null;
            if (dmg > 0m && ctx != null)
            {
                try
                {
                    atk = await DamageCmd.Attack(dmg)
                        .FromCard(card, play)
                        .Targeting(target)
                        .WithHitFx("vfx/vfx_starry_impact", null, null)
                        .Execute(ctx);
                }
                catch (Exception e)
                {
                    Log($"[CharBoss] ⚠ 勤学精进：伤害结算异常（已吞掉）: {e.GetType().Name}: {e.Message}");
                }
            }

            // ④ 斩杀 ⇒ 随机升级**卡组**里一张可升级的牌（永久）
            //
            // ★★ v0.2.987（用户 2026-11-27：「升级手牌是什么鬼，她是升级卡组中的牌啊！你能不能不要改本来的效果？」）
            //   —— 用户说得对，是我读错了观者原实现的枚举值。反编译原文是
            //   `PileTypeExtensions.GetPile((PileType)6, card.Owner).Cards`，而 `PileType` 的枚举是
            //   `None=0 / Draw=1 / Hand=2 / Discard=3 / Exhaust=4 / Play=5 / Deck=6`
            //   ⇒ `(PileType)6` 就是 **Deck（卡组）**，不是手牌。我 0.2.984~0.2.986 那三版一直从手牌里挑，
            //   等于**偷偷改了这张牌的效果**，这是我不该犯的错。现在改回从卡组挑。
            bool killed = KillFlag(atk, target);

            if (_logs++ < 10)
            {
                Logger.Log($"[CharBoss] 勤学精进：目标={SafeName(target)} 伤害={dmg} " +
                           $"致命允许={fatalAllowed} 斩杀={killed}（伤害结果组={CountGroups(atk)}）");
            }

            if (!fatalAllowed)
            {
                return;      // 目标身上有 Power 不允许「死亡触发致命」⇒ 与原实现一致，不升级
            }
            if (!killed) return;

            UpgradeRandomInDeck(card);
        }
        catch (Exception e)
        {
            Log($"[CharBoss] ⚠ 勤学精进：出牌逻辑异常（已吞掉，避免整场卡死）: {e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>斩杀判据：**先看伤害结果**（`DamageResult.WasTargetKilled`，与原实现一致），生物状态只作兜底。</summary>
    private static bool KillFlag(MegaCrit.Sts2.Core.Commands.Builders.AttackCommand? atk, Creature? target)
    {
        try
        {
            if (atk != null)
            {
                foreach (var group in atk.Results)
                {
                    if (group == null) continue;
                    foreach (var r in group)
                    {
                        if (r != null && r.WasTargetKilled) return true;
                    }
                }
            }
        }
        catch { /* 读结果失败就退回生物状态 */ }

        try { return target != null && (target.IsDead || !target.IsAlive); } catch { return false; }
    }

    private static int CountGroups(MegaCrit.Sts2.Core.Commands.Builders.AttackCommand? atk)
    {
        try
        {
            int n = 0;
            if (atk != null) foreach (var _ in atk.Results) n++;
            return n;
        }
        catch { return -1; }
    }

    /// <summary>
    /// 斩杀后：从**卡组**（`PileType.Deck` = 6，与观者原实现的 `(PileType)6` 同一个堆）里
    /// 随机（`RunState.Rng.Niche`，两端同种子 ⇒ 多人一致）挑一张**可升级**的升级掉（永久）。
    /// </summary>
    private static void UpgradeRandomInDeck(CardModel card)
    {
        try
        {
            IReadOnlyList<CardModel> deck;
            try { deck = PileType.Deck.GetPile(card.Owner).Cards; }
            catch { deck = (IReadOnlyList<CardModel>)Array.Empty<CardModel>(); }

            var cands = new List<CardModel>();
            foreach (var c in deck)
            {
                if (c == null) continue;
                try { if (c.IsUpgradable) cands.Add(c); } catch { /* 单张读不到就算了 */ }
            }
            if (cands.Count == 0)
            {
                if (_logs++ < 10)
                {
                    Logger.Log("[CharBoss] 勤学精进：斩杀成功但**卡组里没有可升级的牌** ⇒ 不升级" +
                               $"（卡组 {CountOf(deck)} 张）");
                }
                return;
            }

            CardModel? pick = null;
            try { pick = card.Owner!.RunState.Rng.Niche.NextItem(cands); } catch { pick = null; }
            pick ??= cands[0];

            // 观者原实现也会记进「本节点升级过的牌」历史（用于结算/历史界面），照做
            try
            {
                card.Owner?.RunState?.CurrentMapPointHistoryEntry
                    ?.GetEntry(card.Owner.NetId).UpgradedCards.Add(pick.Id);
            }
            catch { /* 记录失败不影响升级本身 */ }

            bool ok = TryUpgradeNow(pick);

            // 观者原实现升级完会播「升级效果演出」，本机操控者才播；失败就退回普通升级弹窗
            try
            {
                if (LocalContext.IsMe(card.Owner))
                {
                    bool resolved = InvokeVfxBool("ResolveLessonLearned", new object?[] { pick });
                    if (!resolved) InvokeVfx("ShowUpgradePopup", new object?[] { pick });
                }
            }
            catch { /* 纯表现层，失败不影响升级 */ }

            if (_logs++ < 10)
            {
                Logger.Log($"[CharBoss] 勤学精进：斩杀成功 ⇒ {(ok ? "已升级" : "⚠ 升级失败")}卡组里随机一张 " +
                           $"{pick.Id.Entry}（可升级候选 {cands.Count} 张；战斗收尾中=" + EndingFlag() + "）");
            }
        }
        catch (Exception e)
        {
            Log($"[CharBoss] ⚠ 勤学精进：升级卡组牌失败（已忽略）: {e.GetType().Name}: {e.Message}");
        }
    }

    // ── 观者模组的表现层（Lesson 演出 / 升级弹窗）—— 一律反射 + 包 try/catch ─────────
    private static Type? _vfxType;
    private static bool _vfxProbed;

    private static Type? VfxType()
    {
        if (_vfxProbed) return _vfxType;
        _vfxProbed = true;
        try { _vfxType = FindType("WatcherMod.WatcherCardVfx"); } catch { _vfxType = null; }
        return _vfxType;
    }

    private static void InvokeVfx(string name, object?[] args)
    {
        try
        {
            var t = VfxType();
            var m = t?.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (m == null) return;
            m.Invoke(null, args);
        }
        catch { /* 表现层拿不到就算了 */ }
    }

    private static bool InvokeVfxBool(string name, object?[] args)
    {
        try
        {
            var t = VfxType();
            var m = t?.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (m == null) return false;
            return m.Invoke(null, args) is bool b && b;
        }
        catch { return false; }
    }

    /// <summary>战斗是否已进入「收尾」（<c>CombatManager.Instance.IsEnding</c>）。</summary>
    private static string EndingFlag()
    {
        try { return MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.IsEnding == true ? "是" : "否"; }
        catch { return "?"; }
    }

    private static MethodInfo? _upgradeInternalBool;   // 0.113/0.114：UpgradeInternal(bool silent = false)
    private static MethodInfo? _upgradeInternalNoArg;  // 0.111：UpgradeInternal()
    private static MethodInfo? _finalizeUpgrade;       // 两版都有：FinalizeUpgradeInternal()

    /// <summary>
    /// **立刻升级这张牌**（按运行时版本挑对重载）。
    ///
    /// ★★ v0.2.986（用户 2026-11-27「还是不敲牌」，日志实证：斩杀判对、`CardCmd.Upgrade` 也调了但没生效）：
    /// 反编译 `CardCmd.Upgrade(cards, style)` 的第一行是
    /// <code>
    /// if (CombatManager.Instance.IsEnding) return;      // ← 战斗一进入收尾就整段跳过
    /// ...
    /// current.UpgradeInternal();                        // ← 靠默认参数，正是两版签名不一致的那一处
    /// current.FinalizeUpgradeInternal();
    /// </code>
    /// 而玩家这一刀常常就是**把最后一只怪打死**（战斗随即进入 `IsEnding`）⇒ 官方命令静默什么都不做。
    /// 所以这里直接反射调 `UpgradeInternal`（先试 `(bool)`、再试无参），不经过那道 `IsEnding` 门。
    /// </summary>
    private static bool TryUpgradeNow(CardModel card)
    {
        try
        {
            var t = typeof(CardModel);
            _upgradeInternalBool ??= t.GetMethod("UpgradeInternal", new[] { typeof(bool) });
            _upgradeInternalNoArg ??= t.GetMethod("UpgradeInternal", Type.EmptyTypes);
            _finalizeUpgrade ??= t.GetMethod("FinalizeUpgradeInternal", Type.EmptyTypes);
        }
        catch { /* 反射拿不到就退回官方命令 */ }

        if (_upgradeInternalBool != null)
        {
            try { _upgradeInternalBool.Invoke(card, new object[] { false }); }
            catch (Exception e) { Log("[CharBoss] ⚠ 勤学精进：UpgradeInternal(bool) 抛异常: " + e.Message); }
            // ⚠ 以**状态**为准（别用「有没有抛异常」判断：可能已经加了一级才抛）
            if (IsUpgraded(card))
            {
                try { _finalizeUpgrade?.Invoke(card, null); } catch { /* 收尾失败也不影响已升级 */ }
                return true;
            }
        }

        if (_upgradeInternalNoArg != null)
        {
            try { _upgradeInternalNoArg.Invoke(card, null); }
            catch (Exception e) { Log("[CharBoss] ⚠ 勤学精进：UpgradeInternal() 抛异常: " + e.Message); }
            if (IsUpgraded(card))
            {
                try { _finalizeUpgrade?.Invoke(card, null); } catch { /* 同上 */ }
                return true;
            }
        }

        // 最后退回官方命令（它自己会在「战斗正在结束」时什么都不做，只当兜底）
        try { CardCmd.Upgrade(card, CardPreviewStyle.HorizontalLayout); }
        catch (Exception e) { Log("[CharBoss] ⚠ 勤学精进：CardCmd.Upgrade 兜底抛异常: " + e.Message); }
        return IsUpgraded(card);
    }

    private static bool IsUpgraded(CardModel card)
    {
        try { return card.IsUpgraded; } catch { return false; }
    }

    /// <summary>没有出牌目标时的兜底：血最少的可命中敌人。</summary>
    private static Creature? FallbackTarget(CardPlay play)
    {
        try
        {
            if (TargetField == null) return null;
            var owner = play.Player?.Creature;
            var combat = owner?.CombatState
                         ?? MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.DebugOnlyGetState();
            if (owner == null || combat == null) return null;

            Creature? best = null;
            foreach (var c in combat.GetOpponentsOf(owner))
            {
                if (c == null || !c.IsAlive || c.IsDead || !c.IsHittable) continue;
                if (best == null || c.CurrentHp < best.CurrentHp) best = c;
            }
            if (best != null) TargetField.SetValue(play, best);   // 顺手回填，别处读到也有目标
            return best;
        }
        catch { return null; }
    }

    private static int CountOf(IReadOnlyList<CardModel>? cards)
    {
        try { return cards?.Count ?? 0; } catch { return -1; }
    }

    private static void Log(string message)
    {
        try { Logger.Log(message); } catch { /* 忽略 */ }
    }

    private static string SafeName(Creature? c)
    {
        try { return c == null ? "<null>" : (c.Name ?? "?"); } catch { return "?"; }
    }
}
#endif
