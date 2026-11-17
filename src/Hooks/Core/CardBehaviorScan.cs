using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Core;

/// <summary>
/// **自动识别一张牌的「OnPlay 里干了什么」**（用户 2026-11-17）：
///
/// > 「把捞牌（将卡牌从弃牌堆返回手牌）、烧牌（消耗别的牌）、返顶（将卡牌从手牌或弃牌堆放回抽牌堆顶部）
/// >   也算到强化。牌太多了，我感觉我统计起来很麻烦，你看看能不能自动识别？」
///
/// 为什么不靠牌面变量：`DynamicVars` 只写得出伤害/格挡/层数，**「抽 2 张」「把弃牌堆里的一张捞回来」
/// 「消耗一张牌」这些都写在 `OnPlay` 的方法体里**，牌面上根本没有可读数据
/// ⇒ 以前只能一张一张往点名表里加（`BuffVarCards` / `ForceBuffCards` / 手写的 if），越加越漏。
///
/// 做法：把这张牌类型上**所有方法**（含编译器生成的 `<OnPlay>d__N.MoveNext` 状态机）的 IL 读出来，
/// 解出每一条 `call/callvirt/newobj` 的目标方法，再按「方法所属类型 + 方法名」归类：
///
/// | 归类 | 判据（反编译核对过的公开 API） |
/// |---|---|
/// | 抽牌 | `CardPileCmd.Draw` / `DrawInternal` / `DrawWithoutBlockingOnOtherPlayers` |
/// | 印牌 | `CardPileCmd.AddGeneratedCardToCombat`、`CombatState.CreateCard` / `CardFactory.CreateCard` |
/// | 捞牌 | `CardPileCmd.Add(..., PileType.Hand, ...)`（`PileType` 枚举：None 0 / Draw 1 / **Hand 2** / Discard 3 / Exhaust 4） |
/// | 返顶 | `CardPileCmd.MoveToTop*` / `MoveToBottom*` / `Shuffle*`、或 `CardPileCmd.Add(..., PileType.Draw, ...)` |
/// | 烧牌 | `CardCmd.Exhaust*` / `CardPileCmd.Exhaust*` |
/// | 变牌 | `CardCmd.Transform*` / `Transmute*` |
/// | 进入姿态 | 方法名里带 `Stance`，或 `EnterCalm` / `EnterWrath` / `EnterDivinity` |
///
/// 全是「对自己有利」⇒ 对应意图「强化」。`Preview*` 系列（例如 `CardCmd.PreviewCardPileAdd`）只是
/// 播个卡面动画，**不算**，否则会把一堆牌误判成强化。
///
/// ⚠ 结果按**卡牌类型**缓存（一张牌一辈子只扫一次 IL）；扫描失败就当没有（绝不影响战斗）。
/// ⚠ 本类只在「牌面数据本身判不出强化」时才会被问到（见 `BossIntentBuilder` 的 `selfBenefit`），
///   所以扫错了最坏结果也只是多/少一个蓝色上箭头，不会影响任何结算。
/// </summary>
internal static class CardBehaviorScan
{
    private static readonly Dictionary<Type, List<string>> _cache = new();

    private static readonly Dictionary<short, OpCode> OpByCode = BuildOpTable();

    private static int _logLines;

