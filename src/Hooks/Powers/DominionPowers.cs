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
using MegaCrit.Sts2.Core.ValueProps;

namespace CharacterBosses.Powers;

/// <summary>
/// 恐惧模式「**故障机器人的支配**」的**可视载体**（用户 2026-11-13 改口径）。
///
/// 用户原话：
/// > 故障机器人的支配和亡灵契约师的支配改成给所有玩家上一个「故障机器人的支配」或
/// > 「亡灵契约师的支配」的 buff，类似乐加维林祖母和无厌沙虫等 boss 的实现方法（你可以参考一下），
/// > 效果不变（确保旧的实现方式取消了，别重复实现）
///
/// 参考的原版实现（反编译 `Models.Dread.Dominions.LagavulinMatriarchDominion`）：
/// <code>
/// public override async Task AfterRoomEntered(AbstractRoom room)
/// {
///     if (room is CombatRoom cr)
///         await PowerCmd.Apply&lt;LagavulinMatriarchDominionPower&gt;(
///             new ThrowingPlayerChoiceContext(),
///             cr.CombatState.Players.Select(p =&gt; p.Creature), 1m, null, null);
/// }
/// </code>
/// 而它自己那层 power 也是 `PowerType.Debuff`（挂着玩家身上、做实际结算）。
/// ⇒ 我们把**效果搬进 power**，支配本体只负责「进战斗房间时把它挂上去」。
///
/// 效果（与旧实现完全相同）：**在你的回合结束时，每有一名敌人，你就受到 3 点伤害**。
/// 旧写法是支配自己的 `AfterSideTurnEnd` 里遍历所有玩家扣血（已删除，避免重复结算）。
/// </summary>
public sealed class DefectDominionPower : PowerModel
{
    /// <summary>每名敌人让持有者掉多少血。</summary>
    private const int PerEnemy = 3;

    /// <summary>
    /// **N 名敌人时这一回合要挨多少**（单一真值来源：支配本体与「薄荷尖塔」兼容都调这里）。
    /// 用户口径：每有一名敌人，玩家就在自己回合结束时受 3 点伤害。
    /// </summary>
    internal static int DamageFor(int enemies) => PerEnemy * Math.Max(0, enemies);

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    /// <summary>玩家侧回合结束时结算（每个持有者只结算自己）。</summary>
    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side,
                                               IEnumerable<Creature> participants)
    {
        try
        {
            if (side != CombatSide.Player) return;
            var me = Owner;
            if (me == null || !me.IsAlive) return;
            if (me.Monster != null) return;               // 只对真人玩家生效（影子玩家/怪物不算）

            var combat = me.CombatState;
            if (combat == null) return;

            int enemies = 0;
            var monsters = combat.Enemies;
            if (monsters != null)
            {
                foreach (var c in monsters)
                    if (c != null && c.IsAlive) enemies++;
            }
            if (enemies <= 0) return;

            int amount = DamageFor(enemies);
            await CreatureCmd.Damage(choiceContext, me, amount, ValueProp.Unpowered, (Creature?)null);
            Logger.Log($"[CharBoss] 故障机器人的支配：{enemies} 名敌人 ⇒ {me.Name} 受到 {amount} 点伤害");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 故障机器人的支配结算失败: " + e.Message);
        }
    }
}

