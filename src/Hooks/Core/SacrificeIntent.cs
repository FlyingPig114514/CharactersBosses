using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;

namespace CharacterBosses.Core;

/// <summary>
/// 「**牺牲**」意图：这名敌人将付出代价换取强力效果。
///
/// 形态完全复用**强化的蓝色向上箭头**，只是由 UI 层把图标上下翻转成向下的箭头
/// （<c>NIntent</c> 的图标是 <c>Sprite2D _intentSprite</c>，翻 <c>FlipV</c> 即可，
/// 不碰数字标签，也不自造任何美术）。所以这里只做三件事：
///   1. 继承 <see cref="BuffIntent"/>（图标/动画/尺寸全都是原版强制化那一套）；
///   2. 把 <c>IntentPrefix</c> 换成 <c>SACRIFICE</c>，让悬浮说明读我们自己的本地化文案
///      （`content/localization/{zhs,eng}/intents.json`）；
///   3. 把动画名固定回 `buff`，否则 <c>NIntent</c> 会去查 `sacrifice` 的动画帧而拿到空动画。
///
/// 什么时候用它（见 <see cref="BossIntentBuilder"/>）：
///   · 给自己上负面能力（偏差认知 / 幽魂形态 / 精神过载 / 友谊）；
///   · 扣自己的血（血墙 / 御血术）；
///   · 其他代价（暴涨失去充能球栏位、超能光束失去集中、子弹时间/战斗专注不能再抽牌）。
/// </summary>
internal sealed class SacrificeIntent : BuffIntent
{
    protected override string IntentPrefix => "SACRIFICE";

    /// <summary>沿用强化的箭头贴图，UI 层负责上下翻转。</summary>
    protected override string SpritePath => "atlases/intent_atlas.sprites/intent_buff.tres";

    /// <summary>动画名必须回落到原版的 `buff`，否则 <c>IntentAnimData</c> 查不到 `sacrifice` 的帧。</summary>
    public override string GetAnimation(IEnumerable<Creature> targets, Creature owner) => "buff";
}