    /// <summary>这张牌 OnPlay 里会做的「对自己有利」的事（空 = 什么也没识别到）。</summary>
    internal static List<string> ReasonsOf(CardModel? card)
    {
        try
        {
            var type = card?.GetType();
            if (type == null) return Empty;
            if (_cache.TryGetValue(type, out var cached)) return cached;

            var reasons = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var m in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                ScanMethod(m, reasons, seen);
            // 编译器生成的异步状态机是**嵌套类型**，OnPlay 的真正内容在它的 MoveNext 里
            foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                foreach (var m in nested.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    ScanMethod(m, reasons, seen);
                foreach (var deeper in nested.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                {
                    foreach (var m in deeper.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                        ScanMethod(m, reasons, seen);
                }
            }

            _cache[type] = reasons;
            if (reasons.Count > 0 && _logLines++ < 80)
            {
                string entry = "?";
                try { entry = card!.Id.Entry; } catch { /* 忽略 */ }
                Logger.Log($"[CharBoss] 牌面识别（自动）：{entry} ⇒ 强化来源 [{string.Join(",", reasons)}]");
            }
            return reasons;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 牌面识别失败（当作没有强化）: " + e.Message);
            try { if (card != null) _cache[card.GetType()] = Empty; } catch { /* 忽略 */ }
            return Empty;
        }
    }

    /// <summary>这张牌会不会做「对自己有利」的事（抽牌 / 印牌 / 捞牌 / 返顶 / 烧牌 / 变牌 / 进姿态）。</summary>
    internal static bool IsSelfBeneficial(CardModel? card) => ReasonsOf(card).Count > 0;

    private static readonly List<string> Empty = new();

    private static void Add(List<string> reasons, HashSet<string> seen, string reason)
    {
        if (seen.Add(reason)) reasons.Add(reason);
    }

    // ── IL 扫描 ────────────────────────────────────────────────────────────
    private static void ScanMethod(MethodInfo m, List<string> reasons, HashSet<string> seen)
    {
        try
        {
            if (m == null) return;
            if (m.IsAbstract || m.GetMethodBody() == null) return;
        }
        catch { return; }

        byte[]? il;
        Module? module;
        Type?[]? typeArgs = null;
        Type?[]? methodArgs = null;
        try
        {
            var body = m.GetMethodBody();
            if (body == null) return;
            il = body.GetILAsByteArray();
            module = m.Module;
            try { typeArgs = m.DeclaringType?.GetGenericArguments(); } catch { typeArgs = null; }
            try { methodArgs = m.GetGenericArguments(); } catch { methodArgs = null; }
        }
        catch { return; }
        if (il == null || module == null) return;

        var recentInts = new List<int>(4);
        int i = 0;
        while (i < il.Length)
        {
            short code;
            if (il[i] == 0xFE)
            {
                if (i + 1 >= il.Length) break;
                code = (short)(0xFE00 | il[i + 1]);
                i += 2;
            }
            else
            {
                code = il[i];
                i++;
            }
            if (!OpByCode.TryGetValue(code, out var op)) break;

            // ldc.i4.m1 / ldc.i4.0..8 的操作数类型是 InlineNone，但它们的值就是枚举常量
            if (code >= 0x15 && code <= 0x1E)
            {
                recentInts.Add(code - 0x16);
                Trim(recentInts);
                continue;
            }

            switch (op.OperandType)
            {
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineI:
                    if (i + 1 > il.Length) { i = il.Length; break; }
                    recentInts.Add((sbyte)il[i]);
                    Trim(recentInts);
                    i += 1;
                    break;
                case OperandType.InlineI:
                    if (i + 4 > il.Length) { i = il.Length; break; }
                    recentInts.Add(BitConverter.ToInt32(il, i));
                    Trim(recentInts);
                    i += 4;
                    break;
                case OperandType.InlineMethod:
                case OperandType.InlineTok:
                {
                    if (i + 4 > il.Length) { i = il.Length; break; }
                    int token = BitConverter.ToInt32(il, i);
                    i += 4;
                    if (op.OperandType == OperandType.InlineMethod)
                    {
                        MethodBase? mb = null;
                        try { mb = module.ResolveMethod(token, typeArgs, methodArgs); }
                        catch { try { mb = module.ResolveMethod(token); } catch { mb = null; } }
                        if (mb != null) Classify(mb, recentInts, reasons, seen);
                    }
                    recentInts.Clear();
                    break;
                }
                case OperandType.ShortInlineVar:
                    i += 1;
                    recentInts.Clear();
                    break;
                case OperandType.InlineVar:
                    i += 2;
                    recentInts.Clear();
                    break;
                case OperandType.ShortInlineBrTarget:
                    i += 1;
                    break;
                case OperandType.InlineBrTarget:
                    i += 4;
                    break;
                case OperandType.InlineField:
                case OperandType.InlineString:
                case OperandType.InlineSig:
                case OperandType.InlineType:
                    i += 4;
                    break;
                case OperandType.InlineSwitch:
                {
                    if (i + 4 > il.Length) { i = il.Length; break; }
                    int n = BitConverter.ToInt32(il, i);
                    i += 4 + 4 * n;
                    break;
                }
                case OperandType.ShortInlineR:
                    i += 4;
                    break;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    i += 8;
                    break;
                default:
                    i += 4;
                    break;
            }
        }
    }

    private static void Trim(List<int> recentInts)
    {
        while (recentInts.Count > 4) recentInts.RemoveAt(0);
    }

    /// <summary>把一条 `call/callvirt` 的目标方法归类。</summary>
    private static void Classify(MethodBase mb, List<int> recentInts, List<string> reasons, HashSet<string> seen)
    {
        string name;
        string typeName;
        try
        {
            name = mb.Name ?? string.Empty;
            typeName = mb.DeclaringType?.Name ?? string.Empty;
        }
        catch { return; }
        if (name.Length == 0) return;
        if (name.StartsWith("Preview", StringComparison.Ordinal)) return;   // 只播动画，不算

        // 抽牌
        if (name == "Draw" || name == "DrawInternal" || name == "DrawWithoutBlockingOnOtherPlayers")
        { Add(reasons, seen, "抽牌"); return; }

        // 印牌
        if (name == "AddGeneratedCardToCombat" || name.StartsWith("CreateCard", StringComparison.Ordinal))
        { Add(reasons, seen, "印牌"); return; }

        // 烧牌
        if (name == "Exhaust" || name == "ExhaustInternal" || name.StartsWith("Exhaust", StringComparison.Ordinal))
        { Add(reasons, seen, "烧牌"); return; }

        // 变牌
        if (name.StartsWith("Transform", StringComparison.Ordinal) || name.StartsWith("Transmute", StringComparison.Ordinal))
        { Add(reasons, seen, "变牌"); return; }

        // 返顶（抽牌堆顶 / 洗牌）
        if (name.StartsWith("MoveToTop", StringComparison.Ordinal) || name.StartsWith("MoveToBottom", StringComparison.Ordinal)
            || name == "Shuffle" || name == "ShuffleIfNecessary")
        { Add(reasons, seen, "返顶"); return; }

        // 进入姿态（观者）
        if (name.IndexOf("Stance", StringComparison.Ordinal) >= 0
            || name.StartsWith("EnterCalm", StringComparison.Ordinal)
            || name.StartsWith("EnterWrath", StringComparison.Ordinal)
            || name.StartsWith("EnterDivinity", StringComparison.Ordinal))
        { Add(reasons, seen, "进入姿态"); return; }

        // 捞牌 / 返顶：`CardPileCmd.Add(card, PileType, position, …)` —— 第二个参数是目标堆，
        // 枚举值 None 0 / Draw 1 / Hand 2 / Discard 3 / Exhaust 4（`ldc.i4.x` 会留在 recentInts 里，
        // 位置参数在它后面 ⇒ 取倒数第二个）。
        if (typeName == "CardPileCmd" && (name == "Add" || name == "MoveToPile" || name.StartsWith("MoveTo", StringComparison.Ordinal)))
        {
            int pile = recentInts.Count >= 2 ? recentInts[recentInts.Count - 2]
                     : recentInts.Count == 1 ? recentInts[0] : -1;
            if (pile == 2) { Add(reasons, seen, "捞牌"); return; }
            if (pile == 1) { Add(reasons, seen, "返顶"); return; }
        }
    }

    private static Dictionary<short, OpCode> BuildOpTable()
    {
        var table = new Dictionary<short, OpCode>(300);
        try
        {
            foreach (var f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (f.FieldType != typeof(OpCode)) continue;
                if (f.GetValue(null) is not OpCode op) continue;
                table[op.Value] = op;
            }
        }
        catch { /* 极端情况下表不全会退化成「扫不出东西」 */ }
        return table;
    }
}
