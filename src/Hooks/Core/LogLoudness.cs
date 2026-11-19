using System;

namespace CharacterBosses.Core;

/// <summary>
/// **诊断日志的响度闸门**（v0.2.805，用户 2026-11-19 工坊反馈「手机版四人联机卡顿明显，耗电也明显」）。
///
/// 为什么需要：本模组的诊断日志非常密。实测一局 3 小时 40 分写进 `mod_log.txt`
/// **203,646 行 / 27.8 MB**（平均约 15 行/秒），其中光是 `帧回调耗时(每120帧)` 一项就 59,064 行（29%），
/// 而 `ModEntry.Logger` / `Patches.SafeLogPatch` 是**每写一行就 File.AppendAllText 一次**（开→写→关文件）。
/// PC 上这点 I/O 无所谓；**手机上每次写 flash 都贵**，还费电 —— 这才是「手机版卡顿 + 耗电」的大头
/// （帧回调本身实测只有 avg=0.05~0.08ms，占一帧预算的 0.5%）。
///
/// 规则：
///   · 手机上（Android / iOS）⇒ **纯诊断行一律不打**，只保留同步/结阵这类「出问题时要靠它定位」的行；
///   · PC 上 ⇒ 行为与以前完全一致（零回归）；
///   · 手机上也仍然想看到「真的卡了」的证据 ⇒ 用 <see cref="DiagStall"/>（只有超过阈值才打一行）。
/// </summary>
internal static class LogLoudness
{
    private static int _mobile = -1;      // -1 = 还没探测；0 = 否；1 = 是

    /// <summary>本机是不是手机/平板（Android / iOS）。探测失败一律按「不是」处理（宁可多打日志）。</summary>
    internal static bool IsMobile
    {
        get
        {
            if (_mobile >= 0) return _mobile == 1;
            int v = 0;
            try
            {
                string name = Godot.OS.GetName() ?? string.Empty;
                if (name.Equals("Android", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("iOS", StringComparison.OrdinalIgnoreCase))
                {
                    v = 1;
                }
                // 兜底：有些移植版把 GetName() 报成 "Windows" 但特征里有 touchscreen；这里不猜，
                // 只要拿到明确结果就缓存下来。
            }
            catch { v = 0; }
            _mobile = v;
            return v == 1;
        }
    }

    /// <summary>能不能打纯诊断日志（PC = 能，手机 = 不能）。</summary>
    internal static bool Verbose => !IsMobile;

    /// <summary>纯诊断日志（帧耗时、探针、性能采样、效果追踪…）。手机上直接丢掉。</summary>
    internal static void Diag(string message)
    {
        if (IsMobile) return;
        try { Logger.Log(message); } catch { /* 绝不影响主流程 */ }
    }

    /// <summary>
    /// 「只有真的出问题时才值得看一眼」的诊断：手机上只在数值超过阈值时打一行。
    /// 用于帧耗时这类 —— 手机上不想被刷屏，但真卡了必须留证据。
    /// </summary>
    internal static void DiagStall(double valueMs, double thresholdMs, string message)
    {
        try
        {
            if (!IsMobile) { Logger.Log(message); return; }
            if (valueMs >= thresholdMs) Logger.Log(message);
        }
        catch { /* 绝不影响主流程 */ }
    }
}
