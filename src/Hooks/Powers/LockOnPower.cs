using System;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Powers;

/// <summary>
/// 多人模式的「**跟踪锁定**」（设计见 <c>docs/plans/multiplayer-lockon.md</c> §阶段 1）。
///
/// 挂在**玩家**身上：表示「这只 Boss 盯上了你」。它自己不带任何战斗数值，纯粹是给
/// <see cref="Core.BossLockOn"/> 记录「Boss ↔ 玩家」关系用的可视化载体（状态栏上有图标、写 Boss 名）。
///
/// ## ⚠ 一个 Boss 一份实例（用户 2026-10-29 定稿："应该像坚韧之环、炸弹那样可以共存"）
///
/// 以前是 `InstanceType.None`（同一生物上只能有一份），于是**第二只 Boss 锁同一个人时，
/// 新的那份会把旧那份顶掉**（`PowerCmd.Apply` 先摘旧的再建新的）—— 屏幕上只剩一个图标、
/// 悬浮里只剩最新那只 Boss。而原版「坚韧之环」（<c>ToricToughnessPower</c>）、
/// 「炸弹」（<c>TheBombPower</c>）用的是 `PowerInstanceType.Instanced`，多份可以共存。
///
/// 这里用 `InstancedPerApplier`：**每只 Boss 各一份**。反编译 `PowerCmd`：
/// <code>
/// static PowerModel? FindExistingInstanceForStacking(power, target, applier) => power.InstanceType switch {
///     PowerInstanceType.Instanced             => null,                                  // 永远建新的
///     PowerInstanceType.InstancedPerApplier   => target.GetPowerInstances(power.Id)
///                                                      .FirstOrDefault(p => p.Applier == applier),  // 同一施加者合并
///     PowerInstanceType.None                  => target.GetPower(power.Id),             // 同类型只留一份
/// };
/// </code>
/// ⇒ 同一只 Boss 重复 Apply 只会**合并到它自己那份**（不会多出图标），
/// 不同 Boss 各得一份 ⇒ 状态栏上「A 盯上了你」「B 盯上了你」两个图标同时存在。
/// 每个实例的悬浮文案只讲**它自己那只** Boss（`Applier` 就是那只 Boss 的生物）。
/// </summary>
public sealed class LockOnPower : PowerModel
{
    /// <summary>
    /// **`None`（不是 Debuff）** —— 用户 2026-10-26 转来的群友建议，采纳了。
    ///
    /// 这层 Power 只是「这只 Boss 盯上了你」的**可视化载体**，本身零战斗数值（不掉血、不减速、不叠层）。
    /// 写成 `Debuff` 会污染所有「数负面效果」的逻辑：
    ///   · 崩坠枪手等体系里「按身上负面效果数量结算」的牌会把它算进去（凭空变强/变弱）；
    ///   · 「高尚」这类**清除负面效果**的牌会顺手把锁定摘掉，描述与实际不符，Boss 的锁定标记也没了。
    /// 改成 `None` 之后它是中性图标：**不清除、不计数、也不受人工制品拦截**
    /// （锁定每场都必须挂上，被人工制品挡掉会导致面板/选靶判定跟实际不一致）。
    /// </summary>
    public override PowerType Type => PowerType.None;

    /// <summary>
    /// **Single**：层数恒为 1、图标上不显示数字（`NPower.RefreshAmount` 只在 `Counter` 时画数字）。
    ///
    /// ⚠ 曾经改成 Counter、层数当作「有几只 Boss 盯我」——那是**会被联机校验的状态**
    /// （`NetFullCombatState` 里带着 powers 的 Amount），而两边各自在不同时机增删锁定，
    /// 采样时机差一点就分歧：实机诊断包显示 `LOCK_ON_POWER: local 2 / remote 1` ⇒ 直接「多人数据不同步」掉线。
    /// 现在「盯我的是哪几只 Boss」完全由**实例个数**表示（一 Boss 一份），层数恒 1。
    /// </summary>
    public override PowerStackType StackType => PowerStackType.Single;

