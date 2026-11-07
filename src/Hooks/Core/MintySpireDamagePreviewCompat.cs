using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using CharacterBosses.Monsters;
using CharacterBosses.UI;

namespace CharacterBosses.Core;

/// <summary>
/// 与「**薄荷尖塔**」（MintySpire2，工坊 id <c>3737336234</c>）的**伤害预测**兼容。
///
/// 用户 2026-11-01：「「薄荷尖塔」模组的伤害预测现在似乎不会计算「无来源伤害」（如储君的炸弹）。兼容一下（不影响其它逻辑）」。
///
/// ## 对方的算法（反编译 <c>MintySpire2.MintySpire2Code.combat.SummedIncomingDamageRender</c>）
/// 它在**本地玩家**的血条右边画一个 <c>←N</c>（下面那行代码），N 来自
/// <c>private static int CalculateIncomingDamage(Creature creature)</c>，只累加三样：
///   1. 每只敌人的 `NextMove.Intents` 里 <c>IntentType = Attack</c> 的 <c>AttackIntent.GetTotalDamage</c>；
///   2. 玩家身上的 <c>DisintegrationPower</c> / <c>ConstrictPower</c>；
///   3. 玩家**手牌**里「回合结束会伤害自己」的牌（<c>CardTurnEndInspector</c>）。
/// <code>
/// int num = CalculateIncomingDamage(creature);
/// if (num &gt; 0) { val.Text = $"←{num}"; val.Visible = true; }
/// </code>
///
/// ⇒ **按「意图」算**，所以一切都只以 <c>NextMove.Intents</c> 为准。
/// 而我们 Boss 的几样「**无来源伤害**」根本不在意图里（它们在`<see cref="BossHandPanel"/>`
/// 自己那行「独立意图」、以及 <see cref="BossPotionRow"/> 的药水格子上画出来）：
///   · **炸弹**（储君，<c>TheBombPower</c>，剩 1 回合就在**她回合结束**炸，40 / 进阶 9 起 50）；
///   · **冰雹风暴**（能力机，<c>HailstormPower</c>，她回合结束按层数打）；
///   · **充能球被动**（故障机器人两套，回合结束闪电 / 玻璃球各触发一次）；
///   · **药水**（炼药猎，回合**开始**先喝药 —— 爆裂安瓿 10 点打全体，火焰药水 20 点单体）。
/// 于是薄荷尖塔的数字**偏小**。
///
/// ## 做法
/// 反射给她那个 private static 方法挂一个**后置补丁**，把上面三样加进返回值：
///   · 只**加算术**，不碰任何游戏状态 ⇒ 出牌、伤害结算、胜负全都不变（用户要的「不影响其它逻辑」）；
///   · 数字**直接复用** <see cref="BossHandPanel"/> 里那三个算法（`HailstormDamage` / `BombDamage` /
///     <c>OrbPassiveDamage</c>）⇒ 她的预测和我们手牌栏的「独立意图」永远一致，不会出现两个数字打架；
///   · **多人**：炸弹 / 冰雹风暴打的是全场，每个人都该看到真实数字；充能球被动是**单体随机**、
///     而她出手时「可命中敌人」被收窄成被锁定那位 ⇒ 用 <see cref="BossIntentBuilder.RandomAimIsElsewhere"/>
///     判断「本地玩家不是被锁的人」时**不加**这一条（与手牌栏同一口径）；
///   · 只对本模组的 <see cref="CharBossMonster"/> 生效 ⇒ 原版怪物 / 别的模组怪物一个都不受影响。
///
/// ## 兜底
/// 没装薄荷尖塔、或它改了类名/方法名（版本不符）⇒ 反射拿不到就**安静跳过**，只在日志里记一行。
/// 全部包在 try/catch 里，绝不让兼容代码影响主流程。
/// </summary>
internal static class MintySpireDamagePreviewCompat
{
    /// <summary>薄荷尖塔里那个「累计即将受到的伤害」的类（全名，反射用）。</summary>
    private const string TargetTypeName = "MintySpire2.MintySpire2Code.combat.SummedIncomingDamageRender";

    /// <summary>要挂后置补丁的那个方法（`private static int CalculateIncomingDamage(Creature)`）。</summary>
    private const string TargetMethodName = "CalculateIncomingDamage";

    private static bool _installed;

    /// <summary>装一次就够。</summary>
    /// <summary>重试计数（薄荷尖塔可能比我们晚加载；失败不永久放弃，最多记 3 次日志）。</summary>
    private static int _attempts;

    /// <summary>上次扫描时「已加载程序集」的数量 —— 数量没变就说明不可能冒出新类型，直接跳过。</summary>
    private static int _lastScannedAsmCount = -1;

    /// <summary>已经找过但没找到的次数（用于日志节流）。</summary>
    private static int _scanMisses;