/// <summary>
/// 恐惧模式「**亡灵契约师的支配**」的**可视载体**（用户 2026-11-13 改口径）。
///
/// 效果（与旧实现完全相同）：**在你的回合开始时，你随机一张手牌的费用在本回合增加 1**。
/// 旧写法是支配自己的 `AfterPlayerTurnStart`（已删除）。现在挂在玩家身上，每个持有者只管自己。
/// </summary>
public sealed class NecrobinderDominionPower : PowerModel
{
    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    /// <summary>
    /// **确定性挑牌**（用户 2026-11-13：「让耗能增加的牌不因 SL 变化」）。
    ///
    /// 输入只有三样，且这三样在 **SL 前后 / 联机两端 / 同一回合重算** 时都**完全相同**：
    ///   1. 本局种子（`BossLineups.RunSeed` → `RunState.Rng.Seed`）；
    ///   2. 该玩家的 `NetId`（同一玩家在任何机器上都是同一个数）；
    ///   3. 当前「可打出的手牌」按手牌顺序排列的 `id.Entry` 序列。
    /// ⇒ 同样的输入必然算出同样的序号 ⇒ 同一回合永远挑同一张牌，SL 不会换牌，联机两端也不会分叉。
    ///
    /// ⚠ **故意不掺「回合数 / 帧号 / 时间」**：回合数在 SL 后可能对不上，
    /// 而手牌序列本来就随回合变化（抽牌不同），足够把不同回合区分开。
    /// </summary>
    private static int StableIndex(IReadOnlyList<CardModel> cards, MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        try
        {
            if (cards == null || cards.Count == 0) return -1;

            ulong h = 14695981039346656037UL;                 // FNV-1a 偏移基

            h = Mix(h, Core.BossLineups.RunSeed);

            ulong netId = 0;
            try { netId = unchecked((ulong)Convert.ToInt64(player.NetId)); } catch { netId = 0; }
            h = Mix(h, netId);

            foreach (var c in cards)
            {
                if (c == null) continue;
                string entry = string.Empty;
                try { entry = c.Id.Entry ?? string.Empty; } catch { entry = string.Empty; }
                h = MixString(h, entry);
                // 升级状态也掺进去（同名不同升级算两张不同的牌）
                try { if (c.IsUpgraded) h = Mix(h, 0x9E3779B97F4A7C15UL); } catch { /* 忽略 */ }
            }

            return (int)(h % (ulong)cards.Count);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 亡灵契约师的支配：稳定挑牌失败（退回第 0 张）: " + e.Message);
            return 0;
        }
    }

    private static ulong Mix(ulong h, ulong v)
    {
        h ^= v;
        h *= 1099511628211UL;      // FNV-1a 质数
        h ^= h >> 32;
        return h;
    }

    private static ulong MixString(ulong h, string? s)
    {
        if (string.IsNullOrEmpty(s)) return Mix(h, 0UL);
        for (int i = 0; i < s.Length; i++) h = Mix(h, (ulong)s[i]);
        return Mix(h, (ulong)s.Length);
    }

