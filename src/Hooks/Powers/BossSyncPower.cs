using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using HarmonyLib;
using CharacterBosses.Monsters;

namespace CharacterBosses.Powers;

/// <summary>
/// 三层故障机器人**第三套设计「状态机」**的特殊效果「**同步**」（用户 2026-11-05）。
///
/// ⚠ **命名规则（用户 2026-11-05 明确）**：
///   · **Boss 名**和**特殊效果名**是两件独立的事 —— 「状态机」是 Boss 的名字，
///     「同步」是它特殊效果的名字，**不许拿一个推另一个**；
///   · 所有特殊效果的名字都**和某一张卡牌同名**（例外：**死亡收割**，那是 1 代的卡），
///     但**实际效果和那张牌不一定有直接关系**。
///   ⇒ 所以类名 / loc key 用效果名（`BossSyncPower` / `BOSS_SYNC_POWER`），
///     Boss 的名字走 `DEFECT_BOSS.status`（=「状态机」），两者互不影响。
///
/// 用户原话：
/// > 每当故障机器人生成状态牌时，也会将其复制品加入你的弃牌堆。
/// > 每回合**你首次抽到状态牌**时，将其消耗、抽 1 张牌并获得 1 能量。
///   （最后那句「然后故障机器人获得 1 点力量」在 2026-11-05 的削弱里删掉了。）
///
/// ① 挂在**她身上**的这个 Power 覆写 <see cref="AbstractModel.AfterCardGeneratedForCombat"/>
///    —— 这也是原版「烟囱」(<c>SmokestackPower</c>) 用的同一个钩子，所以两边行为一致：
///    她（或她的牌）生成的**状态牌**（`CardType.Status`）会被复制一份丢进每位真人玩家的**弃牌堆**。
///    ⚠ 必须防重入：给玩家塞复制品本身也是一次「生成」，不拦就会无限递归。
///
/// ② 「你的回合里首次抽到状态牌」这一半由 <see cref="FirstStatusDrawPatch"/> 在
///    `Hook.AfterCardDrawn` 上做（玩家侧的状态牌可能来自她、也可能来自别的途径）：
///    消耗它 → 抽 1 张 → 玩家 +1 能量。每人每回合只触发一次。
/// </summary>
public sealed class BossSyncPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    private static int _logs;

    /// <summary>防重入：我们自己给玩家塞复制品时，不再触发本钩子。</summary>
    private static bool _copying;

    /// <summary>她生成了状态牌 ⇒ 给每位真人玩家的弃牌堆塞一张复制品。</summary>
    public override async Task AfterCardGeneratedForCombat(CardModel card, Player? creator)
    {
        if (_copying || card == null) return;
        try
        {
            if (card.Type != CardType.Status) return;

            var me = Owner;
            if (me == null) return;
            // 只认**她自己**（她的牌）生成的状态牌
            if (creator == null || !ReferenceEquals(creator.Creature, me)) return;

            var combat = me.CombatState;
            if (combat == null) return;

            CardModel canonical = card;
            try
            {
                var canonical2 = MegaCrit.Sts2.Core.Models.ModelDb.GetById<CardModel>(card.Id);
                if (canonical2 != null) canonical = canonical2;
            }
            catch { /* 用原牌兜底 */ }
            int given = 0;
            _copying = true;
            try
            {
                foreach (var player in Core.BossLockOn.RealPlayers(combat))
                {
                    var pc = player?.Creature;
                    if (pc == null || !pc.IsAlive) continue;
                    // ★ 用户 2026-11-05：「状态机的特殊效果也是单体的」⇒ 复制品只塞给**被她锁定的**玩家
                    if (!Core.BossLockOn.SpecialEffectAppliesTo(me.Monster as CharBossMonster, player)) continue;
                    try
                    {
                        var copy = combat.CreateCard(canonical, player!);
                        if (copy == null) continue;
                        var added = await CardPileCmd.AddGeneratedCardToCombat(copy, PileType.Discard, player!);
                        // ★ 2026-11-05（用户：塞牌要像怪物给玩家塞牌那样有动画）
                        //   原版怪物塞牌时走的就是 `CardCmd.PreviewCardPileAdd(result)`（羽化也用它）。
                        try { CardCmd.PreviewCardPileAdd(added); }
                        catch (Exception e2) { Logger.Log("[CharBoss] 同步：塞牌动画失败（忽略）: " + e2.Message); }
                        given++;
                    }
                    catch (Exception e)
                    {
                        Logger.Log("[CharBoss] 同步：给玩家塞状态牌复制失败: " + e.Message);
                    }
                }
            }
            finally { _copying = false; }

            if (given > 0 && _logs++ < 20)
            {
                Logger.Log($"[CharBoss] 同步：她生成了状态牌 {SafeEntry(card)} ⇒ 给 {given} 名玩家各塞一张复制品进弃牌堆");
            }
        }
        catch (Exception e)
        {
            _copying = false;
            Logger.Log("[CharBoss] 同步（生成状态牌）失败: " + e.Message);
        }
    }

    internal static string SafeEntry(CardModel? card)
    {
        try { return card?.Id.Entry ?? "?"; } catch { return "?"; }
    }
}

