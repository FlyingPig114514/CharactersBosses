#if STS2_111   // 观者 Boss 只在 0.113 构建里（用户：107 不加这个 Boss）
using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Monsters;

/// <summary>
/// 观者（神格观）专用的几处小逻辑，单独一个 partial 文件免得把主文件撑大。
///
/// 目前只有一条：**她打出「聚能成刃」时，把生成的那张「灭除之刃」亮给玩家看**
/// （用户 2026-11-15：「用事件机的羽化一样的方法展示」「要保证段数正确」）。
///
/// 段数为什么天然正确：灭除之刃是观者模组在 `WatcherConjureBlade.OnPlay` 里
/// 按当时的 X **现场造出来的实体**（`WithHitCount(X)` 写在牌上），我们只是把这张实体拿去展示，
/// 不做任何重新计算。
/// </summary>
public abstract partial class CharBossMonster
{
    /// <summary>刚才打出去的是不是「聚能成刃」（`WATCHER_CONJURE_BLADE`）。</summary>
    private bool IsConjureBlade(CardModel? card)
    {
        try
        {
            if (card == null) return false;
            string e = card.Id.Entry ?? string.Empty;
            return e == "WATCHER_CONJURE_BLADE" || e == "WATCHER-CONJURE_BLADE";   // A / B 两套 id
        }
        catch { return false; }
    }

    /// <summary>她手里现存的「灭除之刃」（`WATCHER_EXPUNGER`）；没有就返回空表。</summary>
    private List<CardModel> GeneratedExpungers()
    {
        var list = new List<CardModel>();
        try
        {
            var pcs = Shadow?.PlayerCombatState;
            var hand = pcs?.Hand;
            if (hand == null) return list;
            foreach (var c in hand.Cards)
            {
                if (c == null) continue;
                string e = c.Id.Entry ?? string.Empty;
                if (e == "WATCHER_EXPUNGER" || e == "WATCHER-EXPUNGER") list.Add(c);   // A / B 两套 id
            }
        }
        catch { /* 纯展示 */ }
        return list;
    }

    /// <summary>内部用同一个 helper 给插入点调用：打的是聚能成刃 ⇒ 有东西可展示。</summary>
    internal bool SurpriseWatcherConjureShown(CardModel? card) => IsConjureBlade(card) && GeneratedExpungers().Count > 0;

    /// <summary>要展示的牌（就是上面那批实体）。</summary>
    internal List<CardModel> WatcherGeneratedCards() => GeneratedExpungers();
}
#endif
