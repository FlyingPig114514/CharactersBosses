using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace CharacterBosses.Core;

/// <summary>
/// **双版本兼容层**（用户 2026-11-05：同时适配 v0.107 正式版与 v0.111 beta）。
///
/// 为什么需要它：这两个版本的 `sts2.dll` 有几处 API 差异（2026-11-05 用两份 dll
/// 各强制 `-t:Rebuild` 编译比对得出），直接引用就会在旧版上**编译不过**或者
/// **运行时 `PatchAll` 抛异常导致后面的补丁一个都装不上**。差异清单：
/// <code>
///   Nodes.Vfx.Forms 命名空间 / NFormVfx / NCreatureVisuals.AddFormVfx   （新版才有，纯外观）
///   Hook.BeforeSideTurnEnd                                             （新版才有）
///   PoisonPower.Trigger                                                （新版才有；旧版叫别的名字/在别处结算）
///   FeralPower.ModifyCardPlayResultLocation + CardLocation 类型         （新版才有）
///   Cards.BladeSymphony                                                （新版才有的卡）
///   PowerModel.ModifyDamageMultiplicative 的**签名**                    （新版多 2 个参数）
///   CardPileCmd.DrawInternal / CombatManager.AwaitTurnEndAndSwitchSides（字符串寻址的补丁目标，旧版没有）
/// </code>
///
/// 做法：**编译期只引用两版都有的 API**，凡是版本相关的都用反射探测 + 布尔开关；
/// 对应的补丁类一律加 `[HarmonyPrepare]` 守卫 —— 探测不到就**整类跳过并记一行日志**，
/// 绝不让 `PatchAll` 抛异常（那会连累其它补丁）。
/// </summary>
internal static class GameCompat
{
    /// <summary>游戏程序集（`sts2.dll`）的 InformationalVersion，形如 `0.1.0+41cef1ea`。</summary>
    public static readonly string GameVersion;

    /// <summary>新版才有：`Nodes.Vfx.Forms` 那套「形态特效」节点（`NFormVfx` / `AddFormVfx`）。</summary>
    public static readonly bool HasFormVfx;

    /// <summary>新版才有：`Hook.BeforeSideTurnEnd`。</summary>
    public static readonly bool HasBeforeSideTurnEnd;

    /// <summary>新版才有：`PoisonPower.Trigger`。</summary>
    public static readonly bool HasPoisonTrigger;

    /// <summary>新版才有：`FeralPower.ModifyCardPlayResultLocation`。</summary>
    public static readonly bool HasFeralResultLocation;

    /// <summary>新版才有的卡「刀刃交响曲」（`BladeSymphony`）。</summary>
    public static readonly bool HasBladeSymphony;

    /// <summary>`CardPileCmd.DrawInternal`（新版才有；补丁按字符串寻址）。</summary>
    public static readonly bool HasDrawInternal;

    /// <summary>`CombatManager.AwaitTurnEndAndSwitchSides`（新版才有；补丁按字符串寻址）。</summary>
    public static readonly bool HasAwaitTurnEndAndSwitchSides;

    /// <summary>旧版才有：`FeralPower.ModifyCardPlayResultPileTypeAndPosition`（新版改名成 …Location）。</summary>
    public static readonly bool HasFeralPileTypeAndPosition;

    /// <summary>旧版才有：`Hook.BeforeTurnEnd`（新版换成了 `Hook.BeforeSideTurnEnd`，旧版没有后者）。</summary>
    public static readonly bool HasHookBeforeTurnEnd;

    /// <summary>旧版才有：`CardPileCmd.Draw(ctx, count, player, fromHandDraw)`（新版走 private `DrawInternal`）。</summary>
    public static readonly bool HasLegacyDraw;

    private static bool _logged;

