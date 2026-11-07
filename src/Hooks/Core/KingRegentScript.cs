using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// 二层「**王储**」（储君 · 王权轴，Key `REGENT_KING`）—— 用户 2026-11-02 设计稿。
/// 这是二层储君的**第二套设计**（第一套是「铸剑储君」`RegentScript.Build`），
/// 与「放血战 / 杂耍战」「无色储君 / 辉星储」同一套机制：
/// **以房间遭遇为准**（`REGENT_KING_BOSS_ENCOUNTER`），拿不到遭遇才按种子 50/50。
///
/// 完整设计记录（用户原话、名字→类对照、能量账、待确认项）见
/// `docs/plans/boss-king-regent.md`。
///
/// ## 特殊效果「征服者」（`Powers/BossConquerorPower.cs`）
/// 用户 2026-11-02 改稿：她每**把一张牌放回抽牌堆顶部**（光子切割 / 宇宙冷漠），
/// 所有玩家各得 1 层「**麻痹**」（`Powers/ParalysisPower`）——
/// 下一张攻击牌伤害 −50%，每多 1 层再 −50%（2 层即 −100%），打出下一张攻击牌后失效。
///
/// ## 能量（写死在剧本里）
/// 基础 3 − 涅奥的祝福 1 = **2**，本变体没有任何加费遗物：
///   1 回合 **2**（封印王座+ **0** + 王之凝视 **2** —— 用户 2026-11-02 定稿：
///     「升级封印王座，降级王之凝视（没什么实际影响）」，两费之和仍是 2）、
///   5 / 7 回合 **3**（征召上前 1 + 君王之剑 2）、9 回合 **1**、
///   其余 **2**。
///
/// ## 「王者之踢」的费用
/// 原版是「每抽到这张牌 −1 费」（4 → 3 → 2 → 1 → 0），而我们的手牌每回合都是
/// `BuildHand` **新建实例** ⇒ 抽牌减费不会发生，所以逐回合写死
/// <see cref="ScriptCard.CostThisTurn"/>：第 2 回合 3（还在手里没打）、第 3 回合 2、
/// 第 6 回合 1、第 9/10 回合 0。
///
/// ## 「返顶」（把牌放到抽牌堆顶）与拦抽牌
/// 光子切割+ / 宇宙冷漠的返顶在原版是**选牌**，Boss 出牌时走 `BossCardSelector` 自动选；
/// 不点名就会按价值乱挑 ⇒ 用 <see cref="BossTurn.PickOrder"/> 把每次选牌钉死。
/// 抽牌本身由 `BossDefinition.KeepsScriptedHandByAct[1] = true` 兜住
/// （抽完把新牌挪回堆顶，手牌恒等于剧本）—— 用户要求「拦掉抉择抉择和光子切割+的抽牌」。
/// </summary>
public static class KingRegentScript
{
    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            // 1（2 费：封印王座+ 0 + 王之凝视 2；笨拙是诅咒，打不出）
            //   ★ 用户 2026-11-02 定稿：**封印王座升级、王之凝视降级**（「没什么实际影响」）。
            //     反编译核对：`TheSealedThrone.OnUpgrade → EnergyCost.UpgradeBy(-1)` ⇒ 封印王座+ = **0 费**
            //     （效果不变，仍是「你每打出一张牌获得 1 星」）；`MonarchsGaze` 未升级 = 2 费、`StrengthLoss`
            //     仍是 1 ⇒ 两费之和依旧 2，与她的基础能量 2 完全对上。
            Turn(2, "封印王座+（0 费）、王之凝视（2 费）、笨拙（诅咒，打不出）", 1,
                C<TheSealedThrone>(true), C<MonarchsGaze>(false), C<Clumsy>()),

            // 2（2 费：光子切割+ 1 + 王者之拳 1）—— 光子切割+ 把**王者之踢**放回抽牌堆顶
            //   ⇒ 王者之踢本回合留在手里打不出（此刻它已经被抽到 1 次 = 3 费）；防御/打击没费。
            new()
            {
                Note = "2. 光子切割+（返顶王者之踢）、王者之拳、王者之踢（被返顶，3 费）、防御（没费）、打击（没费）",
                Energy = 2,
                PickOrder = new List<Type> { typeof(KinglyKick) },
                Cards = Cards(
                    C<PhotonCut>(true),
                    C<KinglyPunch>(),
                    new() { Card = typeof(KinglyKick), CostThisTurn = 3, HoldInstead = true },
                    C<DefendRegent>(), C<StrikeRegent>()),
            },

