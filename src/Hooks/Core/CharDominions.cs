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
/// </summary>
public sealed class IroncladDominion : MegaCrit.Sts2.Core.Models.Dread.DominionModel
{
    public override async Task AfterRoomEntered(AbstractRoom room)
    {
        try
        {
            if (room is not CombatRoom cr) return;
            var enemies = cr.Enemies;
            if (enemies == null) return;

            var targets = new List<Creature>();
            foreach (var c in enemies)
            {
                if (c == null || !c.IsAlive) continue;
                targets.Add(c);
            }
            if (targets.Count == 0) return;

            await MegaCrit.Sts2.Core.Commands.PowerCmd.Apply<MegaCrit.Sts2.Core.Models.Powers.DemonFormPower>(
                new ThrowingPlayerChoiceContext(), targets, 1m, null, null);
            Logger.Log($"[CharBoss] 恐惧模式·铁甲战士的支配：{targets.Count} 个敌人各获得 1 层恶魔形态");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 铁甲战士的支配失败: " + e.Message); }
    }
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

            int stacks = elite ? EliteStacks : NormalStacks;
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
/// **储君的支配**（用户 2026-11-13 改口径）：
/// > 储君的支配的实现效果改成给所有敌人上一层「王之凝视」是 buff
/// > （是储君打能力牌王之凝视获得的那个正面效果，你做一下适配，效果应该是和我之前描述的是一样的）
///
/// 适配依据（反编译）：
/// <code>
/// // MonarchsGaze.OnPlay
/// PowerCmd.Apply&lt;MonarchsGazePower&gt;(ctx, Owner.Creature, DynamicVars["StrengthLoss"], Owner.Creature, this);
///
/// // MonarchsGazePower（PowerType.Buff, StackType.Counter）:
/// //   AfterDamageGiven: if (dealer == Owner &amp;&amp; props.IsPoweredAttack())
/// //       PowerCmd.Apply&lt;MonarchsGazeStrengthDownPower&gt;(ctx, target, Amount, Owner, null);
/// </code>
/// ⇒ 把「王之凝视」这层 **Buff** 挂到**所有敌人**身上：敌人每次用**带攻击力的攻击**打你，
/// 你就获得 `MonarchsGazeStrengthDownPower`（临时 −1 力量、回合结束自动还回）
/// —— 正是原来那条「每当敌人攻击你时，你在本回合失去 1 点力量」。
///
/// ⚠ 旧写法（自己接管 `AfterDamageReceived` 再手动施加 `MonarchsGazeStrengthDownPower`）**已删除**，
/// 否则会和原版这层 Buff 各算一次、变成 −2 力量。
/// </summary>
public sealed class RegentDominion : MegaCrit.Sts2.Core.Models.Dread.DominionModel
{
    public override async Task AfterRoomEntered(AbstractRoom room)
    {
        try
        {
            if (room is not CombatRoom cr) return;
            var enemies = cr.Enemies;
            if (enemies == null)
            {
                // ★ v0.2.776：这条以前是**静默** return —— 2026-11-19 玩家联机的不同步就是
                //   「房主加了王之凝视、客人没加」（客人那一刻拿不到敌人列表）。以后有这行日志
                //   就能一眼分清「没进这个方法」还是「进来了但敌人列表是空的」。
                Logger.Log("[CharBoss] 恐惧模式·储君的支配：房间进入时拿不到敌人列表 ⇒ 这次没发王之凝视" +
                           "（两端会不同步；见 DreadDominionSync 的对齐日志）");
                return;
            }

            var targets = new List<Creature>();
            foreach (var c in enemies)
            {
                if (c == null || !c.IsAlive) continue;
                targets.Add(c);
            }
            if (targets.Count == 0)
            {
                Logger.Log("[CharBoss] 恐惧模式·储君的支配：敌人列表为空 ⇒ 这次没发王之凝视（两端会不同步）");
                return;
            }

            await MegaCrit.Sts2.Core.Commands.PowerCmd.Apply<
                MegaCrit.Sts2.Core.Models.Powers.MonarchsGazePower>(
                new ThrowingPlayerChoiceContext(), targets, 1m, null, null);
            Logger.Log($"[CharBoss] 恐惧模式·储君的支配：{targets.Count} 个敌人各获得 1 层「王之凝视」" +
                       "（它们用攻击打你时，你本回合 −1 力量）");
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
/// **故障机器人的支配**（用户 2026-11-13 改口径）：改成给所有玩家挂一层
/// <see cref="Powers.DefectDominionPower"/>（效果不变：你的回合结束时，每有一名敌人你就受到 3 点伤害）。
///
/// ⚠ 旧的 `AfterSideTurnEnd` 实现**已删除**（效果已搬进 power，否则会打两次）。
/// </summary>
public sealed class DefectDominion : MegaCrit.Sts2.Core.Models.Dread.DominionModel
{
    public override async Task AfterRoomEntered(AbstractRoom room)
    {
        try
        {
            if (room is not CombatRoom cr) return;
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

            await MegaCrit.Sts2.Core.Commands.PowerCmd.Apply<Powers.DefectDominionPower>(
                new ThrowingPlayerChoiceContext(), targets, 1m, null, null);
            Logger.Log($"[CharBoss] 恐惧模式·故障机器人的支配：{targets.Count} 名玩家获得「故障机器人的支配」");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 故障机器人的支配失败: " + e.Message); }
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
/// **观者的支配**（用户 2026-11-15；2026-11-17 增强）：恐惧模式下「所有敌人获得 **3** 层虔信」。
///
/// 虔信 = 一个「**回合开始时给观者真言**」的 buff（见 <see cref="Powers.WatcherFaithPower"/>），
/// 挂在每位玩家身上 ⇒ 他们每人回合开始，观者收 3 层真言（支配是给 Boss 加码的，
/// 和愚者「开局塞 2 张悲伤」同向）。
/// ★ v0.2.742（用户 2026-11-17）：「增强恐惧模式下观者的支配：所有敌人获得 3 层虔信」⇒ 2 → 3。
/// </summary>
public sealed class WatcherDevotionDominion : MegaCrit.Sts2.Core.Models.Dread.DominionModel
{
    /// <summary>每位玩家获得的虔信层数（= 他们每回合开始给观者的真言层数）。</summary>
    private const int FaithStacks = 3;

    public override async Task AfterRoomEntered(AbstractRoom room)
    {
        try
        {
            if (room is not CombatRoom cr) return;
            var players = cr.CombatState?.Players;
            if (players == null)
            {
                Logger.Log("[CharBoss] 恐惧模式·观者的支配：房间进入时拿不到玩家列表 ⇒ 这次的虔信没发出去（两端会不同步）");
                return;
            }

            int given = 0;
            foreach (var p in players)
            {
                var creature = p?.Creature;
                if (creature == null || !creature.IsAlive) continue;
                if (creature.Monster != null) continue;      // 影子玩家 / 怪物不算
                await MegaCrit.Sts2.Core.Commands.PowerCmd.Apply<Powers.WatcherFaithPower>(
                    new ThrowingPlayerChoiceContext(), creature, (decimal)FaithStacks, null, null);
                given++;
            }
            Logger.Log($"[CharBoss] 恐惧模式·观者的支配：{given} 名玩家各获得 {FaithStacks} 层虔信" +
                       $"（他们回合开始时观者获得 {FaithStacks} 层真言）");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 观者的支配失败: " + e.Message); }
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
    public static void Postfix()
    {
        try { DreadDominionSync.AfterGameSetDominion(); } catch { /* 绝不影响主流程 */ }
    }
}
#endif

