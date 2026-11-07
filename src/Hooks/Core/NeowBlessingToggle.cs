using System;
using System.IO;

namespace CharacterBosses.Core;

/// <summary>
/// 「涅奥的祝福」出现开关。
///
/// 这个先古遗物**默认不出现**在涅奥的选项里（避免污染正常爬塔），
/// 想让它出现在涅奥的第三个选项时，在 mod 目录下建一个 <c>charbosses_neow_relic.txt</c> 即可。
/// （角色 Boss 侧的携带不受这个开关影响 —— Boss 永远带着它。）
/// </summary>
public static class NeowBlessingToggle
{
    public const string FileName = "charbosses_neow_relic.txt";

    private static bool _loaded;
    private static bool _enabled;

    public static bool Enabled
    {
        get
        {
            if (_loaded) return _enabled;
            _loaded = true;
            try
            {
                var dir = Path.GetDirectoryName(typeof(NeowBlessingToggle).Assembly.Location);
                _enabled = !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, FileName));
                Logger.Log($"[CharBoss] 涅奥的祝福：{( _enabled ? "已启用（会出现在涅奥选项）" : "默认关闭")}");
            }
            catch
            {
                _enabled = false;
            }
            return _enabled;
        }
    }
}