    internal static void TryInstall()
    {
        if (_installed) return;
        // ★ 2026-11-05（用户实测）：以前拿不到类型就**永久放弃**，而薄荷尖塔在某些加载顺序下比我们晚，
        //   于是「伤害预测」兼容一次都没装上（日志里那句「薄荷尖塔没装」其实是误判）。
        //   现在每次调用都重试，只在前 3 次失败时记日志。
        // ★ v0.2.410：调用点从「只调一次」改成**每帧慢车道 4Hz 重试**（见 `BossFrameTick.TickInner`），
        //   所以这里必须便宜：程序集数量没变就直接返回，不做反射扫描。
        _attempts++;

        try
        {
            int asmCount;
            try { asmCount = AppDomain.CurrentDomain.GetAssemblies().Length; } catch { asmCount = -1; }
            if (asmCount > 0 && asmCount == _lastScannedAsmCount)
            {
                if (++_scanMisses == 3)
                {
                    Logger.Log("[CharBoss] 薄荷尖塔（MintySpire2）仍未加载 ⇒ 兼容继续等它出现（有新的程序集加载时会再找）");
                }
                return;
            }
            _lastScannedAsmCount = asmCount;
            _scanMisses = 0;

            var target = FindType(TargetTypeName);
            if (target == null)
            {
                if (_attempts <= 3) Logger.Log("[CharBoss] 薄荷尖塔（MintySpire2）暂时没找到 ⇒ 稍后重试「伤害预测」兼容");
                return;
            }

            var method = target.GetMethod(TargetMethodName,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
            {
                Logger.Log("[CharBoss] 薄荷尖塔兼容：找到了 " + TargetTypeName +
                           " 但没有 " + TargetMethodName + "（版本不符）⇒ 跳过（不影响对方功能）");
                return;
            }

            var postfix = new HarmonyMethod(typeof(MintySpireDamagePreviewCompat)
                .GetMethod(nameof(Postfix), BindingFlags.Static | BindingFlags.NonPublic)!);
            new Harmony("CharacterBosses.mintyspire.damagepreview").Patch(method, postfix: postfix);

            Logger.Log("[CharBoss] 薄荷尖塔兼容：已给她血条上的「←N」补上本模组 Boss 的**无来源伤害**" +
                       "（炸弹 / 冰雹风暴 / 充能球被动；只加数字，不改任何游戏逻辑）");
            _installed = true;      // 只有真的挂上了才置位；否则下次还会重试
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 薄荷尖塔兼容挂载失败（忽略，不影响任何一方）: " + e.Message);
        }
    }

    /// <summary>在所有已加载的程序集里找这个类型（它不在我们的引用列表里，只能反射找）。</summary>
    private static Type? FindType(string fullName)
    {
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = asm.GetType(fullName, throwOnError: false);
                    if (type != null) return type;

                    // ★ 2026-11-05：命名空间/类名随版本变过 ⇒ 再按「简单类名」扫一遍
                    const string shortName = "SummedIncomingDamageRender";
                    foreach (var candidate in asm.GetTypes())
                    {
                        try
                        {
                            if (candidate != null && candidate.Name == shortName) return candidate;
                        }
                        catch { /* 忽略 */ }
                    }
                }
                catch { /* 忽略这一个程序集 */ }
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    /// <summary>
    /// 后置补丁：`__result` 是薄荷尖塔自己算出来的「即将受到的伤害」，我们把无来源的那几样加进去。
    /// 参数名 <c>creature</c> 必须和对方的方法参数名**完全一样**（Harmony 按名字注入）。
    /// </summary>
    private static void Postfix(Creature? creature, ref int __result)
    {
        try
        {
            int extra = PendingSourcelessDamage(creature);
            if (extra <= 0) return;
            __result += extra;

            // ★ v0.2.410：只在前几次打一行「她原本算 M、我们补 K」——
            //   用户报「充能球被动没被识别」，这一行能立刻分辨是「没装上」还是「装上了但算 0」，
            //   也能看出有没有和意图里的球伤害重复计算。
            if (_loggedAdds++ < 3)
            {
                Logger.Log($"[CharBoss] 薄荷尖塔：血条旁「←」她自己算了 {__result - extra} 点，" +
                           $"我们补上 {extra} 点无来源伤害（冰雹 / 炸弹 / 球被动 / 药水 / 抱抱先生 / 黑洞）");
            }
        }
        catch { /* 忽略：出问题就维持她原来的数字 */ }
    }

    /// <summary>这条日志只打前几次，别把日志刷满。</summary>
    private static int _loggedAdds;

    /// <summary>
    /// 这位玩家**本回合结束**（= 我们的 Boss 这回合行动完之后）还会额外吃到多少「无来源伤害」。
    ///
    /// 口径逐条：
    ///   · 只算真人玩家（<paramref name="player"/>.Monster == null）：影子玩家 / 随从不显示这个数字；
    ///   · 逐个遍历**玩家视角的敌人**，只要是我们自己的 <see cref="CharBossMonster"/> 就算
    ///     （随从、奥斯提这些不是 CharBossMonster ⇒ 自动排除）；
    ///   · 三者相加，其中**充能球被动**在「本地玩家不是这只 Boss 锁定的人」时按 0 算。
    /// </summary>
    internal static int PendingSourcelessDamage(Creature? player)
    {
        try
        {
            if (player == null || !player.IsAlive) return 0;
            if (player.Monster != null) return 0;          // 只对真人玩家
            var combat = player.CombatState;
            if (combat == null) return 0;

            int total = 0;
            foreach (var enemy in combat.Enemies)
            {
                if (enemy == null || !enemy.IsAlive) continue;
                if (enemy.Monster is not CharBossMonster boss) continue;
                total += PendingOf(boss);
            }
            return total;
        }
        catch { return 0; }
    }

    /// <summary>单只 Boss 的「无来源伤害」合计（与手牌栏独立意图同一套算法）。</summary>
    private static int PendingOf(CharBossMonster boss)
    {
        int total = 0;

        // 冰雹风暴：她回合结束按层数打全场（不吃力量/集中）
        try { total += BossHandPanel.HailstormDamage(boss); } catch { /* 忽略 */ }

        // 炸弹：剩 1 回合就炸，打全场
        try { total += BossHandPanel.BombDamage(boss); } catch { /* 忽略 */ }

        // 充能球被动：单体随机 ⇒ 本地玩家不是被锁的人时不算
        try
        {
            int orbs = BossHandPanel.OrbPassiveDamage(boss, quiet: true);
            if (orbs > 0 && BossIntentBuilder.RandomAimIsElsewhere(boss)) orbs = 0;
            total += orbs;
        }
        catch { /* 忽略 */ }

        // 药水（炼药猎）：她在**回合开始**先喝药再出牌，爆裂安瓿是「直接打伤害」的那一瓶。
        // 反编译：`ExplosiveAmpoule` 打 `HittableEnemies`（= 玩家侧全体，Unpowered 10）；
        // `FirePotion` 是单体 20。药水意图只画在我们自己的药水栏格子上、**从不进 `NextMove.Intents`**
        // ⇒ 薄荷尖塔原本一个数都不会算到（用户 2026-11-01 追问：「爆炸药水有被计算吗？」）。
        // 口径：群攻（爆裂安瓿）每个人都算；单体（火焰药水）只有被这只 Boss 锁定的那位算。
        try
        {
            var (area, single) = BossPotionRow.PlannedDirectDamage(boss);
            total += area;
            if (single > 0 && !BossIntentBuilder.RandomAimIsElsewhere(boss)) total += single;
        }
        catch { /* 忽略 */ }

        // 抱抱先生（`MrStruggles`，二层「王储」进阶 9 的奖励遗物）：
        // 她在**自己回合开始**对所有玩家造成「她的回合数」点 `ValueProp.Unpowered` 伤害。
        // 同样不在 `NextMove.Intents` 里（是我们脚本路径手动结算的）⇒ 这里补进薄荷尖塔的数字。
        // ⚠ 群攻（每个玩家都挨）⇒ 不做 `RandomAimIsElsewhere` 判断。
        try { total += Core.MrStrugglesPlays.UpcomingDamage(boss); } catch { /* 忽略 */ }
        // 「水银沙漏」（`MercuryHourglass`）：她**回合开始**对所有玩家造成无来源伤害 ⇒ 补进 ←N。
        // 群攻 ⇒ 不做 `RandomAimIsElsewhere` 判断。
        try { total += BossHandPanel.MercuryHourglassDamage(boss); } catch { /* 忽略 */ }

        // 「黑洞」的**下回合辉星到账**那一下（`StarNextTurnPower` 结算 → `BlackHolePower.Amount` 点无来源伤害）：
        // 发生在**她回合开始、任何一张牌之前**，不在 `NextMove.Intents` 里 ⇒ 薄荷尖塔原本一个数都不会算到。
        // ★ 2026-11-04（用户：「意图显示的伤害不足以打死我，但实际把我打死了」）：这一笔就是"少掉"的那部分之一。
        // 群攻（每名真人玩家都挨）⇒ 不做 `RandomAimIsElsewhere` 判断。
        // ⚠ 只加「回合开始那一笔」：卡牌花星/得星触发的黑洞数已经在手牌栏的意图里、也会随
        //    `PublishVanillaIntents` 进 `NextMove.Intents`，这里再加就是双份。
        try { total += BossHandPanel.BlackHoleAtTurnStart(boss); } catch { /* 忽略 */ }

        return total;
    }
}
