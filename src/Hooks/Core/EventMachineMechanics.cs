using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Monsters;

namespace CharacterBosses.Core;

/// <summary>
/// 三层「**事件机**」（`DEFECT_EVENT`）那两个「剧本点名」的效果执行器 —— 用户 2026-11-03 设计稿。
///
/// 为什么需要单独一个文件：这两个效果都不是「改个数值」能表达的，
/// 而是要在牌打完之后**自己搬牌 / 自己印牌**：
///   · 万物一心（<c>AllForOne</c>）：返手名单由剧本给（<see cref="ScriptCard.ReturnsToHand"/>）；
///   · 羽化（<c>Metamorphosis</c>）：印进抽牌堆的 3 张由剧本给（<see cref="ScriptCard.PrintsToDrawPile"/>），
///     并且要**摆出来给玩家看**。
///
/// 两张牌的**原版随机部分**都已经被拦掉了：
///   · 万物一心：`Patches/BossAllForOneFilterPatch` 让原版 `Filter` 恒为 false（一张都不返）；
///   · 羽化：`Patches/ShadowBossNoRandomCardPatch` 让随机印牌返回空集合。
/// 所以这里搬的牌 / 印的牌就是**唯一**的一份，不会重复。
/// </summary>
internal static class EventMachineMechanics
{
    /// <summary>
    /// 「**一心化万**」：万物一心只把剧本点名的 **0 费攻击牌**算成「从弃牌堆回到手牌」，
    /// 并且**每算一张 ⇒ 所有单位 +1 力量**（她 + 每位真人玩家）。
    ///
    /// ★ v0.2.340（用户 2026-11-03：「**拦掉万物一心的返手，因为我已经提前算了**」）：
    ///   这些牌**不再真的搬运**（剧本早就把它们摆在手上了：`Frame 4/9` 的冰之长枪 / 超能光束本来就是 0 费手牌），
    ///   所以这里只做「**按剧本点名的张数给力量**」这一件事 —— 卡面费用、手牌内容都不会被我们改来改去。
    ///   原版那半（把 0 费牌全拿回来）也仍然被 `Patches/BossAllForOneFilterPatch` 关着。
    ///
    /// 另外「任何一张牌从弃牌堆回到手牌」这件事还有一条**通用**路径：
    /// 全息影像（Hologram）这类「把弃牌堆里的牌拿回手牌」的牌由原版自己搬，
    /// 那条由 <c>Patches/BossDiscardToHandStrengthPatch</c> 统一补力量（用户同一轮提到的 bug）。
    /// </summary>
    public static async Task AllForOneReturnsAsync(CharBossMonster boss, PlayerChoiceContext ctx, CardModel card)
    {
        try
        {
            var entry = BossScriptRunner.FindEntry(boss, card);
            var want = entry?.ReturnsToHand ?? Array.Empty<Type>();
            if (want.Length == 0) return;

            var names = string.Join(",", Array.ConvertAll(want, t => t.Name));
            Logger.Log($"[CharBoss] 一心化万：剧本点名返手 {want.Length} 张（{names}）—— 牌不搬（剧本已经摆好），只给力量");

            await GrantStrengthToAllUnitsAsync(boss, ctx, card, want.Length);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 一心化万结算失败: " + e);
        }
    }

    /// <summary>「所有单位」（她 + 每位真人玩家）各 +<paramref name="amount"/> 力量。</summary>
    public static async Task GrantStrengthToAllUnitsAsync(CharBossMonster boss, PlayerChoiceContext ctx,
                                                          CardModel? source, int amount)
    {
        if (amount <= 0) return;

        var units = new List<Creature>();
        if (boss.Creature != null && boss.Creature.IsAlive) units.Add(boss.Creature);
        try
        {
            foreach (var real in BossLockOn.RealPlayers(boss.Creature?.CombatState))
            {
                var c = real?.Creature;
                if (c != null && c.IsAlive) units.Add(c);
            }
        }
        catch { /* 拿不到玩家列表就只给她自己 */ }

        foreach (var unit in units)
        {
            try
            {
                await PowerCmd.Apply<StrengthPower>(ctx, unit, amount, boss.Creature, source);
            }
            catch (Exception e)
            {
                Logger.Log($"[CharBoss] 一心化万：给 {unit.Name} 加力量失败: {e.Message}");
            }
        }
        Logger.Log($"[CharBoss] 一心化万：{units.Count} 个单位各 +{amount} 力量");
    }

