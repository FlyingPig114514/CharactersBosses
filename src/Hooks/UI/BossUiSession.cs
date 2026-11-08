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
            if (id == 0) return;

            Reset("新的战斗房 " + id);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 会话切换检查失败: " + e.Message);
        }
    }

    /// <summary>把上一场留下的面板 / 层 / 计数 / 锁定全部拆掉。</summary>
    public static void Reset(string why)
    {
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