            // 3（2 费：王者之踢 2（第 2 次抽到）+ 群星斗篷 0）；打击没费。
            //   所向无敌在回合结束正好躺在抽牌堆顶 → 免费打出（`FreePlayTopEntry`）。
            new()
            {
                Note = "3. 王者之踢（2 费）、群星斗篷、打击（没费）；所向无敌在抽牌堆顶 → 回合结束免费打出",
                Energy = 2,
                FreePlayTopEntry = "I_AM_INVINCIBLE",
                Cards = Cards(
                    new() { Card = typeof(KinglyKick), CostThisTurn = 2 },
                    C<CloakOfStars>(), C<StrikeRegent>(),
                    DisplayOnlyPlayed<IAmInvincible>()),
            },

            // 4（2 费：王国资产 1 + 防御 1 + 胜券在王 0）
            Turn(2, "王国资产、防御、胜券在王", 4,
                C<Royalties>(), C<DefendRegent>(), C<RoyalGamble>()),

            // 5（3 费：征召上前 1 + 君王之剑 2）——
            //   抉择，抉择（0 费）把**宇宙冷漠**打出 3 次，三次分别返顶 王者之踢 / 王者之拳 / 所向无敌；
            //   宇宙冷漠由抉择抉择打出，**不花她的能量**，所以它只是「展示牌（会打出去）」。
            //   ★ 用户 2026-11-02 削弱：这一格原本是「何人僭越」（0 费给玩家 1 虚弱 + 1 易伤），
            //     改成诅咒「笨拙」（`Unplayable + Ethereal`：打不出、回合结束自己消耗）⇒ 这一回合
            //     少给玩家一层虚弱与易伤，同时手里多一张死牌。
            new()
            {
                Note = "5. 抉择抉择（打出宇宙冷漠 3 次：返顶王者之踢/王者之拳/所向无敌）、征召上前、星位序列、君王之剑、笨拙（诅咒，打不出）；所向无敌回合结束免费打出",
                Energy = 3,
                FreePlayTopEntry = "I_AM_INVINCIBLE",
                // 第一个是抉择抉择要选的技能牌，后三个是宇宙冷漠三次分别返顶的牌
                PickOrder = new List<Type>
                {
                    typeof(CosmicIndifference), typeof(KinglyKick), typeof(KinglyPunch), typeof(IAmInvincible),
                },
                Cards = Cards(
                    C<DecisionsDecisions>(),
                    new() { Card = typeof(CosmicIndifference), From = "display", DisplayOnly = true, AutoPlayed = true },
                    C<SummonForth>(), C<Alignment>(), C<SovereignBlade>(), C<Clumsy>(),
                    DisplayOnlyPlayed<IAmInvincible>()),
            },

            // 6（2 费：王者之拳 1 + 王者之踢 1（第 3 次抽到））；打击没费
            Turn(2, "王者之拳、王者之踢（1 费）、打击（没费）", 6,
                C<KinglyPunch>(),
                new() { Card = typeof(KinglyKick), CostThisTurn = 1 },
                C<StrikeRegent>()),

            // 7（3 费：征召上前 1 + 星位序列 0 + 君王之剑 2）—— 回合开始洗牌（牌堆见底）
            Turn(3, "洗牌。征召上前（铸造 8 + 把剑放进手牌）、星位序列、君王之剑", 7,
                C<SummonForth>(), C<Alignment>(), C<SovereignBlade>()),

            // 8（2 费：防御 + 防御）；打击没费
            Turn(2, "防御、防御、打击（没费）", 8,
                C<DefendRegent>(), C<DefendRegent>(), C<StrikeRegent>()),

            // 9（1 费：王者之拳 1 + 群星斗篷 0 + 王者之踢 0（第 4 次抽到））
            //   ★ 用户 2026-11-02：「有的回合的王者之拳仍是升级的（进阶9~10），把他们降级」
            //     —— 这一格原来写的是「王者之拳+」（剧本点名的升级版：10 伤、每抽到 +6），
            //     现在改成**未升级**（8 伤、+4），与第 2 / 6 回合口径一致。
            Turn(1, "王者之拳、群星斗篷、王者之踢（0 费）", 9,
                C<KinglyPunch>(), C<CloakOfStars>(),
                new() { Card = typeof(KinglyKick), CostThisTurn = 0 }),

