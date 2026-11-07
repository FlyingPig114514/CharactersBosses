using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// 第三幕「**根除骨**」（亡灵契约师）的剧本与卡组 —— 用户设计。
///
/// 要点：
///   · 血上限 66 基础（第三幕涅奥祝福 +480 = 拾起 120 + 每幕 180×2）＋ 布质果实 +31 ⇒ **577**。
///     ⚠ 那 +31 由遗物自己加，这里**不要**再写进 Hp。
///   · 特殊效果「预借时间」：你每花费 10 能量 → 玩家 +1 层原版预借时间、亡灵契约师 +1 能量（冰淇淋能存到下回合）。
///     累计触发、回合内可叠加。
///   · 第 2、6 回合的「根除」**即使有费也不出**，留在手里下回合照常出（<c>HoldInstead</c>）。
///   · 第 3 回合「钙化」附魔**播种**（打出时 +1 费）；根除会正常触发象牙麻将牌回费。
///   · 7 回合，之后 **5～7 循环**；循环里「能量汲取」的升级顺序：先根除+重压、再出击。
/// </summary>
public static class NecrobinderAct3Script
{
    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            T("戳击、恐惧、友谊", 1,
                C<Poke>(), C<Fear>(), C<Friendship>()),

            T("死亡之舞、吸引仇恨、根除（即使有费也不出，保留到下回合）", 2,
                C<DanseMacabre>(), C<PullAggro>(), Hold<Eradicate>()),

            T("钙化（附魔播种，打出时获得 1 费）、根除（触发象牙麻将牌回费）、违逆、洁净+（没费打不了）", 3,
                new ScriptCard { Card = typeof(Calcify), EnchantSown = true }, C<Eradicate>(), C<Defy>(),
                C<Cleanse>(true)),

            T("出击、重压、能量汲取（升级违逆和吸引仇恨）", 4,
                C<Unleash>(), C<Flatten>(), C<DrainPower>()),

            T("出击、违逆+、恐惧", 5,
                C<Unleash>(), C<Defy>(true), C<Fear>()),

            T("洁净+（烧大～抱抱塞的煤灰）、吸引仇恨+、根除（即使有费也不出，保留到下回合）", 6,
                C<Cleanse>(true), C<PullAggro>(true), Hold<Eradicate>()),

            T("戳击、根除、重压、能量汲取（升级恐惧和戳击）", 7,
                C<Poke>(), C<Eradicate>(), C<Flatten>(), C<DrainPower>()),
        };

        return new BossScript
        {
            Turns = turns,
            LoopFrom = 5,     // 5～7 回合循环
            LoopLength = 3,
            // 这里**故意留空**：第三幕的升级一律由「能量汲取」负责（用户给的完整升级顺序写在
            // CharBossMonster.DrainUpgradeOrderAct3：违逆+吸引仇恨 → 恐惧+戳击 → 根除+重压 → 出击）。
            // 之前这里按循环自动升级根除/重压，会和能量汲取的顺序打架（升级被吃掉、能量汲取什么也不显示）。
            UpgradeByCycle = new List<Type[]>(),
        };
    }

    /// <summary>第三幕「根除骨」的卡组。</summary>
    public static List<Type> Deck() => new()
    {
        typeof(Poke), typeof(Fear), typeof(Friendship),
        typeof(DanseMacabre), typeof(PullAggro), typeof(Eradicate),
        typeof(Calcify), typeof(Defy), typeof(Cleanse),
        typeof(Unleash), typeof(Flatten), typeof(DrainPower),
    };

    /// <summary>遗物（涅奥的祝福由 CarriesNeowBlessing 自动带）。</summary>
    public static List<Type> Relics() => new()
    {
        typeof(BoundPhylactery),    // 缚魂命匣：召唤奥斯提
        typeof(Relics.NeowBlessing),   // 涅奥的祝福：所有 Boss 都是第 2 件（用户 2026-10-27 口径）
        typeof(Shuriken),           // 手里剑（用户订正：把金刚杵换成它）
        typeof(BiiigHug),           // 大～抱抱（洗牌塞煤灰）
        typeof(IceCream),           // 冰淇淋：多余能量留到下回合
        typeof(LoomingFruit),       // 布质果实：+31 最大生命值（所以 Hp 定义里不要再加）
        typeof(IvoryTile),          // 象牙麻将牌
    };

    private static BossTurn T(string note, int turnIndex, params ScriptCard[] cards)
        => new() { Note = $"{turnIndex}. {note}", Cards = new List<ScriptCard>(cards) };

    private static ScriptCard C<T>(bool upgraded = false, string from = "deck")
        where T : MegaCrit.Sts2.Core.Models.CardModel
        => new() { Card = typeof(T), Upgraded = upgraded, From = from };

    /// <summary>「即使有费也不出，留在手里下回合照常出」。</summary>
    private static ScriptCard Hold<T>() where T : MegaCrit.Sts2.Core.Models.CardModel
        => new() { Card = typeof(T), HoldInstead = true };
}
