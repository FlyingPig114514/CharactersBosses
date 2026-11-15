#if STS2_111   // 观者 Boss 只在 0.113 构建里（用户：107 不加这个 Boss）
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Powers;

namespace CharacterBosses.Core;

/// <summary>
/// **观者模组的两处运行期补丁**（用户 2026-11-15）。
///
/// 为什么是「运行期」而不是 `[HarmonyPatch]`：目标类型在**别人的程序集**里（铁律：不编译期引用）。
/// 为什么放在 `Core` 命名空间而不是 `Patches`：`ModEntry` 会扫 `CharacterBosses.Patches` 下
/// 带 `[HarmonyPatch]` 的类去挂 —— 这两个类没有特性，留在那儿只会让挂载器抛一次无用的异常
/// （日志里那两行 `补丁类 … 挂载失败: HarmonyException: Patching exception in method null` 就是它）。
///
/// ## ① 进了神格就不再退出
/// 反编译 `WatcherMod.Divinity`：退出神格发生在它**自己的** `AfterSideTurnEnd` 覆写里
/// （`if (side == Owner.Side &amp;&amp; Owner.Player != null) await ExitStance(Owner.Player)`）。
/// ⚠ 这是**派生类的覆写**：补 `AbstractModel.AfterSideTurnEnd`（基类声明）根本拦不到它
/// —— 第一版就是这么写错的（日志：`Patching exception in method null`）。
/// 现在直接拿 `Divinity.AfterSideTurnEnd` 的 `MethodInfo` 补。
///
/// ## ② 许愿固定选「永生不死」
/// `WatcherWish_P.OnPlay` 会 `ChooseOne(ctx, owner, [Almighty, LiveForever, FameAndFortune], prompt)`，
/// 候选项里出现 `WatcherWishLiveForever` 就说明这张是「许愿」⇒ 直接返回它。
/// </summary>
internal static class WatcherRuntimePatches
{
    private static bool _tried;
    private static int _logs;

    internal static void TryInstall()
    {
        if (_tried) return;
        try
        {
            // 观者模组没装 / 还没加载 ⇒ 下次再试
            if (WatcherCards.ModAssembly() == null) return;

            bool any = InstallDivinityKeep();
            any |= InstallWishChoice();
            if (any) _tried = true;      // 两处都装上了才不再重试
        }
        catch (Exception e) { Logger.Log("[CharBoss] 观者运行期补丁安装失败: " + e.Message); }
    }

