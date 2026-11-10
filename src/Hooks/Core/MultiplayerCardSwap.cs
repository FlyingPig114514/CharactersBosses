using System;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using CharacterBosses.Monsters;

namespace CharacterBosses.Core;

/// <summary>
/// **多人专属换牌**（设计见 <c>docs/plans/multiplayer-cards.md</c>）。
///
/// 只做一件事：在**多人**（玩家数 ≥ 2）时，把剧本/卡组里的某张牌换成另一张牌。
/// 单人（或人数拿不到）时**原样返回**，与现有行为完全一致。
///
/// ⚠ 这里只负责「换成哪张牌」；换上的牌**效果要落在 Boss 侧**（不给玩家收益），
/// 那部分需要在各自的效果上单独实现（见 docs 里三条的「下游落点」）。
///
/// 加新条目只需要在 <see cref="Table"/> 里加一行：`(actIndex, bossType, from, to)`。
/// </summary>
public static class MultiplayerCardSwap
{
    /// <summary>(幕下标, Boss 类型, 原牌, 换成)。幕下标 2 = 第三幕（0 起）。</summary>
    private static readonly (int Act, Type Boss, Type From, Type To)[] Table =
    {
        // A1. 能力机（三层故障机器人）：冰川 → 休眠（当回合冰球给所有 Boss 格挡，不给玩家）
#if STS2_111
        (2, typeof(DefectBoss), typeof(Glacier), typeof(Hibernate)),
#endif

        // A10. 血肉骨（一层亡灵契约师，用户 2026-11-10）：吸引仇恨 → 骸骨军团
        //      （用户：「多人：将吸引仇恨替换为骸骨军团（升级与否相同）」⇒ 不进 UpgradeRows）。
        //      两张都是 2 费 ⇒ 第 5/7 回合的能量账不变；单人的灾厄骨剧本里没有吸引仇恨，
        //      所以这条只对血肉骨生效。
        (0, typeof(NecrobinderBoss), typeof(PullAggro), typeof(LegionOfBone)),

        // A2. 根除骨（三层亡灵契约师）：吸引仇恨 → 骸骨军团（只给 Boss 奥斯提）
        (2, typeof(NecrobinderBoss), typeof(PullAggro), typeof(LegionOfBone)),

        // A3. 刀猎（三层静默猎手·刀）：斗篷与匕首 → 刀刃交响曲（队友打小刀）
        // 注意：静默猎手只有一个怪物类 SilentBoss，涂毒猎/刀猎是靠「幕 + 遭遇」区分，所以这里靠 Act==2 定位刀猎。
#if STS2_111
        (2, typeof(SilentBoss), typeof(CloakAndDagger), typeof(BladeSymphony)),
#endif

        // A4. 物理机（二层故障机器人）：冷却剂 → 一心化万（用户 2026-10-24 设计：
        //     第 4 回合那张「冷却剂（单人）/ 一心化万（多人）」）。
        //     一心化万是原版牌，效果（所有 0 费攻击牌 +3）对双方都生效，不需要额外落点。
#if STS2_111
        (1, typeof(DefectBoss), typeof(Coolant), typeof(OneForAll)),   // 冷却剂 → 一心化万（107 没这张牌 ⇒ 不换）
#endif

        // A9. 彩虹机（二层故障机器人·充能球轴，用户 2026-11-07 设计）：
        //     第 1 回合「碎片整理（单人）/ 模仿学习（多人）」—— 单人没有别的玩家可模仿，
        //     所以只在**多人**换成「模仿学习」。两张都是 1 费，能量账不变。
#if STS2_111   // 0.107 没有「模仿学习」⇒ 不在换牌表里
        (1, typeof(DefectBoss), typeof(Defragment), typeof(ImitationLearning)),
#endif

        // A5. 奇巧猎（第一幕静默猎手·奇巧轴，用户 2026-10-26 设计）：
        //     手里那张稀有牌 余像（单人，1 费打出）→ 鬼祟+（多人，2 费打不起，靠奇巧被丢弃后免费打出）。
        //     「鬼祟+」的升级由下面的 <see cref="UpgradeRows"/> 负责。
        //     ⚠ 换牌只换「是哪张牌」；「这张被丢弃而不是打出」由剧本的
        //     `ScriptCard.DiscardInsteadWhenMultiplayer` 声明。
        (0, typeof(SilentBoss), typeof(Afterimage), typeof(Sneaky)),

        // A7. 放血战（第二幕铁甲战士，用户 2026-10-27 设计）：
        //     剧本第 1／7 回合那张「全身撞击+」在**多人**时换成「恶魔护盾+」
        //     （0 费，把她当前的格挡给一位已行动、格挡最少的友方 Boss）。
        //     换上的那张**必须升级**（设计稿写的就是「恶魔护盾+」）见下面的 UpgradeRows。
        (1, typeof(IroncladBoss), typeof(BodySlam), typeof(DemonicShield)),
        // A6. 黑球机（第一幕故障机器人·暗黑球轴，用户 2026-10-27）：冰川 → **休眠**。
        //     两张都是 2 费技能、都生成 2 个冰霜球 ⇒ 能量账不变；
        //     区别是休眠的「本回合冰球给所有队友格挡」在我方阵营 = 给**所有 Boss** 格挡（不给玩家）。
#if STS2_111
        (0, typeof(DefectBoss), typeof(Glacier), typeof(Hibernate)),
#endif

        // A8. 事件机（三层故障机器人·事件牌轴，用户 2026-11-03 设计）：
        //     第 3 回合「疯狂科学」印出来的那张 —— 单人 = 启动流程，**多人 = 能量涌动**。
        //     两张都是剧本里写 0 费打出的（`ScriptCard.CostThisTurn`），换牌不影响能量账。
        //     ⚠ 只有第三幕的故障机器人有「启动流程」，所以这条不会波及能力机（它的剧本里没有它）。
        (2, typeof(DefectBoss), typeof(BootSequence), typeof(EnergySurge)),
    };

