using System;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace CharacterBosses.UI;

/// <summary>
/// **每场战斗一份的 UI 会话**：进新战斗房时把上一场留下的东西全部拆掉。
///
/// 为什么需要（用户 2026-11-08 斗蛐蛐连打两场实测）：
///   · 第一场打完、第二场开始时出现「攻击莫名其妙失效 / UI 位置不对 / 场地里没有 tooltip」；
///   · 这些都是**静态状态跨场残留**：手牌面板字典里还留着上一场的怪物实例与已释放节点、
///     说明面板挂在自建 HUD 层上没收、翻侧计数（<c>PlayerSideBossDepth</c>）如果因为
///     战斗被中断而没配对减掉，就会让后面所有「可命中敌人」都按玩家侧解析 —— 表现就是
///     玩家打对面像打空气。
///
/// 判据很简单：<see cref="NCombatRoom.Instance"/> 换了一个节点实例 ⇒ 这是一场新战斗。
/// </summary>
internal static class BossUiSession
{
    private static ulong _roomId;

    /// <summary>
    /// 「刚刚重置过，需要把 Boss 面板重建回来」（v0.2.513）。
    ///
    /// 为什么必须补这一手：`EnsureFresh` 每帧在 `BossFrameTick.Tick` 开头判定，而**面板往往比它早一步
    /// 建好**（进战斗房那一帧里 `PrepareTurn` 已经把面板和 6 张牌画出来了，下一帧 `NCombatRoom.Instance`
    /// 才换成新实例）⇒ `Reset()` 里的 `BossHandUi.Clear()` 把刚画好的整块面板拆掉，而 `Refresh` 只在
    /// 她回合开始/数据变化时才调 ⇒ **整场都没有卡牌、遗物、能量**（用户 2026-11-08 截图：
    /// 「boss 的牌和遗物还有能量呢？」）。现在重置完记一个标记，由每帧驱动立刻重建一次。
    /// </summary>
    private static bool _rebuildRequested;

    /// <summary>取走「需要重建面板」的标记（取一次就清掉）。</summary>
    public static bool ConsumeRebuildRequest()
    {
        if (!_rebuildRequested) return false;
        _rebuildRequested = false;
        return true;
    }

    /// <summary>每帧调一次；战斗房换了就做一次全量清理（幂等、便宜）。</summary>
    public static void EnsureFresh()
    {
        try
        {
            var room = NCombatRoom.Instance;
            ulong id = room != null && GodotObject.IsInstanceValid(room) ? room.GetInstanceId() : 0UL;
            if (id == _roomId) return;

            // 注意：id == 0 表示「现在没有战斗房」，也要清理（回主菜单 / 选人界面前的一帧）。
            _roomId = id;
            if (id == 0)
            {
                // ★ 2026-11-15（用户：「特殊效果描述图层太高了，切到地图页面都还能看到」）：
                //   原版 `NHoverTipSet` 是挂在**全局**悬停提示层上的，一旦常驻就跟到哪个界面都显示；
                //   以前这里 `id == 0` 直接 return ⇒ 离开战斗房（去奖励/地图）时**什么都没收**
                //   ⇒ 说明框一直挂在地图上。现在离开战斗房就把我们挂上去的东西全收掉。
                try { HideTransient(); } catch { /* 忽略 */ }
                return;
            }

            Reset("新的战斗房 " + id);
            _rebuildRequested = true;      // 让每帧驱动立刻把面板重建回来（见字段注释）
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 会话切换检查失败: " + e.Message);
        }
    }

    /// <summary>
    /// 只收「跟着房间走」的东西：特殊效果说明框 / 附加状态栏 / 自建 HUD 层。
    /// 不碰手牌面板与锁定记账 —— 那些在下一场战斗的 <see cref="Reset"/> 里统一处理，
    /// 这里只保证**离开战斗房之后屏幕上不留我们的残留**（用户实测：说明框跟到地图页）。
    /// </summary>
    private static void HideTransient()
    {
        try { BossSpecialUi.HideAll(); } catch { /* 忽略 */ }
        try { BossExtraStatusRow.HideAll(); } catch { /* 忽略 */ }
        try { BossHudLayer.Release(); } catch { /* 忽略 */ }
    }

    /// <summary>「本机操控的玩家」上一次的 NetId（见 <see cref="EnsurePerspective"/>）。</summary>
    private static ulong? _lastNetId;

    /// <summary>请求一次面板重建（由帧驱动消费，见 <see cref="ConsumeRebuildRequest"/>）。</summary>
    public static void RequestRebuild(string why)
    {
        _rebuildRequested = true;
        try { Logger.Log("[CharBoss] UI 会话重建请求：" + why); } catch { /* 忽略 */ }
    }

