using System;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace CharacterBosses.UI;

/// <summary>
/// 卡面的鼠标交互 —— <see cref="BossHandCard"/> 的分部文件（守住单文件 ≤200 行）。
///
/// 两件事，都是**用户明确要求/实测踩过**的：
///   1. 悬停把这张卡放大到 2×（只动卡面节点自己的 Scale，意图行与别的卡都不动），并抬高 ZIndex 免得被右边的卡盖住；
///   2. 拆格子前先发 <c>MouseExited</c> —— NCard 自带悬浮大卡预览，节点在悬停状态下被 QueueFree
///      会收不到退出事件，预览面板就残留成一张大卡吊在屏幕中央。
/// </summary>
public static partial class BossHandCard
{
    /// <summary>鼠标悬停时卡面放大倍数（长宽各 ×2），只影响这一张卡，意图等节点不动。</summary>
    private const float HoverScale = 2f;

    private static void WireHover(Control cell, NCard cardNode)
    {
        cardNode.MouseEntered += () => SetHovered(cell, cardNode, true);
        cardNode.MouseExited += () => SetHovered(cell, cardNode, false);
    }

    private static void SetHovered(Control cell, NCard cardNode, bool hovered)
    {
        try
        {
            if (!GodotObject.IsInstanceValid(cell) || !GodotObject.IsInstanceValid(cardNode)) return;
            float scale = hovered ? CardScale * HoverScale : CardScale;
            cardNode.Scale = new Vector2(scale, scale);
            cell.ZIndex = hovered ? 20 : 0;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] card hover failed: " + e.Message);
        }
    }

    /// <summary>
    /// 拆格子前先「礼貌地」把鼠标移出去，关掉 NCard 自带的悬浮大卡预览。
    ///
    /// ⚠ 2026-10-29 加固（用户：「奇巧猎的打击停在了空中」= 一张大卡吊在屏幕中央不走）：
    /// 以前只对**直接子节点**发 <c>MouseExited</c>；万一卡面被再套了一层容器
    /// （或者大卡预览是挂在更深的子节点上的），那一次退出事件就发不到，预览面板就残留。
    /// 现在**递归**遍历整棵子树，每个 <c>NCard</c> 都先把 <c>MouseFilter</c> 设成 Ignore
    /// （这样后续的 enter/exit 一律不会再触发）再发退出事件。
    /// </summary>
    public static void ReleaseCell(Control cell)
    {
        try
        {
            if (cell == null || !GodotObject.IsInstanceValid(cell)) return;
            ReleaseCardsRecursive(cell);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] card release failed: " + e.Message);
        }
    }

    private static void ReleaseCardsRecursive(Node node)
    {
        try
        {
            if (node == null || !GodotObject.IsInstanceValid(node)) return;

            if (node is NCard card)
            {
                card.MouseFilter = Control.MouseFilterEnum.Ignore;
                card.EmitSignal(Control.SignalName.MouseExited);
            }

            foreach (var child in node.GetChildren())
            {
                if (child == null || !GodotObject.IsInstanceValid(child)) continue;
                ReleaseCardsRecursive(child);
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] card release (recursive) failed: " + e.Message);
        }
    }
}
