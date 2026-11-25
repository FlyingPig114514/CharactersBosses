#if STS2_113
using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Cards;
using CharacterBosses.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// 三层 1 号愚者 Boss「**塔罗愚**」的剧本（用户 2026-11-14 设计；完整需求见
/// <c>docs/plans/boss-fool-tarot.md</c>）。
///
/// 要点：卡组只有 3 张神话形态；16 回合后 **10~16 循环**；拦掉所有原版抽牌 / 印牌 / 捞牌；
/// 恐惧模式下**非无色牌全部升级**（走 <see cref="DreadUpgradeTypes"/> + `BossDefinition.DreadUpgradesByAct`
/// 这条运行期通道，不能在 `Build()` 里判 `DreadMode.IsActive` —— 那时是静态初始化，恒为 false）。
///
/// ⚠ 0.113.0 才有「愚者」角色与整套塔罗牌 ⇒ 整个文件只在 111 构建里编译。
/// </summary>
public static class TarotFoolScript
{
    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            T("1. 神话形态、神话形态、神话形态（三张都是升级版）", C<MythicForm>(true), C<MythicForm>(true), C<MythicForm>(true)),

            T("2. 皇帝、神话形态、神话形态（升级版）", C<TheEmperor>(), C<MythicForm>(true), C<MythicForm>(true)),

            // ★ 2026-11-25（用户：削弱，第 3 回合的魔术师换成星星；再削弱，第 3 回合的月亮换成正义）。
            T("3. 女祭司、星星、正义", C<TheHighPriestess>(), C<TheStar>(), C<Justice>()),

            T("4. 战车、世界、皇后", C<TheChariot>(), C<TheWorld>(), C<TheEmpress>()),

            // ★ 2026-11-25（用户：第 5 回合意图改成「正义、女祭司、恶魔」）。
            T("5. 正义、女祭司、恶魔", C<Justice>(), C<TheHighPriestess>(), C<TheDevil>()),

            T("6. 节制、魔术师（1 电 1 冰 1 玻璃）、命运之轮（无效果）", C<Temperance>(), C<TheMagician>(), C<WheelOfFortune>()),

            T("7. 死亡、力量、隐者（无效果）", C<Death>(), C<Strength>(), C<TheHermit>()),

            T("8. 星星、星星、魔术师（1 电 1 冰 1 玻璃）", C<TheStar>(), C<TheStar>(), C<TheMagician>()),

            T("9. 皇帝、皇帝、恋人、被遗忘的仪式、鬼火、恶魔形态、群蛇形态、死神形态、回响形态、" +
              "天人形态、永恒形态、新生之喜、黑暗镣铐、非凡技艺、应急按钮、虚空形态", C<TheEmperor>(), C<TheEmperor>(), C<TheLovers>(),
                C<ForgottenRitual>(), C<Wisp>(),
                C<DemonForm>(), C<SerpentForm>(), C<ReaperForm>(), C<EchoForm>(),
                C<TarotDevaForm>(), C<TarotEternalForm>(),
                C<BundleOfJoy>(), C<DarkShackles>(), C<Prowess>(), C<PanicButton>(), C<VoidForm>()),

            T("10. 愚者（回响形态 ⇒ 打 2 次；按剧本顺序先打女祭司）、女祭司、魔术师", C<TheFool>(), C<TheHighPriestess>(), C<TheMagician>()),

            T("11. 女皇（2 次）、皇帝、教皇（印滚石/永恒铠甲/震荡波）、滚石、永恒铠甲、震荡波", C<TheEmpress>(), C<TheEmperor>(), C<TheHierophant>(),
                C<RollingBoulder>(), C<EternalArmor>(), C<Shockwave>()),

            T("12. 力量（2 次）、战车、恋人、精神过载 ×2、燃烧、灵动步法、创世之柱、血肉戏法、" +
              "碎片整理、斋戒、斗殴、新生之喜、黑暗镣铐、应急按钮、神气制胜", C<Strength>(), C<TheChariot>(), C<TheLovers>(),
                C<Neurosurge>(), C<Neurosurge>(),
                C<Inflame>(), C<Footwork>(), C<PillarOfCreation>(), C<SleightOfFlesh>(), C<Defragment>(),
                C<TarotFasting>(), C<TarotBrawl>(),
                C<BundleOfJoy>(), C<DarkShackles>(), C<PanicButton>(), C<Panache>()),

            T("13. 正义（2 次）、隐者（无效果）、命运之轮（无效果）", C<Justice>(), C<TheHermit>(), C<WheelOfFortune>()),

            // ★ 2026-11-14：**倒吊人的额外回合并进这一行**（原来它是独立的一行 ⇒ 后面所有回合都错位一格，
            //   而且「额外回合」实际上从没真的多打过一个回合）。`ExtraTurnAfterCard = 2` =
            //   「打完前 3 张（节制/倒吊人/死神）之后，把回合开始的钩子重跑一遍，再打后面那 3 张」。
            new BossTurn
            {
                Note = "14. 节制（2 次）、倒吊人、死神 ⇒【倒吊人的额外回合】高塔（2 次）、星星、恶魔（无效果）",
                ExtraTurnAfterCard = 2,
                Cards = new List<ScriptCard>
                {
                    C<Temperance>(), C<TheHangedMan>(), C<Death>(),
                    C<TheTower>(), C<TheStar>(), C<TheDevil>(),
                },
            },