    /// <summary>
    /// **每帧一次：本机操控的玩家变了就重建面板**（用户 2026-11-27 截图：「统帅模式下进入 Boss 战，
    /// 一切换操控的玩家，**所有 Boss 的意图都会消失**」）。
    ///
    /// 为什么会消失：我们这几套 Boss 界面（手牌栏面板 + 每个格子的意图行）是挂在战斗房 UI
    /// `%PlayContainer` 下面的（见 `BossHandUi.EnsurePanel` / `BossHudLayer`）。
    /// 而「一台机器操控多个座位」的模组切视角时会改游戏的 `LocalContext.NetId`，并且会把战斗 UI
    /// 重新绑定/替换一遍（`CommanderMode.RebindCombat` → `Replace(old, fresh)`）——
    /// 我们的面板跟着旧父节点一起被丢掉，而面板重建只在「她回合开始 / 数据变化」时才触发
    /// ⇒ **整场都不再有意图**。
    ///
    /// 判据与 <c>Compat/AiTeammateTreasureCompat</c> 一致：只看 `LocalContext.NetId`（游戏现在认为的"我"），
    /// 变了就：收掉旧面板与自建层 → 请求一次重建（帧驱动会按「每帧 1 只」把它们重新挂回去）。
    /// 单人 / 普通联机时 `LocalContext.NetId` 不会变 ⇒ 这里一个字节都不做。
    /// </summary>
    public static void EnsurePerspective()
    {
        try
        {
            ulong? now = null;
            try { now = MegaCrit.Sts2.Core.Context.LocalContext.NetId; } catch { now = null; }
            if (now == _lastNetId) return;

            ulong? prev = _lastNetId;
            _lastNetId = now;
            if (prev == null || now == null) return;                       // 首次记录 / 离场：不算切换
            if (NCombatRoom.Instance == null) return;                      // 不在战斗房：交给 EnsureFresh

            Logger.Log($"[CharBoss] 本机操控的玩家变了：P#{prev} → P#{now}" +
                       "（切视角的模组改的是 LocalContext.NetId）⇒ 重算意图 + 重建 Boss 面板");
            // ★ v0.2.997（用户 2026-11-28：「统帅模式下抉择储的选牌界面应该跳到对应玩家的视角」）：
            //   观察到视角自己在变 ⇒ 本机确实能操控多个座位 ⇒ 以后遇到要「指定玩家来选牌」的界面，
            //   我们才敢自己把视角切过去（见 Core/SeatSwitch.cs）。
            try { Core.SeatSwitch.NotePerspectiveChanged(); } catch { /* 忽略 */ }

            // ★ v0.2.973（用户 2026-11-27：「切换操控的角色时 boss 的意图不会跟着变 —— 我从玩家 B 切到
            //   玩家 A，boss A 的单体攻击意图伤害还是 0」）：
            //   意图里的伤害数字是**规划期按「当时的本机玩家」算好并缓存的**（`BossIntentBuilder` 里
            //   「她锁的不是本地玩家 ⇒ 对本地显示 0」那套规则）⇒ 视角换了必须**重算**，光重建面板没用
            //   （面板只是把缓存里的意图节点重新排一遍）。这里先按新的本机玩家重算，再重建面板。
            int refreshed = 0;
            foreach (var boss in LiveBosses())
            {
                try { boss.RefreshPlannedIntents(); refreshed++; } catch { /* 单只失败不影响其它 */ }
            }
            if (refreshed > 0)
            {
                Logger.Log($"[CharBoss] 视角切换：已按新的本机玩家（P#{now}）重算 {refreshed} 只 Boss 的意图数值");
            }

            try { BossHandUi.Clear(); } catch { /* 忽略 */ }
            try { BossSpecialUi.HideAll(); } catch { /* 忽略 */ }
            try { BossExtraStatusRow.HideAll(); } catch { /* 忽略 */ }
            try { BossHudLayer.Release(); } catch { /* 忽略 */ }

            RequestRebuild($"视角切换 P#{prev} → P#{now}");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 视角切换检查失败（忽略）: " + e.Message);
        }
    }

    /// <summary>场上**还活着**的、我们模组的 Boss（视角切换后要重算它们各自缓存的意图数值）。</summary>
    private static System.Collections.Generic.IEnumerable<Monsters.CharBossMonster> LiveBosses()
    {
        MegaCrit.Sts2.Core.Combat.CombatState? state = null;
        try { state = MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.DebugOnlyGetState(); }
        catch { state = null; }
        if (state == null) yield break;

        foreach (var creature in state.Enemies)
        {
            if (creature?.Monster is not Monsters.CharBossMonster boss) continue;
            bool alive = false;
            try { alive = creature.IsAlive && !creature.IsDead; } catch { alive = false; }
            if (!alive) continue;
            yield return boss;
        }
    }

    /// <summary>把上一场留下的面板 / 层 / 计数 / 锁定全部拆掉。</summary>
    public static void Reset(string why)    {
        try
        {
            BossHandUi.Clear();
            BossSpecialUi.HideAll();
            BossExtraStatusRow.HideAll();
            BossHudLayer.Release();

            // 翻侧计数：正常配对会归零，异常中断（战斗被打断、怪物节点被释放）就可能残留。
            // 残留的后果是**后面每一场**的可命中敌人都被翻成玩家侧 ⇒ 攻击像打空气。
            Patches.HittableEnemiesSidePatch.BossActingDepth = 0;
            Patches.HittableEnemiesSidePatch.PlayerSideBossDepth = 0;
            Patches.HittableEnemiesSidePatch.MultiTargetCardDepth = 0;

            Core.BossLockOn.Reset();
            Core.BossOsty.ResetForNewFight();

            Logger.Log("[CharBoss] UI 会话已重置（" + why + "）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] UI 会话重置失败: " + e.Message);
        }
    }
}
