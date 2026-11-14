using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Core;

/// <summary>
/// 三层储君 3 号「**抉择储**」的选择记账（用户 2026-11-10 设计）。
///
/// 特殊效果「指导」：她打「抉择，抉择」时，**玩家**替他挑那张要被重复打出 3 次的牌。
/// 挑牌本身是原版 <c>CardSelectCmd.FromHand</c> 发起的（「抉择，抉择」的 `OnPlay` 里），
/// 我们只做两件事：
///   ① <c>Patches/ChoiceRegentPickPatch</c> 把这次选牌从「自动选择器」换成**给玩家看的**
///      选择界面（知识恶魔那套 <c>FromChooseACardScreen</c>），并把玩家挑的那张记在这里；
///   ② 剧本里那些「只有挑了某张才打得出」的牌（<see cref="ScriptCard.OnlyIfChosenCard"/>，
///      第 1 回合的天际钻头）在规划期一律跳过，等选择落地后由
///      <c>CharBossMonster.RunChoiceFollowUpsAsync</c> 按剧本补打。
///
/// 记账按 (Boss, 回合号) 存 —— 和 <see cref="BossCardSelector"/> 的点名单同一套理由：
/// 规划器会在**她回合执行期间**顺手算下一回合，用单个静态字段会被下一次规划覆盖掉。
/// </summary>
internal static class ChoiceRegent
{
    /// <summary>这套 Boss 的设计 Key（阵容 / 遭遇 / 控制台都用它）。</summary>
    internal const string Key = "REGENT_CHOICE";

    /// <summary>她身上有没有「指导」（= 是不是「抉择储」）。</summary>
    internal static bool IsChoiceBoss(Monsters.CharBossMonster? boss)
    {
        try
        {
            if (boss == null) return false;
            if (string.Equals(boss.SpawnVariantKey, Key, StringComparison.Ordinal)) return true;
            return boss.Creature?.GetPower<Powers.BossGuidePower>() != null;
        }
        catch { return false; }
    }

    /// <summary>(Boss, 回合号) → 玩家在「抉择，抉择」里挑中的那张牌的类型（按挑选顺序）。</summary>
    private static readonly Dictionary<(Monsters.CharBossMonster Boss, int Turn), List<Type>> Picks = new();

    /// <summary>
    /// (Boss, 回合号) → 恐惧模式那次「再打一次抉择，抉择」有没有做过。
    ///
    /// 恐惧模式（2026-11-13 用户口径）：储君打出「抉择，抉择」时由玩家挑 **2 张** ——
    /// 5 张里选 1 → 他打出 3 次 → 再打一次「抉择，抉择」（**拦掉它的抽牌**）→ 剩下 4 张里选 1 →
    /// 他再打出 3 次 → 继续后面的牌。这张表就是那个「同一回合只补一次」的闸门。
    /// </summary>
    private static readonly HashSet<(Monsters.CharBossMonster Boss, int Turn)> DreadSecondPlayed = new();

    /// <summary>记住这一次选择（由选牌补丁调用）。按顺序累积 ⇒ 恐惧模式一回合两条。</summary>
    internal static void NotePick(Monsters.CharBossMonster? boss, int turn, Type? cardType, bool second = false)
    {
        try
        {
            if (boss == null || cardType == null) return;
            if (Picks.Count > 256) { Picks.Clear(); DreadSecondPlayed.Clear(); }

            var key = (boss, turn);
            if (!Picks.TryGetValue(key, out var list) || list == null)
            {
                list = new List<Type>();
                Picks[key] = list;
            }
            list.Add(cardType);
            Logger.Log($"[CharBoss] 指导：玩家替她挑了 {SafeName(cardType)}" +
                       $"（本回合第 {list.Count} 张{(second ? "、恐惧模式的第二挑" : string.Empty)}）" +
                       $" ⇒ 这张会被「抉择，抉择」打出 3 次（第 {turn} 回合）");
        }
        catch { /* 忽略 */ }
    }

    /// <summary>这一回合玩家**第一次**挑的是哪张（没挑过 = null）。</summary>
    internal static Type? PickFor(Monsters.CharBossMonster? boss, int turn)
    {
        try
        {
            if (boss == null) return null;
            return Picks.TryGetValue((boss, turn), out var list) && list != null && list.Count > 0 ? list[0] : null;
        }
        catch { return null; }
    }

    /// <summary>这一回合玩家挑过的**全部**卡（恐惧模式一回合两张；没挑过 = 空表）。</summary>
    internal static List<Type> AllPicksFor(Monsters.CharBossMonster? boss, int turn)
    {
        try
        {
            if (boss == null) return new List<Type>();
            return Picks.TryGetValue((boss, turn), out var list) && list != null
                ? new List<Type>(list)
                : new List<Type>();
        }
        catch { return new List<Type>(); }
    }

    /// <summary>
    /// 恐惧模式：这一回合的「再打一次抉择，抉择」现在能不能开始（同一回合只放行一次）。
    /// </summary>
    internal static bool TryBeginDreadSecondPlay(Monsters.CharBossMonster? boss, int turn)
    {
        try
        {
            if (boss == null) return false;
            if (DreadSecondPlayed.Count > 256) DreadSecondPlayed.Clear();
            return DreadSecondPlayed.Add((boss, turn));
        }
        catch { return false; }
    }

    /// <summary>清掉这一回合的记账（回合重排 / 重打时用）。</summary>
    internal static void ClearFor(Monsters.CharBossMonster? boss, int turn)
    {
        try
        {
            if (boss == null) return;
            Picks.Remove((boss, turn));
            DreadSecondPlayed.Remove((boss, turn));
        }
        catch { /* 忽略 */ }
    }

    /// <summary>
    /// 剧本里这一条现在能不能打（<see cref="ScriptCard.OnlyIfChosenCard"/> 为空 = 永远能打）。
    /// 规划期玩家的选择还不知道 ⇒ 返回 false（那张牌压暗、没有意图，留在手里）。
    /// 恐惧模式一回合会挑两张 ⇒ 命中**任意一张**都算（例如恐惧模式下第二挑选了星位序列）。
    /// </summary>
    internal static bool EntryAllowed(Monsters.CharBossMonster? boss, int turn, ScriptCard? entry)
    {
        try
        {
            if (entry?.OnlyIfChosenCard == null) return true;
            foreach (var pick in AllPicksFor(boss, turn))
            {
                if (pick == entry.OnlyIfChosenCard) return true;
            }
            return false;
        }
        catch { return true; }
    }

    /// <summary>这一回合的剧本里有没有「等抉择结果」的牌（有才需要在选择落地后补打一次）。</summary>
    internal static bool TurnHasChoiceGatedCard(Monsters.CharBossMonster? boss, int turn)
    {
        try
        {
            var scriptTurn = boss?.Def?.ScriptForAct(boss.ActIndex)?.TurnAt(turn);
            if (scriptTurn?.Cards == null) return false;
            foreach (var entry in scriptTurn.Cards)
            {
                if (entry?.OnlyIfChosenCard != null) return true;
            }
        }
        catch { /* 忽略 */ }
        return false;
    }

    private static string SafeName(Type? t)
    {
        try
        {
            if (t == null) return "?";
            var id = ModelDb.GetId(t);
            return id?.Entry ?? t.Name;
        }
        catch { return t?.Name ?? "?"; }
    }
}
