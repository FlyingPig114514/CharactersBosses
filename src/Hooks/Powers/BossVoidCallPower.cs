using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Afflictions;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Monsters;

namespace CharacterBosses.Powers;

/// <summary>
/// 二层亡灵契约师**第二套设计「虚无骨」**的特殊效果「**虚空之唤**」（用户 2026-11-09）。
///
/// 用户原话：
/// > 特殊效果：虚空之唤：亡灵契约师首次打出女妖之嚎时，所有玩家的所有卡牌获得虚无，
/// > 直到亡灵契约师死亡。
/// > PS：实现方法为第四回合的那张女妖之嚎会给所有玩家添加**恶咒**……
/// >     多人时，记得让亡灵契约师（虚无骨）死亡后移除所有玩家的恶咒。
///
/// ## 实现
/// 「所有卡牌获得虚无」这件事原版已经做好了 —— 就是 **`HexPower`**（恶咒）：
/// 它会给持有者的**全部卡牌**挂 `Hexed`（邪咒，「这张牌获得虚无」），并且之后新生成的牌也一样。
/// 所以这里只做三件事：
///   1. 挂一个「她身上的标记 power」（就是本类）—— 它同时负责特殊效果说明框的显示；
///   2. 她**第一次**打出「女妖之嚎」（`BANSHEES_CRY`）时，给**所有真人玩家**上 `HexPower`；
///   3. 她**死亡**时，把所有真人玩家身上的 `HexPower` 摘掉（多人局里战斗还没结束就要立刻失效）。
///
/// ⚠ `HEX_POWER.description` 的原版文案写死了「幽灵骑士存活时……」——
///   玩家去打**幽灵骑士**（原版怪）时必须还是原版文案，所以那段文字**只在虚无骨这场战斗里**替换，
///   见 <see cref="VoidHexTextPatch"/>（按「场上有没有活着的虚无骨」判断，不是全局覆盖）。
/// </summary>
public sealed class BossVoidCallPower : PowerModel
{
    /// <summary>女妖之嚎的 entry。</summary>
    private const string BansheeEntry = "BANSHEES_CRY";

    /// <summary>本场是否已经触发过（只触发第一次）。</summary>
    internal bool Applied { get; private set; }

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    /// <summary>
    /// ★ 2026-11-13（恐惧模式「虚空之唤」）：正常版要等她**首次打出「女妖之嚎」**才生效；
    /// 恐惧版 **战斗开始就直接生效**（用户设计表原文：「战斗开始 ⇒ 所有玩家所有牌获得**虚无**，
    /// 直到她死亡」）。
    /// 「直到她死亡」那条本来就由 <c>CharBossMonster.BeforeRemovedFromRoom</c> 调
    /// <see cref="ClearHexFromAllPlayers"/> 负责 ⇒ 这里只管把恶咒提前挂上。
    /// </summary>
    public override async Task AfterApplied(MegaCrit.Sts2.Core.Entities.Creatures.Creature? applier,
                                            CardModel? cardSource)
    {
        try
        {
            if (!Core.DreadMode.IsActive) return;
            var owner = Owner;
            if (owner == null || !owner.IsAlive) return;
            if (Applied) return;

            var combat = owner.CombatState;
            if (combat == null) return;

            var targets = new List<Creature>();
            foreach (var player in Core.BossLockOn.RealPlayers(combat))
            {
                var creature = player?.Creature;
                if (creature == null || !creature.IsAlive) continue;
                if (targets.Contains(creature)) continue;
                targets.Add(creature);
            }
            if (targets.Count == 0) return;

            Applied = true;
            int amount = Math.Max(1, Amount);
            await PowerCmd.Apply<HexPower>(new BlockingPlayerChoiceContext(), targets, amount, owner, null);
            Logger.Log($"[CharBoss] 虚空之唤（恐惧）：战斗开始即生效 ⇒ 给 {targets.Count} 名玩家各挂 " +
                       $"{amount} 层恶咒（所有卡牌获得虚无，直到她死亡）");
            // ★ v0.2.864（玩家反馈：恐惧模式下只有**战斗中生成的牌**才有虚无，开局手里 / 牌堆里的牌都没有）：
            //   原版 `HexPower.AfterApplied` 是遍历 `PlayerCombatState.AllCards` 逐张 `CardCmd.Afflict<Hexed>`，
            //   而我们在「战斗开始」这一刻挂恶咒时玩家的战斗牌堆**还没铺开**（AllCards 是空的）⇒
            //   那条循环一张都没挂上，只有之后进战斗的新牌走 `AfterCardEnteredCombat` 才拿到虚无。
            //   ⇒ 这里紧接着自己补一遍；玩家第一个回合开始时再补一遍（那时牌一定都就位了）。
            await SweepHexAsync("恶咒刚挂上");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 虚空之唤（恐惧）：战斗开始挂恶咒失败: " + e.Message);
        }
    }

