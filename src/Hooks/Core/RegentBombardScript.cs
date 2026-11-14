using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// 一层「**轰击储**」（储君 · 轰击 / 无色轴）—— 用户 2026-11-04 设计稿，
/// 设计原文与核对过程见 `docs/plans/boss-regent-bombard.md`。
///
/// 这是**一层**储君的**第二套设计**（第一套是「碎屑储君」`DebrisRegentScript`），
/// 与「铸剑/王储」「无色/辉星」同一套机制（按阵容/遭遇抽一边）。
///
/// 特殊效果「**环绕轨道**」（`Powers/BossOrbitalBombardPower.cs`）：
///   · 你以 1 层「光谱偏移」（`SpectrumShiftPower`）开始战斗 ⇒ 每回合多摸 1 张**随机无色牌**；
///   · 储君**第一回合被击晕**；第二回合打出「轰击」（`Bombardment`，18 伤、**消耗**）
///     之后它躺在**她的消耗牌堆**里，靠原版 `AfterAutoPrePlayPhaseEnteredEarly`
///     **每回合自动复燃一次**（这就是「环绕」）—— 这期间她一直处于被击晕状态、不出别的牌；
///   · 你**打出过无色牌的回合数**累加到 5 ⇒ 她「醒」过来，**并且轰击的伤害反过来打她自己**
///     （每次复燃 18 点自伤）。唤醒后是**两回合循环**。
///
/// ⚠ 唤醒的那一回合是**按剧本回合号**算的：唤醒发生在玩家第 5 个无色回合结束时，
///   而她的下一份计划是在**下一个玩家回合开始**时才规划的 ⇒ 所以「唤醒后第一回合」= 唤醒后的下一次规划。
///   具体实现见 `BossOrbitalBombardPower.WakeTurn`（记的是剧本回合号）。
/// </summary>
public static class RegentBombardScript
{
    public static BossScript Build()
    {
        // 唤醒后的两回合循环（用户原文）：
        //   A：星灭、隐秘藏品、金斧+（**保留，不出**）
        //   B：群星斗篷、拳斗、金斧+、防御（**没费**）
        //
        // 「不出 / 没费」用本项目一贯的写法表达：把它们列进 `Cards`，靠 `Energy` 让它们排到却打不出
        //（日志会打「跳过 [GOLD_AXE(费不够 1>0)]」，和辉星储第 2 回合的「究极防御没费不出」同一种表达）。
        //   · A 回合：星灭 1 费 + 隐秘藏品 0 费 ⇒ 只给 1 费，金斧+（1 费）打不出 ⇒ 正好「保留，不出」；
        //   · B 回合：群星斗篷 0 + 拳斗 1 + 金斧+ 1 = 2 费 ⇒ 给 2 费，防御（1 费）打不出 ⇒ 正好「没费」。
        var turnBombard = new BossTurn
        {
            Note = "2. 轰击 + 笨拙×2（笨拙不可打出，只是她手里的填充）",
            Energy = 3,                       // 轰击 3 费
            Cards = Cards(C<Bombardment>(), C<Clumsy>(), C<Clumsy>()),
        };
        var wakeA = new BossTurn
        {
            Note = "唤醒后 A：星灭、隐秘藏品、金斧+（保留，不出）",
            // 星灭 1 费 + 隐秘藏品 **1 费** = 2 ⇒ 正好把 2 费花完，金斧+（1 费）打不出 = 用户要的「保留，不出」。
            // ⚠ 2026-11-05 修：这里原来写 Energy = 1（我以为隐秘藏品是 0 费），实测日志
            //   `跳过 [HIDDEN_CACHE(费不够 1>0)]` —— 隐秘藏品是 **1 费**，于是她只打了星灭。
            Energy = 2,
            Cards = Cards(C<DyingStar>(), C<HiddenCache>(), C<GoldAxe>(true)),
        };
        var wakeB = new BossTurn
        {
            Note = "唤醒后 B：群星斗篷、拳斗、金斧+、防御（没费）",
            Energy = 2,
            Cards = Cards(C<CloakOfStars>(), C<Fisticuffs>(), C<GoldAxe>(true), C<DefendRegent>()),
        };

        var script = new BossScript
        {
            // 只放静态回合表（给「没有 Resolve 也能工作」兜底：1 = 被击晕、2 = 轰击、3 = 唤醒 A）
            Turns = new List<BossTurn>
            {
                new() { Note = "1. 被击晕（环绕轨道）", Cards = new List<ScriptCard>() },
                turnBombard,
                wakeA,
            },
            // ★ 真正的回合选择走这里：被击晕的回合数由玩家决定 ⇒ 长度不固定。
            //   2026-11-13（恐惧模式，用户当轮口径）：「轰击储**第一回合就打轰击**，而不是第二回合」
            //   ⇒ 恐惧模式下打轰击的那一回合是第 1 回合，之后立刻进入被击晕状态。
            //   （`DreadMode.IsActive` 在这里是**运行期**求值 —— `Resolve` 每次取回合表时才跑，
            //     不像 `Build()` 那样在 `BossDefs` 静态初始化时就定死。）
            Resolve = turn =>
            {
                bool dread = Core.DreadMode.IsActive;
                int bombardTurn = dread ? 1 : 2;

                if (turn < bombardTurn)
                {
                    return new BossTurn
                    {
                        Note = dread ? "1.（恐惧）先打轰击 —— 见下一回合" : "1. 被击晕（环绕轨道）",
                        Cards = new List<ScriptCard>(),
                    };
                }
                if (turn == bombardTurn) return turnBombard;
                if (!Powers.BossOrbitalBombardPower.Awake)
                    return new BossTurn
                    {
                        Note = "被击晕（轰击在她的消耗牌堆里，每回合自动复燃）",
                        Cards = new List<ScriptCard>(),
                    };

                int k = turn - Powers.BossOrbitalBombardPower.WakeTurn;
                return (k % 2 == 0) ? wakeA : wakeB;
            },
        };
        return script;
    }

    /// <summary>卡组（剧本里出现过的牌；剧本手牌另外自己造，卡组只影响「牌堆里数得到」的那些判定）。</summary>
    public static List<Type> Deck() => new()
    {
        typeof(Bombardment),                    // 轰击
        typeof(DyingStar), typeof(DyingStar),   // 星灭
        typeof(HiddenCache), typeof(HiddenCache),   // 隐秘藏品
        typeof(GoldAxe), typeof(GoldAxe),       // 金斧
        typeof(CloakOfStars),                   // 群星斗篷
        typeof(Fisticuffs), typeof(Fisticuffs), // 拳斗
        typeof(Clumsy), typeof(Clumsy),         // 笨拙（诅咒）
        typeof(DefendRegent), typeof(DefendRegent), typeof(DefendRegent),
    };

    /// <summary>遗物（用户清单）：天赋君权 / 涅奥的祝福 / 红面具 / 烛台。</summary>
    public static List<Type> Relics() => new()
    {
        typeof(DivineRight),         // 天赋君权（起始）：战斗开始获得辉星
        typeof(NeowBlessing),        // 涅奥的祝福（第 2 件，所有 Boss 都一样）
        typeof(RedMask),             // 红面具：战斗开始给对手 1 层虚弱
        typeof(Chandelier),          // 烛台：第 3 回合开始 +3 能量
    };

    // ── 小工具（与其它剧本一致） ──────────────────────────────

    private static List<ScriptCard> Cards(params ScriptCard[] cards) => new(cards);

    private static ScriptCard C<T>(bool upgraded = false) where T : CardModel
        => new() { Card = typeof(T), Upgraded = upgraded, From = "deck" };
}
