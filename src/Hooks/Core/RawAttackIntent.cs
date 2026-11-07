using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;

namespace CharacterBosses.Core;

/// <summary>
/// **「多段 vs 单段」的唯一出口**：段数 ≤ 1 时一律退回**单段**意图。
///
/// 为什么需要（用户 2026-10-28 实测报的两个显示 bug）：
///   · 根除骨的「戳击」（奥斯提 6 伤）显示成 **6×1**；
///   · 刀猎开着「幻影刀刃」那回合打「隐秘匕首」（2 把刀，第一把 +9）显示成 **16、7×1**。
/// 根因是同一类写法：先算出段数、再**无条件**建多段意图，段数正好是 1 时就露出多余的「×1」。
/// 原版只有真的多段（4×2）才该出现乘号，所以这里统一收口，别再各处手写三元表达式。
/// </summary>
internal static class AttackIntent
{
    /// <summary>普通多段意图（**吃**原版伤害管道：力量/虚弱/易伤由节点自己算）。段数 ≤ 1 ⇒ 单段。</summary>
    internal static AbstractIntent Multi(int perHit, int times)
        => times > 1 ? new MultiAttackIntent(perHit, times) : new SingleAttackIntent(perHit);

    /// <summary>Raw 多段意图（数字**原样**显示，不给 <c>Hook.ModifyDamage</c> 机会）。段数 ≤ 1 ⇒ 单段。</summary>
    internal static AbstractIntent Raw(int perHit, int times)
        => times > 1 ? new RawMultiAttackIntent(perHit, times) : new RawAttackIntent(perHit);
}

/// <summary>
/// 显示**原样数字**的攻击意图 —— 不给 <c>Hook.ModifyDamage</c> 再加工的机会。
///
/// 为什么需要它：原版 <c>AttackIntent.GetSingleDamage</c> 会对喂进去的数字**再跑一次**
/// <c>Hook.ModifyDamage</c>（力量、易伤…），所以「数字本来就该原样显示」的效果躲不开加成。
///
/// 目前唯一的用处是**冰雹风暴**（<c>HailstormPower</c>）：
///
/// <code>
/// // HailstormPower.BeforeSideTurnEnd
/// int frost = Owner.Player.PlayerCombatState.OrbQueue.Orbs.Count(o => o is FrostOrb);
/// if (frost >= DynamicVars["FrostOrbs"].IntValue)
///     await CreatureCmd.Damage(ctx, CombatState.HittableEnemies, Amount, ValueProp.Unpowered, Owner);
/// </code>
///
/// 也就是「冰球够数时，造成 **层数** 点伤害，且是 Unpowered（不吃力量/集中）」——
/// 所以意图上的数字必须等于层数本身。
///
/// 做法：<c>SingleAttackIntent.GetTotalDamage</c> 是 <c>override</c>（可再覆盖），
/// 而原版 <c>NIntent.UpdateVisuals</c> 的数字取自 <c>attackIntent.GetIntentLabel(...)</c>、
/// 图标取自 <c>GetTexture(...)</c>，两者内部都用 <c>GetTotalDamage</c> →
/// 覆盖它就同时修好了「数字」和「图标档位」，而且用的仍是原版意图节点。
/// </summary>
internal sealed class RawAttackIntent : SingleAttackIntent
{
    private readonly int _damage;

    public RawAttackIntent(int damage) : base(damage)
    {
        _damage = Math.Max(0, damage);
    }

    public override int GetTotalDamage(IEnumerable<Creature> targets, Creature owner) => _damage;

    /// <summary>打乱顺序（为了日志好认）。</summary>
    public override string ToString() => $"RawAttackIntent({_damage})";
}

/// <summary>
/// 显示**原样数字的多段**攻击意图（<c>14×2</c> 这种），同样不给 <c>Hook.ModifyDamage</c> 机会。
///
/// 用途：充能球激发的伤害（尤其双重释放对电球激发 2 次）。
/// 原版 <see cref="MultiAttackIntent"/> 的可见文字取自 <c>GetIntentLabel</c>、
/// 而它内部用的是 <c>GetSingleDamage</c>（会跑 ModifyDamage，且**不是 virtual**，覆盖不了）。
/// 所以这里直接覆盖 <c>GetIntentLabel</c>（拿同一份 <c>FORMAT_DAMAGE_MULTI</c> 模板）
/// 与 <c>GetTotalDamage</c>（决定图标档位）两者，数字就是（单次伤害 × 次数）。
/// </summary>
internal sealed class RawMultiAttackIntent : MultiAttackIntent
{
    private readonly int _perHit;
    private readonly int _times;

