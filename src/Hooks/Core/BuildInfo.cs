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
    public const string Tag = "v0.2.535 / 2026-11-09（多人勾选真正生效：房主用 BaseLib 的自定义网络消息把「每张地图的 Boss 勾选」广播给所有客人（新文件 `Core/BossPicksSync.cs` / `BossPicksMessage`），两端算阵容用**同一套**候选 ⇒ **主 Boss 与搭子都只从房主勾选里抽**（以前多人局一律用整张花名册，勾选等于没用）。客人收到前先按整张表算、收到后清阵容缓存重算；阵容仍是 (种子,幕,场,勾选) 的纯函数。顺带把「预告第 2/3 幕时却读当前幕地图勾选」改成按幕取地图（`BossSelection.MapEntryOfAct`），配置界面文案同步改成「主 Boss 与搭子都以房主勾选为准」）｜v0.2.534：骸骨军团奥斯提图层归一";

    private static bool _logged;

    /// <summary>打一行「构建：…」，并第一次装上运行时才能装的东西（薄荷尖塔兼容等）。只做一次。</summary>
    public static void LogOnce()
    {
        if (_logged) return;
        _logged = true;

        try { Logger.Log($"[CharBoss] ===== CharacterBosses 构建：{Tag} ====="); }
        catch { /* 忽略 */ }

        // 双版本兼容层（v0.107 正式版 / v0.111 beta）：把游戏版本和各项 API 探测结果打出来，
        // 以后一看日志就知道这一把跑在哪个版本、哪些功能被跳过了。
        try { Core.GameCompat.LogOnce(); }
        catch (Exception e) { try { Logger.Log("[CharBoss] 兼容层探测失败（忽略）: " + e.Message); } catch { } }

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

        // 薄荷尖塔（MintySpire2）的「即将受到的伤害」兼容：它可能排在我们后面加载，
        // 所以等所有程序集都到位后再反射挂补丁（拿不到就安静跳过）。
        try { Core.MintySpireDamagePreviewCompat.TryInstall(); }
        catch (Exception e) { try { Logger.Log("[CharBoss] 薄荷尖塔兼容安装失败（忽略）: " + e.Message); } catch { } }
    }
}
