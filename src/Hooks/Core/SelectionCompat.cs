using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.TestSupport;

namespace CharacterBosses.Core;

/// <summary>
/// **选牌器栈的跨版本出口**。
///
/// ## 大坑（v0.2.639 才真正搞清楚）
///
/// 0.113.0 把「自动选牌器」从 `CardSelectCmd` 挪到了 `RunManager.Instance.CardSelectorStack`，
/// 并且改成**按玩家分栈**。而分栈的实现是：
/// <code>
/// private Stack&lt;ICardSelector&gt; GetSelectorStackForPlayer(Player player)
/// {
///     int slot = _playerCollection.GetPlayerSlotIndex(player);
///     if (slot &lt; 0 || slot &gt;= _selectorStacks.Count)
///         throw new InvalidOperationException($"Couldn't get selector stack for player {player.NetId}!");
///     return _selectorStacks[slot];
/// }
/// </code>
/// **影子玩家不在 `_playerCollection.Players` 里**（它不是真玩家）⇒ `GetPlayerSlotIndex` 返回 −1
/// ⇒ <c>PushSelector</c> 必抛；<c>GetSelector</c> 也必抛。
///
/// 于是：`Push` 每次都失败（`mod_log` 里 49 行「选牌器挂载失败 … Couldn't get selector stack」），
/// **自动选择器从来没挂上过** ⇒ 所有「要选牌的牌」（夜魇 / 手上技法 / 洁净 / 暗影步…）
/// 都掉进「真 UI」分支 ⇒ 影子玩家没有 UI ⇒ 卡面停在半空、后面整条剧本连锁停摆。
///
/// ## 现在的做法
///
/// 不再往栈里塞，而是在**唯一的那一层**直接作答：`Patches/BossSelectorStackPatch`
/// 给 `CardSelectorStack.GetSelector(Player)` 挂前缀 —— 只要问的是我们的影子玩家，
/// 直接返回 <see cref="BossCardSelector.Instance"/>。
/// `CardSelectCmd` 的**每一个**选牌入口（`FromHand` / `FromHandForDiscard` / `FromCombatPile` /
/// `FromSimpleGrid` / `FromDeckForUpgrade` / `FromDeckGeneric` / `FromChooseACardScreen` …）
/// 开头都是 `var selector = …GetSelector(player); if (selector != null) …`，
/// 所以一处接管就覆盖全部，也不必再逐个方法打补丁。
/// </summary>
internal static class SelectionCompat
{
    /// <summary>空作用域（没有玩家 / 不需要挂的时候用，保证 `using` 写法统一）。</summary>
    private sealed class Noop : IDisposable
    {
        public void Dispose() { }
    }

    /// <summary>
    /// 把自动选牌器顶到这名玩家的栈上；作用域结束时自动弹出。
    ///
    /// 0.113（111）：**什么都不用做** —— 选牌器由 `BossSelectorStackPatch` 在 `GetSelector` 里给。
    /// 这里保留调用点只是为了让调用方写法不变；顺带消掉那 49 行「挂载失败」噪音日志。
    /// 107（正式版）：还是老的全局栈，照旧挂。
    /// </summary>
    public static IDisposable Push(Player? player, ICardSelector selector)
    {
        if (player == null || selector == null) return new Noop();
#if STS2_107
        try
        {
            return MegaCrit.Sts2.Core.Commands.CardSelectCmd.PushSelector(selector);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 选牌器挂载失败（不挂也能出牌，只是需要选择的牌可能卡住）: " + e.Message);
            return new Noop();
        }
#else
        return new Noop();
#endif
    }

    // ── 「临时让真界面出现」─────────────────────────────────────────────────
    // 只有「抉择储」的指导需要：那一次选牌必须**弹给真人玩家**（见 ChoiceRegentPickPatch）。

    /// <summary>正在「挂起自动选择器」的影子玩家（按实例记；作用域结束就移除）。</summary>
    private static readonly HashSet<Player> Suspended = new();

    private sealed class ResumeScope : IDisposable
    {
        private readonly Player _player;
        private bool _disposed;
        public ResumeScope(Player player) { _player = player; }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (Suspended) Suspended.Remove(_player);
        }
    }

    /// <summary>这名玩家的自动选择器现在被挂起了吗（`BossSelectorStackPatch` 会问）。</summary>
    internal static bool IsSuspended(Player? player)
    {
        if (player == null) return false;
        lock (Suspended) return Suspended.Contains(player);
    }

    /// <summary>临时把该玩家的自动选牌器摘掉 ⇒ 让**真正的选牌界面**出现（抉择储的「指导」用）。</summary>
    public static IDisposable SuspendForUi(Player? player)
    {
        if (player == null) return new Noop();
#if STS2_107
        try
        {
            return MegaCrit.Sts2.Core.Commands.CardSelectCmd.SuspendSelectorForTest();
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 摘选牌器失败（选牌界面可能不出现）: " + e.Message);
            return new Noop();
        }
#else
        // 影子玩家不在栈里 ⇒ 压 null 那招会抛；改用我们自己的挂起标记
        try
        {
            if (player.Creature?.Monster is Monsters.CharBossMonster)
            {
                lock (Suspended) Suspended.Add(player);
                return new ResumeScope(player);
            }
        }
        catch { /* 读不到就按真人玩家处理 */ }

        // 真人玩家：往他栈上压一个 null ⇒ GetSelector(player) 返回 null ⇒ 走真 UI
        try
        {
            return MegaCrit.Sts2.Core.Runs.RunManager.Instance.CardSelectorStack
                .PushSelector(player, null!);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 摘选牌器失败（选牌界面可能不出现）: " + e.Message);
            return new Noop();
        }
#endif
    }
}