    public RawMultiAttackIntent(int perHit, int times) : base(Math.Max(0, perHit), Math.Max(1, times))
    {
        _perHit = Math.Max(0, perHit);
        _times = Math.Max(1, times);
    }

    public override int Repeats => _times;

    public override int GetTotalDamage(IEnumerable<Creature> targets, Creature owner) => _perHit * _times;

    public override LocString GetIntentLabel(IEnumerable<Creature> targets, Creature owner)
    {
        var format = IntentLabelFormat;
        format.Add("Damage", (decimal)_perHit);
        format.Add("Repeat", (decimal)_times);
        return format;
    }

    public override string ToString() => $"RawMultiAttackIntent({_perHit}×{_times})";
}

/// <summary>
/// 「每次渲染都重算」的**单段**攻击意图 —— 和 <see cref="LiveMultiAttackIntent"/> 同一套思路，
/// 数字在**画出来的那一刻**用原版伤害管道算一次。
///
/// 为什么需要它（用户 2026-10-27 反馈：一层碎屑储君的「彗星」吃虚弱后预览 33 → 16，
/// 而实际只减 25%；在此之前的 2026-10-26 炼药猎也是同一类「预览和实伤对不上」）：
///
/// 原版 <c>SingleAttackIntent.GetTotalDamage</c> = <c>GetSingleDamage</c>，它会把我们喂进去的
/// 数字再跑一遍 <c>Hook.ModifyDamage</c>。而我们这边**也可能已经算过一次**（例如 X 费/奥斯提那几条
/// 走的就是自己的管道）—— 两套并存时就可能出现「预览比实伤多减一次」。
///
/// 这里改成：喂**牌面基础值**，由 <see cref="BossDamagePredictor.FinalDamage"/> 在渲染时
/// 跑**一次**原版管道，然后把结果原样交给意图节点（<c>GetTotalDamage</c>/<c>GetIntentLabel</c>
/// 都覆盖掉，节点不再二次加工）。于是「预览 = 实伤管道的一次结果」，不再有第二遍。
/// </summary>
internal sealed class LiveSingleAttackIntent : SingleAttackIntent
{
    private readonly MegaCrit.Sts2.Core.Models.CardModel _card;
    private readonly int _base;

    private int _cacheFrame = -1;
    private int _cache;

    public LiveSingleAttackIntent(MegaCrit.Sts2.Core.Models.CardModel card, int baseDamage)
        : base(Math.Max(0, baseDamage))
    {
        _card = card;
        _base = Math.Max(0, baseDamage);
    }

    public override int GetTotalDamage(System.Collections.Generic.IEnumerable<Creature> targets, Creature owner)
        => Value(targets, owner);

    public override LocString GetIntentLabel(System.Collections.Generic.IEnumerable<Creature> targets, Creature owner)
    {
        var format = IntentLabelFormat;
        format.Add("Damage", (decimal)Value(targets, owner));
        return format;
    }

    /// <summary>同一帧只算一次（节点会连着问 label / 总伤害）。</summary>
    private int Value(System.Collections.Generic.IEnumerable<Creature>? targets, Creature? owner)
    {
        int frame;
        try { frame = (int)Godot.Engine.GetFramesDrawn(); }
        catch { frame = -1; }
        if (frame >= 0 && frame == _cacheFrame) return _cache;

        try
        {
            Creature? victim = null;
            try
            {
                foreach (var t in targets ?? Array.Empty<Creature>())
                {
                    if (t == null) continue;
                    if (t.Monster != null) continue;      // 只认真人玩家
                    victim = t;
                    break;
                }
            }
            catch { /* 忽略 */ }
            _cache = Math.Max(0, BossDamagePredictor.FinalDamage(_card, _base, victim));
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 动态单段意图重算失败（保留上一次的数）: " + e.Message);
        }
        _cacheFrame = frame;
        return _cache;
    }

    public override string ToString() => $"LiveSingleAttackIntent({_cache})";
}

/// <summary>
/// 「每次渲染都重算」的**单段**攻击意图 —— 数字由调用方给的**委托**算（同一帧内缓存）。
///
/// 为什么需要它（用户 2026-11-04 截图：「玩家有易伤，刀刃之舞还是 4×3」）：
///   小刀那条意图原来是 `RawAttackIntent`（数值在规划那一刻就**冻住**了），
///   而玩家是**后来**才被上易伤的 ⇒ 那条永远是 4，而同时的「冲刺」走
///   <see cref="LiveSingleAttackIntent"/>（渲染时重算）显示的是 15 = 10×1.5。
///   `LiveMultiAttackIntent` 能传委托，但它画的是「A×N」；段数只有 1 时必须画成单段
///   （否则「隐秘匕首」2 把刀会显示成 16、7×1 —— 用户 2026-10-28 实测过）。
///   所以补这一个「带委托的单段」版本。
/// </summary>
internal sealed class LiveDelegateSingleAttackIntent : SingleAttackIntent
{
    private readonly Func<System.Collections.Generic.IEnumerable<Creature>?, Creature?, int> _calc;