    static GameCompat()
    {
        GameVersion = ReadGameVersion();
        HasFormVfx = TypeExists("MegaCrit.Sts2.Core.Nodes.Vfx.Forms.NFormVfx")
                     || MethodExists("MegaCrit.Sts2.Core.Nodes.Combat.NCreatureVisuals", "AddFormVfx");
        HasBeforeSideTurnEnd = MethodExists("MegaCrit.Sts2.Core.Hooks.Hook", "BeforeSideTurnEnd");
        HasPoisonTrigger = MethodExists("MegaCrit.Sts2.Core.Models.Powers.PoisonPower", "Trigger");
        HasFeralResultLocation = MethodExists("MegaCrit.Sts2.Core.Models.Powers.FeralPower", "ModifyCardPlayResultLocation");
        HasBladeSymphony = TypeExists("MegaCrit.Sts2.Core.Models.Cards.BladeSymphony");
        HasDrawInternal = MethodExists("MegaCrit.Sts2.Core.Commands.CardPileCmd", "DrawInternal");
        HasAwaitTurnEndAndSwitchSides = MethodExists("MegaCrit.Sts2.Core.Combat.CombatManager", "AwaitTurnEndAndSwitchSides");
        HasFeralPileTypeAndPosition = MethodExists("MegaCrit.Sts2.Core.Models.Powers.FeralPower", "ModifyCardPlayResultPileTypeAndPosition");
        HasHookBeforeTurnEnd = MethodExists("MegaCrit.Sts2.Core.Hooks.Hook", "BeforeTurnEnd");
        HasLegacyDraw = !HasDrawInternal && MethodExists("MegaCrit.Sts2.Core.Commands.CardPileCmd", "Draw");
    }

    /// <summary>跑一次（`BuildInfo.LogOnce` 里调），把版本与探测结果打进日志。</summary>
    public static void LogOnce()
    {
        if (_logged) return;
        _logged = true;
#if STS2_107
        const string builtFor = "107";
#else
        const string builtFor = "111";
#endif
        // ★ 2026-11-05：**构建目标与当前游戏版本不匹配**是最容易看走眼的一类故障
        //   （用户实测：切回 111 后仍装着 107 的 dll ⇒ 立绘变成占位怪、她也不出牌）。
        //   这里直接把话说清楚，别让人去猜。
        bool mismatch = false;
        try
        {
            // 111 的 InformationalVersion 里带 commit 41cef1ea；107 是 59260271
            if (GameVersion.Contains("41cef1ea") && builtFor == "107") mismatch = true;
            if (GameVersion.Contains("59260271") && builtFor == "111") mismatch = true;
        }
        catch { /* 忽略 */ }
        if (mismatch)
        {
            Logger.Log($"[CharBoss] ⚠⚠ 版本不匹配：这个 dll 是给 **{builtFor}** 编译的，但当前游戏是 {GameVersion}！" +
                       "请重新运行 install-mod.bat（它会按游戏版本自动选 dll），否则会出现「立绘变占位怪 / 她不出牌」这类怪现象。");
        }

        Logger.Log($"[CharBoss] 兼容层：游戏版本 {GameVersion}（本 dll 构建目标={builtFor}）" +
                   $" ⇒ 形态特效={Yn(HasFormVfx)} BeforeSideTurnEnd={Yn(HasBeforeSideTurnEnd)}" +
                   $" PoisonTrigger={Yn(HasPoisonTrigger)} FeralResultLocation={Yn(HasFeralResultLocation)}" +
                   $" BladeSymphony={Yn(HasBladeSymphony)} DrawInternal={Yn(HasDrawInternal)}" +
                   $" AwaitTurnEndAndSwitchSides={Yn(HasAwaitTurnEndAndSwitchSides)}" +
                   $" FeralPileType={Yn(HasFeralPileTypeAndPosition)} BeforeTurnEnd={Yn(HasHookBeforeTurnEnd)}" +
                   $" LegacyDraw={Yn(HasLegacyDraw)}");
    }

    private static string Yn(bool v) => v ? "有" : "无";

    private static string ReadGameVersion()
    {
        try
        {
            var asm = typeof(MegaCrit.Sts2.Core.Hooks.Hook).Assembly;
            var attr = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            string v = attr?.InformationalVersion ?? asm.GetName().Version?.ToString() ?? "?";
            return v;
        }
        catch (Exception e)
        {
            return "?(" + e.GetType().Name + ")";
        }
    }

    /// <summary>游戏程序集里有没有这个类型（全名）。</summary>
    public static bool TypeExists(string fullName)
    {
        try { return AccessTools.TypeByName(fullName) != null; }
        catch { return false; }
    }

