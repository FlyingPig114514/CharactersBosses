using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Core;

/// <summary>
/// 把 <see cref="BossDefinition"/> 里的「按幕 + 基础 + 兜底」三层解析成实际要用的卡/遗物类型。
///
/// 兜底层分两级：
///   ① **按该角色卡池自动配一套占位卡组**（默认走这条）—— 让还没被设计的角色也能打出一套像样的牌，
///      而不是只会甩「打击/防御」；
///   ② 卡池取不到时才退回「角色起始卡组循环复制」。
/// 用户一旦在 <see cref="BossDefinition.DeckByAct"/> / <c>Deck</c> 里写了卡组，兜底层就不再参与。
/// </summary>
public static class BossLoadout
{
    /// <summary>占位卡组的张数。</summary>
    public const int PlaceholderDeckSize = 16;

    /// <summary>
    /// 占位卡组的配比：**按「类型 × 费用」分档取**，而不是笼统地「便宜优先」——
    /// 后者在每个卡池里都能用 1 费牌填满 16 张，结果 Boss 只会刷小牌、没有大伤害（模拟时踩到过）。
    /// </summary>
    private static readonly (CardType Type, int Cost, int Count)[] PlaceholderPlan =
    {
        (CardType.Attack, 1, 4), (CardType.Attack, 2, 4), (CardType.Attack, 3, 1),
        (CardType.Skill, 1, 2), (CardType.Skill, 2, 2), (CardType.Skill, 0, 1),
        (CardType.Power, 1, 1), (CardType.Power, 2, 1),
    };

    public static List<Type> ResolveDeckTypes(BossDefinition def, int act)
    {
        var overridden = def.DeckForAct(act);
        if (overridden is { Count: > 0 }) return overridden.ToList();
        if (def.Deck.Count > 0) return def.Deck.ToList();

        var character = FindCharacter(def);
        if (character == null)
        {
            Logger.Log($"[CharBoss] 找不到角色 {def.CharacterType.Name}，卡组为空");
            return new List<Type>();
        }

        var auto = AutoDeckFromPool(character, def, act);
        if (auto.Count > 0) return auto;

        return StartingDeckFallback(character, def, act);
    }

    public static List<Type> ResolveRelicTypes(BossDefinition def, int act)
    {
        var overridden = def.RelicsForAct(act);
        if (overridden is { Count: > 0 }) return overridden.ToList();
        if (def.Relics.Count > 0) return def.Relics.ToList();

        var character = FindCharacter(def);
        if (character == null) return new List<Type>();
        return character.StartingRelics.Select(r => r.GetType()).ToList();
    }

    /// <summary>按卡池配一套「能打」的占位卡组：便宜的攻击为主 + 几张技能 + 2 张能力。</summary>
    private static List<Type> AutoDeckFromPool(CharacterModel character, BossDefinition def, int act)
    {
        List<CardModel> usable;
        try
        {
            usable = character.CardPool.AllCards.Where(IsAiFriendly).ToList();
        }
        catch (Exception e)
        {
            Logger.Log($"[CharBoss] {def.Key} act{act + 1}: 读卡池失败（{e.Message}），改用起始卡组占位");
            return new List<Type>();
        }

        var picked = new List<CardModel>();
        foreach (var (type, cost, count) in PlaceholderPlan)
        {
            picked.AddRange(TakeCost(usable, type, cost, count));
        }

        // 某档不够就按「1 费优先、0 费最后」补满
        foreach (var card in FillOrder(usable))
        {
            if (picked.Count >= PlaceholderDeckSize) break;
            if (!picked.Contains(card)) picked.Add(card);
        }

        if (picked.Count == 0) return new List<Type>();

        Logger.Log($"[CharBoss] {def.Key} act{act + 1}: 未定义卡组 → 按卡池自动配了 {picked.Count} 张占位卡组" +
                   $"（建议在 BossDefs.cs 里替换成你的设计）[{string.Join(",", picked.Select(c => c.Id.Entry))}]");
        return picked.Select(c => c.GetType()).ToList();
    }

    /// <summary>取某类型、某费用的前 N 张（按名字排序保证确定性）。</summary>
    private static IEnumerable<CardModel> TakeCost(List<CardModel> usable, CardType type, int cost, int count)
        => usable
            .Where(c => c.Type == type && CostOf(c) == cost)
            .OrderBy(c => c.Id.Entry, StringComparer.Ordinal)
            .Take(count);

    /// <summary>补满时的顺序：1 费 → 2 费 → 3 费 → 0 费（0 费多半是条件牌）。</summary>
    private static IEnumerable<CardModel> FillOrder(List<CardModel> usable)
        => usable
            .Select(c => new { Card = c, Cost = CostOf(c) })
            .OrderBy(x => x.Cost == 0 ? 9 : x.Cost)
            .ThenBy(x => x.Card.Id.Entry, StringComparer.Ordinal)
            .Select(x => x.Card);

    /// <summary>AI 能不能用这张牌（和 <see cref="BossPlanner"/> 的出牌过滤保持一致）。</summary>
    private static bool IsAiFriendly(CardModel card)
    {
        if (card.Type is not (CardType.Attack or CardType.Skill or CardType.Power)) return false;
        try
        {
            if (card.Keywords.Contains(CardKeyword.Unplayable)) return false;
            if (card.EnergyCost.CostsX) return false;
            if (card.HasStarCostX) return false;
        }
        catch
        {
            return false;
        }

        if (card.TargetType is TargetType.TargetedNoCreature or TargetType.Osty) return false;

        var cost = CostOf(card);
        return cost is >= 0 and <= 3;
    }

    private static int CostOf(CardModel card)
    {
        try { return Math.Max(0, card.EnergyCost.GetAmountToSpend()); }
        catch { return 0; }
    }

    /// <summary>最后兜底：把角色起始卡组循环复制到 <see cref="PlaceholderDeckSize"/> 张。</summary>
    private static List<Type> StartingDeckFallback(CharacterModel character, BossDefinition def, int act)
    {
        var result = new List<Type>();
        foreach (var card in character.StartingDeck) result.Add(card.GetType());
        if (result.Count == 0) return result;

        var baseDeck = result.ToList();
        while (result.Count < PlaceholderDeckSize)
        {
            foreach (var cardType in baseDeck)
            {
                if (result.Count >= PlaceholderDeckSize) break;
                result.Add(cardType);
            }
        }

        Logger.Log($"[CharBoss] {def.Key} act{act + 1}: 卡池不可用，用角色起始卡组占位 {result.Count} 张");
        return result;
    }

    private static CharacterModel? FindCharacter(BossDefinition def)
    {
        try { return ModelDb.GetById<CharacterModel>(ModelDb.GetId(def.CharacterType)); }
        catch (Exception e)
        {
            Logger.Log($"[CharBoss] 取角色 {def.CharacterType.Name} 失败: {e.Message}");
            return null;
        }
    }
}