    public override Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext,
                                              MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        try
        {
            var me = Owner?.Player;
            if (me == null || player == null) return Task.CompletedTask;
            if (!ReferenceEquals(player, me)) return Task.CompletedTask;   // 每个持有者只管自己的回合

            var all = player.PlayerCombatState?.Hand?.Cards;
            if (all == null || all.Count == 0) return Task.CompletedTask;

            // ★ v0.2.870：与储君那条同因 —— 支配 power 会跟着玩家进 Boss 房，Boss 战里不该再生效。
            try
            {
                var roomNow = player.Creature?.CombatState?.RunState?.CurrentRoom;
                if (CharacterBosses.Core.DreadMode.SkipBecauseActBossRoom(roomNow, "亡灵契约师的支配（回合开始）")) return Task.CompletedTask;
            }
            catch { /* 判断失败就按原逻辑走 */ }
            // ★ 2026-11-13 修（玩家实测：第 1 回合挑中了诅咒牌「登顶者之灾」⇒ 加费毫无意义）：
            //   只从**打得出去**的手牌里随机挑。判据复用规划器那把尺子
            //   （`BossScriptPlanner.IsPlayable`：排除 状态 / 诅咒 / 任务 牌与带 `Unplayable` 关键字的牌，
            //    但「执迷」是**可打出的诅咒**，单独放行）。
            var playable = new List<CardModel>();
            foreach (var c in all)
            {
                if (c == null) continue;
                if (Core.BossScriptPlanner.IsPlayable(c)) playable.Add(c);
            }
            if (playable.Count == 0)
            {
                Logger.Log($"[CharBoss] 亡灵契约师的支配：{player.Character?.GetType().Name} 手上" +
                           $"没有可打出的牌（{all.Count} 张全是状态/诅咒）⇒ 本回合不涨价");
                return Task.CompletedTask;
            }

            // ★ 2026-11-13 修（用户：「怎么 sl 增加耗能的牌还会变？让耗能增加的牌不因 sl 变化」）：
            //   **不能用 `Random.Shared.Next`** —— 那是进程级随机数：
            //     · SL 之后这条钩子若再跑一次，就会挑到**另一张牌**（用户看到的现象）；
            //     · 联机时两端各摇各的 ⇒ 两端看到的加费牌不同。
            //   改成**确定性哈希**：种子只用「本局种子 + 玩家 NetId + 当前手牌的可打出牌 id 序列」，
            //   这三样在 SL 前后、以及联机两端都是**完全相同**的 ⇒ 同一回合永远挑同一张。
            int index = StableIndex(playable, player);
            if (index < 0 || index >= playable.Count) index = 0;
            var pick = playable[index];
            if (pick == null) return Task.CompletedTask;

            // ★ 2026-11-13 修（用户：「每回合都没有手牌的能耗增加」）：
            //   **`AddThisCombat(int amount, bool reduceOnly = false)` 的第二个参数是 `reduceOnly`，不是「本回合」！**
            //   （反编译：`_localModifiers.Add(new LocalCostModifier(amount, Relative, EndOfCombat, reduceOnly))`）
            //   ⇒ 以前写的 `AddThisCombat(1, true)` 意思是「加 1 费、但只允许朝**降低**的方向生效」
            //   ⇒ 正数被整条吞掉，一次都没生效过（从 v0.2.593 第一版支配起就错着）。
            //   正确接口是 `AddThisTurn(1)`（过期时机 = `EndOfTurn`），正好对应「本回合 +1 费」。
            //   注意本模组其它几处 `AddThisCombat(-n, true)` 是**负数**（减费）⇒ `reduceOnly` 恰好满足，那些是对的。
            //
            // ★ 同时做成**幂等 + 自愈**（SL 专用）：
            //   · 这张牌本回合**已经有**本地费用修正 ⇒ 说明这次是重入（SL 后钩子又跑了一遍）⇒ 跳过，不会叠成 +2；
            //   · 修正被 SL 丢掉了 ⇒ `HasLocalModifiers == false` ⇒ 这里会**重新加回去** ⇒ SL 前后费用一致。
            if (pick.EnergyCost.HasLocalModifiers)
            {
                Logger.Log($"[CharBoss] 亡灵契约师的支配：{player.Character?.GetType().Name} 的「" +
                           $"{pick.Id.Entry}」本回合已经加过费（重入/SL）⇒ 不重复叠加");
                return Task.CompletedTask;
            }

            int beforeCost = -999, afterCost = -999;
            try { beforeCost = pick.EnergyCost.GetResolved(); } catch { /* 忽略 */ }

            pick.EnergyCost.AddThisTurn(1);

            try { afterCost = pick.EnergyCost.GetResolved(); } catch { /* 忽略 */ }

            Logger.Log($"[CharBoss] 亡灵契约师的支配：{player.Character?.GetType().Name} 的「" +
                       $"{pick.Id.Entry}」本回合费用 +1（{beforeCost} → {afterCost}；" +
                       $"候选 {playable.Count}/{all.Count} 张，稳定序号 {index}）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 亡灵契约师的支配结算失败: " + e.Message);
        }
        return Task.CompletedTask;
    }
}

#if STS2_113
/// <summary>
/// 恐惧模式「**储君的支配**」的**可视载体**（用户 2026-11-19 改口径）。
///
/// 新效果：**在你的回合开始时，选择 1 张非状态或诅咒牌，将其变化为「碎屑」**。
/// （旧效果「每当敌人攻击你时，你在本回合失去 1 点力量」—— 那套给敌人挂「王之凝视」的实现**整套删除**。）
///
/// 结算时机：`AfterPlayerTurnStartEarly`。必须早于**烘焙手套**（`ToastyMittens`）、
/// **暴政**（`TyrannyPower` 在普通 `AfterPlayerTurnStart` 里让你消耗手牌）、
/// **必备工具**（`ToolsOfTheTradePower`）这些同样挂在「你的回合开始」的效果
/// （用户 2026-11-19：「该效果的结算时间在烘焙手套、暴政、必备工具等效果前」）。
/// 额外回合（佩尔之眼 / 龙涎香 / 腾跃 / 倒吊人…）也算一次「你的回合开始」⇒ 同样触发
/// （用户：「额外回合开始时也触发，因为这本身就是影响玩家的效果」）。
///
/// 一回合只做一次：Early 那一帧若一张可选牌都没有（例如手牌还没抽上来），记一个待办，
/// 到普通 `AfterPlayerTurnStart` 再补一次。
/// </summary>
public sealed class RegentDominionPower : PowerModel
{
    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Single;

