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
/// ⚠ `Tag` 里**不能有反斜杠**（构建脚本会失败）——写路径一律用正斜杠。
/// </summary>
public static class BuildInfo
{
    /// <summary>构建指纹（日志/数据表里用）。每次改动都要 +0.0.1 并写一句改了什么。</summary>
    public const string Tag = "v0.2.981 / 2026-11-27 (**修：多人时玩家自己的黑暗球打队友 —— BossOrbTargeting.BossOf 把玩家的球误认成 Boss 的球**)—— 用户：「游戏内按 F8 生成『最近日志』的功能从没成功过，真正实现出来」。铁证（mod_log.txt）：『最近日志导出：按 **F8**』挂载成功行 58 行，而『最近日志已导出』与『最近日志导出失败』各 0 行，characterbosses_recent.txt 以及同构的 scene_tree.txt 都不存在；Dump() 内部每一步都有 try/catch，真跑进去失败一定留痕，却一条都没有 ⇒ 不是写文件失败，是键没到。改法：① 新增 Core/HotkeyRouter + Core/HotkeyPatches，把 F8/F9 挂到游戏自己的输入入口（NGame._Input 与 NInputManager._UnhandledKeyInput，Harmony 后缀、写死参数类型 typeof(InputEvent)、只旁听绝不吃事件），并保留我们自己 Node 的 _Input/_UnhandledInput/_UnhandledKeyInput，一共四条通道，400ms 去重只触发一次；每条通道前 30 次按键留一行『按键探针』，以后一眼就能看出哪条通道活着。② 导出改成优先读磁盘上 mod_log.txt 的最后 8 MB（那份永远最全，崩溃前的也在），内存环降级成兜底；按行首 [HH:mm:ss] 时间戳裁到最近 60 分钟、最多 6 万行，顺手把 characterbosses_trace / stall / desync 一起打成 characterbosses_report_yyyyMMdd_HHmmss.zip。③ 每次按键必留一行『按到 F8 了（通道=…）』，导出成功留『最近日志已导出：<路径>（N 行，覆盖 hh:mm:ss→hh:mm:ss，数据源=…）』。见 docs/plans/changelog-history.md；0.2.977 补：实机探针证明游戏的 NGame._Input 与 NInputManager._UnhandledKeyInput 两条通道都活着、我们自己 Node 的三条通道一条都没被调到（主菜单 F8 出 53 行、战斗内 F8 覆盖成 239 行、F9 场景树 2472 行），并加一行『导出器节点自检』把 Godot 对 has_method(_input) / 脚本实例 / process 开关的说法打进日志；0.2.978 加设置页 A7「日志在哪里 / 一键导出」（手机没 F8 也能导）；0.2.979 按用户「这不能让玩家看吧」把界面上所有路径都过 PathDisplay.Mask（C:/Users/<名字>/AppData/Roaming → %APPDATA%），玩家截图不会漏自己的用户名；0.2.980 按用户「检查一下，目前本模组有没有什么其它地方写了隐私信息？」做了隐私审查：实测 characterbosses_trace.txt 有 16 处 C:/Users/<用户名> + 3 处 17 位 SteamID、characterbosses_desync.txt 有 117 处 SteamID + 40 处 NetId=数字、characterbosses_recent.txt 有 3 处正斜杠路径漏网（Redact 的路径正则只认反斜杠，而 Godot 的 OS.GetUserDataDir() 给正斜杠）⇒ 现在 CrashTrace.Write / DivergenceStateProbe.AppendToFile / StallWatchdog.WriteReport / SceneTreeDumper 落盘前都过 Redact.Text，导出的 zip 里三个附带文件也改成读出文本脱敏后再写（连旧版本留下的历史文件一并兜住），正则改成同时认反斜杠和正斜杠；顺带把仓库里两处真 SteamID（BossPicksSync.cs 注释、changelog 历史）打码；0.2.981 按用户 2026-11-27「多人时玩家方故障机器人的黑球打队友了（对面 boss 有黑球机，是不是它的黑球索敌影响到玩家了）」修掉真因：BossOrbTargeting.BossOf 的 v0.2.969 兜底「Owner 认不出就问『当前正在行动的 Boss』」没有先排除真人玩家 ⇒ 玩家自己的黑暗球被当成那只 Boss 的球、改写成它锁定的玩家（日志铁证 22:24:26：玩家回合里 `黑球激发：静默猎手 的黑暗球 ⇒ 目标改为 P#c5d6a4`，而静默猎手身上根本没有充能球，紧接着 P#c5d6a4 血量 52→35）；现在 BossOf 只在「Owner 的 Creature 就是我们的 Boss」或「这颗球就在她影子玩家的球队列里（对象身份比对）」时才认领，主人是真人玩家一律直接放弃，并且给玩家自己的黑暗球加了一道与机制无关的兜底（只从 GetOpponentsOf(主人) 里按血最少取目标，永远打不到玩家侧）";

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