    /// <summary><paramref name="typeName"/> 上有没有叫 <paramref name="method"/> 的方法（含非 public）。</summary>
    public static bool MethodExists(string typeName, string method)
    {
        try
        {
            var t = AccessTools.TypeByName(typeName);
            if (t == null) return false;
            return t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Any(m => m.Name == method)
                   || AccessTools.Method(t, method) != null;
        }
        catch { return false; }
    }

    /// <summary>按全名取类型（取不到返回 null；给那些要先判断再反射调用的地方用）。</summary>
    public static Type? TypeOrNull(string fullName)
    {
        try { return AccessTools.TypeByName(fullName); }
        catch { return null; }
    }

    /// <summary>
    /// **只查「这个类型自己声明」的方法**（不往基类找）。
    ///
    /// ⚠ 为什么必须这样（2026-11-05 事故）：`AccessTools.Method(type, name)` 会**连基类一起找**，
    /// 所以「107 的 DiamondDiadem 没有 AfterSideTurnStart」这种判断会**误判为有**
    /// （找到的是 `AbstractModel.AfterSideTurnStart`）⇒ 补丁挂到了**基类**上
    /// ⇒ 所有模型的该钩子都被我们的前缀拦掉（日志实测：一次战斗里连打 7 条「钻石头冠跳过」，
    /// 外加 33 条「Canonical model … used in incorrect place」），最后整场崩溃。
    /// ⇒ 凡是「某类型专属钩子」的补丁守卫，一律用这个，找不到就跳过。
    /// </summary>
    public static MethodInfo? DeclaredMethod(Type? type, string name, params Type[] args)
    {
        try
        {
            if (type == null) return null;
            var m = args.Length == 0
                ? type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                  BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                      .FirstOrDefault(x => x.Name == name)
                : type.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                       BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, args, null);
            return m;
        }
        catch { return null; }
    }
    // ───────────────────────── 两版签名差异的统一入口 ─────────────────────────
    // 下面这些方法把「同一件事、两版签名不同」的调用收在一处，源码里就只剩一行调用。
    // 判据用编译期符号（csproj 里按 Sts2Target 定义 STS2_107 / STS2_111），运行时零开销。

    /// <summary>
    /// **谁打出了这张牌**。新版 `CardPlay.Player` 直接给；旧版没有这个属性 ⇒ 用 `CardPlay.Card.Owner`。
    /// </summary>
    public static MegaCrit.Sts2.Core.Entities.Players.Player? ActorOf(MegaCrit.Sts2.Core.Entities.Cards.CardPlay? play)
    {
        if (play == null) return null;
#if STS2_107
        try { return play.Card?.Owner; } catch { return null; }
#else
        try { return play.Player; } catch { return null; }
#endif
    }

    /// <summary>伤害管道：新版多一个 `CardPlay` 参数（这里统一传 null）。</summary>
    public static decimal ModifyDamage(MegaCrit.Sts2.Core.Runs.IRunState run,
                                       MegaCrit.Sts2.Core.Combat.ICombatState combat,
                                       MegaCrit.Sts2.Core.Entities.Creatures.Creature target,
                                       MegaCrit.Sts2.Core.Entities.Creatures.Creature dealer,
                                       decimal baseDamage,
                                       MegaCrit.Sts2.Core.ValueProps.ValueProp props,
                                       MegaCrit.Sts2.Core.Models.CardModel? cardSource,
                                       MegaCrit.Sts2.Core.Hooks.ModifyDamageHookType hookType,
                                       MegaCrit.Sts2.Core.Entities.Cards.CardPreviewMode previewMode,
                                       out System.Collections.Generic.IEnumerable<MegaCrit.Sts2.Core.Models.AbstractModel>? modifiers)
    {
#if STS2_107
        return MegaCrit.Sts2.Core.Hooks.Hook.ModifyDamage(run, combat, target, dealer, baseDamage, props,
            cardSource, hookType, previewMode, out modifiers);
#else
        return MegaCrit.Sts2.Core.Hooks.Hook.ModifyDamage(run, combat, target, dealer, baseDamage, props,
            cardSource, null, hookType, previewMode, out modifiers);
#endif
    }
}

