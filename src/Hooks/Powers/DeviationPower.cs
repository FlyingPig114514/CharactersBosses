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
    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        try
        {
            var owner = Owner;
            if (owner == null || !owner.IsAlive) return;

            var card = cardPlay?.Card;
            if (card == null || card.Type != CardType.Power) return;
            if (card.Owner?.Creature == owner) return;   // Boss 自己打的能力牌不算

            // ★ v0.2.249（用户 2026-10-30）：**衍生的能力牌不算**。
            //   用户原话：「你每打出 1 张**非衍生的**能力牌……」；衍生牌不止觉醒者的
            //   祭仪 `Ceremony` / 火热研究 `BurningStudy`，还包括**收藏家的藏品牌**、
            //   异蛇的蛇之精髓 `ViperEssence` 等等（原版也有君王之剑之类的衍生牌，
            //   但似乎没有「衍生的能力牌」，所以这条对原版等于无影响）。
            //
            //   判定优先用原版自己的稀有度 **`CardRarity.Token`** —— 那就是「衍生牌」：
            //   反编译确认 `Snecko...Cards.Token.ViperEssence` 构造里就是 `(CardRarity)7` = Token，
            //   崩坠那批 `*.Cards.Token.*` 同理；再叠两条保险：
            //     · 模型命名空间里带 `.Token`（有些模组会漏填稀有度）；
            //     · 所在卡池是 `*Collectible*` 之类（收藏家藏品牌）。
            if (IsDerivedCard(card))
            {
                string entry = SafeEntry(card);
                if (DerivedLogged.Add(entry))
                {
                    Logger.Log($"[CharBoss] 偏差认知：{entry} 是**衍生牌** ⇒ 不给 Boss 集中" +
                               "（衍生牌不算「非衍生的能力牌」）");
                }
                return;
            }

            // ⚠ 用户 2026-10-29：**取消**对崩坠「觉醒者」两张衍生能力牌
            // （祭仪 `Ceremony` / 火热研究 `BurningStudy`）的特判 ——
            // 也就是说现在**任何**能力牌都算（描述已改成「你每打出一张能力牌…」）。
            // 历史原因见 git 之外的 STATUS v0.2.189 一节：那两张本质是「加力量」的产物，
            // 早先怕白送 Boss 集中所以豁免；用户评估后决定不再例外。

            // **对应型**（多人）：只有「锁定这只 Boss」的真人玩家打出的能力牌才算
            // （单人时 SpecialEffectAppliesTo 恒为 true，行为不变）。
            if (!Core.BossLockOn.SpecialEffectAppliesTo(owner.Monster as CharBossMonster, card.Owner)) return;

            Logger.Log($"[CharBoss] 偏差认知：玩家打出能力牌 {card.Id.Entry} → Boss +1 集中");
            await PowerCmd.Apply<FocusPower>(choiceContext, owner, 1m, owner, null, false);
            await ReconcileAsync(choiceContext);
        }
        catch (System.Exception e)
        {
            Logger.Log("[CharBoss] 偏差认知（玩家出能力牌）失败: " + e.Message);
        }
    }

    public override async Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants,
                                                  ICombatState combatState)
    {
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
                await PowerCmd.Apply<FocusPower>(ctx, owner, delta, owner, null, true);
            }
            else
            {
                await PowerCmd.Apply<StrengthPower>(ctx, owner, delta, owner, null, true);
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
