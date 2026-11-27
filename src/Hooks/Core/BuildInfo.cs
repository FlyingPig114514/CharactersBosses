using System;
using MegaCrit.Sts2.Core.Helpers;
using CharacterBosses;

namespace CharacterBosses.Core;

/// <summary>
/// 构建指纹 + 一次性启动日志。
///
/// 用途：
///   · `Tag` 写进日志与上传数据的 `mod` 字段 ⇒ 拿到玩家日志就能确认「他跑的是哪一版」；
///   · <see cref="LogOnce"/> 由 `BossFrameTickPatch` 在第一帧调一次（那时所有程序集都加载完了），
///     顺手装「薄荷尖塔」的伤害预测兼容（见 <c>Core.MintySpireDamagePreviewCompat</c>）。
///
/// ⚠ 改版本时**两处一起改**：这里 + `src/CharacterBosses.json` 的 `version`。
/// </summary>
public static class BuildInfo
{
    /// <summary>构建指纹（日志/数据表里用）。每次改动都要 +0.0.1 并写一句改了什么。</summary>
    public const string Tag = "v0.2.974 / 2026-11-27 (**修：联机里 Boss 被毒死之后整场卡住（原版回合循环 NRE）**)—— 用户：「所有玩家选择回合结束，有一个 boss 被毒死，然后就卡住了」，附四份日志。铁证在 godot.log（房主与客人各一次、完全一致）：`[ERROR] Combat #18 turn loop died while its combat is in progress; the combat is stuck until the room is restarted: System.NullReferenceException at Hook.IterateCombatHookListeners+MoveNext ← Hook.ModifyEnergyGain ← PlayerCmd.GainEnergy ← Relics.PaelsTears.AfterSideTurnStart ← Hook.AfterSideTurnStart ← CombatManager.StartTurn ← …PoisonPower.Trigger`。机制：毒在「敌方回合开始」的钩子广播里把带毒的塔罗愚打死 ⇒ 它被移出战斗、Creature.CombatState 变 null，可这一次广播用的是死亡前取好的监听者快照 ⇒ 她身上 12 件遗物照旧被调（原版尸体身上不可能有遗物，所以原版永远碰不到）⇒「佩尔的泪」一给能量就是 `player.Creature.CombatState`（null）⇒ 原版 NRE ⇒ 回合循环死掉、只能重开房间。修法两层：①新补丁 Patches/HookIterateNullCombatStatePatch 两处兜底 —— 广播收口 `Hook.IterateCombatHookListeners`（战斗状态为 null ⇒ 返回空序列）与 `PlayerCmd.GainEnergy`（player.Creature.CombatState 为 null ⇒ 直接跳过：这条链后面还有 AfterModifyingEnergyGain 与 PlayerCombatState.GainEnergy 两步，对尸体同样会炸）；都节流日志、广播那处前两次打调用栈；②根因侧 ShadowPlayerFactory.SyncHookActivation（每帧、只在翻转时动手）：Boss 死 ⇒ Player.DeactivateHooks()、复活 ⇒ ActivateHooks()，让尸体不再冒充活的监听者。日志：`钩子广播兜底：战斗状态为 null ⇒ …`、`加能量兜底：这只「玩家」已经不在战斗里 ⇒ 跳过给能量`、`影子玩家的钩子参与权：IsActiveForHooks → False`。见 docs/plans/changelog-history.md";

    private static bool _logged;