            T("15. 星星（2 次）、太阳（无效果）、月亮（塞 10 把小刀）", C<TheStar>(), C<TheSun>(), C<TheMoon>()),

            T("16. 审判、世界、恶魔、煤灰 ×2（被消耗）", C<Judgement>(), C<TheWorld>(), C<TheDevil>(), Discard<Soot>(), Discard<Soot>()),
        };

        return new BossScript
        {
            Turns = turns,
            LoopFrom = 10,      // 用户：「之后 10～16 循环」
            LoopLength = 7,
        };
    }

    /// <summary>卡组：用户口径「卡组里只包含 3 张神话形态」（其余牌全是印出来的）。</summary>
    public static List<Type> Deck() => new()
    {
        typeof(MythicForm), typeof(MythicForm), typeof(MythicForm),
    };

    /// <summary>遗物（用户 2026-11-14 给的顺序）。</summary>
    public static List<Type> Relics() => new()
    {
        typeof(SongBow),            // 歌者之弓
        typeof(NeowBlessing),       // 涅奥的祝福（所有 Boss 都是第 2 件）
        typeof(Anchor),             // 锚（用户 2026-11-14：把钨合金棍换成它）
        typeof(RingingTriangle),    // 三角铃鼓
        typeof(PaelsTears),         // 佩尔之泪（⚠ 加费必须真的作用到他的出牌上）
        typeof(Permafrost),         // 永冻冰晶
        typeof(PhilosophersStone),  // 贤者之石
        typeof(MercuryHourglass),   // 水银沙漏
        typeof(IceCream),           // 冰激凌（用户 2026-11-25：全进阶全模式都带）
    };

    /// <summary>
    /// 恐惧模式要升级的牌：**非无色牌全部升级**（用户口径）。
    /// 排除：新生之喜 / 非凡技艺 / 应急按钮 / 黑暗镣铐 / 神气制胜（无色牌）+ 煤灰（诅咒）。
    /// </summary>
    public static List<Type> DreadUpgradeTypes() => new()
    {
        typeof(MythicForm),
        typeof(TheEmperor), typeof(TheEmpress), typeof(Strength), typeof(Justice),
        typeof(TheFool), typeof(TheMagician), typeof(TheHighPriestess),
        typeof(TheChariot), typeof(TheWorld), typeof(Temperance), typeof(WheelOfFortune),
        typeof(TheMoon), typeof(TheStar), typeof(TheSun), typeof(Death), typeof(TheHermit),
        typeof(TheLovers), typeof(TheHangedMan), typeof(TheTower),
        typeof(Judgement), typeof(TheDevil), typeof(TheHierophant),
        typeof(ForgottenRitual), typeof(Wisp),
        typeof(DemonForm), typeof(SerpentForm), typeof(ReaperForm), typeof(EchoForm), typeof(VoidForm),
        typeof(Neurosurge), typeof(Inflame), typeof(Footwork),
        typeof(PillarOfCreation), typeof(SleightOfFlesh), typeof(Defragment),
        typeof(RollingBoulder), typeof(EternalArmor), typeof(Shockwave),
        typeof(TarotFasting), typeof(TarotDevaForm), typeof(TarotBrawl), typeof(TarotEternalForm),
    };

    // ── 剧本小工具 ─────────────────────────────────────────────────────────
    // ⚠ **不覆盖每回合能量**（`BossTurn.Energy = 0` = 用 `BossDefinition.EnergyForAct`）。
    //   用户 2026-11-14 口径：他每回合就是普通的 3 点能量，多出来的部分来自塔罗牌自己 ——
    //     皇帝 `TheEmperor` = 获得 7 点能量、星星 `TheStar` = 下回合 3 点、太阳 `TheSun` = 2 点。
    //   证据：第一回合写的是「神话形态、神话形态（没费）、神话形态（没费）」——
    //   神话形态 3 费，3 点能量正好只够打第一张，后两张必然没费。
    private static BossTurn T(string note, params ScriptCard[] cards)
        => new() { Note = note, Cards = new List<ScriptCard>(cards) };

    private static ScriptCard C<T>(bool upgraded = false)
        where T : MegaCrit.Sts2.Core.Models.CardModel
        => new() { Card = typeof(T), Upgraded = upgraded, From = "deck" };

    /// <summary>被前一张牌（恶魔）消耗掉的那张（煤灰）。</summary>
    private static ScriptCard Discard<T>()
        where T : MegaCrit.Sts2.Core.Models.CardModel
        => new() { Card = typeof(T), From = "deck", DiscardInstead = true };
}
#endif