    /// <summary>
    /// 「**羽化**」：把剧本点名的 3 张牌**印进她的抽牌堆**（本场免费打出 = 费用写成 0），
    /// 然后像「能量汲取」那样**摆出来给玩家看一眼**（她的抽牌堆没有 UI，不摆就什么都看不到）。
    ///
    /// 用户 2026-11-03 明确：只有印到**抽牌堆**的牌需要展示；
    /// 疯狂科学 / 白噪声 / 声东击西印的是**手牌**，直接看得见，不展示。
    /// </summary>
    public static async Task MetamorphosisPrintsAsync(CharBossMonster boss, PlayerChoiceContext ctx, CardModel card)
    {
#if STS2_107
        // ★ 2026-11-05（旧版 107）：随机印牌拦截在旧版上被关掉了（返回空集合会让原版卡死），
        //   原版「羽化」已经自己印了 3 张随机攻击牌 ⇒ 我们再印一遍就成两份，所以这里整段跳过。
        Logger.Log("[CharBoss] 羽化：旧版（107）交给原版印牌，剧本点名的那几张不再额外印");
        await Task.CompletedTask;
        return;
#else
        try
        {
            var entry = BossScriptRunner.FindEntry(boss, card);
            var want = entry?.PrintsToDrawPile ?? Array.Empty<Type>();
            if (want.Length == 0) return;

            var pcs = boss.Shadow?.PlayerCombatState;
            var combat = boss.Creature?.CombatState;
            if (pcs == null || combat == null || boss.Shadow == null) return;

            var made = new List<CardModel>();
            foreach (var type in want)
            {
                var canonical = ModelDb.GetById<CardModel>(ModelDb.GetId(type));
                if (canonical == null)
                {
                    Logger.Log($"[CharBoss] 羽化：取不到 {type.Name} 的 canonical");
                    continue;
                }
                var printed = combat.CreateCard(canonical, boss.Shadow);
                if (printed == null) continue;
                MakeFree(printed);
                try { pcs.DrawPile.AddInternal(printed, -1, true); }
                catch (Exception e) { Logger.Log($"[CharBoss] 羽化：把 {SafeEntry(printed)} 放进抽牌堆失败: {e.Message}"); }
                made.Add(printed);
            }

            Logger.Log($"[CharBoss] 羽化：印进抽牌堆 {made.Count} 张（0 费，本场有效）[{string.Join(",", made.Select(SafeEntry))}]");
            if (made.Count == 0) return;

            if (entry!.ShowPrints)
            {
                // ★ v0.2.340（用户 2026-11-03）：「你这个羽化的塞牌展示界面怎么还是你自己搞的，
                //   不应该是用原版的吗？目前能量汲取用的就是原版的」⇒ 改用**原版卡牌预览界面**
                //   （`CardCmd.Preview` 居中并排、无飞行动画），不再用自绘的 `UI/BossCardShowcase`。
                // ⚠ v0.2.346：**不再 await** 这个预览 —— 它是「展示给玩家看」的纯表现，
                //   一旦里面哪一步（原版 Preview 的收尾 / 副本摘除）没返回，就会把她这一手、
                //   进而整个回合卡住（用户 2026-11-03 报「和事件机的战斗没法结束回合」）。
                //   改成独立的异步任务，出牌流程不等它。
                try { TaskHelper.RunSafely(BossCardPreview.ShowAsync(boss, made, 1.8f, makeFree: true)); }
                catch (Exception e) { Logger.Log("[CharBoss] 羽化：发起展示失败（忽略）: " + e.Message); }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 羽化印牌失败: " + e);
        }
#endif
    }


    /// <summary>把这张**实例**的费用改成 0（本场有效）。卡面、规划、真正扣费三者一致。</summary>
    private static void MakeFree(CardModel card)
    {
        try
        {
            int now = card.EnergyCost.GetWithModifiers(CostModifiers.All);
            if (now > 0) card.EnergyCost.AddThisCombat(-now, true);
        }
        catch (Exception e) { Logger.Log("[CharBoss] 把牌改成 0 费失败: " + e.Message); }
    }

    private static string SafeEntry(CardModel? card)
    {
        try { return card?.Id.Entry ?? "?"; } catch { return "?"; }
    }
}
