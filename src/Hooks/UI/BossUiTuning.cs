using System;
using System.Globalization;
using System.IO;

namespace CharacterBosses.UI;

/// <summary>
/// 界面微调参数（**改文件就生效，不用重新编译**）。
///
/// 文件位置：mods\CharacterBosses\charbosses_ui.txt（和 charbosses_debug.txt 同一目录）
/// 内容形如（`#` 开头是注释，没写到的键用默认值）：
///
/// <code>
/// # 状态条整体上下微调（正数=下移，负数=上移，单位：逻辑像素）
/// relicGapY=0
/// # 能量球左缘相对「最后一张卡右缘」的额外偏移（正数=更靠右）
/// energyGapX=0
/// </code>
///
/// 为什么要这个文件：原版 <c>NRelicInventoryHolder</c> / <c>NEnergyCounter</c> 的图标在各自节点里
/// 可能有内部偏移（阴影/描边/锚点），光看排版数字（`rowY + relicBottom == cardsTop` 已经严格成立）
/// 推不出「看起来」该挪多少 —— 这个值交给使用者实机调最快。
/// 每次排版都会检查文件修改时间（≤1 秒缓存），所以改完保存、下一回合就按新值排。
/// </summary>
internal static class BossUiTuning
{
    private const string FileName = "charbosses_ui.txt";

    // 默认值 = 实机调出来的手感（用户认可的那组）。
    private const int DefaultRelicGapY = -80;
    private const int DefaultEnergyGapX = -10;
    private const int DefaultEnergyGapY = 0;

    public static int RelicGapY { get; private set; } = DefaultRelicGapY;
    public static int EnergyGapX { get; private set; } = DefaultEnergyGapX;

    /// <summary>能量球相对「与遗物中线对齐」的额外上下偏移（正数=下移）。</summary>
    public static int EnergyGapY { get; private set; } = DefaultEnergyGapY;

    /// <summary>「牺牲」意图图标（翻转后的强化箭头）相对原位的上下微调，正数=下移。</summary>
    public static int SacrificeIconY { get; private set; }

    /// <summary>
    /// 药水意图相对「药水图标下沿」再往下多少（用户 2026-10-26 可热调）。
    ///
    /// ⚠ 默认 **-60** = 意图中心正好压在药水图标中心（**故意重叠**）：
    /// 用户 2026-10-26 实测「放在下面会被她打出的那排卡牌挡住」（第三回合她有 8 张牌，卡面顶到状态条下沿），
    /// 所以改成直接叠在药水上、靠药水图标本身当底衬。
    /// 计算：中心 y = 图标高 56 + gap + 32（意图半径）⇒ gap=-60 → 中心 y=28 = 图标中线。
    /// </summary>
    public static int PotionIntentGapY { get; private set; } = DefaultPotionIntentGapY;

    /// <summary>药水意图的缩放百分比（100 = 和卡牌下面那排意图一样大；用户 2026-10-26 嫌 50 太小）。</summary>
    public static int PotionIntentScalePct { get; private set; } = DefaultPotionIntentScalePct;

    /// <summary>
    /// 「特殊效果说明框」在**炼药猎**身上额外上移多少（负数 = 上移，单位：逻辑像素）。
    ///
    /// 只有炼药猎用（她的「调制」描述最长）；其它 Boss 一律 0 = 位置完全不变（用户 2026-10-26 要求）。
    /// </summary>
    public static int BrewSpecialY { get; private set; } = DefaultBrewSpecialY;

    private const int DefaultPotionIntentGapY = -60;
    private const int DefaultPotionIntentScalePct = 100;
    private const int DefaultBrewSpecialY = -120;

    // ── 多 Boss 同场时的站位间距（用户 2026-10-29 要求 4 人联机排成平行四边形）──
    private const int DefaultBossSpreadX = 200;
    private const int DefaultBossSpreadY = 140;
    private const int DefaultBoss4MidX = 110;

    /// <summary>多 Boss 站位：水平方向的基准间距（像素）。</summary>
    public static int BossSpreadX { get; private set; } = DefaultBossSpreadX;

    /// <summary>多 Boss 站位：竖直方向的错开量（像素）。</summary>
    public static int BossSpreadY { get; private set; } = DefaultBossSpreadY;

    /// <summary>4 只同场时，中间那两只离中线的水平距离（「下偏左 / 上偏右」偏多少）。</summary>
    public static int Boss4MidX { get; private set; } = DefaultBoss4MidX;

    private static DateTime _lastWrite = DateTime.MinValue;
    private static DateTime _lastCheck = DateTime.MinValue;
    private static bool _logged;