    /// <summary>Early 阶段没做成（手牌里没有可选牌）⇒ 普通阶段再补一次。</summary>
    private bool _awaitingNormalPhase;

    public override async Task AfterPlayerTurnStartEarly(PlayerChoiceContext choiceContext,
                                                         MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        _awaitingNormalPhase = !await TryResolveAsync(choiceContext, player, "回合开始·早期");
    }

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext,
                                                    MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        if (!_awaitingNormalPhase) return;
        _awaitingNormalPhase = false;
        await TryResolveAsync(choiceContext, player, "回合开始");
    }

    /// <summary>返回 false = 这一帧还做不了（手牌里没有可选牌），留给下一个阶段再试。</summary>
    private async Task<bool> TryResolveAsync(PlayerChoiceContext choiceContext,
                                             MegaCrit.Sts2.Core.Entities.Players.Player player,
                                             string phase)
    {
        try
        {
            if (player?.Creature == null) return true;
            if (!ReferenceEquals(player.Creature, Owner)) return true;    // 每个持有者只管自己
            if (player.Creature.Monster != null) return true;             // 影子玩家 / 怪物不算

            var combat = player.Creature.CombatState;
            var hand = player.PlayerCombatState?.Hand?.Cards;
            if (combat == null || hand == null) return false;

            // ★ v0.2.870（用户 2026-11-21：「恐惧模式联机时，主 boss 为储君时首次进入 boss 房储君的支配
            //   仍会生效（让玩家选择 1 张牌变化为眩晕/粘液/碎屑），然后触发联机不同步（SL 可解决）」）：
            //   进 Boss 房时游戏会摘掉「本幕支配」，但**已经挂到玩家身上的这层 power 还在** ⇒
            //   首次进 Boss 房第 1 回合照样弹「选 1 张牌变形」，而这是**玩家选择**：联机时两端各选各的
            //   ⇒ 立刻分叉（读档后支配已不在，所以 SL 就好了）。与 `AfterRoomEntered` 用同一个闸门。
            try
            {
                var roomNow = combat.RunState?.CurrentRoom;
                if (CharacterBosses.Core.DreadMode.SkipBecauseActBossRoom(roomNow, "储君的支配（回合开始）")) return true;
            }
            catch { /* 判断失败就按原逻辑走 */ }

            int eligible = 0;
            foreach (var c in hand)
            {
                if (IsEligible(c)) eligible++;
            }
            if (eligible == 0) return false;

            var prefs = new MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs(
                MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs.TransformSelectionPrompt, 1);

            var picked = await CardSelectCmd.FromHand(choiceContext, player, prefs, IsEligible, this);
            CardModel? card = null;
            if (picked != null)
            {
                foreach (var c in picked)
                {
                    if (c == null) continue;
                    card = c;
                    break;
                }
            }
            if (card == null)
            {
                Logger.Log($"[CharBoss] 恐惧模式·储君的支配（{phase}）：没挑到牌 ⇒ 这一回合不变化");
                return true;
            }

            string entry = "?";
            try { entry = card.Id.Entry; } catch { /* 忽略 */ }

            // ★ v0.2.844（用户 2026-11-21）：储君的支配按幕分档 —— 1/2/3 幕分别变成 眩晕 / 粘液 / 碎屑
            var transformType = Core.DreadActScaling.RegentTransformCardType();
            string transformLabel = Core.DreadActScaling.RegentTransformCardLabel();
            var canonical = ModelDb.GetById<CardModel>(ModelDb.GetId(transformType));
            if (canonical == null)
            {
                Logger.Log("[CharBoss] 恐惧模式·储君的支配：拿不到变化目标（眩晕/粘液/碎屑）的卡牌模型 ⇒ 这次跳过");
                return true;
            }

            var debris = combat.CreateCard(canonical, player);
            if (debris == null)
            {
                Logger.Log("[CharBoss] 恐惧模式·储君的支配：造不出目标状态牌（CreateCard 返回 null）⇒ 这次跳过");
                return true;
            }

            await CardCmd.Transform(card, debris,
                MegaCrit.Sts2.Core.Nodes.CommonUi.CardPreviewStyle.HorizontalLayout);

            Logger.Log($"[CharBoss] 恐惧模式·储君的支配（{phase}）：{player.Character?.GetType().Name} " +
                       $"把「{entry}」变化成了{transformLabel}（候选 {eligible} 张，已剔除状态牌与诅咒）");
            return true;
        }
        catch (Exception e)
        {
            Logger.Log($"[CharBoss] 储君的支配（{phase}）失败: " + e.Message);
            return true;    // 出过错就不再重试，免得一回合里反复弹窗
        }
    }

    /// <summary>可选牌：**非状态、非诅咒**（用户 2026-11-19）。</summary>
    private static bool IsEligible(CardModel? card)
    {
        try
        {
            if (card == null) return false;
            return card.Type != CardType.Status && card.Type != CardType.Curse;
        }
        catch { return false; }
    }
}