    /// <summary>场上是否还有活着的「虚无骨」（给文案替换用）。</summary>
    internal static bool AnyAliveVoidBoss()
    {
        try
        {
            var combat = MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.DebugOnlyGetState();
            if (combat == null) return false;
            foreach (var creature in combat.Enemies)
            {
                if (creature == null || !creature.IsAlive) continue;
                if (creature.GetPower<BossVoidCallPower>() != null) return true;
            }
        }
        catch { /* 忽略 */ }
        return false;
    }

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        try
        {
            var owner = Owner;
            if (owner == null || !owner.IsAlive) return;
            if (owner.Monster is not CharBossMonster boss) return;
            if (Applied) return;

            var card = cardPlay?.Card;
            if (card == null) return;
            // 只算**她自己**打出的那一张（玩家手里的女妖之嚎不算）
            if (!ReferenceEquals(card.Owner?.Creature, owner)) return;

            string entry;
            try { entry = card.Id.Entry; } catch { return; }
            if (!string.Equals(entry, BansheeEntry, StringComparison.Ordinal)) return;

            var combat = owner.CombatState;
            if (combat == null) return;

            var targets = new List<Creature>();
            try
            {
                foreach (var player in Core.BossLockOn.RealPlayers(combat))
                {
                    var creature = player?.Creature;
                    if (creature == null || !creature.IsAlive) continue;
                    if (targets.Contains(creature)) continue;
                    targets.Add(creature);
                }
            }
            catch { /* 忽略 */ }
            if (targets.Count == 0) return;

            Applied = true;
            int amount = Math.Max(1, Amount);
            var ctx = choiceContext ?? new BlockingPlayerChoiceContext();
            await PowerCmd.Apply<HexPower>(ctx, targets, amount, owner, null);

            Logger.Log($"[CharBoss] 虚空之唤：她首次打出女妖之嚎 ⇒ 给 {targets.Count} 名玩家各挂 {amount} 层恶咒" +
                       "（所有卡牌获得虚无，直到她死亡）");
            try
            {
                var first = targets[0];
                Logger.Log($"[CharBoss] 虚空之唤：施加后 {first.Name} 的恶咒层数 = " +
                           $"{first.GetPowerAmount<HexPower>()}（文案替换生效={AnyAliveVoidBoss()}）");
            }
            catch { /* 忽略 */ }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 虚空之唤：结算失败（忽略）: " + e.Message);
        }
    }

    /// <summary>
    /// ★ v0.2.864：把「恶咒」（`HexPower`）补挂到玩家**现有的每一张牌**上。
    ///
    /// 为什么需要自己补：原版 `HexPower.AfterApplied` 确实会遍历 `PlayerCombatState.AllCards` 逐张
    /// `CardCmd.Afflict&lt;Hexed&gt;`，但我们在「战斗开始」就挂恶咒时玩家的战斗牌堆还没铺开 ⇒ 那次循环是空的。
    /// 这一步与 `TryModifyKeywordsInCombat` 配合（那张牌只要有 `Hexed` 就会被加上 `Ethereal`）。
    /// 只挂在**还没有任何 affliction** 的牌上（与 `HexPower.Afflict` 自己的判据一致）。
    /// </summary>
    internal async Task SweepHexAsync(string why)
    {
        try
        {
            var combat = Owner?.CombatState;
            if (combat == null) return;
            int amount = Math.Max(1, Amount);
            int hexed = 0, skipped = 0, players = 0;

            foreach (var player in Core.BossLockOn.RealPlayers(combat))
            {
                var pcs = player?.PlayerCombatState;
                if (pcs == null) continue;
                players++;
                IEnumerable<CardModel>? cards = null;
                try { cards = pcs.AllCards; } catch { cards = null; }
                if (cards == null) continue;

                foreach (var card in cards)
                {
                    if (card == null) continue;
                    try
                    {
                        if (card.Affliction != null) { skipped++; continue; }
                        await CardCmd.Afflict<Hexed>(card, (decimal)amount);
                        hexed++;
                    }
                    catch { skipped++; }
                }
            }

            _sweeps++;
            Logger.Log($"[CharBoss] 虚空之唤（{why}）：给 {players} 名玩家补挂恶咒 ⇒ 新挂 {hexed} 张、" +
                       $"本来就有 affliction 跳过 {skipped} 张（这些牌都会获得虚无）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 虚空之唤：补挂恶咒失败（忽略）: " + e.Message);
        }
    }

    /// <summary>补挂次数（每场最多补 2 次：挂上那一刻 + 玩家第一个回合开始）。</summary>
    private int _sweeps;

    /// <summary>
    /// ★ v0.2.864：恐惧模式下「战斗开始就给所有玩家恶咒」，但那一刻牌堆还没铺开 ⇒
    /// 在**玩家第一个回合开始**（牌一定就位）再补一次，保证开局手里的牌也有虚无。
    /// 正常模式（非恐惧）走的仍是「她首次打出女妖之嚎」那条，不受影响。
    /// </summary>
    public override async Task AfterSideTurnStart(CombatSide side,
                                                 IReadOnlyList<Creature> participants,
                                                 ICombatState combatState)
    {
        try
        {
            if (side != CombatSide.Player) return;
            if (!Core.DreadMode.IsActive) return;
            if (!Applied) return;
            if (_sweeps >= 2) return;
            await SweepHexAsync("玩家回合开始补一次");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 虚空之唤：回合开始补挂失败（忽略）: " + e.Message);
        }
    }

    /// <summary>
    /// 把所有真人玩家身上的「恶咒」（`HexPower`）摘掉。
    /// 她死亡时由 <c>CharBossMonster.BeforeRemovedFromRoom</c> 调（多人局里战斗还没结束就要立刻失效）。
    /// </summary>
    internal static void ClearHexFromAllPlayers(string reason)
    {
        try
        {
            var combat = MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.DebugOnlyGetState();
            if (combat == null) return;
            int removed = 0;
            foreach (var player in Core.BossLockOn.RealPlayers(combat))
            {
                var creature = player?.Creature;
                if (creature == null) continue;
                var hex = creature.GetPower<HexPower>();
                if (hex == null) continue;
                try { MegaCrit.Sts2.Core.Commands.PowerCmd.Remove(hex); removed++; } catch { /* 单只失败不影响其它 */ }
            }
            if (removed > 0)
                Logger.Log($"[CharBoss] 虚空之唤：{reason} ⇒ 已摘掉 {removed} 名玩家身上的恶咒");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 虚空之唤：清理恶咒失败（忽略）: " + e.Message);
        }
    }
}