/// <summary>
/// 「同步」的第二半：**玩家每回合首次抽到状态牌** ⇒ 消耗它、抽 1 张、+1 能量。
/// 挂 `Hook.AfterCardDrawn`（原版每抽一张牌都会广播一次）。
/// 只对**带着「同步」这只 Boss 的战斗**生效；真人玩家自己的状态牌也一样处理（这是用户要的玩法）。
/// </summary>
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), nameof(MegaCrit.Sts2.Core.Hooks.Hook.AfterCardDrawn))]
public static class FirstStatusDrawPatch
{
    /// <summary>每位玩家「本回合已经触发过」的回合号（按 NetId 存）。</summary>
    private static readonly Dictionary<ulong, int> TriggeredTurn = new();

    private static int _logs;

    [HarmonyPrefix]
    public static bool Prefix(ICombatState combatState, PlayerChoiceContext choiceContext, CardModel card,
                              ref Task __result)
    {
        try
        {
            if (combatState == null || card == null) return true;
            if (card.Type != CardType.Status) return true;

            var player = card.Owner;
            if (player?.Creature == null) return true;
            if (player.Creature.Monster != null) return true;          // 她的牌不算「你抽到」
            if (!player.Creature.IsAlive) return true;

            // 本场战斗里有没有带「同步」的她
            var boss = FindSyncBoss(combatState);
            if (boss?.Creature == null) return true;

            // ★ 用户 2026-11-05：「状态机的特殊效果也是单体的」⇒ 只有**被她锁定的**玩家抽到状态牌才触发
            if (!Core.BossLockOn.SpecialEffectAppliesTo(boss, player)) return true;

            int turn = 0;
            try { turn = player.PlayerCombatState?.TurnNumber ?? 0; } catch { turn = 0; }
            if (TriggeredTurn.TryGetValue(player.NetId, out int done) && done == turn) return true;
            TriggeredTurn[player.NetId] = turn;

            if (_logs++ < 20)
            {
                Logger.Log($"[CharBoss] 同步：玩家第 {turn} 回合首次抽到状态牌 {BossSyncPower.SafeEntry(card)} " +
                           "⇒ 消耗它、抽 1 张、+1 能量，她 +1 力量");
            }

            __result = DoAsync(choiceContext, player, card, boss);
            return false;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 同步（首次抽到状态牌）判定失败，按原版: " + e.Message);
            return true;
        }
    }

    private static async Task DoAsync(PlayerChoiceContext ctx, Player player, CardModel card, CharBossMonster boss)
    {
        try { await CardCmd.Exhaust(ctx, card); }
        catch (Exception e) { Logger.Log("[CharBoss] 同步：消耗状态牌失败: " + e.Message); }

        try { await CardPileCmd.Draw(ctx, 1, player); }
        catch (Exception e) { Logger.Log("[CharBoss] 同步：抽 1 张失败: " + e.Message); }

        try
        {
            var pcs = player.PlayerCombatState;
            if (pcs != null) pcs.Energy += 1;
        }
        catch (Exception e) { Logger.Log("[CharBoss] 同步：+1 能量失败: " + e.Message); }

        // ★ 2026-11-05（用户削弱）：「然后故障机器人获得 1 点力量」**整条删掉**。
        //   她的伤害已经比所有三层 Boss 都高，留着这条会让她越打越强。
        _ = boss;
    }

    private static CharBossMonster? FindSyncBoss(ICombatState combat)
    {
        try
        {
            foreach (var c in combat.Enemies)
            {
                if (c?.Monster is not CharBossMonster boss) continue;
                if (boss.Creature?.GetPower<BossSyncPower>() != null) return boss;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }
}
