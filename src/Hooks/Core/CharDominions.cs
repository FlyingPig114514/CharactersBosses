using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace CharacterBosses.Core;

/// <summary>
/// **恐惧模式（Dread）** 的判定出口（用户 2026-11-13：「适配恐惧模式，不影响其他模式」）。
///
/// 游戏从 0.113.0 起有 `GameMode.Dread`（界面里叫「恐惧」），配一套「支配」（`DominionModel`）：
///   · `DreadRunModel.ApplyCurrentDominion()`：取 `Act.BossEncounter ?? SecondBossEncounter` 的
///     `GetDominion()`，塞进 `RunState.Dominion`；
///   · 进入 **Boss 房**时会被 `RemoveDominionIfRoomIsBoss` 清掉 ⇒ 支配只在**本幕的普通房间**生效。
/// 我们替换了每一幕的 Boss 遭遇，所以必须自己给遭遇挂支配，否则恐惧模式里游戏会打
/// `... which doesn't have any dominion set!`（等于没有支配）。
/// </summary>
internal static class DreadMode
{
    /// <summary>「Boss 房跳过」的日志每个支配只打一行。</summary>
    private static readonly HashSet<string> _bossRoomLogged = new(StringComparer.Ordinal);

    /// <summary>当前是否处于恐惧模式。</summary>
    public static bool IsActive
    {
        get
        {
#if STS2_113
            // ⚠ 107 的 `GameMode` 里根本没有 `Dread`（恐惧模式是 0.113.0 才有的）⇒ 只在 111 构建里判定。
            try
            {
                var run = MegaCrit.Sts2.Core.Runs.RunManager.Instance?.DebugOnlyGetState();
                return run != null && run.GameMode == GameMode.Dread;
            }
            catch { return false; }
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// 这个房间是不是「**本幕的 Boss 遭遇**」——也就是游戏要求**没有支配**的房间。
    ///
    /// 反编译 `DreadRunModel.RemoveDominionIfRoomIsBoss`：
    /// <code>
    /// if (combatRoom.Encounter.RoomType != RoomType.Boss) return false;
    /// if (combatRoom.Encounter.Id == RunState.Act.BossEncounter.Id
    ///     || combatRoom.Encounter.Id == RunState.Act.SecondBossEncounter?.Id)
    /// {
    ///     Log.Info("Removing dominion because we are entering the boss encounter");
    ///     RunState.SetDominion(null);
    ///     combatRoom.CombatState.SetDominion(null);
    ///     return true;
    /// }
    /// </code>
    /// 我们这条判据与游戏**逐字同源**（同一幕的 `BossEncounter` / `SecondBossEncounter` 对比遭遇 id）。
    ///
    /// ★ v0.2.789（玩家反馈「boss 战里也触发了亡灵契约师的支配」+ 日志实证）：
    ///   以前 `DreadDominionSetPatch`（`RunState.SetDominion` 的后缀）会在游戏刚把支配清成 null
    ///   **之后立刻把它塞回去**，于是支配在 Boss 战里照样生效。现在 Boss 房一律不碰支配、也不挂 power。
    /// </summary>
    internal static bool IsActBossRoom(MegaCrit.Sts2.Core.Rooms.AbstractRoom? room)
    {
#if STS2_113
        try
        {
            if (room is not MegaCrit.Sts2.Core.Rooms.CombatRoom cr) return false;

            // ★ v0.2.873（玩家 2026-11-22 的 `mod_log (55)` 实证）：**先按房间类型判**。
            //   游戏那句 `Encounter.RoomType != RoomType.Boss` 用的就是这个字段；而只比遭遇 id 会漏 ——
            //   我们把每一幕的 Boss 遭遇都换成了自己的，`Act.BossEncounter` 那份和房间里的这份不一定是同一个 id
            //   （日志里 Boss 房 `REGENT_DEBRIS_BOSS_ENCOUNTER` 那场，三名玩家身上都挂着 `REGENT_DOMINION_POWER`，
            //    说明闸门没拦住 ⇒ 支配在 Boss 房里照样挂 power。这既是玩法 bug，也会让
            //    「玩家身上多一层 power」这类状态在两端的出现时机不一致 ⇒ 联机不同步）。
            //   Boss 类型的遭遇一律算「Boss 战」，支配本来就不该出现在这里。
            try
            {
                if (cr.Encounter?.RoomType == MegaCrit.Sts2.Core.Rooms.RoomType.Boss) return true;
            }
            catch { /* 拿不到就退回按 id 判 */ }

            var id = cr.Encounter?.Id;
            if (id == null) return false;

            var run = MegaCrit.Sts2.Core.Runs.RunManager.Instance?.DebugOnlyGetState();
            var acts = run?.Acts;
            int idx = run?.CurrentActIndex ?? -1;
            if (acts == null || idx < 0 || idx >= acts.Count) return false;

            var act = acts[idx];
            if (act == null) return false;

            var first = act.BossEncounter?.Id;
            var second = act.SecondBossEncounter?.Id;
            return (first != null && Equals(id, first)) || (second != null && Equals(id, second));
        }
        catch { return false; }
#else
        return false;
#endif
    }

    /// <summary>
    /// 「本幕 Boss 房 ⇒ 这个支配不该生效」的**统一闸门**：是 Boss 房就返回 true（调用方直接 return），
    /// 并给每个支配各打一行日志，方便核对。
    /// </summary>
    internal static bool SkipBecauseActBossRoom(MegaCrit.Sts2.Core.Rooms.AbstractRoom? room, string who)
    {
        if (!IsActBossRoom(room)) return false;
        try
        {
            if (_bossRoomLogged.Add(who))
                Logger.Log($"[CharBoss] 恐惧模式·{who}：本幕 Boss 房 ⇒ 不生效（Boss 战里不该有支配）");
        }
        catch { /* 纯日志 */ }
        return true;
    }

    /// <summary>
    /// 这批生物所在战斗的**当前房间**（用于 `AfterEnemiesEnteredCombat` 这种拿不到 room 参数的钩子里判 Boss 房）。
    /// </summary>
    internal static MegaCrit.Sts2.Core.Rooms.AbstractRoom? FirstRoomOf(IEnumerable<Creature>? creatures)
    {
        try
        {
            if (creatures == null) return null;
            foreach (var c in creatures)
            {
                if (c == null) continue;
                var room = c.CombatState?.RunState?.CurrentRoom;
                if (room != null) return room;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }
}

/// <summary>玩家角色的下标（与 `BossKeys` 的 Character 字段一致）。</summary>
internal static class DreadCharacters
{
    public const int Ironclad = 0;
    public const int Silent = 1;
    public const int Defect = 2;
    public const int Necrobinder = 3;
    public const int Regent = 4;
    /// <summary>愚者（0.113 才有的角色；本模组的「塔罗愚」用它）。</summary>
    public const int Fool = 5;
    /// <summary>观者（**别的模组**的角色；本模组的「神格观」用它）。</summary>
    public const int Watcher = 6;
}

#if STS2_113
// ⚠ 恐惧模式（`Models.Dread.DominionModel`）是 0.113.0 才有的类型 ⇒ 下面五个支配类
//   只在 111 构建里存在；107（正式版）没有恐惧模式，自然也不需要。
/// <summary>
/// **铁甲战士的支配**（用户 2026-11-13）：每场战斗开始时，所有敌人获得 1 层恶魔形态。
///
/// ★ 2026-11-19（用户）：「**boss 不应该受支配影响**」⇒ **跳过我们自己的角色 Boss**
///   （`Monsters.CharBossMonster`）。她只在本幕 Boss 房上场，而 Boss 房会先把支配清掉
///   （`RemoveDominionIfRoomIsBoss`），所以这条跳过主要是防「调试/普通战斗版遭遇里她上场」
///   这种情形 —— 那时支配还在，不能把恶魔形态白送给一个有自己机制的 Boss。
///
/// ★ v0.2.793（玩家反馈 2026-11-19：「三层战士的支配里，活体盾和机枪手的组合，活体盾有恶魔形态、
///   机枪手没有」）：以前只在 `AfterRoomEntered` 那一刻给 `cr.Enemies` 里**当时已经在列表里**的敌人挂。
///   而原版 `DominionModel` 的设计是两条路：
///   <code>
///   AfterRoomEntered(room)          → AfterEnemiesEnteredCombat(所有敌人)
///   AfterCreatureAddedToCombat(cr)  → AfterEnemiesEnteredCombat(这一个)
///   </code>
///   也就是说「进房之后才加入战斗的敌人」（编队里后生成的那只、召唤物…）要走第二条。
///   我们以前没实现第二条 ⇒ 晚进列表的那只（机枪手）一条霸符都拿不到。
///   现在补上 `AfterEnemiesEnteredCombat` 这条，并且两侧都做**幂等**（身上已经有就不再叠）。
/// </summary>
public sealed class IroncladDominion : MegaCrit.Sts2.Core.Models.Dread.DominionModel
{
    public override async Task AfterRoomEntered(AbstractRoom room)
    {
        try
        {
            if (room is not CombatRoom cr) return;
            // ★ v0.2.789：本幕 Boss 房 ⇒ 这个支配不生效（游戏要求 Boss 战里没有支配）
            if (DreadMode.SkipBecauseActBossRoom(room, GetType().Name)) return;
            await GiveDemonFormAsync(cr.Enemies, "进房时");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 铁甲战士的支配失败: " + e.Message); }
    }

    /// <summary>
    /// 原版在「有新敌人加入战斗」时会派发这个（`DominionModel.AfterCreatureAddedToCombat`
    /// → `AfterEnemiesEnteredCombat`）⇒ 晚进列表的那只也能吃到恶魔形态（v0.2.793）。
    /// </summary>
    protected override async Task AfterEnemiesEnteredCombat(IEnumerable<Creature> creatures)
    {
        try
        {
            var room = DreadMode.FirstRoomOf(creatures);
            if (room != null && DreadMode.SkipBecauseActBossRoom(room, GetType().Name)) return;
            await GiveDemonFormAsync(creatures, "后加入战斗");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 铁甲战士的支配（后加入的敌人）失败: " + e.Message); }
    }

    /// <summary>把恶魔形态发给这批敌人（幂等：身上已经有就跳过；角色 Boss 跳过）。</summary>
    private static async Task GiveDemonFormAsync(IEnumerable<Creature>? enemies, string why)
    {
        try
        {
            if (enemies == null) return;

            var targets = new List<Creature>();
            int skippedBosses = 0, already = 0;
            foreach (var c in enemies)
            {
                if (c == null || !c.IsAlive) continue;
                if (c.Side != CombatSide.Enemy) continue;
                // ★ 用户 2026-11-19：Boss 不吃支配
                if (c.Monster is Monsters.CharBossMonster) { skippedBosses++; continue; }
                // 幂等：进房那条路与「后加入」那条路都覆盖到同一只时不叠成 2 层
                if (c.GetPower<MegaCrit.Sts2.Core.Models.Powers.DemonFormPower>() != null) { already++; continue; }
                targets.Add(c);
            }

            if (skippedBosses > 0)
                Logger.Log($"[CharBoss] 恐惧模式·铁甲战士的支配：跳过 {skippedBosses} 只角色 Boss（Boss 不受支配影响）");
            if (targets.Count == 0)
            {
                if (already > 0)
                    Logger.Log($"[CharBoss] 恐惧模式·铁甲战士的支配（{why}）：{already} 个敌人已有恶魔形态 ⇒ 不重复挂");
                return;
            }

            // ★★ v0.2.838（玩家 4 人恐惧模式实测截图：「战士的支配只给第一个怪加了恶魔形态」）：
            //   以前是把整个 List 一次交给 `PowerCmd.Apply<IEnumerable<Creature>>` —— 那个群体重载
            //   在「刚进场 / 血量还没初始化」的生物上会静默不挂（本项目早先也踩过「单体 / 群体两个
            //   6 参泛型重载挑错」的坑）。现在改成**逐只 apply**，每只各自 try/catch 并单独打日志，
            //   谁没挂上一眼就能看出来。
            int ok = 0, failed = 0;
            foreach (var c in targets)
            {
                if (await ApplyOneAsync(c)) ok++;
                else failed++;
            }

            Logger.Log($"[CharBoss] 恐惧模式·铁甲战士的支配（{why}）：{ok} 个敌人各获得 1 层恶魔形态" +
                       (failed > 0 ? $"（{failed} 个失败）" : string.Empty) +
                       (already > 0 ? $"（另有 {already} 个原本就有，未叠加）" : string.Empty));
        }
        catch (Exception e) { Logger.Log("[CharBoss] 铁甲战士的支配（施放恶魔形态）失败: " + e.Message); }
    }

    /// <summary>给**一只**敌人挂 1 层恶魔形态（幂等）。返回是否成功。</summary>
    private static async Task<bool> ApplyOneAsync(Creature? c)
    {
        try
        {
            if (c == null || !c.IsAlive) return false;
            if (c.Side != CombatSide.Enemy) return false;
            if (c.Monster is Monsters.CharBossMonster) return false;
            if (c.GetPower<MegaCrit.Sts2.Core.Models.Powers.DemonFormPower>() != null) return true;

            await MegaCrit.Sts2.Core.Commands.PowerCmd.Apply<MegaCrit.Sts2.Core.Models.Powers.DemonFormPower>(
                new ThrowingPlayerChoiceContext(), c, 1m, null, null);
            return c.GetPower<MegaCrit.Sts2.Core.Models.Powers.DemonFormPower>() != null;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 铁甲战士的支配：给一只敌人挂恶魔形态失败: " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// ★ v0.2.838：**后加入战斗的敌人**补挂（玩家 4 人截图：三只敌人只有第一只拿到恶魔形态）。
    ///
    /// 由 <c>Patches.IroncladDominionLateEnemyPatch</c>（`CombatManager.AfterCreatureAdded` 后缀）调用。
    /// 为什么要延迟一下：那只生物刚 add 进来时 `IsAlive` / 血量可能还没初始化，
    /// 原版 `AfterEnemiesEnteredCombat` 那一刻判定「不是活着的敌人」就跳过了，之后没人再来一次。
    /// 这里 300ms 后再看一次（仍然幂等、仍然跳过我们自己的角色 Boss）。
    /// </summary>
    internal static void EnsureForLateEnemy(Creature? creature)
    {
#if STS2_113
        try
        {
            if (creature == null) return;
            var run = MegaCrit.Sts2.Core.Runs.RunManager.Instance?.DebugOnlyGetState();
            if (run?.Dominion is not IroncladDominion) return;      // 本幕不是「铁甲战士的支配」就什么都不做
            _ = EnsureForLateEnemyAsync(creature);
        }
        catch { /* 绝不影响战斗 */ }
#endif
    }

#if STS2_113
    private static async Task EnsureForLateEnemyAsync(Creature creature)
    {
        try
        {
            if (creature.GetPower<MegaCrit.Sts2.Core.Models.Powers.DemonFormPower>() != null) return;
            await Task.Delay(300);                                  // 等它的血量/阵营初始化好
            if (!creature.IsAlive || creature.Side != CombatSide.Enemy) return;
            if (creature.GetPower<MegaCrit.Sts2.Core.Models.Powers.DemonFormPower>() != null) return;

            bool ok = await ApplyOneAsync(creature);
            Logger.Log($"[CharBoss] 恐惧模式·铁甲战士的支配：后加入战斗的敌人补挂恶魔形态 ⇒ " +
                       (ok ? "成功" : "失败（幂等检查后仍没有）"));
        }
        catch (Exception e) { Logger.Log("[CharBoss] 铁甲战士的支配（后加入补挂）失败: " + e.Message); }
    }
#endif
}

/// <summary>
/// **静默猎手的支配**（用户 2026-11-13）：每场战斗开始时，你失去 1 点力量并获得 1 层虚弱。
/// </summary>
public sealed class SilentDominion : MegaCrit.Sts2.Core.Models.Dread.DominionModel
{
    /// <summary>普通战斗给的层数（用户 2026-11-17：普通战保持 −1 力量 +1 虚弱）。</summary>
    private const int NormalStacks = 1;

    /// <summary>精英战斗给的层数（用户 2026-11-17：精英战 −2 力量 +2 虚弱）。</summary>
    private const int EliteStacks = 2;

    /// <summary>已经结算过的战斗（「房间进入」与「首个玩家回合开始」两条路只能给一次）。</summary>
    private static readonly HashSet<object> Applied = new();
    public override async Task AfterRoomEntered(AbstractRoom room)
    {
        try
        {
            if (room is not CombatRoom cr) return;
            // ★ v0.2.789：本幕 Boss 房 ⇒ 这个支配不生效（游戏要求 Boss 战里没有支配）
            if (DreadMode.SkipBecauseActBossRoom(room, GetType().Name)) return;
            await ApplyAsync(cr.CombatState, cr.RoomType == MegaCrit.Sts2.Core.Rooms.RoomType.Elite);
        }
        catch (Exception e) { Logger.Log("[CharBoss] 静默猎手的支配失败: " + e.Message); }
    }

    /// <summary>
    /// **兜底**（用户 2026-11-17：「猎人的支配在精英战中无效果」）：
    /// 精英战有时在「房间进入」那一刻 `CombatState.Players` 还没就绪（普通战就正常）
    /// ⇒ 玩家第一个回合开始时再补一次；普通战在房间进入时已经给过，这里靠 `Applied` 跳过。
    /// </summary>
    public override async Task AfterSideTurnStart(CombatSide side,
                                                 IReadOnlyList<Creature> participants,
                                                 ICombatState combatState)
    {
        try
        {
            if (side != CombatSide.Player) return;
            // ★ v0.2.789：本幕 Boss 房 ⇒ 这条兜底也不许生效（否则 Boss 战里照样 −力量 +虚弱）
            try
            {
                if (DreadMode.SkipBecauseActBossRoom(combatState?.RunState?.CurrentRoom, "静默猎手的支配（兜底）"))
                    return;
            }
            catch { /* 判不出来就按原样走 */ }
            bool elite = false;
            try { elite = combatState?.RunState?.CurrentRoom?.RoomType == MegaCrit.Sts2.Core.Rooms.RoomType.Elite; } catch { elite = false; }
            await ApplyAsync(combatState, elite);
        }
        catch (Exception e) { Logger.Log("[CharBoss] 静默猎手支配（兜底）失败: " + e.Message); }
    }

    private static async Task ApplyAsync(ICombatState? combatState, bool elite)
    {
        try
        {
            if (combatState == null) return;
            lock (Applied)
            {
                if (!Applied.Add(combatState)) return;      // 这场战斗已经给过
                if (Applied.Count > 32) Applied.Clear();    // 防御性
            }

            var players = combatState.Players;
            if (players == null) return;

            // ★ v0.2.844（用户 2026-11-21）：改成**按幕分档** —— 1/2/3 幕 = -1/-2/-3 力量 + 1/2/3 虚弱。
            //   以前按「普通战 1 / 精英战 2」分，现在用户明确要按层数，精英那套作废（参数保留只为日志）。
            int stacks = DreadActScaling.ActNumber();
            _ = elite;
            int given = 0;
            foreach (var p in players)
            {
                var creature = p?.Creature;
                if (creature == null || !creature.IsAlive) continue;
                if (creature.Monster != null) continue;      // 影子玩家 / 怪物不算
                await MegaCrit.Sts2.Core.Commands.PowerCmd.Apply<MegaCrit.Sts2.Core.Models.Powers.StrengthPower>(
                    new ThrowingPlayerChoiceContext(), creature, -(decimal)stacks, null, null);
                await MegaCrit.Sts2.Core.Commands.PowerCmd.Apply<MegaCrit.Sts2.Core.Models.Powers.WeakPower>(
                    new ThrowingPlayerChoiceContext(), creature, (decimal)stacks, null, null);
                given++;

                // ★ v0.2.652（用户：「静默猎手的支配给予的虚弱持续了 2 回合，在第三回合开始时才失去。
                //   应当第二回合开始时就失去」）：
                //   反编译 `PowerCmd.Apply` 里有一段
                //   <code>
                //   if (target.Side == CombatSide.Player
                //       &amp;&amp; combatState.CurrentSide == CombatSide.Enemy
                //       &amp;&amp; power.Type == PowerType.Debuff)
                //       power.SkipNextDurationTick = true;
                //   </code>
                //   —— 「**在敌方回合里给玩家上负面**」时，原版会跳过下一次时长结算，
                //   免得同一次敌方回合结束就把它扣掉。而我们在**房间进入**那一刻上虚弱，
                //   那时的 `CurrentSide` 还是 Enemy（玩家回合要到下一帧才开始）⇒ 被跳过一次
                //   ⇒ 虚弱多撑一整轮，到第 3 回合才掉。
                //   这里把标记清掉，让它照常在「第一次敌方回合结束」时结算：
                //   第 1 回合吃到，第 2 回合开始时就没有了（与用户口径一致）。
                try
                {
                    var weak = creature.GetPower<MegaCrit.Sts2.Core.Models.Powers.WeakPower>();
                    if (weak != null && weak.SkipNextDurationTick)
                    {
                        weak.SkipNextDurationTick = false;
                        Logger.Log("[CharBoss] 恐惧模式·静默猎手的支配：已清掉虚弱的「跳过本次结算」标记" +
                                   "（否则会多撑一整个回合）");
                    }
                }
                catch (Exception e) { Logger.Log("[CharBoss] 静默猎手的支配：清 Weak 跳过标记失败: " + e.Message); }
            }
            Logger.Log($"[CharBoss] 恐惧模式·静默猎手的支配（{(elite ? "精英战" : "普通战")}）：" +
                       $"{given} 名玩家 −{stacks} 力量、+{stacks} 虚弱");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 静默猎手的支配失败: " + e.Message); }
    }
}


/// <summary>
/// **储君的支配**（用户 2026-11-19 改口径）：**在你的回合开始时，选择 1 张非状态或诅咒牌，将其变化为「碎屑」**。
///
/// 与旧实现的区别（旧效果整套删除）：以前是给所有敌人挂「王之凝视」——
/// 敌人用攻击打你时你在本回合 −1 力量。现在改成给**所有玩家**挂一层
/// <see cref="Powers.RegentDominionPower"/>（debuff 可视载体），真正的结算在 power 里做。
///
/// 结算时机由 power 自己保证：`AfterPlayerTurnStartEarly`（早于烘焙手套 / 暴政 / 必备工具），
/// 额外回合同样触发。
/// </summary>
public sealed class RegentDominion : MegaCrit.Sts2.Core.Models.Dread.DominionModel
{
    public override async Task AfterRoomEntered(AbstractRoom room)
    {
        try
        {
            if (room is not CombatRoom cr) return;
            // ★ v0.2.789：本幕 Boss 房 ⇒ 这个支配不生效（游戏要求 Boss 战里没有支配）
            if (DreadMode.SkipBecauseActBossRoom(room, GetType().Name)) return;
            var players = cr.CombatState?.Players;
            if (players == null) return;

            var targets = new List<Creature>();
            foreach (var p in players)
            {
                var creature = p?.Creature;
                if (creature == null || !creature.IsAlive) continue;
                if (creature.Monster != null) continue;      // 影子玩家 / 怪物不算
                targets.Add(creature);
            }
            if (targets.Count == 0) return;

            await MegaCrit.Sts2.Core.Commands.PowerCmd.Apply<Powers.RegentDominionPower>(
                new ThrowingPlayerChoiceContext(), targets, 1m, null, null);
            Logger.Log($"[CharBoss] 恐惧模式·储君的支配：{targets.Count} 名玩家获得「储君的支配」" +
                       "（回合开始时选 1 张非状态/非诅咒牌变化成碎屑）");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 储君的支配失败: " + e.Message); }
    }
}

/// <summary>
/// **亡灵契约师的支配**（用户 2026-11-13 改口径）：改成给所有玩家挂一层
/// <see cref="Powers.NecrobinderDominionPower"/>（效果不变：你的回合开始时随机一张手牌本回合 +1 费）。
///
/// ⚠ 旧的 `AfterPlayerTurnStart` 实现**已删除**（效果已搬进 power，否则会打两次）。
/// </summary>
public sealed class NecrobinderDominion : MegaCrit.Sts2.Core.Models.Dread.DominionModel
{
    public override async Task AfterRoomEntered(AbstractRoom room)
    {
        try
        {
            if (room is not CombatRoom cr) return;
            // ★ v0.2.789：本幕 Boss 房 ⇒ 这个支配不生效（游戏要求 Boss 战里没有支配）
            if (DreadMode.SkipBecauseActBossRoom(room, GetType().Name)) return;
            var players = cr.CombatState?.Players;
            if (players == null) return;

            var targets = new List<Creature>();
            foreach (var p in players)
            {
                var creature = p?.Creature;
                if (creature == null || !creature.IsAlive) continue;
                if (creature.Monster != null) continue;      // 影子玩家 / 怪物不算
                targets.Add(creature);
            }
            if (targets.Count == 0) return;

            await MegaCrit.Sts2.Core.Commands.PowerCmd.Apply<Powers.NecrobinderDominionPower>(
                new ThrowingPlayerChoiceContext(), targets, 1m, null, null);
            Logger.Log($"[CharBoss] 恐惧模式·亡灵契约师的支配：{targets.Count} 名玩家获得「亡灵契约师的支配」");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 亡灵契约师的支配失败: " + e.Message); }
    }
}

/// <summary>
/// **故障机器人的支配**（用户 2026-11-19 改口径）：**所有敌人获得 1 层人工制品**。
///
/// 与旧实现的区别：
///   · 旧版是给**所有玩家**挂一层 <see cref="Powers.DefectDominionPower"/>（回合结束按敌人数挨伤害）；
///     ⇒ 现在**不再给玩家上「故障机器人的支配」的 debuff**（那层 power 类型保留，只为了旧存档能反序列化）。
///   · 新版与**铁甲战士的支配**同一套时机：进战斗房间给 `cr.Enemies`、之后加入战斗的敌人由
///     `AfterEnemiesEnteredCombat` 补发（用户 2026-11-19 选的口径），两条路都做幂等。
/// </summary>
public sealed class DefectDominion : MegaCrit.Sts2.Core.Models.Dread.DominionModel
{
    public override async Task AfterRoomEntered(AbstractRoom room)
    {
        try
        {
            if (room is not CombatRoom cr) return;
            // ★ v0.2.789：本幕 Boss 房 ⇒ 这个支配不生效（游戏要求 Boss 战里没有支配）
            if (DreadMode.SkipBecauseActBossRoom(room, GetType().Name)) return;
            // ★ v0.2.862（用户 2026-11-21）：**改回开局立刻给** —— 人工制品必须在玩家那些
            //   「开局给敌人上负面」的遗物（红面具等）生效**之前**就在场，否则它挡不住。
            //   怪物自带负面那一侧改由 `ArtifactIgnoresMonsterDebuffPatch` 按施加者判定放行，不再靠拖时间。
            var enemies = new List<Creature>();
            foreach (var c in cr.Enemies) if (c != null) enemies.Add(c);
            if (enemies.Count == 0) return;
            await GiveArtifactAsync(enemies, "进房时");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 故障机器人的支配失败: " + e.Message); }
    }

    /// <summary>进房之后才加入战斗的敌人也吃人工制品（与铁甲战士/储君那两条同一个洞）。</summary>
    protected override async Task AfterEnemiesEnteredCombat(IEnumerable<Creature> creatures)
    {
        try
        {
            var room = DreadMode.FirstRoomOf(creatures);
            if (room != null && DreadMode.SkipBecauseActBossRoom(room, GetType().Name)) return;
            await GiveArtifactAsync(creatures, "后加入战斗");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 故障机器人的支配（后加入的敌人）失败: " + e.Message); }
    }

    /// <summary>
    /// 给这批敌人补到**本幕目标层数**的人工制品。
    ///
    /// ★ v0.2.855（用户 2026-11-21：「2 层时故障机器人的支配有问题，有的敌人 1 层人工制品有的敌人 2 层」）：
    ///   以前是「身上**有**人工制品就整只跳过」⇒ 那只敌人如果自己本来就带 1 层（或上一批只发了 1 层），
    ///   它就永远停在 1 层，而同一场里其它敌人是 2 层 —— 视觉上就是「有的 1 层有的 2 层」。
    ///   现在改成**补到目标**：已有层数 < 本幕目标 ⇒ 补齐差额；已达到或更高 ⇒ 不动。
    ///   目标 = `DreadActScaling.ActNumber()`（1/2/3 幕 = 1/2/3 层，v0.2.844 口径）。
    ///   同时把**逐只明细**打进日志（`名字=层数`），下次再出现「有的 1 有的 2」能一眼看出是哪只、为什么。
    /// </summary>
    /// <summary>
    /// ★ v0.2.861：这一场**我们**给每只敌人发过多少层人工制品（幂等闸门）。
    /// 用它而不是「看它身上有几层」：啃咬机这类**自带 2 层**的敌人本来就 ≥ 目标，
    /// 用「已有层数」判幂等会让它永远拿不到支配那 2 层（用户 2026-11-21 报的正是这个）。
    /// </summary>
    private static readonly Dictionary<Creature, int> _grantedArtifact = new();

    /// <summary>★ v0.2.861：发放完那一刻它**应该**有多少层（自带 + 支配），兜底回读时按这个补。</summary>
    private static readonly Dictionary<Creature, int> _expectedArtifactTotal = new();

    private static async Task GiveArtifactAsync(IEnumerable<Creature>? enemies, string why,
                                                bool deferUntilCombatStart = false)
    {
        try
        {
            if (enemies == null) return;

            // 先把敌人名单**拷下来**（晚点再发的时候，房间里的集合可能已经变了）
            var all = new List<Creature>();
            foreach (var c in enemies) if (c != null) all.Add(c);
            if (all.Count == 0) return;

            // ★ v0.2.860（用户 2026-11-21：「阻止人工制品挡掉怪物自带的 debuff…或者应该这么说：
            //   在它获得自己的所有 buff 与 debuff 后再给予它们人工制品」）
            //
            //   `DominionModel.AfterRoomEntered` 跑在 `CombatManager.StartCombat` **之前**
            //   （日志实证：支配那行后面紧跟着 `UpdateTrack…战斗开始时` 与 `UI 会话已重置（新的战斗房 …）`）⇒
            //   敌人**自己开战期**的 buff/debuff 是在我们发完人工制品之后才落地的，
            //   于是人工制品替它挡掉了它本该吃的那层负面并 −1：
            //   `落地: 盛碗虫（石）=2` + `人工制品抵消：盛碗虫（石） 挡掉了「IMBALANCED_POWER」⇒ 剩 1 层`。
            //   ⇒ 进房这条路改成**等战斗真正开始、再留 600ms 让开战期能力结算完**才发（之后还要两轮兜底回读）。
            //   ⚠ 这一步**不能**在 `AfterRoomEntered` 里被 await（那会在 StartCombat 之前死等）⇒
            //     调用方改成**不 await**（fire-and-forget），见 `DefectDominion.AfterRoomEntered`。
            if (deferUntilCombatStart)
            {
                for (int wait = 0; wait < 40; wait++)
                {
                    bool inProgress = false;
                    try { inProgress = MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.IsInProgress ?? false; }
                    catch { inProgress = false; }
                    if (inProgress) break;
                    try { await Task.Delay(100); } catch { break; }
                }
                try { await Task.Delay(600); } catch { /* 忽略 */ }
            }

            // ★ v0.2.844：按幕分档 —— 1/2/3 幕 = 1/2/3 层人工制品（先算一次，别在循环里反复取）
            int amount = DreadActScaling.ActNumber();

            // ★ v0.2.857（用户 2026-11-21：「多人层数增加正是我想要的，保持按原版规则增加层数」）：
            //   期望值 = 本幕层数 + (人数 - 1)，这正是 `ArtifactPower.GetScaledAmountForMultiplayer`
            //   （`amount + users.Count - 1`）在 `PowerCmd.Apply` 里给出的结果，不额外动手。
            int players = 1;
            try
            {
                var cs = all[0].CombatState;
                if (cs?.Players != null && cs.Players.Count > 0) players = cs.Players.Count;
            }
            catch { players = 1; }
            int expected = amount + Math.Max(0, players - 1);

            var targets = new List<Creature>();
            var detail = new List<string>();
            int skippedBosses = 0, already = 0;
            var grants = new List<(Creature c, int delta, int have)>();
            foreach (var c in all)
            {
                if (!c.IsAlive) continue;
                if (c.Side != CombatSide.Enemy) continue;
                // ★ 用户 2026-11-19：Boss 不吃支配
                if (c.Monster is Monsters.CharBossMonster) { skippedBosses++; continue; }

                int have = 0;
                try
                {
                    have = (int)Math.Round((double)c.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.ArtifactPower>());
                }
                catch
                {
                    try { have = c.GetPower<MegaCrit.Sts2.Core.Models.Powers.ArtifactPower>() != null ? 1 : 0; }
                    catch { have = 0; }
                }
                string who = "?";
                try { who = c.Name ?? "?"; } catch { /* 忽略 */ }

                // ★ v0.2.861（用户 2026-11-21：「自身就有 2 层人工的啃咬机不会额外获得人工（加上支配应该是 4 层）」）：
                //   支配是**额外追加**，不是「补到目标」—— 敌人自带的层数原样保留（啃咬机自带 2 + 支配 2 = 4）。
                //   以前拿「已有层数 ≥ 目标就跳过」当幂等闸门 ⇒ 自带 2 层的敌人一层都拿不到。
                //   现在幂等改成**按「我们这一场给它发过多少」记账**（`_grantedArtifact`）：
                //   同一只敌人被「进房」和「后加入」两条路、以及后面的兜底回读反复碰到也只发一次。
                // ★ v0.2.871（用户 2026-11-21：「4 人联机、2 层故障机器人的支配，但所有敌人都有 8 层人工制品
                //   （不算本来就有人工制品的怪），你这直接 2*4 了」）：
                //   **双重计入**：我们自己按「本幕层数 + (人数−1)」算了一个 `expected` 当发放量，
                //   而 `PowerCmd.Apply` 里原版还会再按 `ArtifactPower.GetScaledAmountForMultiplayer`
                //   （`amount + 人数 − 1`）加一次 ⇒ 4 人局 = 2 + 3 + 3 = **8**。
                //   ⇒ 现在**只把「本幕基础层数」交给原版**（2 层），缩放全部由 `PowerCmd.Apply` 负责；
                //   记账 `_grantedArtifact` 也改记「我们发了多少基础层数」，因此幂等判据随之改成 `amount`。
                int handed = 0;
                try { handed = _grantedArtifact.TryGetValue(c, out int g) ? g : 0; } catch { handed = 0; }
                int delta = amount - handed;
                if (delta <= 0)
                {
                    already++;
                    detail.Add($"{who}=自带 {have}、已由支配发过 {handed} ⇒ 不重复");
                    continue;
                }

                targets.Add(c);
                grants.Add((c, delta, have));
                detail.Add($"{who}=自带 {have} + 支配基础 {delta}" +
                           (players > 1 ? $"（多人 {players} 人 ⇒ 原版再 +{players - 1} ⇒ 期望 {have + expected}）" : $"（期望 {have + expected}）"));
            }

            if (skippedBosses > 0)
                Logger.Log($"[CharBoss] 恐惧模式·故障机器人的支配：跳过 {skippedBosses} 只角色 Boss（Boss 不受支配影响）");
            if (targets.Count == 0)
            {
                if (already > 0)
                    Logger.Log($"[CharBoss] 恐惧模式·故障机器人的支配（{why}）：目标 {expected} 层，" +
                               $"{already} 个敌人已由支配发过 ⇒ 不重复挂 | 明细: {string.Join(", ", detail)}");
                return;
            }

            // 逐只发放（`PowerCmd.Apply` 是按「只」结算的，避免一次性给一组时只喂到第一只）
            foreach (var (c, delta, have) in grants)
            {
                try
                {
                    await MegaCrit.Sts2.Core.Commands.PowerCmd.Apply<
                        MegaCrit.Sts2.Core.Models.Powers.ArtifactPower>(
                        new ThrowingPlayerChoiceContext(), c, (decimal)delta, null, null);
                    try { _grantedArtifact[c] = amount; } catch { /* 忽略 */ }
                    try { _expectedArtifactTotal[c] = have + expected; } catch { /* 忽略 */ }
                }
                catch (Exception e)
                {
                    Logger.Log($"[CharBoss] 故障机器人的支配：给 {(c.Name ?? "?")} 发人工制品失败: " + e.Message);
                }
            }

            // ★ v0.2.862：**不再做「补到期望」**。
            //   以前（v0.2.857~861）怕「发完被削」，所以回读不足就 `SetAmount` 补齐 ——
            //   但那等于**把玩家施加的负面又退回去**（红面具的虚弱本该被人工制品挡掉一层）。
            //   现在「怪物自身的负面不吃人工制品」改由 `ArtifactIgnoresMonsterDebuffPatch` 按**施加者**判定，
            //   所以这里只回读一份落地值写日志，一个 `SetAmount` 都不做。
            var landed = new List<string>();
            foreach (var c in targets)
            {
                string who = "?";
                try { who = c.Name ?? "?"; } catch { /* 忽略 */ }
                try
                {
                    if (c == null || !c.IsAlive) { landed.Add($"{who}=<已死/离场>"); continue; }
                    var power = c.GetPower<MegaCrit.Sts2.Core.Models.Powers.ArtifactPower>();
                    if (power == null) { landed.Add($"{who}=<没有人工制品>"); continue; }
                    landed.Add($"{who}={(int)Math.Round((double)power.Amount)}");
                }
                catch (Exception e) { landed.Add($"{who}=<读不到: {e.Message}>"); }
            }

            Logger.Log($"[CharBoss] 恐惧模式·故障机器人的支配（{why}）：本幕 {amount} 层" +
                       (players > 1 ? $"（多人 {players} 人 ⇒ 原版缩放 +{players - 1} ⇒ 支配发 {expected} 层）" : string.Empty) +
                       $"，{targets.Count} 个敌人已发（**叠加**在它们自带的层数之上）" +
                       (already > 0 ? $"（另有 {already} 个已由支配发过，不重复）" : string.Empty) +
                       $" | 明细: {string.Join(", ", detail)}" +
                       $" | 落地: {string.Join(", ", landed)}" +
                       $" | 各自 power: {string.Join(" ; ", DigestPowers(targets))}");

            // 记账表别无限长（每场战斗的 Creature 都是新对象）
            try
            {
                if (_grantedArtifact.Count > 400)
                {
                    _grantedArtifact.Clear();
                    _expectedArtifactTotal.Clear();
                }
            }
            catch { /* 忽略 */ }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 故障机器人的支配（施放人工制品）失败: " + e.Message); }
    }

    /// <summary>把一批敌人身上的 power 打成一串 `ID=层数`（诊断用，只读）。</summary>
    private static List<string> DigestPowers(List<Creature> creatures)
    {
        var list = new List<string>();
        foreach (var c in creatures)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                string who = "?";
                try { who = c.Name ?? "?"; } catch { /* 忽略 */ }
                foreach (var p in c.Powers)
                {
                    if (p == null) continue;
                    string id = "?";
                    int amt = 0;
                    try { id = p.Id.Entry; } catch { /* 忽略 */ }
                    try { amt = (int)Math.Round((double)p.Amount); } catch { /* 忽略 */ }
                    if (sb.Length > 0) sb.Append(',');
                    sb.Append(id).Append('=').Append(amt);
                }
                list.Add($"{who}[{(sb.Length == 0 ? "无" : sb.ToString())}]");
            }
            catch (Exception e) { list.Add("?" + "[" + e.Message + "]"); }
        }
        return list;
    }
}

/// <summary>
/// **愚者的支配**（用户 2026-11-13 新增；目前还没有「愚者」的 Boss，先把类型写好）。
/// 效果：**每场战斗开始时，将 2 张「悲伤」加入你的抽牌堆**。
/// 同样只负责「进战斗房间时给所有玩家挂一层 <see cref="Powers.FoolDominionPower"/>」，
/// 真正的发牌在 power 里做。
/// </summary>
public sealed class FoolDominion : MegaCrit.Sts2.Core.Models.Dread.DominionModel
{
    public override async Task AfterRoomEntered(AbstractRoom room)
    {
        try
        {
            if (room is not CombatRoom cr) return;
            // ★ v0.2.789：本幕 Boss 房 ⇒ 这个支配不生效（游戏要求 Boss 战里没有支配）
            if (DreadMode.SkipBecauseActBossRoom(room, GetType().Name)) return;
            var players = cr.CombatState?.Players;
            if (players == null) return;

            var targets = new List<Creature>();
            foreach (var p in players)
            {
                var creature = p?.Creature;
                if (creature == null || !creature.IsAlive) continue;
                if (creature.Monster != null) continue;      // 影子玩家 / 怪物不算
                targets.Add(creature);
            }
            if (targets.Count == 0) return;

            await MegaCrit.Sts2.Core.Commands.PowerCmd.Apply<Powers.FoolDominionPower>(
                new ThrowingPlayerChoiceContext(), targets, 1m, null, null);
            Logger.Log($"[CharBoss] 恐惧模式·愚者的支配：{targets.Count} 名玩家获得「愚者的支配」");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 愚者的支配失败: " + e.Message); }
    }
}
#endif
#if STS2_113

/// <summary>
/// **观者的支配**（用户 2026-11-15；2026-11-17 增强 2 → 3 层；2026-11-19 **回调成 2 层**）：
/// 恐惧模式下「**所有敌人**获得 **2** 层虔信」。
///
/// ★ v0.2.781（用户 2026-11-19 截图 + 订正）：以前错在**挂给玩家**了 ——
///   > 「观者的支配有 bug，并没有成功给予敌人 3 层虔信。并且按这个的实现方式，
///   >   其实并不应该给玩家上 debuff，而是只给敌人上虔信」
///   截图实证：3 层虔信挂在**玩家**身上（玩家血条下面那个带 3 的图标），敌人（7/16 那只）
///   身上什么都没有；而且提示框显示的是原始 key（因为 powers.json 里从来没有
///   `WATCHER_FAITH_POWER.title/.description` 这两条，v0.2.781 一起补了 16 语言）。
///
/// 现在按口径改成**只给敌人**挂，而且挂的是专门给这类怪物写的
/// <see cref="Powers.WatcherDominionDevotionPower"/>（不是观者自己那层 `WatcherFaithPower`），
/// 因为用户把效果说清楚了：
///   > 「就是给敌人挂虔信，积累 10 点真言后消耗 10 点真言进神格（本回合 3 倍伤害）一回合。
///   >   这些敌人的虔信在**玩家回合开始时**触发，并且在进神格的当回合开始时不会退出神格，
///   >   **执行完自己的意图才会退神格**……（也就是怪物第四回合、第七回合等回合会进神格，
///   >   当回合伤害翻三倍）」
/// ⇒ 2 层/回合 ⇒ 第 5 / 10 / 15 回合各进一次神格。**与神格观那条链完全隔离**，
///   她身上不会挂这个（持有者是我们自己的 `CharBossMonster` 时直接跳过）。
/// </summary>
public sealed class WatcherDevotionDominion : MegaCrit.Sts2.Core.Models.Dread.DominionModel
{
    /// <summary>
    /// 每个敌人获得的虔信层数（= 每次「玩家回合开始」它获得的真言层数）。
    ///
    /// ★ 2026-11-19（用户）：「回调观者的支配，改回给敌人 2 虔信（第 5、10、15 等回合进神格）」
    ///   ⇒ 3 → **2**。对账：2/回合 ⇒ 第 1 回合 2、第 2 回合 4、第 3 回合 6、第 4 回合 8、
    ///   第 5 回合 10（扣 10 进神格）、第 10 回合再满 10（2/回合正好 5 回合一轮）、第 15 回合……
    ///   —— 与用户说的「第 5、10、15」一致。
    /// </summary>
    private const int FaithStacks = 2;

    public override async Task AfterRoomEntered(AbstractRoom room)
    {
        try
        {
            if (room is not CombatRoom cr) return;
            // ★ v0.2.789：本幕 Boss 房 ⇒ 这个支配不生效（游戏要求 Boss 战里没有支配）
            if (DreadMode.SkipBecauseActBossRoom(room, GetType().Name)) return;
            await GiveFaithAsync(cr.Enemies, "进房时");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 观者的支配失败: " + e.Message); }
    }

    /// <summary>
    /// ★ v0.2.793：进房之后才加入战斗的敌人也要吃虔信
    /// （原版 `AfterCreatureAddedToCombat` → `AfterEnemiesEnteredCombat`；与铁甲战士/储君的支配同一个洞）。
    /// </summary>
    protected override async Task AfterEnemiesEnteredCombat(IEnumerable<Creature> creatures)
    {
        try
        {
            var room = DreadMode.FirstRoomOf(creatures);
            if (room != null && DreadMode.SkipBecauseActBossRoom(room, GetType().Name)) return;
            await GiveFaithAsync(creatures, "后加入战斗");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 观者的支配（后加入的敌人）失败: " + e.Message); }
    }

    /// <summary>给这批敌人挂「虔信」（幂等：已经有就不再叠；角色 Boss 跳过）。</summary>
    private static async Task GiveFaithAsync(IEnumerable<Creature>? enemies, string why)
    {
        try
        {
            if (enemies == null)
            {
                Logger.Log("[CharBoss] 恐惧模式·观者的支配：拿不到敌人列表 ⇒ 这次的虔信没发出去（两端会不同步）");
                return;
            }

            int given = 0;
            int skippedBosses = 0, already = 0;
            foreach (var creature in enemies)
            {
                if (creature == null || !creature.IsAlive) continue;
                if (creature.Side != CombatSide.Enemy) continue;
                // ★ 用户 2026-11-19：「boss 不应该受支配影响」⇒ 我们自己的角色 Boss（比如神格观）
                //   有**真的**真言/神格，一层虔信都不给她（她上场那场支配本来也已被游戏清掉）
                if (creature.Monster is Monsters.CharBossMonster) { skippedBosses++; continue; }
                // 幂等：同一场里已经挂过就不叠（重入/重连/两条路都覆盖 都不该变成 4 层）
                if (creature.GetPower<Powers.WatcherDominionDevotionPower>() != null) { already++; continue; }

                // ★ v0.2.781：挂到**敌人**身上（以前挂玩家 ⇒ 既不符合「所有敌人获得虔信」的口径，
                //   也给玩家挂了一个莫名其妙的 debuff 图标）。虔信的实际结算在
                //   `WatcherDominionDevotionPower`：**玩家回合开始**给这个敌人 +2 真言，
                //   满 10 层扣 10 进神格（它那一回合攻击三倍伤害，意图执行完退出）。
                await MegaCrit.Sts2.Core.Commands.PowerCmd.Apply<Powers.WatcherDominionDevotionPower>(
                    new ThrowingPlayerChoiceContext(), creature, (decimal)FaithStacks, null, null);
                given++;
            }
            if (skippedBosses > 0)
                Logger.Log($"[CharBoss] 恐惧模式·观者的支配：跳过 {skippedBosses} 只角色 Boss（Boss 不受支配影响）");
            if (given == 0 && already > 0)
            {
                Logger.Log($"[CharBoss] 恐惧模式·观者的支配（{why}）：{already} 个敌人已有虔信 ⇒ 不重复挂");
                return;
            }
            Logger.Log($"[CharBoss] 恐惧模式·观者的支配（{why}）：{given} 个敌人各获得 {FaithStacks} 层虔信" +
                       $"（玩家回合开始时它们各 +{FaithStacks} 真言，满 10 层扣 10 进神格：那一回合攻击三倍伤害）" +
                       (already > 0 ? $"（另有 {already} 个原本就有，未叠加）" : string.Empty));
        }
        catch (Exception e) { Logger.Log("[CharBoss] 观者的支配（施放虔信）失败: " + e.Message); }
    }
}

/// <summary>
/// **恐惧模式：把「本幕支配」对齐到「本幕最终 Boss 遭遇」**（2026-11-19 玩家联机日志实证）。
///
/// 现象（`godot (12).log` 14004/14005 与 14419/14420）：
/// <code>
/// [INFO] Applying dominion because act 0 entered
/// [INFO] Setting dominion to DOMINION.CEREMONIAL_BEAST_DOMINION   ← 客人：还没收到房主的结论 ⇒ 用的是**原版** Boss 的支配
/// …（不同步断线、重连之后）
/// [INFO] Applying dominion because run loaded
/// [INFO] Setting dominion to DOMINION.REGENT_DOMINION             ← 这时才对上房主（晚了）
/// </code>
/// 于是在同一场**普通**战斗里：客人那只 `SHRINKER_BEETLE` 只有原版的 `RITUAL_POWER`，
/// 房主那只多了 `MONARCHS_GAZE_POWER`（我们的「储君的支配」）⇒ 第一个
/// `After player turn start` 校验和就分叉（`Local: 864895115 / Remote: 1856624699`）。
///
/// 根因：游戏的 `DreadRunModel.ApplyCurrentDominion()` 取的是
/// **`Act.BossEncounter ?? SecondBossEncounter` 的 `GetDominion()`**，而 `BossEncounter`
/// 是**各端本地替换**的（客人要等房主广播）⇒ 广播晚到（或像旧版那样根本发不出去）时，
/// 客人这一幕拿到的是**原版 Boss 的支配**，之后整幕每一场普通战斗的敌人身上都会多/少一层 power。
///
/// 做法：在**我们改完 / 收到**本幕最终遭遇之后各调一次 —— 若 `RunState.Dominion` 与
/// 「按最终遭遇算出来的那个」类型不同，就按游戏同一个出口 `RunState.SetDominion(...)` 重设一遍。
/// 反编译确认 `SetDominion` 只是 `dominion.MutableClone()` + 赋值 `RunState`，**不触发任何钩子**
/// ⇒ 重复调用安全（本轮战斗已经发出去的 power 收不回来，但从下一场起两端就一致了）。
/// </summary>
internal static class DreadDominionSync
{
    private static string _lastLogged = string.Empty;
    private static bool _inHook;

    /// <summary>对齐一次（幂等）。<paramref name="why"/> 只用于日志。</summary>
    internal static void EnsureCurrentAct(string why)
    {
        try
        {
            if (!DreadMode.IsActive) return;
            var run = MegaCrit.Sts2.Core.Runs.RunManager.Instance?.DebugOnlyGetState();
            if (run == null || run.Acts == null) return;
            if (run.CurrentActIndex < 0 || run.CurrentActIndex >= run.Acts.Count) return;

            var act = run.Acts[run.CurrentActIndex];
            if (act == null) return;

            // ★ v0.2.789（玩家反馈「boss 战里也触发了亡灵契约师的支配」）：
            //   日志实证 `本幕支配 无 → NECROBINDER_DOMINION（游戏设完支配后立刻核对）`，
            //   紧跟着 boss 战里就出现「亡灵契约师的支配…本回合费用 +1」。
            //   也就是游戏刚在进 Boss 房时把支配清成 null（`RemoveDominionIfRoomIsBoss`），
            //   我们这条后缀又把它塞了回去 ⇒ **Boss 房一律不碰**（游戏要求 Boss 战里没有支配）。
            if (DreadMode.IsActBossRoom(run.CurrentRoom) || DreadMode.IsActBossRoom(run.BaseRoom))
            {
                if (_lastLogged != "boss-room")
                {
                    _lastLogged = "boss-room";
                    Logger.Log("[CharBoss] 恐惧模式·支配对齐：本幕 Boss 房 ⇒ 不动支配" +
                               "（游戏要求 Boss 战里没有支配；" + why + "）");
                }
                return;
            }

            var want = ResolveWant(act, out string source);
            if (want == null)
            {
                // ★ v0.2.780：**客人拿不到房主结论时什么都不做**（宁可等，也不要用本机还没同步的槽位去猜）。
                //   以前这里会退回本机槽位 ⇒ 拿到原版 Boss 的支配 ⇒ 与房主不同 ⇒ 整幕普通战斗不同步。
                if (source != _lastLogged)
                {
                    _lastLogged = source;
                    Logger.Log($"[CharBoss] 恐惧模式·支配对齐：**先不动**（{source}；{why}）" +
                               "—— 等房主结论到了再对齐，避免拿原版 Boss 的支配去和房主对齐");
                }
                return;
            }

            var have = run.Dominion;
            if (have != null && have.GetType() == want.GetType()) return;   // 已经一致

            run.SetDominion(want);
            string line = $"{EntryOf(have)} → {EntryOf(want)}（{why}；来源={source}）";
            if (line != _lastLogged)
            {
                _lastLogged = line;
                Logger.Log($"[CharBoss] 恐惧模式·支配对齐：本幕支配 {line}" +
                           "（否则两端普通战斗里敌人身上会多/少一层 power ⇒ 联机不同步）");
            }

            // ★★ v0.2.803：`RunState` 对齐了但 `CombatState` 里那份还是旧的（房主结论晚于进房间那一刻的拷贝）
            //   ⇒ 战斗里挂 power 用的是 CombatState 那一份，客人会整场吃不到支配。这里顺手补一次。
            try { AlignCombatState(run.CurrentRoom, "本幕支配对齐后同步战斗状态"); } catch { /* 忽略 */ }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 恐惧模式·支配对齐失败: " + e.Message); }
    }

    /// <summary>
    /// 本幕该用哪个支配。
    ///
    /// · **房主 / 单人**：按本机本幕的 Boss 槽位（那上面就是我们刚替换进去的遭遇），与游戏的
    ///   `DreadRunModel.ApplyCurrentDominion()`（`act.SecondBossEncounter ?? act.BossEncounter`）同一口径。
    /// · **客人**：**只认房主下发的「每场最终遭遇」**（`BossPicksSync.HostSaysVanilla / HostBossOf`）。
    ///   本机槽位这时可能还是原版 Boss（房主的数据还没落到本机）⇒ 拿它解析就会得到**原版 Boss 的支配**
    ///   （玩家日志实证：客人是 `CEREMONIAL_BEAST_DOMINION` → 仪式兽的 `RITUAL_POWER`，
    ///   房主是 `REGENT_DOMINION` → `MONARCHS_GAZE_POWER`，同一个甲虫两个 power ⇒ ID 1 就分叉）。
    ///   房主结论还没到 ⇒ 返回 null（= 等，不猜）。
    /// </summary>
    private static MegaCrit.Sts2.Core.Models.Dread.DominionModel? ResolveWant(
        MegaCrit.Sts2.Core.Models.ActModel act, out string source)
    {
        source = "本机槽位";
        var localSlot = act.SecondBossEncounter ?? act.BossEncounter;

        bool guest;
        try { guest = Core.NetRole.IsGuest; } catch { guest = false; }
        if (!guest) return localSlot?.GetDominion();

        // 客人：第 2 场优先（与游戏 `Second ?? First` 同序）
        for (int fight = 1; fight >= 0; fight--)
        {
            bool? vanilla;
            try { vanilla = Core.BossPicksSync.HostSaysVanilla(act.Index, fight); } catch { vanilla = null; }
            if (vanilla == null) continue;                       // 这一场房主还没算出来
            if (vanilla == true)
            {
                source = $"房主结论(第 {fight + 1} 场=保持原版)";
                return localSlot?.GetDominion();                  // 房主也没换 ⇒ 用原版那只的支配
            }
            string? key;
            try { key = Core.BossPicksSync.HostBossOf(act.Index, fight); } catch { key = null; }
            if (string.IsNullOrEmpty(key)) continue;
            MegaCrit.Sts2.Core.Models.EncounterModel? enc = null;
            try { enc = Core.BossKeys.EncounterOf(key!); } catch { enc = null; }
            if (enc == null) continue;
            source = $"房主结论(第 {fight + 1} 场={key})";
            return enc.GetDominion();
        }

        source = "房主的「每场最终遭遇」还没收到";
        return null;
    }

    /// <summary>
    /// 游戏自己设完支配（`RunState.SetDominion`，Boss 房/进幕/读档都会走）之后立刻纠正一次 ——
    /// 这样**第一场普通战斗开始之前**两端就已经是同一个支配（支配的 power 是在房间进入时挂的，
    /// 等到第一个 `After player turn start` 校验和才发现就晚了）。
    /// </summary>
    internal static void AfterGameSetDominion()
    {
        if (_inHook) return;          // 防重入（我们自己在 EnsureCurrentAct 里也会调 SetDominion）
        _inHook = true;
        try { EnsureCurrentAct("游戏设完支配后立刻核对"); }
        catch { /* 忽略 */ }
        finally { _inHook = false; }
    }

    private static string EntryOf(MegaCrit.Sts2.Core.Models.Dread.DominionModel? d)
    {
        try { return d?.Id?.Entry ?? "无"; } catch { return "?"; }
    }

    /// <summary>
    /// ★★ v0.2.803（玩家反馈「恐惧模式，联机，好友吃不到支配效果而导致不同步」）：
    ///   反编译 `DreadRunModel.BeforeRoomEntered` 是**支配进战斗状态的唯一一处**：
    ///     · 普通战斗房：`combatRoom.CombatState.SetDominion(base.RunState.Dominion)`；
    ///     · Boss 房：`RunState.SetDominion(null)` + `CombatState.SetDominion(null)`。
    ///   而战斗里派发 `AfterEnemiesEnteredCombat` / `AfterPlayersEnteredCombat`（挂 power 的那两个钩子）
    ///   用的是 **CombatState 上那一份**，不是 `RunState` 那一份。
    ///   客人的「本幕支配以房主为准」经常**晚于**这次拷贝（房主结论还没到）⇒ 我们事后只对齐了
    ///   `RunState.Dominion`，`CombatState.Dominion` 还是旧的 / 空的 ⇒ **客人这一场根本没有支配效果**
    ///   （＝「好友吃不到支配效果」），房主那边有 ⇒ 第一个回合开始校验和就分叉。
    ///   所以在拷贝之后补一次：两者类型不一致就把 CombatState 也改成一致的（此时战斗还没开始，power 会正常挂上）。
    /// </summary>
    internal static void AlignCombatState(AbstractRoom? room, string why)
    {
        try
        {
            if (!DreadMode.IsActive) return;
            var run = MegaCrit.Sts2.Core.Runs.RunManager.Instance?.DebugOnlyGetState();
            if (run == null) return;

            var combat = (room as MegaCrit.Sts2.Core.Rooms.CombatRoom)?.CombatState;
            if (combat == null) return;

            var want = run.Dominion;
            var have = combat.Dominion;
            if (want == null && have == null) return;
            if (want != null && have != null && want.GetType() == have.GetType()) return;

            combat.SetDominion(want);
            Logger.Log($"[CharBoss] 恐惧模式·支配对齐：**战斗状态**里的支配 {EntryOf(have)} → {EntryOf(want)}（{why}）" +
                       "—— 战斗里挂 power 用的是 CombatState 那一份，以前只改 RunState ⇒ 客人整场吃不到支配、与房主不同步");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 恐惧模式·支配对齐（战斗状态）失败: " + e.Message); }
    }
}

/// <summary>
/// ★★ v0.2.803：在 `DreadRunModel.BeforeRoomEntered` **前后各挂一次**（见 <see cref="DreadDominionSync.AlignCombatState"/>）。
///   · 前缀：先把 `RunState.Dominion` 对齐到房主那一套 ⇒ 游戏随后拷进 CombatState 的就是正确的；
///   · 后缀：再核对 CombatState —— 房主结论是「拷贝之后」才到的那些场次也补上。
/// ⚠ 不要只保留后缀：`AfterEnemiesEnteredCombat` 是在战斗开始时派的，越早对齐越安全。
/// </summary>
[HarmonyLib.HarmonyPatch(typeof(MegaCrit.Sts2.Core.Models.Dread.DreadRunModel),
                          nameof(MegaCrit.Sts2.Core.Models.Dread.DreadRunModel.BeforeRoomEntered),
                          new[] { typeof(AbstractRoom) })]
internal static class DreadDominionBeforeRoomPatch
{
    [HarmonyLib.HarmonyPrefix]
    public static void Prefix()
    {
        try { DreadDominionSync.EnsureCurrentAct("进房间之前"); } catch { /* 绝不影响主流程 */ }
    }

    [HarmonyLib.HarmonyPostfix]
    public static void Postfix(AbstractRoom room)
    {
        try
        {
            // ⚠ 顺序很重要：原版方法体里会先 `ApplyCurrentDominion()`（按**本机**槽位重设 RunState.Dominion），
            //   所以前缀那次对齐会被它盖掉 ⇒ 这里必须**先重新对齐 RunState，再把 CombatState 同步过去**。
            DreadDominionSync.EnsureCurrentAct("进房间之后（游戏已按本机槽位设过支配）");
            DreadDominionSync.AlignCombatState(room, "进房间之后核对战斗状态");
        }
        catch { /* 绝不影响主流程 */ }
    }
}

/// <summary>
/// ★ v0.2.780：`RunState.SetDominion` 是游戏设置「本幕支配」的**唯一出口**
/// （`DreadRunModel.ApplyCurrentDominion()` / 读档，反编译确认它只是 `MutableClone()` + 赋值、不触发钩子）。
/// 在这里挂一个后缀，客人端一旦已经有房主结论就立刻把支配改成房主那一套 ——
/// 早于「本幕第一场战斗开始」⇒ 敌人身上的 power 两端一致。
/// </summary>
[HarmonyLib.HarmonyPatch(typeof(MegaCrit.Sts2.Core.Runs.RunState), nameof(MegaCrit.Sts2.Core.Runs.RunState.SetDominion))]
internal static class DreadDominionSetPatch
{
    [HarmonyLib.HarmonyPostfix]
    public static void Postfix(object[] __args)
    {
        try
        {
            // ★ v0.2.789：游戏把支配设成 **null** 的唯一场合就是「正在进本幕 Boss 遭遇」
            //   （`RemoveDominionIfRoomIsBoss`）⇒ 尊重它，**绝不**再塞回去。
            //   以前这里不看实参，于是刚被清掉就立刻被改回本幕的支配 ⇒ Boss 战里支配照样生效
            //   （玩家反馈 + 日志实证：`本幕支配 无 → NECROBINDER_DOMINION（游戏设完支配后立刻核对）`）。
            if (__args != null && __args.Length > 0 && __args[0] == null) return;

            DreadDominionSync.AfterGameSetDominion();
        }
        catch { /* 绝不影响主流程 */ }
    }
}
#endif

/// <summary>
/// ★ v0.2.862（用户 2026-11-21：「能不能让它既不会抵消怪物本来就有的 debuff 也不会让它挡不住玩家开局施加
/// debuff 的遗物？」）
///
/// 反编译查清了两种来源的差别：
/// <code>
/// // 盛碗虫（石）：失衡是**它自己**给自己上的
/// BowlbugRock.AfterAddedToRoom:
///     PowerCmd.Apply&lt;ImbalancedPower&gt;(ctx, bowlbugRock.Creature, 1m, bowlbugRock.Creature, null);
///                        //                        ^target            ^applier = 它自己
///
/// // 红面具：虚弱是**玩家**上的
/// RedMask:
///     PowerCmd.Apply&lt;WeakPower&gt;(ctx, combatState.HittableEnemies, value, redMask.Owner.Creature, null);
///                        //                                        ^applier = 玩家
/// </code>
/// 而 `ArtifactPower.TryModifyPowerAmountReceived` 会把「负面」整条吞掉（`modifiedAmount = 0` → 返回 true），
/// 随后 `AfterModifyingPowerAmountReceived` 里 `PowerCmd.Decrement` 扣掉一层。
///
/// ⇒ 这里加一道前缀：**目标是敌人、且施加者不是玩家**（怪物自己 / 另一个怪物 / 无来源）时，
///   直接跳过人工制品的「吞掉」逻辑 —— 这层负面照常真正生效，人工制品一层都不掉。
///   玩家（遗物 / 牌 / 药水）施加的负面**照旧被人工制品挡掉**（红面具依然会因为人工制品而失效）。
/// </summary>
[HarmonyLib.HarmonyPatch(typeof(MegaCrit.Sts2.Core.Models.Powers.ArtifactPower),
                          nameof(MegaCrit.Sts2.Core.Models.Powers.ArtifactPower.TryModifyPowerAmountReceived))]
internal static class ArtifactIgnoresMonsterDebuffPatch
{
    private static int _logs;

    [HarmonyLib.HarmonyPrefix]
    public static bool Prefix(MegaCrit.Sts2.Core.Models.Powers.ArtifactPower __instance,
                              MegaCrit.Sts2.Core.Models.PowerModel canonicalPower,
                              MegaCrit.Sts2.Core.Entities.Creatures.Creature target,
                              decimal amount,
                              MegaCrit.Sts2.Core.Entities.Creatures.Creature? _,
                              ref decimal modifiedAmount,
                              ref bool __result)
    {
        try
        {
            if (target == null) return true;
            // 只管敌人身上的（玩家自己的人工制品行为不变）
            if (target.Side != MegaCrit.Sts2.Core.Combat.CombatSide.Enemy) return true;

            bool fromPlayer = false;
            try { fromPlayer = _ != null && _.Side == MegaCrit.Sts2.Core.Combat.CombatSide.Player; }
            catch { fromPlayer = false; }
            if (fromPlayer) return true;      // 玩家的遗物/牌/药水 ⇒ 照原版被挡

            // 怪物自己/怪物之间/无来源 ⇒ 不吃这一层
            modifiedAmount = amount;
            __result = false;
            if (_logs++ < 40)
            {
                string who = "?", what = "?", by = "(无来源)";
                int amt = 0;
                try { who = target.Name ?? "?"; } catch { /* 忽略 */ }
                try { what = canonicalPower?.Id.Entry ?? "?"; } catch { /* 忽略 */ }
                try { if (_ != null) by = _.Name ?? "?"; } catch { /* 忽略 */ }
                try { amt = (int)Math.Round((double)(__instance?.Amount ?? 0m)); } catch { /* 忽略 */ }
                Logger.Log($"[CharBoss] 人工制品放行：{who} 的「{what}」由 {by} 施加（不是玩家）⇒ 照常生效，" +
                           $"人工制品不扣（仍有 {amt} 层）");
            }
            return false;
        }
        catch { return true; }
    }
}

/// <summary>
/// ★ v0.2.858（用户 2026-11-21：「还和刚刚一样」）：**人工制品到底被谁吃了**。
///
/// 本机日志已经把「支配发多少」这一侧钉死了（v0.2.857，16:00:58）：
/// <code>
/// 落地: 盛碗虫（石）=2, 盛碗虫（蜜）=2
/// 各自 power: 盛碗虫（石）[ARTIFACT_POWER=2] ; 盛碗虫（蜜）[ARTIFACT_POWER=2]
/// </code>
/// 开战那一刻两只都只有一条 `ARTIFACT_POWER=2`，没有别的图标 ⇒ 屏幕上出现的 1 只能是**之后**被吃掉的。
/// 反编译 `ArtifactPower.AfterModifyingPowerAmountReceived`：它挡掉一个负面之后会 `PowerCmd.Decrement`
/// （层数 −1）。这里挂个后缀，凡是发生「人工制品挡掉一次负面」就打一行，谁吃的、吃什么、剩几层一目了然。
/// </summary>
[HarmonyLib.HarmonyPatch(typeof(MegaCrit.Sts2.Core.Models.Powers.ArtifactPower),
                          nameof(MegaCrit.Sts2.Core.Models.Powers.ArtifactPower.AfterModifyingPowerAmountReceived))]
internal static class ArtifactConsumeTracePatch
{
    private static int _logs;

    [HarmonyLib.HarmonyPostfix]
    public static void Postfix(MegaCrit.Sts2.Core.Models.Powers.ArtifactPower __instance,
                               MegaCrit.Sts2.Core.Models.PowerModel power)
    {
        try
        {
            if (_logs++ >= 40) return;
            string owner = "?", what = "?";
            int amt = 0;
            try { owner = __instance?.Owner?.Name ?? "?"; } catch { /* 忽略 */ }
            try { what = power?.Id.Entry ?? "?"; } catch { /* 忽略 */ }
            try { amt = (int)Math.Round((double)(__instance?.Amount ?? 0m)); } catch { /* 忽略 */ }
            Logger.Log($"[CharBoss] 人工制品抵消：{owner} 挡掉了「{what}」⇒ 剩 {amt} 层" +
                       "（人工制品每挡一个负面就 −1，这是原版机制；若这里没有任何一行，说明那个图标不是人工制品）");
        }
        catch { /* 纯诊断 */ }
    }
}

#if STS2_113   // 恐惧模式的 DominionModel 是 0.113 才有的类型
/// <summary>
/// ★ v0.2.844（用户 2026-11-21）：「只显示当前层数的效果」。
///
/// `DominionModel.Description` **不是 virtual**（反编译确认）⇒ 只能挂 Harmony 后缀，
/// 在我们这三条按幕分档的支配上把占位符填成**当前幕**的数值：
///   · 静默猎手：`{Strength}` / `{Weak}`
///   · 故障机器人：`{Artifact}`
///   · 储君：`{Transform}`（= 当前幕要变成的那张状态牌的名字，取卡牌模型的 Title，
///     所以 16 种语言自动本地化）
/// 战士 / 观者 / 愚者三条支配不带占位符，这里也不会碰它们。
/// </summary>
[HarmonyLib.HarmonyPatch(typeof(MegaCrit.Sts2.Core.Models.Dread.DominionModel), "get_Description")]
internal static class DominionDescriptionActScalingPatch
{
    [HarmonyLib.HarmonyPostfix]
    public static void Postfix(MegaCrit.Sts2.Core.Models.Dread.DominionModel __instance,
                               ref MegaCrit.Sts2.Core.Localization.LocString __result)
    {
        try
        {
            if (__instance == null || __result == null) return;
            int n = DreadActScaling.ActNumber();

            if (__instance is SilentDominion)
            {
                __result.Add("Strength", (decimal)n);
                __result.Add("Weak", (decimal)n);
            }
            else if (__instance is DefectDominion)
            {
                __result.Add("Artifact", (decimal)n);
            }
            else if (__instance is RegentDominion)
            {
                var type = DreadActScaling.RegentTransformCardType();
                var card = MegaCrit.Sts2.Core.Models.ModelDb.GetById<MegaCrit.Sts2.Core.Models.CardModel>(
                    MegaCrit.Sts2.Core.Models.ModelDb.GetId(type));
                // ★ v0.2.889（用户 2026-11-22：「2 层储君的支配的描述错误的写为变化成碎屑（应为粘液）」）：
                //   只 `Add` 会沿用**缓存的同一个 LocString 实例**里上一次填的值（例如在第 3 幕填过「碎屑」），
                //   于是第 2 幕也显示碎屑。这里每次重新构造一份，保证拿到的是**当前幕**那张牌的名字。
                if (card?.Title != null)
                {
                    var fresh = new MegaCrit.Sts2.Core.Localization.LocString("ascension", "DREAD.REGENT.description");
                    fresh.Add("Transform", card.Title);
                    __result = fresh;
                }
            }
        }
        catch { /* 填不上就显示原文，绝不影响 UI */ }
    }
}
#endif
