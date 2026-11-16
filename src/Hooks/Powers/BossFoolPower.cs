#if STS2_111   // ⚠ 0.113.0 才有「愚者」与整套塔罗牌 ⇒ 用户 2026-11-14：107 版本不加这个 Boss，整个文件只在 111 构建里编译
using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Core;

namespace CharacterBosses.Powers;

/// <summary>
/// 三层 1 号愚者 Boss「塔罗愚」的特殊效果「**愚者**」（用户 2026-11-14）：
///
/// > 愚者的卡组中只包含 3 张神话形态，杝主要使用塔罗牌进行战斗。
/// > 每当你获得灾厄时，若你有格挡，先用格挡抵消等量灾厄。
/// > PS：与灾厄骨的时候不同，这里是被施加灾厄的时候，如果玩家有格挡，就用格挡抵消等额灾厄，
/// >     玩家身上已经有的灾厄没法通过这种方式消除或减少。
///
/// 实现：挂在**她（他）自己**身上（`BossDefinition.SpecialPowerByAct`），
/// 走 `AbstractModel.ModifyPowerAmountGivenAdditive` —— 这个钩子在「**即将**把 power 层数给到某个目标」
/// 时被调用，返回值就是**实际会给的层数**。所以我们只改「这一次的施加量」：
///   · 只认 `DoomPower`（灾厄）、正向层数、目标是有 `Player` 的真玩家；
///   · 多人时只对「被他锁定的那位」生效（`BossLockOn.SpecialEffectAppliesTo`，单人恒成立）；
///   · 目标有格挡 ⇒ `LoseBlockInternal(min(格挡, 灾厄))` 扣掉等量格挡，并把灾厄等量减掉；
///   · **已经在他身上的灾厄完全不受影响**（这个钩子只在「施加」那一刻触发）。
/// </summary>
public sealed class BossFoolPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    /// <summary>
    /// 这次给出去的灾厄层数的**加值**。⚠ 名字里的 Additive 是**增量**、不是最终值：
    /// `AbstractModel` 里的默认实现就是 `return 0m`，调用方算的是 `amount + 本方法的返回值`。
    ///
    /// 所以这里**只能返回「要扣掉的负数」**：返回 `-absorb` ⇒ 实际施加量 = `amount - absorb`。
    /// （v0.2.657 的第一版误按「返回最终值」写、把 `amount` 原样返回 ⇒ 他身上**每一个** power
    ///   都被翻倍：神话形态一次 +2 层、力量一次 +10，用户实机一眼看出来。）
    /// </summary>
    public override decimal ModifyPowerAmountGivenAdditive(PowerModel power, Creature giver, decimal amount,
                                                          Creature target, CardModel cardSource)
    {
        // ── 临时诊断（v0.2.725，用户 2026-11-16：「他打死亡的那回合我的格挡会自己消失，
        //    打一张防牌刚获得的格挡就马上消失，控制台调格挡也会消失」）──
        // 现象是**格挡一有就被吃**，而本方法只在 `PowerCmd.Apply<DoomPower>` 里被调用。
        // 所以要么这个钩子被反复调用（预览？别的 mod 试算？），要么真有人在反复施加灾厄。
        // 这里把「调用次数 + 调用栈」打出来，下次一进日志就能定位是谁在叫。
        try
        {
            if (power is DoomPower)
            {
                _doomHookCalls++;
                if (_doomHookCalls <= 12 || _doomHookCalls % 25 == 0)
                {
                    string blockNow = "?";
                    try { blockNow = target?.Block.ToString() ?? "null"; } catch { /* 忽略 */ }
                    Logger.Log($"[CharBoss] 愚者·灾厄钩子第 {_doomHookCalls} 次：amount={amount} " +
                               $"target={target?.Name ?? "null"} 格挡={blockNow} " +
                               $"来源牌={SafeEntry(cardSource)}");
                    if (_doomHookCalls == 1)
                    {
                        try
                        {
                            var frames = (Environment.StackTrace ?? string.Empty)
                                .Split('\n');
                            int shown = 0;
                            foreach (var f in frames)
                            {
                                if (shown >= 14) break;
                                var t = f.Trim();
                                if (t.Length == 0) continue;
                                if (t.StartsWith("at System.Environment", StringComparison.Ordinal)) continue;
                                Logger.Log("[CharBoss] 愚者·灾厄钩子调用栈 " + (++shown) + ": " + t);
                            }
                        }
                        catch { /* 诊断失败不影响功能 */ }
                    }
                }
            }
        }
        catch { /* 诊断失败不影响功能 */ }

        try
        {
            if (power is not DoomPower) return 0m;
            if (amount <= 0m) return 0m;                     // 只处理「获得」灾厄
            if (target?.Player == null) return 0m;           // 只对真人玩家；召唤物/影子玩家不管

            var boss = Owner?.Monster as Monsters.CharBossMonster;
            if (!BossLockOn.SpecialEffectAppliesTo(boss, target.Player)) return 0m;

            decimal block = 0m;
            try { block = target.Block; } catch { block = 0m; }
            if (block <= 0m) return 0m;

            decimal absorb = Math.Min(block, amount);
            if (absorb <= 0m) return 0m;

            try { target.LoseBlockInternal(absorb); }
            catch (Exception e)
            {
                Logger.Log("[CharBoss] 愚者：扣格挡失败（这次不抵消）: " + e.Message);
                return 0m;
            }

            Logger.Log($"[CharBoss] 愚者：{target.Name} 用 {absorb} 点格挡抵消了 {absorb} 层灾厄" +
                       $"（原本 {amount} → {(amount - absorb)}）");
            return -absorb;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 愚者：格挡抵灾厄时出错（按原样施加）: " + e.Message);
            return 0m;
        }
    }

    /// <summary>诊断用：本场战斗里「灾厄钩子」被调用的次数。</summary>
    private static int _doomHookCalls;

    private static string SafeEntry(CardModel? card)
    {
        try { return card?.Id.Entry ?? "null"; } catch { return "?"; }
    }
}
#endif
