using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// 二层储君的**第三套设计「仆从储」**（Key `REGENT_MINION`，用户 2026-11-05 设计）。
/// 同层另外两套：铸剑储君（主设计 <see cref="RegentScript"/>）/ 王储（`REGENT_KING`）。
///
/// 用户原话：
/// > 特殊效果「**征召上前**」：每当储君打出仆从牌时，若其仆从数不足 2，改为召唤对应种类的仆从。
/// > 每当储君获得力量时，他的所有其它友方也会获得等量力量。
/// > （三条限制：只在他**用卡牌**获得力量时才给；被玩家减力量而失去力量时不连累友方；
/// >   临时减力量结束而回涨时也不给。实现在 <c>Powers/BossCallToArmsPower</c>）
/// > 仆从：打击仆从 / 俯冲仆从 / 捐躯仆从（三种新怪物，见 <c>Monsters/MinionMonsters.cs</c>），
/// > 先于储君行动、站储君左边、相对独立（不替他承伤）、带爪牙（储君死亡时一起死）。
/// > 遗物：天赋君权、涅奥的祝福、星系尘埃、大抱抱、维特鲁威仆从、君王矿石。
/// > 进阶 8：升级护驾！！！、群星之子；进阶 9：升级下去！、冲锋！！、类星体（震荡波、究极打击）。
/// > 收藏家藏品：护驾！！！。
///
/// 能量账：角色基础 3 − 涅奥的祝福 1 = **2**（剧本里的「没费」都按这套账）。
/// 循环：第 **7～9** 回合。
///
/// 「仆从打击 / 仆从俯冲 / 仆从捐躯」这三张牌在原版只是**带 `CardTag.Minion` 的普通牌**
/// （打击 + 抽牌 / 打击 / 格挡），并没有真的召唤单位 —— 召唤是「征召上前」给的。
/// 所以剧本里照常写这三张牌；**真正是召唤还是原本效果，由特殊效果在打出时决定**。
/// </summary>
public static class RegentMinionScript
{
    /// <summary>每回合能量（3 基础 − 1 涅奥的祝福）。</summary>
    private const int TurnEnergy = 2;

    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            // 1：碰撞轨迹（印碎屑）、下去！（变碎屑）、仆从打击（召唤）、隐秘藏品+；防御没费
            Turn("碰撞轨迹（印碎屑）、下去！（变碎屑）、仆从打击（不造成伤害，召唤打击仆从）、隐秘藏品+、防御（没费）", 1,
                C<CollisionCourse>(), C<Begone>(), C<MinionStrike>(), C<HiddenCache>(true), C<DefendRegent>()),

            // 2：胜券在王、光谱偏移+、冲锋！！（当回合无效果）
            Turn("胜券在王、光谱偏移+、冲锋！！（当回合无效果）", 2,
                C<RoyalGamble>(), C<SpectrumShift>(true), C<Charge>()),

            // 3：仆从俯冲 ×2（不够 2 只就召唤，够 2 只才造成伤害）、非凡技艺、共鸣
            Turn("仆从俯冲（不够 2 只则召唤）、仆从俯冲（同上）、非凡技艺、共鸣", 3,
                C<MinionDiveBomb>(), C<MinionDiveBomb>(), C<Prowess>(), C<Resonance>()),

            // 4：暗黑镣铐、类星体（印震荡波；进阶 9 类星体已升级 ⇒ 震荡波也是升级版）、震荡波、防御×2（没费）
            Turn("暗黑镣铐、类星体（印震荡波）、震荡波、防御×2（没费）", 4,
                C<DarkShackles>(), C<Quasar>(), C<Shockwave>(), C<DefendRegent>(), C<DefendRegent>()),

            // 5：护驾！！！、仆从捐躯×3（不够 2 只则召唤，够 2 只才给格挡）
            Turn("护驾！！！、仆从捐躯×3（不够 2 只则召唤，否则获得格挡）", 5,
                C<Guards>(), C<MinionSacrifice>(), C<MinionSacrifice>(), C<MinionSacrifice>()),

            // 6：撕碎（无色牌）、群星之子、笨拙（诅咒，打不出）、防御（没费）
            Turn("撕碎（无色牌）、群星之子、笨拙（诅咒，打不出）、防御（没费）", 6,
                C<Rend>(), C<ChildOfTheStars>(), C<Clumsy>(), C<DefendRegent>()),

