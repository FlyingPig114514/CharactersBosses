using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Random;

namespace CharacterBosses.Monsters;

/// <summary>
/// 非招式状态：每次进入时做本回合的计划，然后转到出牌招式。
/// 之所以用状态机而不是 Harmony，是因为 <see cref="MonsterState.GetNextState"/> 是 virtual，
/// 而 <c>MonsterModel.RollMove</c> 恰好会调用它 —— 正好是「玩家回合开始前」这个时机。
/// </summary>
public sealed class BossThinkState : MonsterState
{
    public override string Id => CharBossMonster.ThinkStateId;

    public override string GetNextState(Creature owner, Rng rng)
    {
        if (owner.Monster is CharBossMonster boss)
        {
            boss.PrepareTurn();
        }
        return CharBossMonster.PlayStateId;
    }

    public override void RegisterStates(Dictionary<string, MonsterState> monsterStates)
    {
        monsterStates[Id] = this;
    }
}
