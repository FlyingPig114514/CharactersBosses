#if STS2_111
using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.TestSupport;
using CharacterBosses.Core;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// ★ 2026-11-13（用户：「洁净也会卡在空中。你看看还有哪些牌涉及选牌？一起修一下」）
///
/// ## 唯一的根因
///
/// `mod_log` 里 49 行：
/// <code>选牌器挂载失败（不挂也能出牌，只是需要选择的牌可能卡住）: Couldn't get selector stack for player 18446744069414631638!</code>
/// 反编译 0.113 的 <c>CardSelectorStack.GetSelectorStackForPlayer</c>：
/// <code>
/// int slot = _playerCollection.GetPlayerSlotIndex(player);
/// if (slot &lt; 0 || slot &gt;= _selectorStacks.Count)
///     throw new InvalidOperationException($"Couldn't get selector stack for player {player.NetId}!");
/// </code>
/// **影子玩家不在 `_playerCollection.Players` 里** ⇒ `PushSelector` / `GetSelector` 这两条都会抛。
/// 所以我们的 <see cref="BossCardSelector"/> **一次都没挂上过**，所有要选牌的牌都走「真 UI」分支 ⇒
/// 影子玩家没有 UI ⇒ 牌的 `OnPlay` 永远 await 在那里 ⇒ **卡面停在半空**，整条剧本连锁停摆
/// （用户先后报过：隐秘匕首、铭记死亡、夜魇、夜宴、手上技法、洁净 —— 全是这一个原因）。
///
/// 之前两版都在「逐个方法」上打补丁（先 `FromHandForDiscard`，再 `FromHand`），
/// 但 `CardSelectCmd` 一共有 **17 个**选牌入口（手牌 / 抽牌堆 / 牌库 / 三选一网格 / 三选一卡面…），
/// 漏一个就漏一种牌（「洁净」走的正是 `FromCombatPile`，从抽牌堆里选）。
///
/// ## 修法：只在 `GetSelector` 这一层接管
///
/// `CardSelectCmd` 的每一个入口开头都是同一句：
/// <code>
/// ICardSelector selector = RunManager.Instance.CardSelectorStack.GetSelector(player);
/// if (selector != null) { await selector.GetSelectedCards(...); } else { …真 UI / 软锁兜底… }
/// </code>
/// ⇒ 只要 `GetSelector` 对我们的影子玩家**不返回 null**，全部入口一次性都好了，且不必关心
/// 将来新增的入口。
///
/// 返回的是 <see cref="BossCardSelector.Instance"/>（原来自动选择器的那套逻辑：
/// 剧本点名的弃牌清单、选择队列 `PickOrder`、必备工具、奇巧优先、价值兜底 —— 一个字都没改）。
///
/// **例外**：「抉择储」的「指导」要把候选牌**弹给真人玩家选**，
/// 那一次由 <c>ChoiceRegentPickPatch</c>（`FromHand` 的前缀）接管，它先摘挂起标记
/// （<see cref="SelectionCompat.SuspendForUi"/>）、再走真界面 —— 这里看到挂起标记就返回 true 放行。
///
/// 真人玩家、原版怪物、别的 mod 一律 `return true` 走原版（零回归）。
/// </summary>
[HarmonyPatch(typeof(CardSelectorStack), nameof(CardSelectorStack.GetSelector),
    new[] { typeof(Player) })]
internal static class BossSelectorStackPatch
{
    [HarmonyPrefix]
    public static bool Prefix(Player player, ref ICardSelector? __result)
    {
        try
        {
            // 只接管我们的 Boss 的影子玩家
            if (player?.Creature?.Monster is not CharBossMonster) return true;

            // 「指导」那一刻要真界面 ⇒ 放行原版（它会因为栈里没东西而返回 null，再走本地 UI）
            if (SelectionCompat.IsSuspended(player)) return true;

            __result = BossCardSelector.Instance;
            return false;
        }
        catch (Exception e)
        {
            try { Logger.Log("[CharBoss] 选牌器接管失败（按原版执行）: " + e.Message); } catch { /* 忽略 */ }
            return true;
        }
    }
}
#endif
