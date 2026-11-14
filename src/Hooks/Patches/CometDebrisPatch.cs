using System;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using HarmonyLib;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// 第一幕「碎屑储君」的「**彗星**」第一段（用户设计，2026-10-24 改版）：
/// **每当储君生成碎屑时，反而把它塞进被锁定玩家的抽牌堆。**
///
/// 踩过的坑（别再走一遍）：只在 <c>CardPileCmd.AddGeneratedCardToCombat</c> 的前缀里改
/// <c>newPileType</c> / <c>creator</c> 是**无效**的 —— 那个方法最终是按 <c>card.Owner</c> 去找牌堆的，
/// 而碎屑是 <c>CreateCard&lt;Debris&gt;(collisionCourse.Owner)</c> 造出来的，Owner 一开始就是她的影武者，
/// 所以改了 creator 也还是进她的牌堆（日志会打「改判给玩家」但实际没动，用户实测）。
///
/// 现在的做法：出完一张牌之后，把她三个牌堆里的碎屑**真的搬**到玩家的**抽牌堆**（原来是弃牌堆）。
/// 由 <see cref="CharBossMonster"/> 每打出一张牌后调用。
/// </summary>
public static class CometDebrisRedirect
{
    private const string DebrisEntry = "DEBRIS";

    /// <summary>把她牌堆里的碎屑全部改判给被锁定玩家的弃牌堆（返回真正搬走的那些，用于展示）。</summary>
    public static async System.Threading.Tasks.Task<System.Collections.Generic.List<MegaCrit.Sts2.Core.Models.CardModel>>
        RedirectDebrisToPlayers(CharBossMonster boss)
    {
        var moved = new System.Collections.Generic.List<MegaCrit.Sts2.Core.Models.CardModel>();
        try
        {
            var pcs = boss.Shadow?.PlayerCombatState;
            var state = boss.CombatState;
            if (pcs == null || state == null) return moved;

            // 「彗星」是**对应型**特殊效果：碎屑该进「锁定这只 Boss 的那名玩家」的弃牌堆。
            //
            // ⚠ 以前取的是「Players 里第一个不是她的人」——而影子 Player 也在 CombatState.Players 里，
            //   双 Boss 同场时那个「第一个」很可能**是另一只 Boss 的影子 Player**，于是碎屑塞进了
            //   队友 Boss 的牌堆（等于既没给到玩家、又凭空给 Boss 多一张牌）。现在走锁定关系。
            var victim = Core.BossLockOn.TargetFor(boss);
            if (victim?.Creature?.Monster != null) victim = null;      // 影子 Player 不算玩家
            victim ??= Core.BossLockOn.RealPlayers(state).FirstOrDefault();
            var victimPcs = victim?.PlayerCombatState;
            if (victim == null || victimPcs == null) return moved;

            foreach (var pile in new[] { pcs.Hand, pcs.DrawPile, pcs.DiscardPile })
            {
                if (pile?.Cards == null) continue;
                var debris = pile.Cards.Where(c => c != null && EntryOf(c) == DebrisEntry).ToList();
                foreach (var card in debris)
                {
                    try
                    {
                        // ⚠ 不再「把她那张牌跨归属搬过去」：那条路会把牌堆状态搞坏
                        //（用户实测：之后玩家洗不了牌）。改成原版生成卡牌的标准做法 ——
                        // 她那张直接移除，另造一张**归玩家所有**的碎屑塞进他的弃牌堆，
                        // 视觉/动画（先居中显示、再飞进弃牌堆）全交给原版 AddGeneratedCardToCombat。
                        pile.RemoveInternal(card, true);

                        var canonical = MegaCrit.Sts2.Core.Models.ModelDb.GetById<MegaCrit.Sts2.Core.Models.CardModel>(
                            MegaCrit.Sts2.Core.Models.ModelDb.GetId(
                                typeof(MegaCrit.Sts2.Core.Models.Cards.Debris)));
                        if (canonical == null) continue;

                        var copy = state.CreateCard(canonical, victim);
                        if (copy == null) continue;

                        // ⚠ `creator` 传 null（不是玩家）：`PillarOfCreationPower` 的判据是
                        // 「creator == 我」才给格挡 —— 这是**储君生成、塞给玩家**的牌，
                        // 和原版怪物塞状态牌一样不该触发玩家的创世之柱（用户 2026-10-24）。
                        // 牌归谁看 `copy.Owner`（= victim），与 creator 无关。
                        //
                        // ★ 2026-11-13（用户订正）：**碎屑就塞进抽牌堆（Random）**。
                        //   v0.2.620 我擅自把恐惧版改成「抽牌堆顶部」，用户原文只是「加入你的抽牌堆」
                        //   ⇒ 已回退。恐惧模式的强化在于「碎屑本身对玩家没有正面效果」，
                        //   不需要再动放置位置。
                        await MegaCrit.Sts2.Core.Commands.CardPileCmd.AddGeneratedCardToCombat(
                            copy, PileType.Draw, null, CardPilePosition.Random);

                        // 原版「塞牌给玩家」就是这么展示的（照抄 BiiigHug/大～抱抱塞煤灰那一段）：
                        //   AddGeneratedCardToCombat(...) → CardCmd.Preview(card, 0.75f) → Cmd.Wait(1f)
                        MegaCrit.Sts2.Core.Commands.CardCmd.Preview(copy, 0.75f);
                        await MegaCrit.Sts2.Core.Commands.Cmd.Wait(1f);

                        moved.Add(copy);
                        Logger.Log($"[CharBoss] 彗星：碎屑（玩家所有）洗进 {victim.Character?.GetType().Name} 的抽牌堆" +
                                   $"（原是她{pile.Type}里的那张）");
                    }
                    catch (Exception e)
                    {
                        Logger.Log("[CharBoss] 彗星：碎屑改判失败: " + e.Message);
                    }
                }
            }

            // 牌**已经**在玩家弃牌堆里了（上面 await 完的），这里不再自己画一套展示 ——
            // 原版 AddGeneratedCardToCombat 自己会把牌居中显示再飞进弃牌堆（用户要求的就是这个观感）。
            if (moved.Count > 0)
                Logger.Log($"[CharBoss] 彗星：本次把 {moved.Count} 张碎屑塞进玩家弃牌堆");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 彗星：碎屑改判失败: " + e.Message);
        }
        return moved;
    }

