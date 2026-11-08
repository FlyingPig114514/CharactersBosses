using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using CharacterBosses.Monsters;

namespace CharacterBosses.UI;

/// <summary>
/// 「把这些牌亮出来给玩家看」—— 纯展示，**不写入任何牌堆**。
///
/// 用途：亡灵契约师的「能量汲取」（<c>DrainPower</c>）会随机升级**她弃牌堆里**可升级的牌
/// （反编译：`PileType.Discard.GetPile(Owner).Cards.Where(IsUpgradable).TakeRandom(N)` → `CardCmd.Upgrade`）。
/// 玩家自己打这张牌时，升级发生在**可见的玩家弃牌堆**里，能看见牌面变化；
/// 而她的弃牌堆没有 UI ⇒ 什么都看不到。用户要求：把升级后的牌展示出来，
/// 但**别塞进玩家弃牌堆**（那会污染玩家的牌堆数据）。
///
/// 做法：复用原版卡牌节点（<see cref="BossHandCard.BuildCell"/>），在她的立绘上方摆一排，
/// 过一会儿自动消失；全程只读那几张卡实例，不碰任何 PileType。
/// </summary>
internal static class BossCardShowcase
{
    private const float CellSpacing = 112f;
    private const float CardScale = 0.34f;

    /// <summary>把这几张牌摆出来看一下（默认 2.5 秒后自动消失）。</summary>
    public static void Show(CharBossMonster boss, IReadOnlyList<CardModel> cards, float seconds = 2.5f)
    {
        if (boss == null || cards == null || cards.Count == 0) return;

        try
        {
            var parent = BossHudLayer.Resolve(out _);
            if (parent == null) return;

            var layer = new Control
            {
                Name = "BossCardShowcase",
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            parent.AddChild(layer);

            var creatureNode = boss.Creature != null ? NCombatRoom.Instance?.GetCreatureNode(boss.Creature) : null;
            Vector2 center = creatureNode?.GlobalPosition ?? new Vector2(1400f, 560f);
            float width = CellSpacing * cards.Count;
            layer.Position = center + new Vector2(-width * 0.5f, -300f);

            var cells = new List<Control>();
            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                if (card == null) continue;

                // planned 非 null 才会按原色显示（否则会被压暗成"不打的牌"）
                var planned = new Core.PlannedPlay
                {
                    Card = card,
                    Target = boss.Creature,
                    Cost = 0,
                    StarCost = 0,
                };
                var cell = BossHandCard.BuildCell(layer, boss, card, planned, bright: true);
                cell.Position = new Vector2(i * CellSpacing, 0f);
                cells.Add(cell);
            }

            Logger.Log($"[CharBoss] 展示 {cells.Count} 张升级后的牌（仅展示，不写入任何牌堆）");
            TaskHelper.RunSafely(DismissLater(layer, cells, seconds));
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 展示升级牌失败: " + e.Message);
        }
    }

    private static async Task DismissLater(Control layer, List<Control> cells, float seconds)
    {
        try
        {
            await Cmd.CustomScaledWait(seconds, seconds + 0.6f);
            foreach (var cell in cells)
            {
                if (!GodotObject.IsInstanceValid(cell)) continue;
                BossHandCard.ReleaseCell(cell);
                cell.QueueFree();
            }
            if (GodotObject.IsInstanceValid(layer)) layer.QueueFree();
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 展示牌收尾失败: " + e.Message);
        }
    }
}
