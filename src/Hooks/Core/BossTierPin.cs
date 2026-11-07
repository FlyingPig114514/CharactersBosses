namespace CharacterBosses.Core;

/// <summary>
/// 「这个遭遇把强度档位钉死在第几幕」。
///
/// 正常流程里选谁、用哪一档，全靠**当前幕数**（<see cref="BossActIndex"/>）——
/// 同一个 <c>NECROBINDER_BOSS_ENCOUNTER</c>，第一幕是灾厄骨、第二幕是灵魂骨、第三幕是根除骨。
/// 这让「用控制台直接打某一只」变得很别扭：得先 <c>act 2</c> 才能打到灵魂骨。
///
/// 所以给**每只 Boss 设计**再配一个自己的遭遇（各自一个 id），由它把档位钉死：
/// <c>fight NECROBINDER_SOUL_BOSS_ENCOUNTER</c> 不管在第几幕，永远是二层灵魂骨
/// （血量 / 卡组 / 遗物 / 剧本 / 能量全按第二幕那一档）。
///
/// 消费方是 <see cref="Monsters.CharBossMonster.ActIndex"/>：
/// 先看调试文件的强制档位，再看这个钉子，最后才按真实幕数走。
/// </summary>
public interface IBossTierPin
{
    /// <summary>钉死的档位（0 = 第一幕，1 = 第二幕，2 = 第三幕）；<c>null</c> = 不钉死，跟着当前幕走。</summary>
    int? TierAct { get; }
}