    // ── ① 神格不退 ─────────────────────────────────────────────────────────
    private static bool InstallDivinityKeep()
    {
        try
        {
            // 两条路：A 的 `Divinity.AfterSideTurnEnd`（回合结束退）、B 的 `DivinityStance.BeforeSideTurnStart`（回合开始退）
            var type = WatcherCards.T("Divinity") ?? WatcherCards.T("DivinityStance");
            if (type == null) return false;
            var method = type.GetMethod("AfterSideTurnEnd",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                         ?? type.GetMethod("BeforeSideTurnStart",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (method == null)
            {
                Logger.Log("[CharBoss] 神格不退：Divinity 上没有 AfterSideTurnEnd（版本不同）⇒ 跳过");
                return true;      // 不再重试
            }

            var prefix = new HarmonyMethod(typeof(WatcherRuntimePatches)
                .GetMethod(nameof(DivinityKeepPrefix), BindingFlags.Static | BindingFlags.NonPublic)!);
            new Harmony("CharacterBosses.watcher.divinitykeep").Patch(method, prefix: prefix);
            Logger.Log("[CharBoss] 神格不退：已挂上观者的 Divinity.AfterSideTurnEnd");
            return true;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 神格不退：挂载失败: " + e.Message);
            return true;
        }
    }

    /// <summary>参数名要跟目标一致（Harmony 按名字注入）；`__instance` 是那个 Divinity power。</summary>
    private static bool DivinityKeepPrefix(object __instance,
                                           PlayerChoiceContext choiceContext,
                                           CombatSide side,
                                           IEnumerable<Creature> participants,
                                           ref Task __result)
    {
        try
        {
            var power = __instance as PowerModel;
            if (power == null) return true;

            Creature? owner = null;
            try { owner = power.Owner; } catch { return true; }
            if (owner == null) return true;

            var boss = OurBossOwning(owner);
            if (boss == null) return true;
            try { if (boss.Creature?.GetPower<BossDevotionPower>() == null) return true; }
            catch { return true; }

            __result = Task.CompletedTask;
            if (_logs++ < 3)
                Logger.Log("[CharBoss] 神格观：她进了神格 ⇒ 压掉原版的「回合结束退神格」");
            return false;
        }
        catch { return true; }
    }

    // ── ② 许愿固定选「永生不死」 ────────────────────────────────────────────
    private static bool InstallWishChoice()
    {
        try
        {
            var helper = WatcherCards.T("WatcherCombatHelper");
            if (helper == null)
            {
                if (WatcherCards.Current != WatcherCards.Provider.None)
                    Logger.Log("[CharBoss] 许愿固定选项：当前观者实现没有 `WatcherCombatHelper.ChooseOne`（lamali 版）⇒ 沿用该模组自己的选法");
                return true;      // 不再重试
            }
            var method = helper.GetMethod("ChooseOne",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[]
                {
                    typeof(PlayerChoiceContext), typeof(Player),
                    typeof(IReadOnlyList<CardModel>), typeof(LocString), typeof(bool),
                },
                null);
            if (method == null)
            {
                Logger.Log("[CharBoss] 许愿固定选项：观者的 ChooseOne 签名不符 ⇒ 跳过");
                return true;
            }

            var prefix = new HarmonyMethod(typeof(WatcherRuntimePatches)
                .GetMethod(nameof(WishChoicePrefix), BindingFlags.Static | BindingFlags.NonPublic)!);
            new Harmony("CharacterBosses.watcher.wishchoice").Patch(method, prefix: prefix);
            Logger.Log("[CharBoss] 许愿固定选项：已挂上观者的 ChooseOne（只在候选项含「永生不死」时生效）");
            return true;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 许愿固定选项：挂载失败: " + e.Message);
            return true;
        }
    }

    private static bool WishChoicePrefix(PlayerChoiceContext choiceContext, Player owner,
                                         IReadOnlyList<CardModel> options, LocString prompt, bool cancelable,
                                         ref Task<CardModel> __result)
    {
        try
        {
            if (options == null || options.Count == 0) return true;
            if (!IsOurBossShadowPlayer(owner)) return true;

            CardModel? liveForever = null;
            foreach (var opt in options)
            {
                if (opt == null) continue;
                string typeName;
                try { typeName = opt.GetType().Name; } catch { continue; }
                if (string.Equals(typeName, "WatcherWishLiveForever", StringComparison.Ordinal))
                { liveForever = opt; break; }
            }
            if (liveForever == null) return true;

            __result = Task.FromResult(liveForever);
            if (_logs++ < 6) Logger.Log("[CharBoss] 许愿：替她选了「永生不死」");
            return false;
        }
        catch { return true; }
    }

    // ── 共用判定 ──────────────────────────────────────────────────────────
    private static Monsters.CharBossMonster? OurBossOwning(Creature owner)
    {
        try
        {
            var enemies = owner.CombatState?.Enemies;
            if (enemies == null) return null;
            foreach (var c in enemies)
            {
                if (c?.Monster is not Monsters.CharBossMonster boss) continue;
                var shadowCreature = boss.Shadow?.Creature;
                if (shadowCreature != null && ReferenceEquals(shadowCreature, owner)) return boss;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    private static bool IsOurBossShadowPlayer(Player owner)
    {
        try
        {
            if (owner == null) return false;
            var enemies = owner.Creature?.CombatState?.Enemies;
            if (enemies == null) return false;
            foreach (var c in enemies)
            {
                if (c?.Monster is not Monsters.CharBossMonster boss) continue;
                if (ReferenceEquals(boss.Shadow, owner)) return true;
            }
        }
        catch { /* 忽略 */ }
        return false;
    }
}
#endif
