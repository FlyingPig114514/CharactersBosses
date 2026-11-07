using System;
using Godot;

namespace CharacterBosses.UI;

/// <summary>
/// 状态条的定位 —— <see cref="BossStatusRow"/> 的分部文件。
///
/// 用户给的相对约束（不用数像素）：
///   · **能量栏左缘** = 最后一张卡的右缘；
///   · **遗物下端** = 卡牌上端。
///
/// 两个横向基准都取自**本回合最大手牌数**，所以打出卡牌时这条不会跟着缩、不会左滑；
/// 而且每条状态条都只算自己那只 Boss 的手牌栏，多 Boss 同场也各归各的。
///
/// 踩过的坑：
///   ① 曾用「玩家 HUD 遗物栏第一排最右缘」当参照 —— 玩家遗物一少参照点就跑到左上角；
///   ② 曾用手牌栏宽度右对齐 —— 手牌越打越少，宽度缩、整块重新居中，于是往左滑；
///   ③ 曾把 y 也当屏幕坐标换算 —— 整条被推到屏幕上方之外看不见。
/// </summary>
public static partial class BossStatusRow
{
    /// <summary>
    /// 贴到卡牌的右上角（用户给的相对约束，不用数像素）：
    ///   · **能量栏左缘** = 最后一张卡的右缘；
    ///   · **遗物下端** = 卡牌上端。
    ///
    /// 参数都是**手牌栏局部坐标**。<paramref name="cardsRightX"/> 用「本回合最大手牌数」算，
    /// 所以打出卡牌时这条不会跟着缩。
    /// </summary>
    public static void PlaceBesideCards(Control row, float cardsRightX, float cardsTopY)
    {
        BossUiTuning.Refresh();     // 实机可调的微调值（charbosses_ui.txt）

        float width = ContentWidth(row);
        float height = Height(row);
        row.Size = new Vector2(width, height);
        LayoutChildren(row, width);

        float energyWidth = 0f;
        var energy = EnergyOf(row);   // 用这一行自己的能量球（多行状态栏时不能读静态那个）
        if (energy != null && GodotObject.IsInstanceValid(energy)) energyWidth = SizeOf(energy).X;

        // 右端再让出能量球那一格：它的左缘正好落在最后一张卡的右缘上（可加微调）
        float rowRight = cardsRightX + energyWidth + BossUiTuning.EnergyGapX;

        // 遗物那一行的下沿：按**行内子节点的实际位置**算（别写死 IconSide ——
        // 能量比遗物高，各种图标高度也不一定正好等于 IconSide）
        float relicBottom = 0f;
        foreach (var child in row.GetChildren())
        {
            if (child is not Control control || !GodotObject.IsInstanceValid(control)) continue;
            if (ReferenceEquals(control, energy)) continue;      // 能量球不算「遗物那一行」
            relicBottom = Math.Max(relicBottom, control.Position.Y + SizeOf(control).Y);
        }
        if (relicBottom <= 1f) relicBottom = IconSide;

        row.Position = new Vector2(rowRight - width, cardsTopY - relicBottom + BossUiTuning.RelicGapY);

        if (_pinLogs++ < 20)
        {
            Logger.Log($"[CharBoss] 状态条定位: rowY={row.Position.Y:0} relicBottom={relicBottom:0} " +
                       $"cardsTop={cardsTopY:0} rowW={width:0} energyW={energyWidth:0} h={height:0} " +
                       $"gapY={BossUiTuning.RelicGapY} gapX={BossUiTuning.EnergyGapX} 子节点={row.GetChildCount()}");
        }
    }

    /// <summary>定位日志上限（原来每次排版都打，一秒几十行；见 <see cref="PerfLog"/>）。</summary>
    private static int _pinLogs;

    /// <summary>状态条内容实际需要的宽度（各图标宽度之和 + 间距）。</summary>
    private static float ContentWidth(Control row)
    {
        float width = 0f;
        int count = 0;
        foreach (var child in row.GetChildren())
        {
            if (child is not Control control || !GodotObject.IsInstanceValid(control)) continue;
            width += SizeOf(control).X;
            count++;
        }
        return count == 0 ? IconSide : width + Math.Max(0, count - 1) * Gap;
    }

    /// <summary>从右往左逐个摆子节点；高度不同的图标按 <see cref="IconSide"/> 这条基线视觉居中。</summary>
    private static void LayoutChildren(Control row, float width)
    {
        float x = width;
        for (int i = row.GetChildCount() - 1; i >= 0; i--)
        {
            if (row.GetChild(i) is not Control child || !GodotObject.IsInstanceValid(child)) continue;
            var size = SizeOf(child);
            x -= size.X;
            // 顶部不再硬贴 0：能量球比遗物高，就往上抬半格，两者的中线才对得齐；
            // 能量球另外可以用 energyGapY 单独上下微调
            float y = (IconSide - size.Y) * 0.5f;
            if (ReferenceEquals(child, EnergyOf(row))) y += BossUiTuning.EnergyGapY;
            child.Position = new Vector2(x, y);
            x -= Gap;
        }
    }
}
