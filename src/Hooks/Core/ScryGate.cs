#if STS2_111   // 观者 Boss 只在这个构建里
using System.Threading.Tasks;

namespace CharacterBosses.Core;

/// <summary>
/// **「预见观」让玩家预见 2 时的全局闸门**（用户 2026-11-18：「类似抉择储，她特殊效果触发让玩家预见 2
/// 的时候自身停止打牌，也阻止其他 Boss 开始打牌，直到玩家选完牌关闭预见界面再打牌」）。
///
/// 做法和「抉择储」的「抉择，抉择」同一套：
/// · <see cref="Hold"/>：在她回合开始、真正调起预见**之前**同步占住（不能等异步任务跑起来才占，
///   否则她这一回合的头几张牌可能已经打出去了）；
/// · `CharBossMonster.PlayOne` 开头 `await` 这个闸门 ⇒ **她**和**同场的其它 Boss** 都停在这里；
/// · <see cref="Release"/>：玩家选完、预见界面关掉（`ScryPlayerAsync` 返回）后放行；
/// · 同时把 `StrayBossCardQueuePatch.HoldReap` 立起来 —— 否则 4Hz 慢车道会把「停在半空的卡面」
///   当成残留从出牌队列里摘掉，等于提前打断她这一手（抉择储踩过这个坑）。
/// 恐惧模式下玩家**不**预见（改成丢他一张牌）⇒ 完全不走这条闸门。
/// </summary>
internal static class ScryGate
{
    private static TaskCompletionSource? _tcs;

    internal static bool Held => _tcs != null;

    internal static Task WaitAsync() => _tcs?.Task ?? Task.CompletedTask;

    internal static void Hold()
    {
        try
        {
            if (_tcs != null) return;
            _tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Patches.StrayBossCardQueuePatch.HoldReap = true;
            Logger.Log("[CharBoss] 通晓万物：玩家要预见 ⇒ 全体 Boss 暂停出牌，等她选完牌");
        }
        catch { /* 忽略 */ }
    }

    internal static void Release()
    {
        try
        {
            var t = _tcs;
            _tcs = null;
            Patches.StrayBossCardQueuePatch.HoldReap = false;
            t?.TrySetResult();
            Logger.Log("[CharBoss] 通晓万物：玩家选完牌 ⇒ 放行，Boss 继续出牌");
        }
        catch { /* 忽略 */ }
    }
}
#endif