    /// <summary>排版前调一次：文件变了就重读。</summary>
    public static void Refresh()
    {
        try
        {
            if ((DateTime.UtcNow - _lastCheck).TotalSeconds < 1.0) return;
            _lastCheck = DateTime.UtcNow;

            string path = PathOf();
            if (!File.Exists(path))
            {
                WriteTemplate(path);
                return;
            }

            var stamp = File.GetLastWriteTimeUtc(path);
            if (stamp == _lastWrite) return;
            _lastWrite = stamp;

            RelicGapY = DefaultRelicGapY;
            EnergyGapX = DefaultEnergyGapX;
            EnergyGapY = DefaultEnergyGapY;
            PotionIntentGapY = DefaultPotionIntentGapY;
            PotionIntentScalePct = DefaultPotionIntentScalePct;
            BrewSpecialY = DefaultBrewSpecialY;
            SacrificeIconY = 0;
            BossSpreadX = DefaultBossSpreadX;
            BossSpreadY = DefaultBossSpreadY;
            Boss4MidX = DefaultBoss4MidX;

            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                var key = line.Substring(0, eq).Trim();
                var value = line.Substring(eq + 1).Trim();
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) continue;

                if (key.Equals("relicGapY", StringComparison.OrdinalIgnoreCase)) RelicGapY = n;
                else if (key.Equals("energyGapX", StringComparison.OrdinalIgnoreCase)) EnergyGapX = n;
                else if (key.Equals("energyGapY", StringComparison.OrdinalIgnoreCase)) EnergyGapY = n;
                else if (key.Equals("potionIntentGapY", StringComparison.OrdinalIgnoreCase)) PotionIntentGapY = n;
                else if (key.Equals("potionIntentScalePct", StringComparison.OrdinalIgnoreCase)) PotionIntentScalePct = n;
                else if (key.Equals("brewSpecialY", StringComparison.OrdinalIgnoreCase)) BrewSpecialY = n;
                else if (key.Equals("sacrificeIconY", StringComparison.OrdinalIgnoreCase)) SacrificeIconY = n;
                else if (key.Equals("bossSpreadX", StringComparison.OrdinalIgnoreCase)) BossSpreadX = n;
                else if (key.Equals("bossSpreadY", StringComparison.OrdinalIgnoreCase)) BossSpreadY = n;
                else if (key.Equals("boss4MidX", StringComparison.OrdinalIgnoreCase)) Boss4MidX = n;
            }

            if (_logged)
            {
                Logger.Log($"[CharBoss] UI 微调已更新 {path}: relicGapY={RelicGapY} energyGapX={EnergyGapX}");
            }
            else
            {
                Logger.Log($"[CharBoss] UI 微调已读取 {path}: relicGapY={RelicGapY} energyGapX={EnergyGapX}");
            }
            _logged = true;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 读 UI 微调失败: " + e.Message);
        }
    }

    private static string PathOf()
    {
        // ⚠ 不能用 AppContext.BaseDirectory：程序集是被游戏进程加载的，
        // 那里指向**游戏 exe 目录**（踩过：模板文件被生成到 data_sts2_windows_x86_64\，
        // 用户改 mods\CharacterBosses\ 里那份永远读不到）。
        // 用程序集自己的位置才是真正的 mod 目录。
        try
        {
            var location = typeof(BossUiTuning).Assembly.Location;
            if (!string.IsNullOrEmpty(location))
            {
                var dir = Path.GetDirectoryName(location);
                if (!string.IsNullOrEmpty(dir)) return Path.Combine(dir, FileName);
            }
        }
        catch { /* 退回落目录 */ }

        return Path.Combine(AppContext.BaseDirectory, FileName);
    }

    private static void WriteTemplate(string path)
    {
        try
        {
            File.WriteAllText(path,
                "# CharacterBosses 界面微调（改完保存，下一回合生效；不用重新编译）\n" +
                "# relicGapY: 状态条整体上下微调，正数=下移、负数=上移（逻辑像素）\n" +
                $"relicGapY={DefaultRelicGapY}\n" +
                "# energyGapX: 能量球左缘相对「最后一张卡右缘」的额外偏移，正数=更靠右\n" +
                $"energyGapX={DefaultEnergyGapX}\n" +
                "# energyGapY: 能量球相对「与遗物中线对齐」的上下偏移，正数=下移、负数=上移\n" +
                $"energyGapY={DefaultEnergyGapY}\n" +
                "# 药水意图：potionIntentGapY = 箭头中心相对药水图标下沿再往下多少（正数=更往下）\n" +
                $"potionIntentGapY={DefaultPotionIntentGapY}\n" +
                "# potionIntentScalePct: 药水意图缩放百分比（100 = 与卡牌下面那排意图同大）\n" +
                $"potionIntentScalePct={DefaultPotionIntentScalePct}\n" +
                "# brewSpecialY: 只有「炼药猎」的特殊效果说明框额外上下偏移（负数=上移；其它 Boss 不受影响）\n" +
                $"brewSpecialY={DefaultBrewSpecialY}\n" +
                "# bossSpreadX / bossSpreadY: 多人同场多只 Boss 时的站位间距（水平 / 竖直，像素）\n" +
                $"bossSpreadX={DefaultBossSpreadX}\n" +
                $"bossSpreadY={DefaultBossSpreadY}\n" +
                "# boss4MidX: 4 只同场时中间两只（下偏左 / 上偏右）离中线多远\n" +
                $"boss4MidX={DefaultBoss4MidX}\n");
            _lastWrite = File.GetLastWriteTimeUtc(path);
            Logger.Log("[CharBoss] 已生成 UI 微调模板: " + path);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 生成 UI 微调模板失败: " + e.Message);
        }
    }
}
