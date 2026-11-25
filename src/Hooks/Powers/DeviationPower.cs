using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Monsters;

namespace CharacterBosses.Powers;

/// <summary>
/// 故障机器人（三层）的特殊效果「偏差认知」—— 注意：**和同名卡牌不是一回事**。
///
/// 两条效果（用户设计）：
///   1. **玩家**每打出一张能力牌，Boss 获得 1 点集中；
///   2. Boss 的力量与集中**数值始终相同** —— 任意一边变化，另一边跟着变同样的差值
///      （所以「暴涨」的 +2 力量会同步 +2 集中；而「偏差认知」每回合掉 1 点集中，
///       力量也会跟着掉 1 点）。
///
/// 实现方式：本效果是挂在 Boss 身上的一层 Power，因此能收到 <c>AfterCardPlayed</c> 与回合钩子；
/// 数值同步在「每次出牌后 / 回合开始 / 回合结束」对账：记录上次的值判断是**哪一边**变了，
/// 再把差值补给另一边。
/// </summary>
public sealed partial class DeviationPower : PowerModel
{
    private int _lastStrength = int.MinValue;
    private int _lastFocus = int.MinValue;
    private bool _syncing;

    public override PowerType Type => PowerType.Buff;

    /// <summary>不可叠加：有这一层就够了。</summary>
    public override PowerStackType StackType => PowerStackType.Single;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<FocusPower>(1m),
    };

    // 图标由本地化资源决定（原版按 model id 找贴图）；我们自己的状态栏图标另外走 BossSpecialChip。

    /// <summary>效果 1：玩家打出能力牌 → Boss +1 集中（随后力量会同步）。</summary>
    ///
    /// ★★ v0.2.944（用户 2026-11-25：「削弱能力机」）—— **两种模式从此完全不同**：
    ///   · **恐惧模式** = 原来普通模式那一套：每打出 1 张**非衍生的**能力牌 ⇒ 她 +1 集中。
    ///     取消 v0.2.6xx 的两条恐惧加强（衍生牌也算、免疫「偏差认知」）。
    ///   · **普通模式** = 新效果：你在**一回合内打出过能力牌** ⇒ 她**当回合的「偏差认知」不生效**
    ///     （她回合开始不扣集中 —— 拦截在 `Patches/BiasedCognitionShieldPatch`；
    ///      当回合的伤害预览也要把那笔「会掉集中」的修正去掉 —— 见 `BossIntentBuilder.PendingDrop`）。
    ///   两种模式都保留「力量与集中数值始终相等」（那部分在 `DeviationPower.Mirror.cs`）。
    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        try
        {
            var owner = Owner;
            if (owner == null || !owner.IsAlive) return;

            var card = cardPlay?.Card;
            if (card == null || card.Type != CardType.Power) return;
            if (card.Owner?.Creature == owner) return;   // Boss 自己打的能力牌不算

            // **对应型**（多人）：只有「锁定这只 Boss」的真人玩家打出的能力牌才算
            // （单人时 SpecialEffectAppliesTo 恒为 true，行为不变）。
            if (!Core.BossLockOn.SpecialEffectAppliesTo(owner.Monster as CharBossMonster, card.Owner)) return;

            // ── 普通模式（v0.2.944 新效果）：不再白拿集中，改成「这一回合她的偏差认知失效」 ──
            if (!Core.DreadMode.IsActive)
            {
                _biasShieldArmed = true;
                Logger.Log($"[CharBoss] 偏差认知（普通模式）：你打出了能力牌 {SafeEntry(card)} ⇒ " +
                           "她**当回合的「偏差认知」不生效**（她回合开始不扣集中，伤害预览也不再减这一笔）");
                // 她身上那笔「回合开始要掉 N 点集中」的预补要立刻撤掉（意图是按「已经掉过」算的）
                try { if (owner.Monster is CharBossMonster armed) armed.IntentsDirty = true; } catch { /* 纯显示 */ }
                return;
            }

            // ── 恐惧模式：与「以前的普通模式」相同 —— 只有**非衍生的**能力牌才给集中 ──
            // 判定见 IsDerivedCard（原版稀有度 Token / 命名空间 .Token / 卡池 Token·Collectible）。
            if (IsDerivedCard(card))
            {
                string entry = SafeEntry(card);
                if (DerivedLogged.Add(entry))
                {
                    Logger.Log($"[CharBoss] 偏差认知：{entry} 是**衍生牌** ⇒ 不给 Boss 集中" +
                               "（衍生牌不算「非衍生的能力牌」；恐惧模式也一样）");
                }
                return;
            }

            Logger.Log($"[CharBoss] 偏差认知：玩家打出能力牌 {card.Id.Entry} → Boss +1 集中");
            await PowerCmd.Apply<FocusPower>(choiceContext, owner, 1m, owner, null, false);
            await ReconcileAsync(choiceContext);
        }
        catch (System.Exception e)
        {
            Logger.Log("[CharBoss] 偏差认知（玩家出能力牌）失败: " + e.Message);
        }
    }

    /// <summary>
    /// 普通模式：她这一回合的「偏差认知」是否**已被玩家的能力牌挡掉**。
    /// 由 `Patches/BiasedCognitionShieldPatch` 在她回合开始时消费（消费掉就恢复正常衰减）。
    /// </summary>
    private bool _biasShieldArmed;

    /// <summary>预览侧用：这一回合的偏差认知是不是不会生效（恐惧模式恒 false —— 那一版没有这个机制）。</summary>
    internal static bool BiasShieldArmed(Creature? owner)
    {
        try
        {
            var power = owner?.GetPower<DeviationPower>();
            return power != null && power._biasShieldArmed && !Core.DreadMode.IsActive;
        }
        catch { return false; }
    }

    /// <summary>她回合开始时消费一次：返回 true = 这一次的「偏差认知」扣集中被跳过。</summary>
    internal bool TryConsumeBiasShield()
    {
        if (Core.DreadMode.IsActive) return false;
        if (!_biasShieldArmed) return false;
        _biasShieldArmed = false;
        return true;
    }

    public override async Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants,
                                                  ICombatState combatState)
    {
        // 新的一轮开始（玩家侧回合开始）⇒ 上一轮那个「挡掉偏差认知」的标记作废
        //（正常情况下它已经在她回合开始被消费掉了；这里只是防止她身上没有那层 power 时残留）
        if (side == CombatSide.Player) _biasShieldArmed = false;
        await ReconcileAsync(null);
    }

    public override async Task BeforeSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side,
                                                 IEnumerable<Creature> participants)
    {
        await ReconcileAsync(choiceContext);
    }

    /// <summary>给怪物类用：出完一张牌就对账一次（谁变了就同步另一边）。</summary>
    public static async Task ReconcileFor(CharBossMonster boss)
    {
        try
        {
            var power = boss.Creature?.GetPower<DeviationPower>();
            if (power != null) await power.ReconcileAsync(null);
        }
        catch (System.Exception e)
        {
            Logger.Log("[CharBoss] 偏差认知对账（外部调用）失败: " + e.Message);
        }
    }

    /// <summary>「衍生牌」判定（用户 2026-10-30：偏差认知只数**非衍生的**能力牌）。</summary>
    ///
    /// <remarks>
    /// 主判据是原版自己的稀有度 <see cref="CardRarity.Token"/>（= 衍生牌）：
    /// 反编译实测 `Awakened...Cards.Token.Ceremony`、`Snecko...Cards.Token.ViperEssence`
    /// （构造里写死 `(CardRarity)7`）、收藏家那批 `Collector...Cards.Token.*` 都落在这一类；
    /// 原版的衍生牌（君王之剑一类）同理，所以这条规则对原版也自洽。
    ///
    /// 保险（万一某个模组的 Token 牌没填稀有度）：
    ///   · 模型类型的命名空间里带 `.Token`；
    ///   · 所在卡池类型名带 `Token` / `Collectible`；
    ///   · 稀有度是 状态 / 诅咒 / 事件 / 任务（它们本来就不可能是能力牌，一起挡掉更稳）。
    /// </remarks>
    internal static bool IsDerivedCard(CardModel? card)
    {
        if (card == null) return false;

        try
        {
            var rarity = card.Rarity;
            if (rarity == CardRarity.Token) return true;
            if (rarity == CardRarity.Status || rarity == CardRarity.Curse ||
                rarity == CardRarity.Event || rarity == CardRarity.Quest) return true;
        }
        catch { /* 读不到稀有度就继续用下面两条保险 */ }

        try
        {
            string ns = card.GetType().Namespace ?? "";
            if (ns.Contains(".Token", System.StringComparison.Ordinal) ||
                ns.EndsWith("Token", System.StringComparison.Ordinal)) return true;
        }
        catch { /* 忽略 */ }

        try
        {
            string pool = card.Pool?.GetType().Name ?? "";
            if (pool.Contains("Token", System.StringComparison.Ordinal) ||
                pool.Contains("Collectible", System.StringComparison.Ordinal)) return true;
        }
        catch { /* 忽略 */ }

        return false;
    }

    /// <summary>「这张衍生牌被跳过」的日志每张牌只打一次。</summary>
    private static readonly HashSet<string> DerivedLogged = new(System.StringComparer.Ordinal);

    private static string SafeEntry(CardModel card)
    {
        try { return card.Id.Entry ?? "?"; } catch { return "?"; }
    }

    /// <summary>把力量与集中对成相等：判断是哪一边变了，把差值补给另一边。</summary>
    public async Task ReconcileAsync(PlayerChoiceContext? choiceContext)
    {
        if (_syncing) return;

        var owner = Owner;
        if (owner == null || !owner.IsAlive) return;

        int strength = AmountOf<StrengthPower>(owner);
        int focus = AmountOf<FocusPower>(owner);

        if (_lastStrength == int.MinValue)
        {
            _lastStrength = strength;
            _lastFocus = focus;
            // ★ v0.2.418（用户实测：「能力机为什么在玩家进战斗的时候它身上数据磁盘给的一点集中没了？」）：
            //   数据磁盘在战斗开始给她 +1 集中，而那时她力量是 0 ⇒ 以前这里按「focus > strength」
            //   走 `focusDelta: true, delta = strength - focus = -1` —— 也就是**把集中压回 0**
            //   （日志实证：`集中 0 → 1` 紧接着 `集中 1 → 0`）。遗物的效果就这样被吃掉了。
            //   现在改成**只抬高、不压低**：谁低就把谁抬到对方的数值。
            if (focus > strength)
            {
                Logger.Log($"[CharBoss] 偏差认知：初始对齐 集中 {focus} > 力量 {strength} ⇒ 力量抬到 {focus}");
                await SyncAsync(owner, focusDelta: false, focus - strength, choiceContext);
            }
            else if (strength > focus)
            {
                Logger.Log($"[CharBoss] 偏差认知：初始对齐 力量 {strength} > 集中 {focus} ⇒ 集中抬到 {strength}");
                await SyncAsync(owner, focusDelta: true, strength - focus, choiceContext);
            }
            return;
        }

        int dStrength = strength - _lastStrength;
        int dFocus = focus - _lastFocus;

        if (dStrength != 0 && dFocus == 0)
        {
            Logger.Log($"[CharBoss] 力量 {dStrength:+0;-0} → 同步集中");
            await SyncAsync(owner, focusDelta: true, dStrength, choiceContext);
        }
        else if (dFocus != 0 && dStrength == 0)
        {
            Logger.Log($"[CharBoss] 集中 {dFocus:+0;-0} → 同步力量");
            await SyncAsync(owner, focusDelta: false, dFocus, choiceContext);
        }
        else if (dStrength != 0 && dFocus != 0 && strength != focus)
        {
            // 两边同时变了（少见）：以力量为准把集中补齐
            await SyncAsync(owner, focusDelta: true, strength - focus, choiceContext);
        }
        else
        {
            _lastStrength = strength;
            _lastFocus = focus;
        }
    }

    private static int AmountOf<TPower>(Creature owner) where TPower : PowerModel
    {
        try { return owner.GetPower<TPower>()?.Amount ?? 0; }
        catch { return 0; }
    }

    /// <summary>
    /// 把差值补给**另一边**（不是两边同时加！）。
    /// 之前两边都加了一次，于是「获得 6 集中 → 同步 6 力量 → 6 力量变化又同步 6 集中」，
    /// 集中直接翻倍（用户实测：6 变 12），典型的左脚踩右脚。
    /// </summary>
    private async Task SyncAsync(Creature owner, bool focusDelta, int delta, PlayerChoiceContext? choiceContext)
    {
        if (delta == 0)
        {
            _lastStrength = AmountOf<StrengthPower>(owner);
            _lastFocus = AmountOf<FocusPower>(owner);
            return;
        }

        _syncing = true;
        try
        {
            var ctx = choiceContext ?? new BlockingPlayerChoiceContext();
            if (focusDelta)
            {
                await PowerCmd.Apply<FocusPower>(ctx, owner, delta, owner, null, false);
            }
            else
            {
                await PowerCmd.Apply<StrengthPower>(ctx, owner, delta, owner, null, false);
            }
        }
        catch (System.Exception e)
        {
            Logger.Log("[CharBoss] 偏差认知同步数值失败: " + e.Message);
        }
        finally
        {
            _lastStrength = AmountOf<StrengthPower>(owner);
            _lastFocus = AmountOf<FocusPower>(owner);
            _syncing = false;
        }
    }
}
