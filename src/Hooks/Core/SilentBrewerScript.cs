using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.Potions;
using CharacterBosses.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// 三层「**炼药猎**」（静默猎手·药水轴）—— 群友投稿、用户 2026-10-25 让先做出来实验。
/// 完整设计见 <c>docs/plans/boss-silent-brewer.md</c>。
///
/// 特殊效果「**调制**」：初始药水栏位没被移除（**8 个**），战斗中会用药水。
/// 回血（<see cref="BossPotions.DrinkAsync"/> → <c>HealPerPotion</c>）用户 2026-10-26 砍成 **0**，
/// 代码保留，填 &gt;0 就能一键恢复。
///
/// 每回合 **2 费**（用户口径：上限 2，但「明耀酊剂」会续杯所以常常其实是 3 费）。
/// 剧本每回合都是「**先喝药水、再出牌**」，所以药水挂在 <see cref="BossTurn.Potions"/>。
///
/// ⚠ 用户 2026-10-27：和其它 Boss 一样，她也带「涅奥的祝福」（**第 2 件遗物**）——
///   血量 70 + 480 = 550、费用上限 3 − 1 = 2、手牌 6 − 2 = 4 都是这套机制给的。
/// </summary>
public static class SilentBrewerScript
{
    /// <summary>
    /// 每回合**计划**能量 = **3**（用户 2026-10-25 订正）。
    ///
    /// 她的费用**上限是 2**（定义值 3 − 涅奥祝福 1），但「明耀酊剂」是**无限续杯**的：
    /// 喝下之后每回合开始都额外 +1，于是每回合实际可花的钱一直是 3。
    /// 所以这里直接把每回合的预算写成 3，比去预测 `RadiancePower` 更稳（它已经计入这笔加费，
    /// 原版那条 `AfterEnergyReset` 对我们也是压掉的，见 `BossRadianceSuppressPatch`）。
    /// </summary>
    private const int TurnEnergy = 3;

    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            // 1
            new()
            {
                Note = "1. 明晰提取物、明耀酊剂、敏捷药水｜扫腿、生存者（丢腐朽）、尖啸、精确切击、防御（没费）",
                Energy = TurnEnergy + 1,   // 3 + 灯笼（第 1 回合 +1）
                Potions = Potions<Clarity, RadiantTincture, DexterityPotion>(),
                Cards = Cards(
                    C<LegSweep>(), C<Survivor>(), Discarded<Decay>(),
                    C<PiercingWail>(), A<PreciseCut>(9), C<DefendSilent>()),
            },
            // 2
            new()
            {
                // 用户 2026-10-26：把第 3 回合的「爆炸药水」提前到第 2 回合扔（其它都不变）
                // 用户 2026-10-26 追加：第 2 回合的「尖啸」换成「中和」
                // 用户 2026-10-26 追加：**瓶中船改到「炼制药水之后」喝** —— 这瓶是她这回合现炼出来的，
                //   回合开始时栏位里根本没有它（日志里就有「剧本要喝 ShipInABottle，但栏位里没有 → 跳过」）。
                Note = "2. 镣铐药水、爆炸药水｜夜魇（复制炼药）、炼制药水+（灵魂之力）→ 喝瓶中船、中和、防御（没费）",
                Energy = TurnEnergy,
                Potions = Potions<ShacklingPotion, ExplosiveAmpoule>(),
                Cards = Cards(
                    C<Nightmare>(true), Brew(true, typeof(ShipInABottle)),
                    Drink(typeof(ShipInABottle), A<Neutralize>(8)), C<DefendSilent>()),
            },
            // 3
            new()
            {
                // 用户 2026-10-26：中和移到第 2 回合了，这里去掉
                Note = "3. 瓶中船、罐装幽灵｜炼制药水+×3（格挡/复制/异鱼之油）、灵动步法、爆发、残影",
                Energy = TurnEnergy,
                Potions = Potions<ShipInABottle, GhostInAJar>(),
                Cards = Cards(
                    C<SerpentForm>(),
                    Brew(true, typeof(BlockPotion)),
                    Brew(true, typeof(Duplicator)),
                    Brew(true, typeof(FyshOil)),
                    C<Footwork>(), C<Burst>(), C<Blur>()),
            },
            // 4
            new()
            {
                Note = "4. 格挡药水、异鱼之油、混沌药水｜预判、复制药水、坚韧之环×2、拳斗、偏折",
                Energy = TurnEnergy,
                Potions = Potions<BlockPotion, FyshOil, EntropicBrew, CunningPotion>(),   // 最后这瓶只有二阶段（精灵已消耗）才存在
                Cards = Cards(
                    C<Anticipate>(), Drink(typeof(Duplicator), C<ToricToughness>()),
                    A<Fisticuffs>(9), C<Deflect>()),
            },
            // 5
            new()
            {
                Note = "5. 洗牌。明晰提取物、明耀酊剂、敏捷药水｜炼制药水+（炼格挡药水）、扫腿、精确切击、生存者",
                Energy = TurnEnergy,
                Potions = Potions<Clarity, RadiantTincture, DexterityPotion>(),
                Cards = Cards(
                    Brew(true, typeof(BlockPotion)), C<LegSweep>(), A<PreciseCut>(9), C<Survivor>()),
            },
            // 6
            new()
            {
                Note = "6. 镣铐药水｜炼制药水+（瓶中船）、炼制药水+（异鱼之油）、防御、腐朽",
                Energy = TurnEnergy,
                Potions = Potions<ShacklingPotion>(),
                Cards = Cards(
                    Brew(true, typeof(ShipInABottle)), Brew(true, typeof(FyshOil)),
                    C<DefendSilent>(), C<Decay>()),
            },
            // 7
            new()
            {
                Note = "7. 瓶中船、罐装幽灵、爆炸药水｜中和、炼制药水+（复制药水）、爆发、残影",
                Energy = TurnEnergy,
                Potions = Potions<ShipInABottle, GhostInAJar, ExplosiveAmpoule>(),
                Cards = Cards(
                    A<Neutralize>(8), Brew(true, typeof(Duplicator)), C<Burst>(), C<Blur>()),
            },
            // 8
            new()
            {
                Note = "8. 格挡药水、异鱼之油、混沌药水、（二阶段：混沌补出的狡诈药水 → 3 张小刀当回合打出）｜预判、复制药水、坚韧之环×2、拳斗、偏折",
                Energy = TurnEnergy,
                Potions = Potions<BlockPotion, FyshOil, EntropicBrew, CunningPotion>(),   // 最后这瓶只有二阶段（精灵已消耗）才存在
                Cards = Cards(
                    C<Anticipate>(), Drink(typeof(Duplicator), C<ToricToughness>()),
                    A<Fisticuffs>(9), C<Deflect>()),
            },
        };

        return new BossScript
        {
            Turns = turns,
            LoopFrom = 5,     // 5～8 循环
            LoopLength = 4,
        };
    }

    /// <summary>卡组（剧本实际造牌；`炼制药水`是无色牌，其余是静默猎手的牌 + 两张状态牌）。</summary>
    public static List<Type> Deck() => new()
    {
        typeof(LegSweep), typeof(Survivor), typeof(SerpentForm), typeof(PiercingWail),
        typeof(PreciseCut), typeof(Nightmare), typeof(Alchemize), typeof(Neutralize),
        typeof(Burst), typeof(Blur), typeof(Anticipate), typeof(ToricToughness),
        typeof(Fisticuffs), typeof(Deflect),
        typeof(DefendSilent), typeof(DefendSilent),
        typeof(Decay), typeof(Decay),
    };

    /// <summary>
    /// 遗物（用户清单：白兽雕像按游戏里的正式名）。
    /// 顺序和其它 Boss 一致：**第 1 件是自己的起始遗物（蛇之戒指）、第 2 件固定是「涅奥的祝福」**，
    /// 其余按用户清单往后排（`ShadowPlayerFactory` 发现已在列表里就不再加一次）。
    /// </summary>
    public static List<Type> Relics() => new()
    {
        typeof(RingOfTheSnake),      // 蛇之戒指（起始）
        typeof(NeowBlessing),        // 涅奥的祝福（所有 Boss 都是第 2 件）
        typeof(ReptileTrinket),      // 爬行动物饰品
        typeof(AlchemicalCoffer),    // 炼金箱
        typeof(PotionBelt),          // 药水腰带
        typeof(WhiteBeastStatue),    // 白兽雕像（设计稿写的「白象雕像」）
        typeof(DelicateFrond),       // 娇嫩蕨草
        typeof(PaperKrane),          // 纸鹤
        typeof(Lantern),             // 灯笼：第 1 回合 +1 能量
        typeof(Chandelier),          // 吊灯：第 3 回合开始 +3 能量（用户 2026-10-26 追加）
    };

    /// <summary>初始药水（8 瓶，正好铺满 8 个栏位）。涅奥的祝福由 <c>CarriesNeowBlessing</c> 自动带。</summary>
    public static List<Type> InitialPotions() => new()
    {
        typeof(EntropicBrew),    // 混沌药水
        typeof(Clarity),         // 明晰提取物
        typeof(RadiantTincture), // 明耀酊剂
        typeof(DexterityPotion),       // 敏捷药水
        typeof(ShacklingPotion),       // 镣铐药水
        typeof(ExplosiveAmpoule), // 爆炸药水（游戏里叫「爆炸安瓿」；用户 2026-10-27 订正：不是火焰药水）
        typeof(GhostInAJar),     // 罐装幽灵
        typeof(FairyInABottle),  // 瓶中精灵
    };

    // ── 小工具 ──────────────────────────────────────────────
    private static BossTurn T(string note, params ScriptCard[] cards) => new()
    {
        Note = note, Energy = TurnEnergy, Cards = new List<ScriptCard>(cards),
    };

    private static List<ScriptCard> Cards(params ScriptCard[] cards) => new(cards);

    private static ScriptCard C<T>(bool upgraded = false) where T : CardModel
        => new() { Card = typeof(T), Upgraded = upgraded, From = "deck" };

    /// <summary>进阶 <paramref name="minAscension"/> 起这张牌是**升级版**（用户 2026-10-26 给炼药猎配的进阶加成）。</summary>
    private static ScriptCard A<T>(int minAscension) where T : CardModel
        => new() { Card = typeof(T), From = "deck", UpgradeMinAscension = minAscension };

    /// <summary>
    /// 这张牌**不主动打**，而是被上一张牌（生存者「弃 1 张」）丢掉 —— 丢掉之后靠它自己的
    /// 「奇巧」（Sly）**自动打出**。用户 2026-10-25：第 1 回合「生存者应该丢弃磨蚀让它自动打出」。
    /// </summary>
    private static ScriptCard Discarded<T>(bool upgraded = false) where T : CardModel => new()
    {
        Card = typeof(T), Upgraded = upgraded, From = "deck", DiscardInstead = true,
    };
    /// <summary>打这张牌之前先喝一瓶药水（复制药水夹在牌中间）。</summary>
    private static ScriptCard Drink(Type potion, ScriptCard card) => new()
    {
        Card = card.Card, Upgraded = card.Upgraded, From = card.From,
        EnchantSouls = card.EnchantSouls, BrewPotion = card.BrewPotion,
        DrinkBefore = potion,
    };

    /// <summary>炼制药水（可附魔灵魂之力、可指定炼哪一瓶）。</summary>
    private static ScriptCard Brew(bool souls = false, Type? brews = null) => new()
    {
        Card = typeof(Alchemize), Upgraded = true, From = "deck",
        EnchantSouls = souls, BrewPotion = brews,
    };

    private static List<ScriptPotion> Potions<T1>() => new()
    {
        new ScriptPotion { Potion = typeof(T1) },
    };

    private static List<ScriptPotion> Potions<T1, T2>() => new()
    {
        new ScriptPotion { Potion = typeof(T1) },
        new ScriptPotion { Potion = typeof(T2) },
    };

    private static List<ScriptPotion> Potions<T1, T2, T3>() => new()
    {
        new ScriptPotion { Potion = typeof(T1) },
        new ScriptPotion { Potion = typeof(T2) },
        new ScriptPotion { Potion = typeof(T3) },
    };
    /// <summary>
    /// 4 瓶版（v0.2.377 加）：第 8 回合用它把「**狡诈药水**」排在**混沌药水之后**。
    ///
    /// 为什么不需要「条件回合」机制：`DrinkPlannedPotionsAsync` 是**按类型在栏位里找**的，
    /// 找不到就只打一行「栏位里没有 → 跳过」继续（见 `CharBossMonster.DrinkPlannedPotionsAsync`）。
    /// 所以：
    ///   · **一阶段**（她还带着瓶中精灵）：混沌药水补回来的是瓶中精灵 ⇒ 栏位里没有狡诈 ⇒ 这一口自动跳过；
    ///   · **二阶段**（瓶中精灵已被消耗）：`BossPotions.DrinkAsync` 在补混沌药水时会把那一格换成
    ///     `CunningPotion` ⇒ 她当场把狡诈喝掉 ⇒ 狡诈药水本身给 3 把小刀，
    ///     再由 `SilentBlades.FrontloadRealShivs` 摆上牌桌当回合打出去。
    /// 顺序必须放在混沌药水**之后**，否则那一刻狡诈药水还不存在。
    /// </summary>
    private static List<ScriptPotion> Potions<T1, T2, T3, T4>() => new()
    {
        new ScriptPotion { Potion = typeof(T1) },
        new ScriptPotion { Potion = typeof(T2) },
        new ScriptPotion { Potion = typeof(T3) },
        new ScriptPotion { Potion = typeof(T4) },
    };

}