/// <summary>
/// 恐惧模式「**愚者的支配**」的**可视载体**（用户 2026-11-13 新增；2026-11-23 改成按幕分档 2/3/4 张）。
///
/// ⚠ 只在 111 构建里：它要发的是 0.113 才有的状态牌 **「悲伤」`Sadness`**
/// （`base(-1, CardType.Status, CardRarity.Status, TargetType.None)`）。107 没有这张牌，
/// 而恐惧模式本身就是 0.113 独有的 ⇒ 整类放进 `#if STS2_111`。
/// </summary>
public sealed class FoolDominionPower : PowerModel
{
    /// <summary>
    /// 给几张「悲伤」—— **按幕分档 2 / 3 / 4**（用户 2026-11-23，见
    /// <see cref="Core.DreadActScaling.SadnessCount"/>；以前是固定 2 张）。
    /// </summary>
    private static int SadnessNow() => Core.DreadActScaling.SadnessCount();

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    /// <summary>
    /// 描述里的 <c>{Sadness}</c> 现填当前幕的数字（2 / 3 / 4）。
    /// 和 <see cref="IroncladRagePower.Description"/> 同一套做法：文案里不能用 <c>{Amount}</c>
    /// （那是原版层数，改它会牵动结算），所以单独用占位符。
    /// </summary>
    public override MegaCrit.Sts2.Core.Localization.LocString Description
    {
        get
        {
            var description = base.Description;
            try { description.Add("Sadness", (decimal)SadnessNow()); }
            catch { /* 忽略：取不到就把占位符原样显示，不崩 */ }
            return description;
        }
    }

    /// <summary>本场是否已经发过（每场战斗只发一次）。</summary>
    private bool _given;

    public override async Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side,
                                                   IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        try
        {
            if (_given) return;
            if (side != CombatSide.Player) return;

            var me = Owner;
            if (me == null || !me.IsAlive) return;
            var player = me.Player;
            if (player == null || player.PlayerCombatState == null) return;

            var canonical = ModelDb.GetById<CardModel>(ModelDb.GetId(
                typeof(MegaCrit.Sts2.Core.Models.Cards.Sadness)));
            if (canonical == null)
            {
                Logger.Log("[CharBoss] 愚者的支配：取不到「悲伤」（Sadness）⇒ 不发牌");
                return;
            }

            int given = 0;
            int want = SadnessNow();
            for (int i = 0; i < want; i++)
            {
                var made = combatState.CreateCard(canonical, player);
                if (made == null) continue;
                try
                {
                    var added = await CardPileCmd.AddGeneratedCardToCombat(
                        made, PileType.Draw, player, CardPilePosition.Random);
                    try { CardCmd.PreviewCardPileAdd(added); } catch { /* 动画失败不影响发牌 */ }
                    given++;
                }
                catch (Exception e)
                {
                    Logger.Log("[CharBoss] 愚者的支配：塞「悲伤」失败: " + e.Message);
                }
            }

            _given = true;
            Logger.Log($"[CharBoss] 愚者的支配：给 {me.Name} 的抽牌堆洗入 {given} 张「悲伤」" +
                       $"（第 {Core.DreadActScaling.ActNumber()} 幕 ⇒ 应给 {want} 张）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 愚者的支配结算失败: " + e.Message);
        }
    }
}

