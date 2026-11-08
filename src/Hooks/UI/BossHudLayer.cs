using Godot;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace CharacterBosses.UI;

/// <summary>
/// 自建 HUD 层（屏幕坐标）+ 战斗内 UI 父节点的统一选择逻辑。
///
/// 以前只在「拿不到战斗房 UI」时才用它；现在多了一个必须用的场合：
/// **斗蛐蛐（CricketFightMod）会把整个 CombatUi 隐藏**（2026-11-08 实测 `CombatUi.Visible=false`），
/// 挂在它下面的手牌栏 / 特殊效果锚点全部 `IsVisibleInTree()=false`，表现为
/// 「Boss 头顶不显示卡牌、遗物不渲染」以及 tooltip 每 400ms 重建一次的闪烁。
/// 所以手牌栏、特殊效果锚点、卡牌展示都改成**按可见性**挑父节点，不可见时统一退回这一层。
/// </summary>
internal static class BossHudLayer
{
    private const int Layer = 80;

    private static CanvasLayer? _layer;

    /// <summary>
    /// 挑战斗内 UI 的父节点：
    /// 战斗房 UI 可见 → 挂它（原版弹窗/提示能盖住我们）；不可见（斗蛐蛐）→ 自建 HUD 层。
    /// </summary>
    public static Node? Resolve(out string why)
    {
        var ui = NCombatRoom.Instance?.Ui;
        var play = ui?.GetNodeOrNull<Control>("%PlayContainer");

        if (play != null && play.IsInsideTree() && play.IsVisibleInTree())
        {
            why = "战斗房 UI 层";
            return play;
        }
        if (ui != null && ui.IsInsideTree() && ui.IsVisibleInTree())
        {
            why = "战斗房 UI 根节点（PlayContainer 不可见）";
            return ui;
        }

        var layer = Get();
        why = ui == null ? "自建 HUD 层：没有战斗房" : "自建 HUD 层：战斗 UI 被隐藏（斗蛐蛐）";
        return layer;
    }

    /// <summary>拿到（必要时创建）共用 HUD 层；没有 SceneTree 时返回 null。</summary>
    public static CanvasLayer? Get()
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return null;

        if (_layer != null && GodotObject.IsInstanceValid(_layer)) return _layer;

        _layer = new CanvasLayer { Name = "CharacterBossesHud", Layer = Layer };
        tree.Root.AddChild(_layer);
        Logger.Log("[CharBoss] 已创建自建 HUD 层 CharacterBossesHud（战斗 UI 不可见时使用）");
        return _layer;
    }

    /// <summary>战斗结束/界面收尾时释放。</summary>
    public static void Release()
    {
        if (_layer != null && GodotObject.IsInstanceValid(_layer)) _layer.QueueFree();
        _layer = null;
    }
}
