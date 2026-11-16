#if STS2_111   // ⚠ 0.113.0 才有「愚者」与整套塔罗牌 ⇒ 用户 2026-11-14：107 版本不加这个 Boss，整个文件只在 111 构建里编译
using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
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
/// ## ★ v0.2.726 重做原因（用户 2026-11-16）
/// > 「我刚刚在和塔罗愚对战，为什么他打死亡的那回合我的格挡会自己消失？
/// >   我打一张防牌然后刚获得的格挡就马上消失了，我拿控制台调格挡也会消失」
///
/// 旧实现把「扣格挡」写在了 <see cref="ModifyPowerAmountGivenAdditive"/> 里。反编译
/// `PowerCmd.Apply`（`sts2.dll`）的调用顺序是：
/// <code>
/// await Hook.BeforePowerAmountChanged(...);
/// modifiedAmount = Hook.ModifyPowerAmountGiven(...);   ← 我们的钩子在这里（**只要有人算一次就会调用**）
/// ...TryModifyPowerAmountReceived / BeforeApplied / AfterApplied...
/// await Hook.AfterPowerAmountChanged(..., modifiedAmount, ...);   ← 真的挂上去了才会走到这里
/// </code>
/// 也就是说这个「给定层数」钩子**并不保证后面真的会施加**：任何「先算一下会加多少」的预览/试算
/// 路径（UI 重绘、别的 mod 的意图预测…）都会把它调用一遍 —— 而旧实现**顺手就把玩家的格挡扣了**。
/// 实机日志正好是这个症状：`愚者：静默猎手 用 48 点格挡抵消了 48 层灾厄（原本 75 → 27）`
/// 在一次 `PLAY DEATH` 之前就出现了 3 次（22:25:58 规划期 / 22:26:12 / 22:26:21 玩家回合中），
/// 而她第 7 回合出手前的玩家快照里**根本没有 DOOM_POWER** ⇒ 那几次灾厄一次都没真正加上去，
/// 只有格挡被扣掉了。所以玩家一打防牌，格挡就在下一次预览时被吃掉，控制台加的格挡同样保不住。
///
/// 现在：**「给定层数」钩子只读不写**（永远返回 0，绝不改任何状态）；
/// 真正扣格挡 + 减灾厄放到 <see cref="AfterPowerAmountChanged"/>（只有真的挂上了才会触发），
/// 那里 `amount` 就是这次实际给出的层数，`power.Owner` 就是被施加的那位，
/// 于是「只减这次新增的量、已经存在的灾厄一点不动」这条口径天然满足。
/// </summary>
public sealed class BossFoolPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    /// <summary>
    /// **只读**：这里一个副作用都不能有（见类注释）。返回 0 =「不改变这次要给的层数」，
    /// 真正的「格挡抵灾厄」在 <see cref="AfterPowerAmountChanged"/> 里做。
    ///
    /// 诊断计数器保留：`_hookCalls` = 这个钩子被调用的次数（包含预览），
    /// `_applied` = 真的挂上灾厄的次数。两者差得越大，说明「有人在白白试算」越频繁。
    /// </summary>
    public override decimal ModifyPowerAmountGivenAdditive(PowerModel power, Creature giver, decimal amount,
                                                          Creature target, CardModel cardSource)
    {
        try
        {
            if (power is DoomPower)
            {
                _hookCalls++;
                if (_hookCalls <= 6 || _hookCalls % 40 == 0)
                {
                    Logger.Log($"[CharBoss] 愚者·灾厄试算第 {_hookCalls} 次（只读，不改任何状态）：" +
                               $"amount={amount} target={target?.Name ?? "null"} " +
                               $"格挡={SafeBlock(target)} 来源={SafeEntry(cardSource)}" +
                               $"｜真正挂上过 {_applied} 次");
                }
            }
        }
        catch { /* 诊断失败不影响功能 */ }
        return 0m;
    }

    /// <summary>
    /// 灾厄**真的**挂到某个真人玩家身上之后：用他的格挡抵消等量灾厄（只抵消这一次新增的那部分）。
    ///
    /// `amount` = 这次实际给的层数（已过 `RuinedHelmet` 之类的「收到量」修正），
    /// `power.Owner` = 被施加的那位生物；`power.Amount` = 叠加后的总数。
    /// </summary>
    public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power,
                                                       decimal amount, Creature applier, CardModel cardSource)
    {
        try
        {
            if (power is not DoomPower) return;
            if (amount <= 0m) return;                       // 只处理「获得」灾厄（减灾厄的调用跳过）

            var target = power.Owner;
            if (target?.Player == null) return;             // 只对真人玩家；召唤物/影子玩家不管

            var boss = Owner?.Monster as Monsters.CharBossMonster;
            if (!BossLockOn.SpecialEffectAppliesTo(boss, target.Player)) return;

            decimal block = SafeBlock(target);
            if (block <= 0m) return;

            decimal absorb = Math.Min(block, amount);
            if (absorb <= 0m) return;

            try { target.LoseBlockInternal(absorb); }
            catch (Exception e)
            {
                Logger.Log("[CharBoss] 愚者：扣格挡失败（这次不抵消）: " + e.Message);
                return;
            }
            // 抵消掉的那部分灾厄从总数里减掉（`SetAmount` 静默，不走完整的「移除能力」链路）
            try { power.SetAmount(Math.Max(0, power.Amount - (int)absorb), true); }
            catch (Exception e) { Logger.Log("[CharBoss] 愚者：减灾厄层数失败: " + e.Message); }

            _applied++;
            Logger.Log($"[CharBoss] 愚者：{target.Name} 用 {absorb} 点格挡抵消了 {absorb} 层灾厄" +
                       $"（这次给 {amount} → 实际 {Math.Max(0m, amount - absorb)}，当前灾厄 {power.Amount}）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 愚者：格挡抵灾厄时出错（灾厄按原样保留）: " + e.Message);
        }
        await Task.CompletedTask;
    }

    // ── 诊断计数器（v0.2.726 起只做观测，不再影响任何结算）──────────────
    private static int _hookCalls;
    private static int _applied;

    private static decimal SafeBlock(Creature? creature)
    {
        try { return creature?.Block ?? 0m; } catch { return 0m; }
    }

    private static string SafeEntry(CardModel? card)
    {
        try { return card?.Id.Entry ?? "null"; } catch { return "?"; }
    }
}
#endif