/// <summary>
/// 恐惧模式「**观者的支配**」的**虔信**（挂在**敌人**身上）——用户 2026-11-19 定的口径：
/// > 「就是给敌人挂虔信，积累 10 点真言后消耗 10 点真言进神格（本回合 3 倍伤害）一回合。
/// >   这些敌人的虔信在**玩家回合开始时**触发，并且在进神格的当回合开始时不会退出神格，
/// >   **执行完自己的意图才会退神格**（这些描述不影响 boss，尤其是与神格观的机制无关，
/// >   别改这个的时候把神格观带偏了）（也就是怪物第四回合、第七回合等回合会进神格，
/// >   当回合伤害翻三倍）」
///
/// ⇒ 三个 power 各管一段：
///   · **本类（虔信）**：`Amount` = 每次给几层真言（支配给 2）；**玩家侧回合开始**时给持有者加真言
///     （★ v0.2.815：**玩家额外回合不算**，见 `Core/ExtraTurnWatch`），
///     满 <see cref="Threshold"/> 就扣 10 并让他进神格；
///   · **<see cref="DominionMantraPower"/>（真言）**：就是那个层数/计数，界面上看得见进度；
///   · **<see cref="DominionDivinityPower"/>（神格）**：`ModifyDamageMultiplicative` 把他自己的
///     攻击伤害 ×3，**它自己的回合结束（= 意图执行完）才摘掉**，回合开始不摘。
///
/// ⚠ 与「神格观」那条链（`BossDevotionPower` + `WatcherFaithPower`）**完全分开、互不影响**：
///   那边是观者 Boss 自己的真言 / 姿态 / 进神格加力量加能量；这边是**普通怪物**的仿制版，
///   持有者如果是我们自己的 `CharBossMonster`（她有自己的真神格）就**直接跳过**。
///
/// 数值对账（**2 层/回合**、满 10 进神格，用户 2026-11-19 订正为 2）：
/// 第 5 回合（2+2+2+2+2=10，扣 10 剩 0）、第 10 回合（又是 5 个回合各 2 ⇒ 10，扣 10 剩 0）、
/// 第 15 回合……—— 与用户说的「怪物第 5、10、15 等回合会进神格」完全一致。
/// （曾经按 3 层/回合做过一版，那时是第 4 / 7 / 10 回合；用户要求回调成 2 层。）
/// </summary>
public sealed class WatcherDominionDevotionPower : PowerModel
{
    /// <summary>真言满多少层触发神格。</summary>
    internal const int Threshold = 10;

    /// <summary>
    /// ★ 2026-11-19（用户）：「这里的虔信不应该是 debuff，不应该显示成红的框」
    ///   ⇒ 从 `Debuff` 改成 `Buff`（虔信是给这个敌人加码的祝福，红框看着像玩家给他上的减益）。
    /// </summary>
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    /// <summary>层数不随人数放大：支配固定给 2 层，多一个人不该变成 4。</summary>
    public override bool ShouldScaleInMultiplayer => false;

    public override bool ShouldPlayVfx => false;