    private int _cacheFrame = -1;
    private int _cache;

    public LiveDelegateSingleAttackIntent(Func<System.Collections.Generic.IEnumerable<Creature>?, Creature?, int> calc)
        : base(0)
    {
        _calc = calc;
    }

    public override int GetTotalDamage(System.Collections.Generic.IEnumerable<Creature> targets, Creature owner)
        => Value(targets, owner);

    public override LocString GetIntentLabel(System.Collections.Generic.IEnumerable<Creature> targets, Creature owner)
    {
        var format = IntentLabelFormat;
        format.Add("Damage", (decimal)Value(targets, owner));
        return format;
    }

    private int Value(System.Collections.Generic.IEnumerable<Creature>? targets, Creature? owner)
    {
        int frame;
        try { frame = (int)Godot.Engine.GetFramesDrawn(); }
        catch { frame = -1; }
        if (frame >= 0 && frame == _cacheFrame) return _cache;

        try { _cache = Math.Max(0, _calc(targets, owner)); }
        catch (Exception e) { Logger.Log("[CharBoss] 动态单段（委托）意图重算失败（保留上一次的数）: " + e.Message); }
        _cacheFrame = frame;
        return _cache;
    }

    public override string ToString() => $"LiveDelegateSingleAttackIntent({_cache})";
}

/// <summary>
/// 「每次渲染都重算」的多段攻击意图 —— 数字在**画出来的那一刻**才算。
///
/// 为什么需要它（用户 2026-10-23 根除骨截图）：普通意图节点每帧都会重跑
/// <c>Hook.ModifyDamage</c>，所以玩家在她回合外给她减力量，显示会立刻跟着变；
/// 而我们把「根除」这类 X 费多段意图的每段伤害**在玩家回合开始就算死并冻住了**
/// （`RawMultiAttackIntent` 存的是一个 int） ⇒ 玩家用崩坠「哀嚎」减了她 11 点力量，
/// 意图还是 10×4（应该 0×4）。
///
/// 这里改成传一个**计算委托**：<see cref="GetIntentLabel"/> / <see cref="GetTotalDamage"/> /
/// <see cref="Repeats"/> 每次被问到时都重新算一遍（同一帧内缓存，避免每帧算几十次
/// `Hook.ModifyDamage`）。计算本身走 <see cref="BossDamagePredictor.FinalDamage"/>，
/// 也就是原版那条伤害管道 ⇒ 原版力量、虚弱、易伤、以及**任何模组**的临时/永久减力量都算进来。
/// </summary>
internal sealed class LiveMultiAttackIntent : MultiAttackIntent
{
    private readonly Func<IEnumerable<Creature>?, Creature?, (int PerHit, int Times)> _calc;

    private int _cacheFrame = -1;
    private int _cachePerHit;
    private int _cacheTimes = 1;

    public LiveMultiAttackIntent(Func<IEnumerable<Creature>?, Creature?, (int, int)> calc)
        : base(0, 1)
    {
        _calc = calc;
    }

    public override int Repeats => Value(null, null).Times;

    public override int GetTotalDamage(IEnumerable<Creature> targets, Creature owner)
    {
        var (perHit, times) = Value(targets, owner);
        return perHit * times;
    }

    public override LocString GetIntentLabel(IEnumerable<Creature> targets, Creature owner)
    {
        var (perHit, times) = Value(targets, owner);
        var format = IntentLabelFormat;
        format.Add("Damage", (decimal)perHit);
        format.Add("Repeat", (decimal)times);
        return format;
    }

    /// <summary>同一帧只算一次（意图节点会连着问 label / 总伤害 / 段数）。</summary>
    private (int PerHit, int Times) Value(IEnumerable<Creature>? targets, Creature? owner)
    {
        int frame;
        try { frame = (int)Godot.Engine.GetFramesDrawn(); }
        catch { frame = -1; }

        if (frame >= 0 && frame == _cacheFrame) return (_cachePerHit, _cacheTimes);

        try
        {
            var (perHit, times) = _calc(targets, owner);
            _cachePerHit = Math.Max(0, perHit);
            _cacheTimes = Math.Max(1, times);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 动态意图重算失败（保留上一次的数）: " + e.Message);
        }
        _cacheFrame = frame;
        return (_cachePerHit, _cacheTimes);
    }

    public override string ToString() => $"LiveMultiAttackIntent({_cachePerHit}×{_cacheTimes})";
}