/// <summary>
/// **只在虚无骨这场战斗里**把 `HEX_POWER.description` 换成我们自己的文案。
///
/// 为什么不能直接改本地化文件（用户 2026-11-09 特别强调）：
/// 那条文案是原版给**幽灵骑士**（原版怪 `SPECTRAL_KNIGHT`）用的，写死了「幽灵骑士存活时……」。
/// 如果我们在 pck 里全局覆盖这个 key，玩家去打幽灵骑士时就会看到「亡灵契约师存活时……」。
/// ⇒ 改成**运行时按战斗替换**：场上还有活着的虚无骨时返回我们的文案，否则原样返回。
/// </summary>
[HarmonyLib.HarmonyPatch(typeof(MegaCrit.Sts2.Core.Localization.LocString),
                          nameof(MegaCrit.Sts2.Core.Localization.LocString.GetFormattedText))]
public static class VoidHexTextPatch
{
    private static int _logs;

    [HarmonyLib.HarmonyPostfix]
    public static void Postfix(MegaCrit.Sts2.Core.Localization.LocString __instance, ref string __result)
    {
        try
        {
            // 先比表名（最便宜），再比 key —— 这个方法超级热，别做多余的活
            if (__instance == null) return;
            if (!string.Equals(__instance.LocTable, "powers", StringComparison.Ordinal)) return;
            if (!string.Equals(__instance.LocEntryKey, "HEX_POWER.description", StringComparison.Ordinal)) return;
            if (!BossVoidCallPower.AnyAliveVoidBoss()) return;      // 幽灵骑士那边保持原版

            var mine = new MegaCrit.Sts2.Core.Localization.LocString(
                "powers", "BOSS_VOID_CALL_POWER.hexText");
            string text = null;
            try { text = mine.Exists() ? mine.GetFormattedText() : null; } catch { text = null; }
            if (string.IsNullOrWhiteSpace(text)) text = "亡灵契约师存活时，你的所有卡牌都拥有[gold]虚无[/gold]。";

            __result = text;
            if (_logs++ < 5)
                Logger.Log("[CharBoss] 虚空之唤：本场把「恶咒」的描述替换成虚无骨版本（幽灵骑士那边仍是原版）");
        }
        catch { /* 换不掉就保持原版文案 */ }
    }
}
