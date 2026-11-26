using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace CharacterBosses.Patches;

/// <summary>
/// **诊断：谁在把同一张牌重复塞进手牌**（用户 2026-11-26：「你试试加诊断补丁」）。
///
/// 背景（`docs/plans/diag-hand-duplicate-hang.md`）：玩家用观者 mod（0.10.20，按 0.113 编的）
/// 在第三幕骑士精英战里卡死两次，Windows 记的是 `AppHangB1`；两次死前最后一条引擎日志都是
/// 原版那句
/// <code>
/// [ERROR] Tried to add duplicate card CARD.WATCHER_MIRACLE (59626191) to hand!
///         Holder already exists which has the same node. Ignoring.
///   at NPlayerHand.Add_Patch2(...) ← CardPileCmd.&lt;GetTweenForCardsChangingPiles&gt;b__4()
/// </code>
/// 原版 `NPlayerHand.Add` 的重复判定是这样的（反编译 `.refs/sts2_113.dll`）：
/// <code>
/// foreach (var holder in Holders.Concat(_holdersAwaitingQueue)) {
///     if (holder.CardNode == card)              { Log.Error("…same node…");        return holder; }
///     if (holder.CardNode?.Model == card.Model) { Log.Error("…same backing model…"); card.QueueFreeSafely(); return holder; }
/// }
/// </code>
/// 即「第二次插入被吞掉、返回旧 holder」，那条把牌挪进手牌的补间就不会再推进。
///
/// 本补丁**按同一套判据在 Prefix 里先预判**：只有这次真的会命中重复分支时才打印，
/// 所以正常抽牌一张日志都不会多。命中时把**完整调用栈**写进 `mod_log.txt`，
/// 同时留在 <see cref="LastRecord"/> 里给 <see cref="Core.StallWatchdog"/> 的停摆报告引用。
///
/// ⚠ 纯诊断：不修改任何返回值、不拦原版行为，最多打印 30 条。
/// </summary>
[HarmonyPatch(typeof(NPlayerHand), nameof(NPlayerHand.Add))]
internal static class HandDuplicateDiagPatch
{
    /// <summary>最多打印多少条重复插入的调用栈（之后仍然更新 <see cref="LastRecord"/>，不再刷日志）。</summary>
    private const int MaxLogs = 30;

    private static int _logged;

    /// <summary>最近一次重复插入的完整记录（含调用栈），给停摆报告用。</summary>
    internal static string LastRecord = string.Empty;

    [HarmonyPrefix]
    private static void Prefix(NPlayerHand? __instance, NCard? card)
    {
        try
        {
            if (__instance == null || card == null) return;

            int holders = 0, awaiting = 0;
            string why = MatchDuplicate(__instance, card, ref holders, ref awaiting);
            if (why.Length == 0) return;      // 不是重复插入 ⇒ 正常路径，一个字都不打

            string name = SafeName(card);
            string stack = string.Empty;
            try { stack = new StackTrace(1, true).ToString(); } catch { /* 忽略 */ }

            string record = $"[{DateTime.Now:HH:mm:ss}] 手牌重复插入：{name}（{why}）"
                          + $"｜现有 holder={holders} 待入场={awaiting}"
                          + Environment.NewLine + stack;

            LastRecord = record;
            Core.StallWatchdog.LastHandDuplicate = record;
            Core.StallWatchdog.Mark($"手牌重复插入：{name}（{why}）holder={holders} 待入场={awaiting}");

            if (_logged >= MaxLogs) return;
            _logged++;
            Logger.Log("[CharBoss][诊断] " + record);
            if (_logged == MaxLogs)
            {
                Logger.Log("[CharBoss][诊断] 手牌重复插入的调用栈已记录 " + MaxLogs +
                           " 条，后续同类不再打印（仍会进停摆报告 characterbosses_stall.txt）");
            }
        }
        catch { /* 诊断永远不许影响游戏 */ }
    }

    /// <summary>
    /// 复刻原版的重复判据（同节点 / 同牌模型），命中时返回一句人话说明，否则返回空串。
    /// 顺带数出 `Holders` 与 `_holdersAwaitingQueue` 的条数，方便判断手牌是不是处于半截状态。
    /// ⚠ 这两个成员在原版里是 **internal/private**（`Holders` 属性也不是 public）⇒ 全部走反射只读。
    /// </summary>
    private static string MatchDuplicate(NPlayerHand hand, NCard card, ref int holders, ref int awaiting)
    {
        string why = Scan(Reflected.Holders(hand), card, ref holders, "手牌");
        if (why.Length > 0) return why;
        return Scan(Reflected.Awaiting(hand), card, ref awaiting, "待入场");
    }

    /// <summary>扫一串 holder，命中就返回说明（同时把扫到的条数累加到 <paramref name="count"/>）。</summary>
    private static string Scan(System.Collections.IEnumerable? seq, NCard card, ref int count, string where)
    {
        if (seq == null) return string.Empty;
        try
        {
            foreach (var item in seq)
            {
                count++;
                if (item is not NHandCardHolder h) continue;
                var node = h.CardNode;
                if (ReferenceEquals(node, card)) return $"同一个节点（same node，{where}）";
                if (node != null && card.Model != null && ReferenceEquals(node.Model, card.Model))
                    return $"同一张牌、换了个新节点（same backing model，{where}）";
            }
        }
        catch { /* 忽略 */ }
        return string.Empty;
    }

    /// <summary>`NPlayerHand.Holders` / `_holdersAwaitingQueue` 的只读反射访问器（探一次就缓存）。</summary>
    private static class Reflected
    {
        private static bool _probed;
        private static PropertyInfo? _holdersProp;
        private static FieldInfo? _awaitingField;

        private static void Probe()
        {
            if (_probed) return;
            _probed = true;
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try { _holdersProp = typeof(NPlayerHand).GetProperty("Holders", Flags); } catch { _holdersProp = null; }
            try { _awaitingField = typeof(NPlayerHand).GetField("_holdersAwaitingQueue", Flags); } catch { _awaitingField = null; }
        }

        public static System.Collections.IEnumerable? Holders(NPlayerHand hand)
        {
            Probe();
            try { return _holdersProp?.GetValue(hand) as System.Collections.IEnumerable; } catch { return null; }
        }

        public static System.Collections.IEnumerable? Awaiting(NPlayerHand hand)
        {
            Probe();
            try { return _awaitingField?.GetValue(hand) as System.Collections.IEnumerable; } catch { return null; }
        }
    }

    private static string SafeName(NCard card)
    {
        try
        {
            var model = card.Model;
            if (model != null) return model.ToString() ?? "?";
        }
        catch { /* 忽略 */ }
        try { return "节点#" + card.GetInstanceId(); } catch { return "?"; }
    }
}
