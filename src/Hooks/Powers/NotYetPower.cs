using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace CharacterBosses.Powers;

/// <summary>
/// 亡灵契约师（第一幕 Boss）的特殊效果「**时候未到**」—— 注意它挂在 **Boss 身上**，
/// 但它管的是**玩家**的格挡与灾厄（描述里的「你」都指玩家）。
///
/// 两条效果（用户设计）：
///
/// ① **每当玩家的格挡将要因回合开始而消失时，它们将抵消等量灾厄。**
///    实现：玩家回合开始（<see cref="BeforeSideTurnStart"/>）读玩家当前格挡 B，
///    把灾厄减掉 B（不低于 0）。格挡本身照原版在回合开始被清掉，所以「消失的格挡」正好变成「抵消的灾厄」。
///    格挡有可能在钩子之前就被清掉了 —— 所以上一回合结束时记一份（<see cref="_blockCarry"/>），
///    钩子里拿不到实时格挡就用记录值。
/// ② **玩家回合将要结束时，若会因灾厄死亡，立刻用格挡（再用奥斯提生命值）抵消灾厄直到不会死。**
///    原版 <c>DoomPower</c> 在玩家回合结束时（`AfterSideTurnEnd`，side = Player）判定死亡并直接
///    <c>CreatureCmd.Kill</c>。所以这里在 <see cref="BeforeSideTurnEnd"/> 先救：
///    需要减掉的灾厄 = 灾厄 − 当前生命 + 1，格挡能付多少就抵消多少，并同步扣掉格挡。
///
/// 灾厄判定口径按原版：`DoomPower.Amount >= 当前生命` 即会死（与 `GetDoomedCreatures` 一致）。
/// </summary>
public sealed class NotYetPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    /// <summary>上一回合结束时玩家的格挡（格挡若在钩子前被清掉，用它兜底）。</summary>
    private readonly Dictionary<Creature, int> _blockCarry = new();

    /// <summary>① 玩家回合开始：格挡抵消等量灾厄。</summary>
    public override async Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side,
                                                   IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        if (side != CombatSide.Player) return;

        foreach (var player in participants)
        {
            if (player == null || !player.IsAlive) continue;
            try
            {
                int block = BlockOf(player);
                bool fromCarry = false;
                if (block <= 0)
                {
                    // ⚠ 这个暂存值必须**取一次就清掉**：以前只读不清，于是上一回合记下的格挡
                    //    会在之后每一回合被反复"白用"——玩家明明这回合一点格挡都没打，
                    //    灾厄却每回合自己往下掉（用户实测：「灾厄不知道为什么自己消失了」）。
                    block = TakeCarry(player);
                    fromCarry = block > 0;
                }
                else
                {
                    _blockCarry.Remove(player);   // 有真实格挡 → 顺手把旧暂存丢掉
                }
                if (block <= 0) continue;

                var sw1 = System.Diagnostics.Stopwatch.StartNew();
                int removed = await ReduceDoomAsync(choiceContext, player, block);
                var msDoom = sw1.ElapsedMilliseconds;
                // 抵消用的格挡必须真的消失：有「残影/壁垒」时原版不会在回合开始清格挡，
                // 不自己扣掉就变成「格挡留着 + 灾厄也减了」的双倍收益（用户实测）。
                var sw2 = System.Diagnostics.Stopwatch.StartNew();
                if (removed > 0) await LoseBlockAsync(choiceContext, player, removed);
                Logger.Log($"[CharBoss] 时候未到①: 玩家格挡 {block}" +
                           (fromCarry ? "(上回合暂存)" : string.Empty) +
                           $" → 抵消灾厄 {removed}" +
                           $"（减灾厄 {msDoom}ms / 扣格挡 {sw2.ElapsedMilliseconds}ms）");
            }
            catch (Exception e)
            {
                Logger.Log("[CharBoss] 时候未到①失败: " + e.Message);
            }
        }
    }

    /// <summary>
    /// ② 玩家回合将要结束：会因灾厄死亡时，用格挡续命。
    ///
    /// ★ 2026-11-13（恐惧模式「时候未到」，用户当轮口径）：
    ///   > 灾厄骨是**去掉**「当你的回合将要结束时，若你将要因灾厄死亡，立刻用格挡和召唤物血量抵消灾厄，
    ///   > 直到灾厄不再会使你死亡。」，让玩家的容错更强。
    ///   ⇒ 恐惧模式下**整条②关掉**：只剩①（回合开始用格挡抵消灾厄），玩家必须自己算准灾厄。
    ///   ①照常生效。
    /// </summary>
    public override async Task BeforeSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side,
                                                 IEnumerable<Creature> participants)
    {
        if (side != CombatSide.Player) return;

        // 恐惧模式：这一整条「续命」不再存在（用户要的就是容错更低）
        if (Core.DreadMode.IsActive) return;

        foreach (var player in participants)
        {
            if (player == null || !player.IsAlive) continue;
            try
            {
                int doom = DoomOf(player);
                int hp = player.CurrentHp;
                if (doom < hp) { SaveCarry(player, BlockOf(player)); continue; }   // 不会死

                var sw = System.Diagnostics.Stopwatch.StartNew();
                int need = doom - hp + 1;                 // 要让灾厄 < 生命
                if (need <= 0) { SaveCarry(player, BlockOf(player)); continue; }

                // 先用格挡，再用（Boss 的）奥斯提生命值 —— 用户设计的第二条：
                // 「立刻用格挡和奥斯提的生命值抵消灾厄直到……（格挡优先）」
                int block = BlockOf(player);
                int useBlock = Math.Min(block, need);
                int removed = 0;
                if (useBlock > 0)
                {
                    removed += await ReduceDoomAsync(choiceContext, player, useBlock);
                    await LoseBlockAsync(choiceContext, player, useBlock);
                }

                int left = need - removed;
                int usedSummons = 0;
                if (left > 0)
                {
                    // 用**玩家的召唤物**的生命继续顶（用户订正：召唤物是玩家那一侧的 ——
                    // 例如玩家自己的奥斯提，或别的模组给的召唤物，比如女王模组的火炬头）。
                    foreach (var summon in SummonsOf(player))
                    {
                        if (left <= 0) break;
                        int summonHp;
                        try { summonHp = summon.CurrentHp; } catch { continue; }
                        if (summonHp <= 0) continue;

                        int use = Math.Min(summonHp, left);
                        int offset = await ReduceDoomAsync(choiceContext, player, use);
                        if (offset > 0) await LoseHpAsync(choiceContext, summon, offset);

                        usedSummons += offset;
                        removed += offset;
                        left -= offset;
                    }
                }

                SaveCarry(player, Math.Max(0, block - useBlock));
                Logger.Log($"[CharBoss] 时候未到②: 灾厄 {doom}/生命 {hp} → 格挡 {useBlock} + 召唤物 {usedSummons} " +
                           $"共抵消 {removed}，耗时 {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception e)
            {
                Logger.Log("[CharBoss] 时候未到②失败: " + e.Message);
            }
        }
    }

    /// <summary>把玩家的灾厄减掉 <paramref name="amount"/>（不低于 0），返回实际减掉多少。</summary>
    private async Task<int> ReduceDoomAsync(PlayerChoiceContext ctx, Creature player, int amount)
    {
        int doom = DoomOf(player);
        int use = Math.Min(doom, Math.Max(0, amount));
        if (use <= 0) return 0;
        // ⚠ 不能用 PowerCmd.Apply<DoomPower>(-N)：扣到 0 时它会走完整的"移除能力"链路
        //（钩子广播 + 移除动画/等待），实测一次 ~115ms，玩家每回合都能感到卡顿。
        // 直接改层数（silent）即可；层数为 0 时灾厄不再致死，原版之后自行清理。
        // 另外这也顺带避开了「Applier 记成玩家 → 白嫖死亡之门」那个坑。
        try
        {
            var power = player.GetPower<DoomPower>();
            if (power != null)
            {
                int next = Math.Max(0, power.Amount - use);
                power.SetAmount(next, true);
                return use;
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 直接减灾厄失败，退回 Apply: " + e.Message);
        }

        await PowerCmd.Apply<DoomPower>(ctx, player, -use, Owner, null, false);
        return use;
    }

    private static int DoomOf(Creature creature)
    {
        try { return creature.GetPower<DoomPower>()?.Amount ?? 0; }
        catch { return 0; }
    }

    private static int BlockOf(Creature creature)
    {
        try { return creature.Block; }
        catch { return 0; }
    }

    private async Task LoseBlockAsync(PlayerChoiceContext ctx, Creature creature, int amount)
    {
        try { await CharacterBosses.Core.GameCompatExtra.LoseBlock(ctx, creature, amount, Owner); }
        catch (Exception e) { Logger.Log("[CharBoss] 扣格挡失败: " + e.Message); }
    }

    private void SaveCarry(Creature player, int block) => _blockCarry[player] = Math.Max(0, block);

    /// <summary>
    /// 取「上一回合结束时记下的格挡」并且**立刻清掉**（一次性）。
    /// 只读不清的话，这个值会在之后每一回合被重复使用 → 灾厄每回合自己掉（旧 bug）。
    /// </summary>
    private int TakeCarry(Creature player)
    {
        if (!_blockCarry.TryGetValue(player, out var v)) return 0;
        _blockCarry.Remove(player);
        return Math.Max(0, v);
    }

    /// <summary>
    /// **玩家侧**的召唤物（不是她的）。
    ///
    /// 用户明确：这条效果说的是「**你**将要用格挡和**召唤物**血量抵消灾厄」——
    /// 召唤物归玩家，例如玩家自己那只奥斯提、或者别的模组给的召唤物（女王模组的火炬头）。
    /// 判定：在**玩家侧**、且是被召唤出来的（<c>PetOwner != null</c> 宠物，或带原版 <c>MinionPower</c> 爪牙）。
    /// </summary>
    private List<Creature> SummonsOf(Creature player)
    {
        var result = new List<Creature>();
        try
        {
            var combat = Owner?.CombatState;
            if (combat == null || player == null) return result;

            // 玩家侧的生物：同一边（player.Side）的都算候选
            var playerSide = player.Side;
            foreach (var list in new[] { combat.Allies, combat.Enemies })
            {
                if (list == null) continue;
                foreach (var creature in list)
                {
                    if (creature == null || !creature.IsAlive) continue;
                    if (ReferenceEquals(creature, player)) continue;
                    try { if (creature.Side != playerSide) continue; } catch { continue; }
                    if (!IsSummon(creature, player)) continue;
                    if (!result.Contains(creature)) result.Add(creature);
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 找玩家的召唤物失败: " + e.Message);
        }
        return result;
    }

    private static bool IsSummon(Creature creature, Creature player)
    {
        try
        {
            if (creature.PetOwner != null) return true;                       // 宠物（奥斯提这类）
            if (creature.GetPowerAmount<MinionPower>() > 0) return true;      // 爪牙
            if (creature.Player != null && ReferenceEquals(creature.Player.Creature, player)) return true;
        }
        catch { /* 忽略 */ }
        return false;
    }

    private async Task LoseHpAsync(PlayerChoiceContext ctx, Creature creature, int amount)
    {
        try
        {
            // 没有 LoseHp：用 Unpowered 伤害（不吃力量/易伤），效果等同「付出生命」
            await CreatureCmd.Damage(ctx, creature, amount, MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered,
                                     null, null);
        }
        catch (Exception e) { Logger.Log("[CharBoss] 扣奥斯提生命失败: " + e.Message); }
    }
}
