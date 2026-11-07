using System;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Runs;

namespace CharacterBosses.Core;

/// <summary>
/// 「Boss 现在打的是第几幕」的唯一口径（0 = 第一幕，1 = 第二幕，2 = 第三幕）。
///
/// 优先取**当前这一幕**的 <c>ActModel.Index</c>（密林与暗港都是第一幕、Index 都是 0），
/// 这样与 <c>CharBossEncounters.ActTable</c> 的行号是同一个来源、必然一致；
/// 拿不到再退回幕数位置 <c>RunState.CurrentActIndex</c>。
///
/// **致命坑（吃过一次，进战斗直接黑屏）**：<c>MonsterModel.CombatState</c> 在怪物还没创建完时
/// 抛 <c>InvalidOperationException("Creature was accessed before it was set.")</c>，
/// 而 <c>Creature</c> 的构造函数恰好在这个阶段问 <c>MinInitialHp</c>。
/// 所以这里**不能**直接接收 <c>CombatState</c> 的值（那会在进入本方法之前就抛出去），
/// 必须接收一个延迟取值的委托，把取值动作放进 try 里；拿不到就改问 <c>RunManager.Instance.State</c>。
/// </summary>
public static class BossActIndex
{
    private static bool _loggedFailure;

    public static int Resolve(Func<ICombatState?> combatState)
    {
        try
        {
            var run = combatState()?.RunState;
            var act = run?.Act;
            if (act != null) return Math.Clamp(act.Index, 0, 2);
            if (run != null) return Math.Clamp(run.CurrentActIndex, 0, 2);
        }
        catch (Exception e)
        {
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                Logger.Log("[CharBoss] act index via combat state failed, falling back to RunManager: " + e.Message);
            }
        }

        // 怪物创建过程中（Creature 还没赋值）只能问全局的 RunState
        return FromRunManager();
    }

    private static int FromRunManager()
    {
        try
        {
            // RunManager.State 在引用程序集里是 internal（游戏内部用），对外公开的是这个取值方法
            var run = RunManager.Instance?.DebugOnlyGetState();
            var act = run?.Act;
            if (act != null) return Math.Clamp(act.Index, 0, 2);
            return Math.Clamp(run?.CurrentActIndex ?? 0, 0, 2);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] act index fallback failed: " + e.Message);
            return 0;
        }
    }
}
