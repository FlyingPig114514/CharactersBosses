using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;

namespace CharacterBosses.Core;

/// <summary>
/// 「**诱惑**」意图：这名敌人将以**给予你正面效果**为代价换取强力效果。
///
/// 用户 2026-11-13 设计：
/// > 诱惑：这名敌人将以给予你正面效果为代价换取强力效果。
/// > 适用范围：会给予对方（这里就是玩家）正面效果的牌，例如「与我一战」（会给目标增加一点力量）。
/// > 图标：把**策略**的图标倒过来，就像牺牲是把强化的图标倒过来一样。
///
/// 反编译依据 —— 「与我一战」（`FightMe`）：
/// <code>
/// CanonicalVars = { DamageVar(5), HitCountVar(2), PowerVar&lt;StrengthPower&gt;(3), DynamicVar("EnemyStrength", 1) }
/// OnPlay: PowerCmd.Apply&lt;StrengthPower&gt;(ctx, cardPlay.Target, EnemyStrength, Owner, this);   // ← 给玩家 +1 力量
///         PowerCmd.Apply&lt;StrengthPower&gt;(ctx, Owner,              StrengthPower,  Owner, this);  // ← 自己 +3 力量
/// </code>
///
/// 实现方式与 <see cref="SacrificeIntent"/> 完全对称：
///   1. 继承 <see cref="DebuffIntent"/>（图标 / 尺寸 / 动画全是原版「策略」那一套）；
///   2. `IntentPrefix` 换成 `TEMPTATION` ⇒ 悬浮说明读我们自己的本地化文案
///      （`content/localization/&lt;lang&gt;/intents.json` 的 `TEMPTATION.title` / `.description`）；
///   3. 动画名固定回 `debuff`（否则 `NIntent` 会去找 `temptation` 的动画帧而拿到空动画）；
///   4. 由 UI 层把图标**上下翻转**（同牺牲）—— 见 `UI/BossHandCard.AddIntent`。
/// </summary>
internal sealed class TemptationIntent : DebuffIntent
{
    protected override string IntentPrefix => "TEMPTATION";

    /// <summary>沿用原版「策略」的红色箭头贴图，UI 层负责上下翻转。</summary>
    protected override string SpritePath => "atlases/intent_atlas.sprites/intent_debuff.tres";

    /// <summary>动画名必须回落到原版的 `debuff`，否则查不到动画帧、图标不会像其它意图那样上下浮动。</summary>
    public override string GetAnimation(IEnumerable<Creature> targets, Creature owner) => "debuff";

    public override string ToString() => "TemptationIntent";
}

#if STS2_111
/// <summary>
/// 「**审判**」意图：这名敌人将**立刻使你死亡**。
///
/// 用户 2026-11-13 设计：
/// > 审判：这名敌人将立刻使你死亡。
/// > 适用范围：具有秒杀性质的卡牌，例如新版本增加的衍生牌「审判」（只在符合秒杀条件，
/// > 即被该卡牌使用者锁定的玩家血量不高于 30 或 40 的时候对该玩家显示该意图，否则不显示该意图）。
/// > 目前似乎没有别的牌符合这个条件。（更低的版本不增加这个意图类型）
/// > 图标：使用 debuff 灾厄的图标。要让这个图标与其他的意图图标一样上下的移动。
///
/// 反编译依据 —— 「审判」（`Judgement`，0.113 新增的 `CardRarity.Token` 衍生牌，0 费技能、消耗）：
/// <code>
/// CanonicalVars = { IntVar("Threshold", 30) }           // 升级 OnUpgrade: +10 ⇒ 40
/// OnPlay: if (cardPlay.Target.IsAlive &amp;&amp; cardPlay.Target.CurrentHp &lt;= Threshold)
///             CreatureCmd.SetCurrentHp(cardPlay.Target, 0);   // ← 直接秒杀
/// </code>
///
/// 图标不用意图图集：直接借**「灾厄」（`DoomPower`）的能力图标**
/// （`ModelDb.Power&lt;DoomPower&gt;().PackedIconPath`，与 `Patches/BossSpecialPowerIconPatch`
///  借「好奇」图标是同一套做法）。动画名回落 `debuff` ⇒ 和别的意图一样上下浮动。
/// </summary>
internal sealed class JudgmentIntent : DebuffIntent
{
    protected override string IntentPrefix => "JUDGEMENT";

    /// <summary>兜底贴图（灾厄图标拿不到时仍有一个能显示的意图）。</summary>
    protected override string SpritePath => "atlases/intent_atlas.sprites/intent_debuff.tres";

    /// <summary>动画名回落 `debuff` —— 这是「图标像其它意图一样上下移动」的来源。</summary>
    public override string GetAnimation(IEnumerable<Creature> targets, Creature owner) => "debuff";

    /// <summary>图标：优先用原版「灾厄」的能力图标，取不到才退回策略箭头。</summary>
    public override Godot.Texture2D? GetTexture(IEnumerable<Creature> targets, Creature owner)
    {
        var doom = DoomIcon();
        if (doom != null) return doom;
        return base.GetTexture(targets, owner);
    }

    private static Godot.Texture2D? DoomIcon()
    {
        try
        {
            var doom = MegaCrit.Sts2.Core.Models.ModelDb.Power<MegaCrit.Sts2.Core.Models.Powers.DoomPower>();
            if (doom == null) return null;

            string path = doom.PackedIconPath;
            if (string.IsNullOrEmpty(path)) return null;
            if (!path.StartsWith("res://", System.StringComparison.Ordinal)) path = "res://images/" + path;

            return MegaCrit.Sts2.Core.Assets.PreloadManager.Cache.GetTexture2D(path);
        }
        catch (System.Exception e)
        {
            Logger.Log("[CharBoss] 审判意图：取灾厄图标失败（退回策略箭头）: " + e.Message);
            return null;
        }
    }

    public override string ToString() => "JudgmentIntent";
}
#endif
