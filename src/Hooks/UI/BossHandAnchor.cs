using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using CharacterBosses.Monsters;

namespace CharacterBosses.UI;

/// <summary>
/// 手牌栏的锚点：Boss 立绘包围盒的顶部中心。
/// 注意不能用 <c>NCreature</c> 自己的矩形 —— 那只是交互框，比立绘矮得多，手牌栏会压在 Boss 身上
/// （实测：NCreature 给的顶部是 y=740，而立绘实际从 y=462 开始）。
/// </summary>
public static class BossHandAnchor
{
    /// <summary>换 Boss / 离开战斗时清掉缓存（保留接口，位置现在是实时量取的）。</summary>
    public static void ResetLock()
    {
    }

    /// <summary>返回（水平中心, 顶部, 调试描述, 是否量到了真立绘包围盒）。</summary>
    public static (float X, float Top, string Debug, bool Trusted) Resolve(CharBossMonster? boss, Vector2 viewport)
    {
        var creatureNode = boss != null && NCombatRoom.Instance != null
            ? NCombatRoom.Instance.GetCreatureNode(boss.Creature)
            : null;

        string? bossKey = null;
        try { bossKey = boss?.Def?.Key; } catch { /* 忽略 */ }

        return Measure(creatureNode, viewport, bossKey);
    }

    /// <summary>
    /// 立绘包围盒（屏幕坐标）。给「以 Boss 为坐标」的东西用（状态条的横向定位就靠它）——
    /// 这样多个角色 Boss 同场时，每条状态条只跟着自己的 Boss，与手牌张数无关。
    /// </summary>
    public static (float Left, float Top, float Width, float Height) ResolveRect(CharBossMonster? boss, Vector2 viewport)
    {
        var creatureNode = boss != null && NCombatRoom.Instance != null
            ? NCombatRoom.Instance.GetCreatureNode(boss.Creature)
            : null;

        if (creatureNode == null) return (0f, 0f, viewport.X, viewport.Y);

        var bounds = creatureNode.Visuals?.Bounds;
        if (bounds != null && GodotObject.IsInstanceValid(bounds))
        {
            var pos = bounds.GetScreenPosition();
            var size = bounds.Size;
            if (size.X > 1f && size.Y > 1f) return (pos.X, pos.Y, size.X, size.Y);
        }

        // 兜底：NCreature 自己的矩形（只是交互框，比立绘矮）
        var fallbackPos = creatureNode.GetScreenPosition();
        var fallbackSize = creatureNode.Size;
        return (fallbackPos.X, fallbackPos.Y, Math.Max(1f, fallbackSize.X), Math.Max(1f, fallbackSize.Y));
    }

    /// <summary>
    /// 每次都按**当前**立绘包围盒实时量取。
    ///
    /// 早先这里做过「锁一次偏移、之后复用」，本意是防受击抖动；
    /// 但实测发现：用模组拖动 Boss 时手牌栏不跟（日志确认帧回调是跑着的），
    /// 说明锁定把位移吃掉了。相对位置的稳定交给排版本身（各行都在面板内按固定数值排），
    /// 位置就老实跟着立绘走。
    /// </summary>
    private static (float X, float Top, string Debug, bool Trusted) Measure(
        NCreature? creatureNode, Vector2 viewport, string? bossKey = null)
    {
        var anchorX = viewport.X * 0.5f;
        var anchorTop = viewport.Y * 0.35f;
        var debug = "fallback";

        if (creatureNode == null) return (anchorX, anchorTop, debug, false);

        var screenPos = creatureNode.GetScreenPosition();
        var width = creatureNode.Size.X > 1f ? creatureNode.Size.X : 0f;
        anchorX = screenPos.X + width * 0.5f;
        anchorTop = screenPos.Y;
        debug = $"creature={screenPos}/{creatureNode.Size}";

        var bounds = creatureNode.Visuals?.Bounds;
        if (bounds != null && GodotObject.IsInstanceValid(bounds))
        {
            var boundsPos = bounds.GetScreenPosition();
            var boundsSize = bounds.Size;
            debug += $", bounds={boundsPos}/{boundsSize}";
            // 「尺寸正常」还不够：如果这个包围盒其实就是交互框（顶部和 creature 顶几乎一样），
            // 那它同样是不可信的（实测：六火亡魂当玩家时，这两者会重合到只差几像素）。
            if (boundsSize.X > 1f && boundsSize.Y > 1f && boundsPos.Y < screenPos.Y - MinArtRise)
            {
                anchorX = boundsPos.X + boundsSize.X * 0.5f;
                anchorTop = boundsPos.Y;
                // 量到真立绘了 —— 这是可信锚点（绝大多数情况，其它角色都走这条）
                return (anchorX, anchorTop, debug, true);
            }
        }

        // 量不到可信的立绘包围盒（`Visuals.Bounds` 缺失 / 等于交互框，实测：玩家用「六火亡魂」
        // 「史莱姆老大」这类崩坠角色时会发生）⇒ 现在手里的 anchorTop 是 `NCreature` 的**交互框顶**，
        // 比立绘实际顶部低 200 上下，整块手牌栏会压到 Boss 身上。
        // 用「立绘比交互框高多少」补偿回去（每个角色的美术不同，见 ArtRiseOf），
        // 并返回 Trusted=false，让调用方再叠一道「屏幕上半区」的兜底。
        float artRise = ArtRiseOf(bossKey);
        anchorTop -= artRise;
        debug += $", 补偿ArtRise={artRise:0}";
        return (anchorX, anchorTop, debug, false);
    }

    /// <summary>
    /// 各角色立绘顶部比 <c>NCreature</c> 交互框顶部高多少（实测值，来自 `mod_log.txt` 里
    /// `anchor=creature=(1440, 680)/(0, 0), bounds=(…, 457~485)/…` 这类行：680 − boundsY）。
    ///
    /// ⚠ 只在**量不到可信包围盒**时才用；正常情况（绝大多数角色）一个像素都不用。
    /// </summary>
    private static float ArtRiseOf(string? bossKey) => bossKey switch
    {
        "IRONCLAD_BOSS" => 222f,
        "SILENT_BOSS" => 196f,
        "DEFECT_BOSS" => 210f,      // 没量到过实测值，用中值
        "NECROBINDER_BOSS" => 216f,
        "REGENT_BOSS" => 268f,
        _ => FallbackArtRise,
    };

    /// <summary>认不出角色时用的中值补偿。</summary>
    private const float FallbackArtRise = 210f;

    /// <summary>包围盒顶端至少要高出交互框这么多，才算「真的量到立绘了」（实测正常是 195~268）。</summary>
    private const float MinArtRise = 60f;
}