    /// <summary>
    /// **`InstancedPerApplier`** —— 每只 Boss 各一份实例，可以共存（见类注释）。
    /// </summary>
    public override PowerInstanceType InstanceType => PowerInstanceType.InstancedPerApplier;

    public override bool ShouldPlayVfx => false;

    /// <summary>诊断用：本次会话一共创建过几个锁定图标实例。</summary>
    private static int _created;

    /// <summary>这份图标代表的**那只 Boss**（施加者）。</summary>
    internal Monsters.CharBossMonster? Boss
    {
        get
        {
            try { return Applier?.Monster as Monsters.CharBossMonster; }
            catch { return null; }
        }
    }

    /// <summary>这份图标现在还该不该在：它那只 Boss 还活着、还在这一场、而且确实锁着我。</summary>
    internal bool StillValid()
    {
        try
        {
            var boss = Boss;
            if (boss?.Creature == null) return false;
            if (!boss.Creature.IsAlive) return false;

            var mine = Owner?.CombatState;
            if (mine == null) return true;                        // 拿不到战斗状态就不下结论
            if (!ReferenceEquals(boss.Creature.CombatState, mine)) return false;   // 上一场的旧实例

            var me = Owner?.Player;
            if (me == null) return true;
            return Core.BossLockOn.IsLocked(boss, me);
        }
        catch { return true; }      // 判定失败就保留，宁可多一个图标也不要误删
    }

    /// <summary>
    /// 描述：**只说这一份图标代表的那只 Boss**（一 Boss 一份，用户 2026-10-29 定稿）。
    ///
    /// 取不到施加者（旧存档 / 施加者为空）时退回注册表里的第一个锁定者，
    /// 再退回「一只角色 Boss」，保证不会出现空文案。
    /// </summary>
    public override MegaCrit.Sts2.Core.Localization.LocString Description
    {
        get
        {
            var description = base.Description;
            try
            {
                var boss = Boss;
                if (boss == null)
                {
                    // 兜底：注册表里锁着我的第一只（老存档 / Applier 丢失）
                    try
                    {
                        var me = Owner?.Player;
                        if (me != null)
                        {
                            var list = Core.BossLockOn.BossesLocking(me, Owner?.CombatState);
                            if (list.Count > 0) boss = list[0];
                        }
                    }
                    catch { /* 忽略 */ }
                }

                string name = boss != null ? DisplayNameOf(boss) : "一只角色 Boss";
                string pronoun = "它";
                string pronoun2 = "its";
                string extra = string.Empty;

                if (boss != null)
                {
                    // 人称（用户指定）：储君/铁甲战士 → 他；静默猎手/亡灵契约师 → 她；故障机器人 → 它。
                    pronoun = boss switch
                    {
                        Monsters.RegentBoss or Monsters.IroncladBoss => "他",
                        Monsters.SilentBoss or Monsters.NecrobinderBoss => "她",
                        _ => "它",
                    };
                    pronoun2 = boss switch
                    {
                        Monsters.RegentBoss or Monsters.IroncladBoss => "his",
                        Monsters.SilentBoss or Monsters.NecrobinderBoss => "her",
                        _ => "its",
                    };
                    extra = ExtraFor(boss, withName: false);
                }

                description.Add("Boss", name);
                description.Add("Pronoun", pronoun);
                description.Add("Pronoun2", pronoun2);
                description.Add("Extra", extra);
            }
            catch { /* 忽略 */ }
            return description;
        }
    }

    /// <summary>
    /// 全局型特殊效果（对所有玩家生效）——这些**不**加「只对你生效」那句。
    /// 依据 docs/plans/multiplayer-lockon.md §阶段 2：带毒刺击 / 时候未到 / 何人僭越 是全局型；
    /// 偏差认知 / 袖里乾坤 / 共鸣 / 预借时间 / 彗星 是对应型（只对被锁定的玩家生效）。
    /// </summary>
    private static bool IsGlobalSpecial(Type powerType)
    {
        return powerType == typeof(PoisonStrikePower)
            || powerType == typeof(NotYetPower)
            || powerType == typeof(KnowThyPlacePower);
    }

