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
    public const string Tag = "v0.2.901 / 2026-11-23 (**F8 导出改成「最近一小时」**）—— 用户 2026-11-23 定口径：「改成大概最近一个小时吧，一局最长大概也就这么久」。v0.2.900 我按「最近 3000 行」设的，按实测密度（≈15 行/秒）只有 3 分钟 ⇒ 玩家打完了再按 F8 早就滚出去了。现在 `SafeLogPatch` 改成**按时间窗裁剪**（60 分钟；行数只留 90000 行内存兜底，每 64 行扫一次），F8 导出的 `characterbosses_recent.txt` 开头会写**覆盖时段**（HH:mm:ss → HH:mm:ss）+ 行数，正文仍已脱敏。顺带把报 bug 模板里的「几十 KB」改成「最近 1 小时，约 5~8 MB」。完整历史见 docs/plans/changelog-history.md";

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

        try { Logger.Log("[CharBoss] 启动阶段 5/5：完成（如果日志停在这之前，就说明死在对应那一步）"); } catch { /* 忽略 */ }
        try { CrashTrace.Mark("启动阶段 5/5"); } catch { /* 忽略 */ }
    }
}