    /// <summary>
    /// 换牌时**顺带把这几种牌变成升级版**：<c>(幕下标, Boss 类型, 原牌)</c>。
    /// 目前只有奇巧猎的「余像 → 鬼祟+」（设计稿明确写的是「鬼祟**+**」）。
    /// </summary>
    private static readonly (int Act, Type Boss, Type From, int MinAscension)[] UpgradeRows =
    {
        (0, typeof(SilentBoss), typeof(Afterimage), 0),
        (1, typeof(IroncladBoss), typeof(BodySlam), 0),   // 放血战：换上的「恶魔护盾」是升级版
        // 用户 2026-10-31：**多人 + 进阶 9 及以上**时，刀猎的「斗篷与匕首 → 刀刃交响曲」那张要升级
        // （2 费 → 1 费）。配合第 1 回合 3 费（灯笼），第 1 回合因此打得出一张防御。
        (2, typeof(SilentBoss), typeof(CloakAndDagger), 9),
    };

    /// <summary>这一档是不是**多人局**（≥2 名真人玩家）。单人恒 false ⇒ 一切原样。</summary>
    public static bool IsMultiplayer(CharBossMonster? boss = null)
    {
        try
        {
            if (!MultiBossPlan.WantsSecondBoss(out int players)) return false;
            return players >= 2;
        }
        catch { return false; }
    }

    /// <summary>这张剧本牌在多人局换上之后要不要**强制升级**（奇巧猎：鬼祟 → 鬼祟+）。</summary>
    public static bool ForcesUpgrade(Type scripted, CharBossMonster? boss)
    {
        try
        {
            if (scripted == null || boss == null) return false;
            if (!IsMultiplayer(boss)) return false;
            int act = boss.ActIndex;
            var bossType = boss.GetType();
            int ascension = 0;
            try { ascension = BossLineups.Ascension(); } catch { ascension = 0; }

            foreach (var row in UpgradeRows)
            {
                if (row.Act != act || row.Boss != bossType || row.From != scripted) continue;
                if (ascension < row.MinAscension) continue;
                return true;
            }
        }
        catch { /* 忽略 */ }
        return false;
    }

    /// <summary>把一张剧本牌按多人换牌表映射成实际要造的牌（不是多人就原样返回）。</summary>
    public static Type Resolve(Type scripted, CharBossMonster boss)
    {
        try
        {
            if (scripted == null || boss == null) return scripted;
            if (!MultiBossPlan.WantsSecondBoss(out int players) || players < 2) return scripted;

            int act = boss.ActIndex;
            var bossType = boss.GetType();
            foreach (var row in Table)
            {
                if (row.Act != act) continue;
                if (row.Boss != bossType) continue;
                if (row.From != scripted) continue;
                Logger.Log($"[CharBoss] 多人换牌（{players} 人）：{row.From.Name} → {row.To.Name}" +
                           $"（{bossType.Name} 第 {row.Act + 1} 幕）");
                return row.To;
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 多人换牌失败（按原牌处理）: " + e.Message);
        }
        return scripted;
    }

    /// <summary>
    /// 同 <see cref="Resolve"/>，但**不打日志** —— 给「按换牌后的类型比对手牌」这种每回合要调用很多次的地方用。
    /// （<see cref="Resolve"/> 每命中一次就记一行，放在逐张比对里会刷屏。）
    /// </summary>
    public static Type? ResolveQuiet(Type scripted, CharBossMonster boss)
    {
        try
        {
            if (scripted == null || boss == null) return null;
            if (!MultiBossPlan.WantsSecondBoss(out int players) || players < 2) return null;

            int act = boss.ActIndex;
            var bossType = boss.GetType();
            foreach (var row in Table)
            {
                if (row.Act != act) continue;
                if (row.Boss != bossType) continue;
                if (row.From != scripted) continue;
                return row.To;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    /// <summary>这张牌在当前（多人）配置下是不是被换掉了 —— 给「能量汲取升级顺序」等地方判断用。</summary>
    public static bool IsSwapped(Type scripted, CharBossMonster boss)
        => Resolve(scripted, boss) != scripted;

    /// <summary>
    /// 这个**卡牌类型**是不是换牌表里「换上去的那张」（不看幕/职业，纯按类型）。
    ///
    /// 用途：出牌优先级。换上的三张（休眠 / 骸骨军团 / 刀刃交响曲）都比原牌贵
    /// （冰川 2→休眠 2 同价，吸引仇恨 1→骸骨军团 2，斗篷与匕首 1→刀刃交响曲 2/升级 1），
    /// 而剧本给的回合能量是按**原牌**算的。用户口径是「2 费打出去之后，后面的防御自然就打不出了」，
    /// 所以换上那张必须**先打**（排序时排最前），否则它会永远被同档里更便宜的防御挤掉、效果一点看不到。
    /// </summary>
    public static bool IsSwappedIn(Type card)
    {
        if (card == null) return false;
        foreach (var row in Table)
        {
            if (row.To == card) return true;
        }
        return false;
    }
}