    /// <summary>取特殊效果的名字（本地化文本的第一行，例如「彗星」「共鸣」）。</summary>
    private static string SpecialEffectName(Monsters.CharBossMonster? boss)
    {
        try
        {
            if (boss == null) return string.Empty;
            var key = boss.Def.SpecialLocForAct(boss.ActIndex);
            if (string.IsNullOrWhiteSpace(key)) return string.Empty;
            var text = new MegaCrit.Sts2.Core.Localization.LocString("monsters", key!).GetFormattedText();
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            return text!.Split('\n')[0].Trim();
        }
        catch { return string.Empty; }
    }

    /// <summary>这只 Boss 的「只对你生效」那段（全局型 special 返回空）。</summary>
    private static string ExtraFor(Monsters.CharBossMonster? charBoss, bool withName)
    {
        try
        {
            if (charBoss == null) return string.Empty;
            var powerType = charBoss.Def.SpecialPowerForAct(charBoss.ActIndex);
            if (powerType == null || IsGlobalSpecial(powerType)) return string.Empty;
            string effectName = SpecialEffectName(charBoss);
            if (string.IsNullOrWhiteSpace(effectName)) return string.Empty;
            return withName
                ? $"\n「{effectName}」只对你生效（{charBoss.Title?.GetFormattedText()}）。"
                : $"\n「{effectName}」只对你生效。";
        }
        catch { return string.Empty; }
    }

    /// <summary>
    /// 描述里显示的名字：用户 2026-10-26 指定要**角色名**（铁甲战士 / 静默猎手 / 故障机器人 /
    /// 亡灵契约师 / 储君），**不要**变体设计名（愤怒战 / 奇巧猎 / 炼药猎…）——
    /// 因为这层图标讲的是「哪只 Boss 盯上你」，用角色名和血条/立绘对得上。
    ///
    /// 名字来源：怪物名走本地化表 `monsters` 的 `<ID>.title`（我们已按角色名补齐），
    /// 取不到才退回这一档的遭遇名，最后才是兜底文案。
    /// （`__` 是游戏渲染「缺 key」的占位，这里一律当"取不到"处理。）
    /// </summary>
    private static string DisplayNameOf(Monsters.CharBossMonster boss)
    {
        // ① 怪物自己的 Title（= 角色名，blood bar/立绘用的就是它）
        try
        {
            var text = boss.Title?.GetFormattedText();
            if (Usable(text)) return text!.Trim();
        }
        catch { /* 继续 */ }

        // ② 兜底：这一档的遭遇名（例如「静默猎手」的遭遇里写的是「炼药猎」）
        try
        {
            var encounter = Encounters.CharBossEncounters.EncounterForBoss(boss);
            var text = encounter?.Title?.GetFormattedText();
            if (Usable(text)) return text!.Trim();
        }
        catch { /* 继续 */ }

        return "一只角色 Boss";
    }

    /// <summary>文本能不能用（空、或游戏渲染「缺 key」的那个 `__` 占位都不算）。</summary>
    private static bool Usable(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text!.Trim();
        if (trimmed == "__") return false;
        return true;
    }

    /// <summary>挂上来时 → 把「我被这只 Boss 锁了」回报给注册表（图标本身由 `Applier` 表示）。</summary>
    public override async System.Threading.Tasks.Task AfterApplied(
        MegaCrit.Sts2.Core.Entities.Creatures.Creature applier,
        MegaCrit.Sts2.Core.Models.CardModel? cardSource)
    {
        try
        {
            if (Owner != null) Core.BossLockOn.NoteLocked(Owner, applier);

            // 诊断（用户 2026-10-29）：每成功建一个**实例**都会走到这里。
            // 一 Boss 一份 ⇒ 4 人局每人可能看到 1~2 个图标（被几只 Boss 锁就有几个）。
            try
            {
                _created++;
                Logger.Log($"[CharBoss] 锁定图标实例 #{_created} 创建：{Owner?.Name}" +
                           $"（applier={applier?.Monster?.Id.Entry ?? applier?.Name ?? "?"}）");
            }
            catch { /* 忽略 */ }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 跟踪锁定记录失败: " + e.Message);
        }
        await System.Threading.Tasks.Task.CompletedTask;
    }
}