    private static string EntryOf(MegaCrit.Sts2.Core.Models.CardModel? card)
    {
        try { return card?.Id.Entry ?? string.Empty; }
        catch { return string.Empty; }
    }
}

/// <summary>
/// 「如此甚好」（MAKE_IT_SO）打出后会把**自己返回手牌**（原版实现：
/// <c>CardPileCmd.Add(this, PileType.Hand)</c>）。用户设计里，返手的那一张已经**直接摆在剧本手牌里**了
/// （第 1 回合两张、第 6 回合一张），所以这里要把他自己的返手拦掉，否则手里会多出一张。
///
/// 拦法：把目标牌堆从 <c>Hand</c> 改成 <c>Discard</c> —— 不去改返回值
/// （`CardPileAddResult` 的处理交给原版），牌就静静地进弃牌堆，不会凭空多出一张「如此甚好」。
/// </summary>
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Commands.CardPileCmd), "Add",
              new[] { typeof(MegaCrit.Sts2.Core.Models.CardModel), typeof(PileType),
                      typeof(CardPilePosition), typeof(MegaCrit.Sts2.Core.Models.AbstractModel), typeof(bool) })]
public static class MakeItSoNoReturnPatch
{
    [HarmonyPrefix]
    public static void Prefix(MegaCrit.Sts2.Core.Models.CardModel card, ref PileType newPileType)
    {
        try
        {
            if (card == null || newPileType != PileType.Hand) return;
            if (EntryOf(card) != "MAKE_IT_SO") return;
            // 只拦她（影武者）的那张；玩家自己若有同名牌不受影响
            if (card.Owner?.Creature?.Monster is not CharBossMonster) return;

            newPileType = PileType.Discard;
            Logger.Log("[CharBoss] 如此甚好：拦掉返手（返手那张已在剧本手牌里）→ 改为进弃牌堆");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 如此甚好（拦返手）失败: " + e.Message);
        }
    }

    private static string EntryOf(MegaCrit.Sts2.Core.Models.CardModel? card)
    {
        try { return card?.Id.Entry ?? string.Empty; }
        catch { return string.Empty; }
    }
}