            // 10（2 费：宇宙冷漠 1 + 光子切割+ 1 + 王者之踢 0）；打击×2 没费。
            //    宇宙冷漠返顶王者之踢（来自弃牌堆），光子切割+ 返顶**手里那张**所向无敌
            //    ⇒ 它正好躺在抽牌堆顶，回合结束免费打出。
            new()
            {
                Note = "10. 宇宙冷漠（返顶王者之踢）、光子切割+（返顶所向无敌）、王者之踢（0 费）、打击×2（没费）；所向无敌回合结束免费打出",
                Energy = 2,
                FreePlayTopEntry = "I_AM_INVINCIBLE",
                PickOrder = new List<Type> { typeof(KinglyKick), typeof(IAmInvincible) },
                Cards = Cards(
                    C<CosmicIndifference>(), C<PhotonCut>(true),
                    new() { Card = typeof(KinglyKick), CostThisTurn = 0 },
                    DisplayOnlyPlayed<IAmInvincible>(),
                    C<StrikeRegent>(), C<StrikeRegent>()),
            },
        };

        return new BossScript
        {
            Turns = turns,
            LoopFrom = 7,      // 用户：「之后 7～10 循环」
            LoopLength = 4,
        };
    }

    /// <summary>卡组（剧本里出现过的牌按最大同时张数收集；剧本手牌不依赖这份清单，只用于展示/统计）。</summary>
    public static List<Type> Deck() => new()
    {
        typeof(TheSealedThrone),     // 封印王座
        typeof(MonarchsGaze),        // 王之凝视
        // 笨拙（诅咒，`Unplayable + Ethereal`）：第 1 回合 / 第 5 回合各一张
        //（第 5 回合那张是用户 2026-11-02 削弱时把「何人僭越」换下来的）
        typeof(Clumsy), typeof(Clumsy),
        typeof(PhotonCut),           // 光子切割
        typeof(KinglyPunch),         // 王者之拳
        typeof(KinglyKick),          // 王者之踢
        typeof(CloakOfStars),        // 群星斗篷
        typeof(IAmInvincible),       // 所向无敌
        typeof(Royalties),           // 王国资产
        typeof(RoyalGamble),         // 胜券在王
        typeof(DecisionsDecisions),  // 抉择，抉择
        typeof(CosmicIndifference),  // 宇宙冷漠
        typeof(SummonForth),         // 征召上前（v0.2.325 替换战火铸就）
        typeof(Alignment),           // 星位序列
        // ⚠ **不要**把君王之剑写进卡组（用户 2026-11-02：「他没有击剑指南…不应该在战斗开始时
        //   就把剑给铸造出来飘在他身边。应该在他第五回合打「铸造」牌的时候再铸造出来」）。
        //   卡组里有这张牌 ⇒ 战斗开始时它就在她的牌堆里，UI 会把它当成「已有的剑」补出浮空剑。
        //   这里不写 ⇒ 她是**第 5 回合打出「征召上前」时**由 `ForgeCmd.Forge` 现造一把（反编译确认：
        //   `Forge` 在 `blades.Count == 0` 时会 `CreateCard<SovereignBlade>` + 放进手牌 + `AddDamage` +
        //   `PlayCombatRoomForgeVfx`，正好就是「打铸造牌的那一刻把剑铸出来」）。
        //   剧本里的「君王之剑」条目靠 `BossScriptRunner.TakeExistingBlade` 取那一把。
        typeof(StrikeRegent), typeof(StrikeRegent),
        typeof(StrikeRegent), typeof(StrikeRegent),
        typeof(DefendRegent), typeof(DefendRegent), typeof(DefendRegent),
    };

    /// <summary>遗物（用户清单）：天赋君权、护喉甲、尘封魔典、红面具（涅奥的祝福由 <c>CarriesNeowBlessing</c> 自动带）。</summary>
    public static List<Type> Relics() => new()
    {
        typeof(DivineRight),    // 天赋君权（起始）：战斗开始给星
        typeof(NeowBlessing),   // 涅奥的祝福（第 2 件）
        typeof(Gorget),         // 护喉甲：战斗开始 4 层覆甲
        typeof(DustyTome),      // 尘封魔典：远古遗物，效果在拾起时 ⇒ 战斗内零影响
        typeof(RedMask),        // 红面具：战斗开始给所有玩家虚弱
    };

    // ── 小工具 ──────────────────────────────────────────────

    private static List<ScriptCard> Cards(params ScriptCard[] cards) => new(cards);

    private static BossTurn Turn(int energy, string note, int turnIndex, params ScriptCard[] cards)
        => new() { Note = $"{turnIndex}. {note}", Cards = new List<ScriptCard>(cards), Energy = energy };

    private static ScriptCard C<T>(bool upgraded = false) where T : CardModel
        => new() { Card = typeof(T), Upgraded = upgraded, From = "deck" };

    /// <summary>「回合结束会从抽牌堆顶免费打出」的展示牌（所向无敌）：亮着 + 画出格挡意图。</summary>
    private static ScriptCard DisplayOnlyPlayed<T>() where T : CardModel
        => new() { Card = typeof(T), From = "display", DisplayOnly = true, AutoPlayed = true };
}