/// <summary>
/// 少量**需要泛型/返回值不同**的版本差异入口（放在同一个 partial 里方便找）。
/// </summary>
internal static partial class GameCompatExtra
{
    /// <summary>旧版 `Shiv.CreateInHand(owner, count, combatState)`，新版多一个参数。</summary>
    public static System.Threading.Tasks.Task CreateShivInHand(
        MegaCrit.Sts2.Core.Entities.Players.Player owner, int count,
        MegaCrit.Sts2.Core.Combat.ICombatState combat)
    {
#if STS2_107
        return MegaCrit.Sts2.Core.Models.Cards.Shiv.CreateInHand(owner, count, combat);
#else
        return MegaCrit.Sts2.Core.Models.Cards.Shiv.CreateInHand(owner, count, combat, null);
#endif
    }

    /// <summary>「给某人塞 1 把小刀」那种三参形式（新版多一个 creator）。</summary>
    public static System.Threading.Tasks.Task CreateShivInHandOne(
        MegaCrit.Sts2.Core.Entities.Players.Player owner,
        MegaCrit.Sts2.Core.Combat.ICombatState combat,
        MegaCrit.Sts2.Core.Entities.Players.Player creator)
    {
#if STS2_107
        return MegaCrit.Sts2.Core.Models.Cards.Shiv.CreateInHand(owner, combat);
#else
        return MegaCrit.Sts2.Core.Models.Cards.Shiv.CreateInHand(owner, combat, creator);
#endif
    }
    /// <summary>旧版 `OrbCmd.Passive(ctx, orb, target)`，新版多一个 `countAffectedByHooks`。</summary>
    public static async System.Threading.Tasks.Task OrbPassive(
        MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext ctx,
        MegaCrit.Sts2.Core.Models.OrbModel orb,
        MegaCrit.Sts2.Core.Entities.Creatures.Creature? target)
    {
#if STS2_107
        // ★ 2026-11-05（用户实测：「情感芯片导致的闪电充能球被动没有因为镀金缆线变成 2 次」）：
        //   旧版的 `OrbCmd.Passive` **自己不查遗物钩子**（新版是靠第 4 个参数
        //   `countAffectedByHooks: true` 才让 `ModifyOrbPassiveTriggerCounts` 参与）。
        //   所以旧版这里手动问一遍遗物：`GoldPlatedCables.ModifyOrbPassiveTriggerCounts(orb, n)`
        //   —— 最右侧那颗球（`Orbs[0]`）会被改成 2 ⇒ 我们按这个次数逐次触发。
        int times = 1;
        try
        {
            var player = orb?.Owner;
            var relics = player?.Relics;
            if (relics != null)
            {
                foreach (var relic in relics)
                {
                    if (relic == null) continue;
                    var m = relic.GetType().GetMethod("ModifyOrbPassiveTriggerCounts",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                    if (m == null) continue;
                    var r = m.Invoke(relic, new object?[] { orb, times });
                    if (r is int n && n > 0) times = n;
                }
            }
        }
        catch { /* 算不出来就按一次 */ }

        for (int i = 0; i < Math.Max(1, times); i++)
        {
            await MegaCrit.Sts2.Core.Commands.OrbCmd.Passive(ctx, orb!, target);
        }
        if (times > 1)
        {
            Logger.Log($"[CharBoss] 充能球被动按遗物触发 {times} 次（旧版需要手动算缆线）");
        }
#else
        await MegaCrit.Sts2.Core.Commands.OrbCmd.Passive(ctx, orb, target, true);
#endif
    }

    /// <summary>旧版 `CreatureCmd.LoseBlock(creature, amount)`，新版多 `ctx` / `dealer`。</summary>
    public static System.Threading.Tasks.Task LoseBlock(
        MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext ctx,
        MegaCrit.Sts2.Core.Entities.Creatures.Creature creature, int amount,
        MegaCrit.Sts2.Core.Entities.Creatures.Creature? dealer)
    {
#if STS2_107
        return MegaCrit.Sts2.Core.Commands.CreatureCmd.LoseBlock(creature, amount);
#else
        return MegaCrit.Sts2.Core.Commands.CreatureCmd.LoseBlock(ctx, creature, amount, dealer!);
#endif
    }
}