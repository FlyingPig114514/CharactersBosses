using System;
using System.IO;
using System.Text;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace CharacterBosses.Core;

/// <summary>
/// **按 F9 把当前 Godot 场景树导成 txt**（用户 2026-11-11 要「能看各种节点」）。
///
/// 为什么不直接用 Godot 编辑器：游戏是编译好的 release 包，编辑器只能靠「远程调试」挂上去，
/// 而且要在编辑器里点菜单。这里给一条**游戏内一键**的路：任意界面按 F9 →
///   · <c>%APPDATA%\SlayTheSpire2\scene_tree.txt</c>        整棵树（路径 / 类名 / 可见性 / 尺寸 / 文本）
///   · <c>%APPDATA%\SlayTheSpire2\scene_tree_combat.txt</c> 只导当前战斗房那棵子树（有战斗时）
/// 同时在 <c>mod_log.txt</c> 里打一行「已导出：…（N 行）」。
///
/// Shift+F9 = 连**隐藏节点**一起导（默认也导，但会把 <c>Visible=false</c> 标出来；
/// 两个键的区别只是「是否裁剪掉文本为空又没有子节点的叶子」，一般用 F9 就够）。
/// </summary>
internal sealed partial class SceneTreeDumper : Node
{
    private const int MaxLines = 60000;
    private static SceneTreeDumper? _inst;

    /// <summary>幂等挂载（由帧回调每帧调一次，内部只有一次真正创建）。</summary>
    internal static void Ensure()
    {
        try
        {
            if (_inst != null && GodotObject.IsInstanceValid(_inst)) return;
            if (Engine.GetMainLoop() is not SceneTree tree) return;
            _inst = new SceneTreeDumper { Name = "CharacterBossesTreeDumper" };
            tree.Root.AddChild(_inst);
            _inst.SetProcessUnhandledKeyInput(true);
            Logger.Log($"[CharBoss] 场景树导出：按 **F9** 导出到 {TargetPath()}（战斗房子树另存一条）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 场景树导出器挂载失败: " + e.Message);
        }
    }

    private static string TargetPath()
        => Path.Combine(OS.GetUserDataDir(), "scene_tree.txt");

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        try
        {
            if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;
            if (key.Keycode != Key.F9 && key.PhysicalKeycode != Key.F9) return;
            DumpAll();
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 场景树导出失败: " + e.Message);
        }
    }

    /// <summary>导整棵树 + （有战斗时）战斗房子树。</summary>
    internal static void DumpAll()
    {
        try
        {
            if (Engine.GetMainLoop() is not SceneTree tree) return;

            var (text, lines) = Dump(tree.Root);
            var path = TargetPath();
            File.WriteAllText(path, text, new UTF8Encoding(false));
            Logger.Log($"[CharBoss] 场景树已导出：{path}（{lines} 行）");

            var combat = NCombatRoom.Instance;
            if (combat != null && GodotObject.IsInstanceValid(combat))
            {
                var (ctext, clines) = Dump(combat);
                var cpath = Path.Combine(OS.GetUserDataDir(), "scene_tree_combat.txt");
                File.WriteAllText(cpath, ctext, new UTF8Encoding(false));
                Logger.Log($"[CharBoss] 战斗房子树已导出：{cpath}（{clines} 行）");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 场景树导出（整体）失败: " + e.Message);
        }
    }

    /// <summary>把一个节点子树写成缩进文本。</summary>
    private static (string Text, int Lines) Dump(Node root)
    {
        var sb = new StringBuilder();
        int lines = 0;
        int skipped = 0;

        void Walk(Node node, int depth)
        {
            if (node == null || !GodotObject.IsInstanceValid(node)) return;
            if (lines >= MaxLines) { skipped++; return; }

            var pad = new string(' ', depth * 2);
            string path;
            try { path = node.GetPath().ToString(); } catch { path = "?"; }
            string extra = string.Empty;
            try
            {
                if (node is CanvasItem ci)
                {
                    extra += $" visible={ci.Visible} a={ci.Modulate.A:0.00}";
                    if (node is Control c)
                        extra += $" pos=({c.GlobalPosition.X:F0},{c.GlobalPosition.Y:F0}) size=({c.Size.X:F0}x{c.Size.Y:F0})";
                }
                string text = string.Empty;
                if (node is Label lb) text = lb.Text ?? string.Empty;
                else if (node is RichTextLabel rtl) text = rtl.GetParsedText() ?? string.Empty;
                else if (node is Button bt) text = bt.Text ?? string.Empty;
                else if (node is CheckBox cb) text = cb.Text ?? string.Empty;
                else if (node is Godot.TextEdit te) text = te.Text ?? string.Empty;
                else if (node is Godot.LineEdit le) text = le.Text ?? string.Empty;
                text = text.Replace('\n', ' ').Trim();
                if (text.Length > 60) text = text.Substring(0, 60) + "…";
                if (text.Length > 0) extra += $" text=\"{text}\"";
            }
            catch { /* 单个节点读不到就少写几列 */ }

            sb.Append(pad).Append(node.Name).Append(" (").Append(node.GetType().Name).Append(')')
              .Append(extra).Append("  @").Append(path).Append('\n');
            lines++;

            var children = node.GetChildren();
            foreach (var child in children)
            {
                if (child is Node n) Walk(n, depth + 1);
            }
        }

        Walk(root, 0);
        if (skipped > 0) sb.Append("…（超过 ").Append(MaxLines).Append(" 行，已截断，跳过 ").Append(skipped).Append(" 个分支）\n");
        sb.Append("共 ").Append(lines).Append(" 行\n");
        return (sb.ToString(), lines);
    }
}