    /// <summary>玩家（任意一位真人的）回合开始 ⇒ 这个敌人 +N 层真言，满了就进神格。</summary>
    public override async Task AfterSideTurnStart(CombatSide side,
                                                  IReadOnlyList<Creature> participants,
                                                  ICombatState combatState)
    {
        try
        {
            if (side != CombatSide.Player) return;                 // ★ 玩家回合开始触发（不是它自己的回合）
            var me = Owner;
            if (me == null || !me.IsAlive) return;
            if (me.Monster is Monsters.CharBossMonster) return;    // 我们自己的 Boss 走真神格，别串台

            // ★ v0.2.815（用户 2026-11-19 同一类问题）：额外回合（佩尔之眼 / 龙涎香 / 腾跃 / 倒吊人 …）
            //   不是真回合 ⇒ 不叠真言，否则怪物会提前一轮进神格（第 5/10/15 回合那个节奏被打破）。
            //   判据见 Core/ExtraTurnWatch.cs（读 `CombatManager.PlayersTakingExtraTurn`）。
            if (Core.ExtraTurnWatch.IsExtraPlayerTurn)
            {
                Logger.Log($"[CharBoss] 观者的支配·虔信：{me.Name} **跳过本次（额外回合）**，不叠真言");
                return;
            }

            int gain = (int)Math.Round((double)Amount);
            if (gain <= 0) return;

            var ctx = new ThrowingPlayerChoiceContext();

            var mantra = me.GetPower<DominionMantraPower>();
            int now;
            if (mantra == null)
            {
                var made = await PowerCmd.Apply<DominionMantraPower>(ctx, me, gain, me, null, false);
                now = (int)Math.Round((double)(made?.Amount ?? gain));
            }
            else
            {
                now = await PowerCmd.ModifyAmount(ctx, mantra, gain, me, null, false);
            }
            Logger.Log($"[CharBoss] 观者的支配·虔信：{me.Name} 真言 +{gain} ⇒ {now}/{Threshold}");

            // 满了就扣 10 进神格（正常一次只跨一次，guard 只防数据异常的死循环）
            int guard = 0;
            while (now >= Threshold && guard++ < 4)
            {
                var cur = me.GetPower<DominionMantraPower>();
                if (cur == null) break;
                now = await PowerCmd.ModifyAmount(ctx, cur, -Threshold, me, null, false);
                await PowerCmd.Apply<DominionDivinityPower>(ctx, me, 1m, me, null, false);
                Logger.Log($"[CharBoss] 观者的支配·虔信：{me.Name} 真言满 {Threshold} ⇒ 消耗 {Threshold} 层，" +
                           $"进入神格（它这一回合的攻击三倍伤害，执行完意图后退出），剩余真言 {now}");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 观者的支配·虔信结算失败: " + e.Message);
        }
    }
}

/// <summary>
/// 「**真言**」计数（挂在拿了观者支配虔信的**敌人**身上）——纯计数，不含逻辑：
/// 每玩家回合开始 +N（<see cref="WatcherDominionDevotionPower"/> 加），满 10 层被扣掉 10 进神格。
/// </summary>
public sealed class DominionMantraPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldScaleInMultiplayer => false;

    public override bool ShouldPlayVfx => false;
}

/// <summary>
/// 「**神格**」（挂在拿了观者支配虔信的**敌人**身上，只持续**它自己的一回合**）。
///
/// 用户口径：「积累 10 点真言后消耗 10 点真言进神格（**本回合 3 倍伤害**）一回合……
/// 并且在进神格的**当回合开始时不会退出神格**，**执行完自己的意图才会退神格**」
/// ⇒ ① 倍率走原版钩子 `ModifyDamageMultiplicative`（反编译确认 `Hook.ModifyDamageInternal`
///      会遍历**全部** hook 监听者 ⇒ 挂在敌人身上的 power 也能改它自己打出来的伤害），
///      只认「自己作为 dealer 的、有来源的攻击」（`props.IsPoweredAttack()`）；
///   ② 退出时机放在 `AfterSideTurnEnd` 且**必须是自己那一侧、自己在参与者里**——
///      这样「它的回合开始」不会摘、别的回合结束也不会误摘。
/// </summary>
public sealed class DominionDivinityPower : PowerModel
{
    /// <summary>这一回合的攻击伤害倍率。</summary>
    private const decimal Multiplier = 3m;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldScaleInMultiplayer => false;

    public override bool ShouldPlayVfx => false;

    public override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props,
                                                      Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
    {
        try
        {
            if (dealer == null || Owner == null) return 1m;
            if (!ReferenceEquals(dealer, Owner)) return 1m;      // 只放大它自己打出来的伤害
            if (!props.IsPoweredAttack()) return 1m;             // 只认攻击（状态/毒这类不算）
            return Multiplier;
        }
        catch { return 1m; }
    }

    /// <summary>它自己的回合结束（= 意图已经执行完）⇒ 退出神格。</summary>
    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side,
                                               IEnumerable<Creature> participants)
    {
        try
        {
            if (side != CombatSide.Enemy) return;
            var me = Owner;
            if (me == null) return;

            bool mine = false;
            if (participants != null)
            {
                foreach (var c in participants)
                {
                    if (ReferenceEquals(c, me)) { mine = true; break; }
                }
            }
            if (!mine) return;

            Logger.Log($"[CharBoss] 观者的支配·神格：{me.Name} 的意图已执行完 ⇒ 退出神格");
            await PowerCmd.Remove(this);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 观者的支配·神格移除失败: " + e.Message);
        }
    }
}
#endif