    /// <summary>打一行「构建：…」，并第一次装上运行时才能装的东西（薄荷尖塔兼容等）。只做一次。</summary>
    public static void LogOnce()
    {
        if (_logged) return;
        _logged = true;

        // ★ v0.2.834：**先**装启动期异常快照器（FirstChanceException）——
        //   玩家那个「致命错误！在游戏启动期间遇到了问题」的弹窗来自游戏自己的
        //   `NGame.GameStartupWrapper`（它 catch 之后又 throw），所以之前交上来的日志里什么都看不到。
        //   装上之后，启动期任何被游戏 catch 掉的异常都会在 mod_log 里留一行「⚠ 启动期异常#N」。
        // ★ v0.2.839：第一件事就是装「崩溃面包屑」（同步落盘，进程被强杀也留得住）
        try { CrashTrace.Install(); } catch { /* 忽略 */ }
        try { CrashTrace.Mark("BuildInfo.LogOnce 开始（第一帧）"); } catch { /* 忽略 */ }
        try { StartupWatchdog.Install(); }
        catch { /* 忽略 */ }
        // ★ v0.2.966（用户 2026-11-26：「你试试加诊断补丁」）：
        //   主线程停摆看门狗的心跳 —— 挂 `SceneTree.process_frame`（主菜单也在跳，
        //   不能只靠 BossFrameTick：那个只在战斗里有节点时才被调到）。
        try { StallWatchdog.EnsureHooked(); } catch { /* 忽略 */ }

        try { Logger.Log($"[CharBoss] ===== CharacterBosses 构建：{Tag} ====="); }
        catch { /* 忽略 */ }

        // 启动阶段标记：万一以后又死在初始化中间，日志最后一行就能告诉我们是哪一步
        try { Logger.Log("[CharBoss] 启动阶段 1/5：遥测补发"); } catch { /* 忽略 */ }
        try { CrashTrace.Mark("启动阶段 1/5"); } catch { /* 忽略 */ }


        // 双版本兼容层（v0.107 正式版 / v0.111 beta）：把游戏版本和各项 API 探测结果打出来，
        // 以后一看日志就知道这一把跑在哪个版本、哪些功能被跳过了。
        // 上次没发出去的遥测记录：启动就补发一次（用户 2026-11-20：「加上下次重试」）
        try { Telemetry.RetryPendingUploads(); }
        catch (Exception e) { try { Logger.Log("[CharBoss] 启动补发失败（忽略）: " + e.Message); } catch { } }
        try { Logger.Log("[CharBoss] 启动阶段 2/5：小小姐护栏"); } catch { /* 忽略 */ }
        try { CrashTrace.Mark("启动阶段 2/5"); } catch { /* 忽略 */ }
        // 与「小小姐的战斗提示」(XiaoSpireHint) 的互操作护栏：别让它的自动选牌 AI 接管角色 Boss 的选牌/出牌
        try { Patches.XiaoHintBossShield.TryApply(); }
        catch (Exception e) { try { Logger.Log("[CharBoss] 小小姐护栏安装异常（忽略）: " + e.Message); } catch { } }
        try { Logger.Log("[CharBoss] 启动阶段 3/5：兼容层探测"); } catch { /* 忽略 */ }
        try { CrashTrace.Mark("启动阶段 3/5"); } catch { /* 忽略 */ }
        try { Core.GameCompat.LogOnce(); }
        catch (Exception e) { try { Logger.Log("[CharBoss] 兼容层探测失败（忽略）: " + e.Message); } catch { } }
        try { Logger.Log("[CharBoss] 启动阶段 4/5：手写补丁安装"); } catch { /* 忽略 */ }
        try { CrashTrace.Mark("启动阶段 4/5"); } catch { /* 忽略 */ }

        // ★★ v0.2.533（玩家 2026-11-09：「炼药猎的**群蛇形态多人时会打其他玩家**」）：
        //   `SerpentFormLockOnPatch` / `DarkOrbLockOnPatch` / `PlayerEffectExemption` / `BossBlockAbsorbPatch`
        //   这几套是**手写 Harmony**（要挂 async 状态机的 `MoveNext`，没法用 `[HarmonyPatch]` 特性），
        //   靠自己的 `TryInstall()` 安装 —— 而**这四个 TryInstall 一个调用点都没有**，
        //   也就是说它们**从来没生效过**（日志里也从来没有「已挂 N 个状态机」那一行）。
        //   ⇒ 「多人时群蛇形态随机打人」就是收窄逻辑压根没挂上。
        //
        //   只安装**明确对应「随机/球被动只打被锁定那位」**的两套（用户口径）：
        //     · SerpentFormLockOnPatch：群蛇形态（炼药猎的「蛇形」牌）
        //     · DarkOrbLockOnPatch：黑暗充能球的被动
        //   `BossBlockAbsorbPatch` **故意不装**：用户明确要求「只有炼药猎能挡失去生命」，
        //   那件事由 `BrewerBossBlockableHpLossPatch` 负责（装了这个就变成所有 Boss 都能挡）。
        //   `PlayerEffectExemption` 也先不装（会改变「玩家施加的效果打谁」，需要单独确认）。
        try { Patches.SerpentFormLockOnPatch.TryInstall(); }
        catch (Exception e) { try { Logger.Log("[CharBoss] 群蛇形态锁定补丁安装失败（忽略）: " + e.Message); } catch { } }
        try { Patches.DarkOrbLockOnPatch.TryInstall(); }
        catch (Exception e) { try { Logger.Log("[CharBoss] 黑暗球锁定补丁安装失败（忽略）: " + e.Message); } catch { } }

        // ★ v0.2.896：纯诊断（玩家 2026-11-23「抉择储的天际钻头好像不会优先攻击瓦皮」）。
        //   瓦皮承伤是**原版钩子**（`WuppysLoyaltyPower.ModifyPreBlockDamageTarget`）干的，
        //   我们的选靶补丁改不动它 ⇒ 先照出「是被哪一条挡下」（目标/强化攻击/瓦皮已死），再决定要不要动行为。
        try { Patches.WuppyRedirectTracePatch.TryInstall(); }
        catch (Exception e) { try { Logger.Log("[CharBoss] 瓦皮承伤诊断安装失败（忽略）: " + e.Message); } catch { } }

        // 薄荷尖塔（MintySpire2）的「即将受到的伤害」兼容：它可能排在我们后面加载，
        // 所以等所有程序集都到位后再反射挂补丁（拿不到就安静跳过）。
        try { Core.MintySpireDamagePreviewCompat.TryInstall(); }
        catch (Exception e) { try { Logger.Log("[CharBoss] 薄荷尖塔兼容安装失败（忽略）: " + e.Message); } catch { } }

        // ★★ v0.2.903（用户 2026-11-23「疾风观」订正时发现）：
        //   `Core.WatcherRuntimePatches.TryInstall()` **一直没有调用点** —— 和上面 v0.2.533 记的那四个
        //   一模一样的毛病：神格不退 / 许愿固定选 / 真言满 10 层接管 / B 版姿态伤害兜底 这些
        //   运行期补丁**从来没被安装过**（日志里也没有「已挂上…」那几行）。
        //   现在补上调用；它内部对「观者 mod 没装」是**安全跳过 + 下次再试**的（`ModAssembly() == null` 直接 return），
        //   所以 0.107 / 没装观者的玩家完全不受影响。
        //   新增的「斋戒不再使她失去能量」也挂在这批里（`InstallFastingNoEnergyDown`）。
        try { Core.WatcherRuntimePatches.TryInstall(); }
        catch (Exception e) { try { Logger.Log("[CharBoss] 观者运行期补丁安装失败（忽略）: " + e.Message); } catch { } }

        try { Logger.Log("[CharBoss] 启动阶段 5/5：完成（如果日志停在这之前，就说明死在对应那一步）"); } catch { /* 忽略 */ }
        try { CrashTrace.Mark("启动阶段 5/5"); } catch { /* 忽略 */ }
    }
}