            // 7：**洗牌** + 闪亮登场、碰撞轨迹、下去！、仆从打击（同上）、隐秘藏品+、防御（没费）
            Turn("**洗牌**、闪亮登场、碰撞轨迹（印碎屑）、下去！（变碎屑）、仆从打击、隐秘藏品+、防御（没费）", 7, true,
                C<DramaticEntrance>(), C<CollisionCourse>(), C<Begone>(), C<MinionStrike>(),
                C<HiddenCache>(true), C<DefendRegent>()),

            // 8：仆从俯冲×2、非凡技艺、共鸣
            Turn("仆从俯冲×2（同上）、非凡技艺、共鸣", 8,
                C<MinionDiveBomb>(), C<MinionDiveBomb>(), C<Prowess>(), C<Resonance>()),

            // 9：撕碎（无色牌）、类星体（印亮剑 / 进阶 9 起印究极防御+）、究极防御、大奖（没费）、防御×2（没费）
            Turn("撕碎（无色牌）、类星体（印亮剑）、究极打击、大奖（没费）、防御×2（没费）", 9,
                C<Rend>(), C<Quasar>(), C<UltimateDefend>(), C<Jackpot>(),
                C<DefendRegent>(), C<DefendRegent>()),
        };

        return new BossScript
        {
            Turns = turns,
            LoopFrom = 7,     // 用户：「之后 7～9 循环」
            LoopLength = 3,
        };
    }

    /// <summary>卡组（剧本里出现过的牌按张数收集，用于展示与自检）。</summary>
    public static List<Type> Deck() => new()
    {
        typeof(CollisionCourse), typeof(CollisionCourse),
        typeof(Begone), typeof(Begone),
        typeof(MinionStrike), typeof(MinionStrike),
        typeof(HiddenCache), typeof(HiddenCache), typeof(HiddenCache),
        typeof(DefendRegent), typeof(DefendRegent), typeof(DefendRegent),
        typeof(DefendRegent), typeof(DefendRegent), typeof(DefendRegent),
        typeof(RoyalGamble),
        typeof(SpectrumShift),
        typeof(Charge),
        typeof(MinionDiveBomb), typeof(MinionDiveBomb), typeof(MinionDiveBomb),
        typeof(MinionDiveBomb), typeof(MinionDiveBomb), typeof(MinionDiveBomb),
        typeof(Prowess), typeof(Prowess),
        typeof(Resonance), typeof(Resonance),
        typeof(DarkShackles),
        typeof(Quasar), typeof(Quasar),
        typeof(Shockwave),
        typeof(Guards),
        typeof(MinionSacrifice), typeof(MinionSacrifice), typeof(MinionSacrifice),
        typeof(Rend), typeof(Rend),
        typeof(ChildOfTheStars),
        typeof(Clumsy),
        typeof(DramaticEntrance),
        typeof(UltimateDefend),
        typeof(Jackpot),
        typeof(FlashOfSteel),
    };

    /// <summary>
    /// 遗物：天赋君权 / 涅奥的祝福 / 星系尘埃 / 大抱抱 / 维特鲁威仆从 / 君王矿石。
    /// 天赋君权（战斗开始给辉星）由 <c>EnsureStarterStars</c> 补；
    /// 君王矿石（每回合第一次生成牌 ⇒ 格挡）的钩子对影子玩家不生效时同样由桥接补。
    /// </summary>
    public static List<Type> Relics() => new()
    {
        typeof(DivineRight),        // 天赋君权：战斗开始获得辉星
        typeof(NeowBlessing),       // 涅奥的祝福（已在身上时跳过）
        typeof(GalacticDust),       // 星系尘埃：每消耗 N 点辉星获得格挡
        typeof(BiiigHug),           // 大～抱抱：每次洗牌生成煤灰
        typeof(VitruvianMinion),    // 维特鲁威仆从：名字里有「仆从」的牌造成双倍伤害与格挡
        typeof(Regalite),           // 君王矿石：每回合第一次生成一张牌 ⇒ 获得格挡
    };

    private static BossTurn Turn(string note, int turnIndex, params ScriptCard[] cards)
        => new()
        {
            Note = $"{turnIndex}. {note}",
            Cards = new List<ScriptCard>(cards),
            Energy = TurnEnergy,
        };

    /// <summary>带「这一回合先洗牌」的回合（第 7 回合，用户明确要求真的洗一次）。</summary>
    private static BossTurn Turn(string note, int turnIndex, bool shuffle, params ScriptCard[] cards)
        => new()
        {
            Note = $"{turnIndex}. {note}",
            Cards = new List<ScriptCard>(cards),
            Energy = TurnEnergy,
            ShuffleDrawPile = shuffle,
        };

    private static ScriptCard C<T>(bool upgraded = false)
        where T : CardModel
        => new() { Card = typeof(T), Upgraded = upgraded, From = "deck" };
}
