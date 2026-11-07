using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace CharacterBosses.Patches;

/// <summary>
/// 皮肤 mod 的**第三层**兼容：把别的模组挂在 <c>NCreature._Ready</c> 上的后置补丁，
/// 对我们这只 Boss 的节点**补跑一次**。
///
/// ## 为什么前两层不够
/// 皮肤 mod 有两种完全不同的做法：
///  1. **改 <c>CharacterModel.VisualsPath</c>**（或干脆用 pck 覆盖同路径的场景资源）
///     —— 第一层 `<c>CharBossMonster.VisualsPath</c>` 读的就是角色模型的运行时 getter，能继承；
///  2. **改 <c>CharacterModel.CreateVisuals()</c>**
///     —— 第二层 `BossCharacterVisualsPatch` 直接调 `character.CreateVisuals()`，能继承；
///  3. **在 <c>NCreature._Ready</c> 的后置补丁里判断「这个生物属于哪个角色」然后塞自己的场景**
///     —— 这一类（例如工坊的「空洞骑士」皮肤：`Hcxmmx_Hollow_Knight_Skin.dll` 的
///     `NCreature_Ready_Knight_Patch`）前两层**一点都管不着**，它压根不读 VisualsPath / CreateVisuals。
///
/// 第 3 类的判据是（反编译原文）：
/// <code>
/// Player player = entity.Player;
/// if (player == null || player.Character.Id.Entry != "NECROBINDER") return;   // ← 关键
/// // 之后把 %Visuals 的 alpha 设 0，再把自己的场景 AddChild 进 visuals
/// </code>
/// 而我们的 Boss **在 <c>_Ready</c> 那一刻 <c>Entity.Player</c> 还是 null**
/// —— 影子 Player 是进房之后 `AfterAddedToRoom` 才绑上去的（日志实证：先打印
/// 「立绘实例来自角色模型 CreateVisuals()」，下一行才是「shadow player ready」）
/// ⇒ 皮肤补丁直接 return，Boss 永远是原版模型。**这就是玩家反馈「皮肤换不了 Boss」的根因。**
///
/// ## 为什么不干脆「提前绑影子玩家」
/// 因为 <c>Creature.IsPlayer =&gt; Player != null</c>（反编译，**没有** Monster 判据）：
/// 一旦在 `_Ready` 之前绑上，`NCreature._Ready` 会走玩家分支 ——
///   · `if (Entity.IsPlayer)` → 给我们这只怪**挂一个原版 <c>NOrbManager</c>**（和 `BossOrbDisplay` 打架）；
///   · `_isRemotePlayerOrPet = Entity.IsPlayer &amp;&amp; !LocalContext.IsMe(Entity)` → **血条直接被隐藏**；
///   · `NCombatRoom.AddCreature` 里 `if (creature.IsPlayer || PetOwner != null)` → 她会**被摆到玩家那一侧**。
/// 三条都要额外打补丁兜，而且 `IsPlayer` 这种一行 getter 极可能被 JIT 内联、补丁不一定生效。
///
/// ## 所以这里的做法
/// 影子玩家照旧在 `AfterAddedToRoom` 绑好之后，**只把「别的模组挂在 `NCreature._Ready` 上的后置补丁」
/// 对我们这只 Boss 的节点再调一次**：此时 `Entity.Player != null` 成立，皮肤补丁的条件就过了，
/// 它自己会去隐藏原立绘、挂上它的场景。
///
/// 三点保守设计：
///   · **每只生物只补跑一次**（`Done`：这类补丁多半不是幂等的 —— 那个皮肤每调一次就 AddChild 一个场景）；
///   · 只调**后置**补丁里签名最简单的那种（`static void X(NCreature)`，也就是皮肤 mod 的实际写法），
///     签名不认识的一律跳过，绝不猜参数；
///   · 跳过**我们自己**挂的补丁，并且每一个都 try/catch —— 出事只丢一行日志，绝不影响战斗。
/// </summary>
internal static class SkinReadyPatchReplay
{
    private static MethodInfo? _readyMethod;
    private static bool _readySearched;

    private static MethodInfo? _getPatchInfo;
    private static bool _getPatchInfoSearched;

    /// <summary>已经补跑过的生物（用对象身份哈希，避免把节点引用长期握在手里）。</summary>
    private static readonly HashSet<int> Done = new();

    /// <summary>本场补跑成功了几次（只用于日志）。</summary>
    private static int _invoked;
    private static int _logged;

    /// <summary>战斗结束：清掉「补跑过谁」的记账。</summary>
    internal static void ResetForNewFight()
    {
        try
        {
            Done.Clear();
            _invoked = 0;
            _logged = 0;
        }
        catch { /* 忽略 */ }
    }

    /// <summary>对这只 Boss 的节点补跑一次别的模组的 <c>NCreature._Ready</c> 后置补丁。</summary>
    /// <param name="node">Boss 的生物节点（<c>NCombatRoom.GetCreatureNode</c>）。</param>
    /// <param name="why">日志里标一下是谁触发的（诊断用）。</param>
    internal static void Replay(NCreature? node, string why)
    {
        try
        {
            if (node == null || !Godot.GodotObject.IsInstanceValid(node)) return;
            int key = RuntimeHelpers.GetHashCode(node);
            if (!Done.Add(key)) return;                       // 每只生物只补一次

            var ready = ReadyMethod();
            if (ready == null) return;

            var patches = PatchInfoOf(ready);
            if (patches == null) return;

            var own = typeof(SkinReadyPatchReplay).Assembly;
            int tried = 0, ok = 0;
            foreach (var patch in patches.Postfixes)
            {
                var method = PatchMethodOf(patch);
                if (method == null) continue;
                if (method.DeclaringType?.Assembly == own) continue;      // 我们自己挂的，跳过

                // 只认 `static void X(NCreature)` 这一种形态（皮肤 mod 的写法）；
                // 别的形态（带 __state / __result / ref 参数）一律不猜，直接跳过。
                var ps = method.GetParameters();
                if (!method.IsStatic || ps.Length != 1) continue;
                if (!typeof(NCreature).IsAssignableFrom(ps[0].ParameterType)) continue;

                tried++;
                try
                {
                    method.Invoke(null, new object[] { node });
                    ok++;
                }
                catch (Exception e)
                {
                    if (_logged++ < 6)
                    {
                        Logger.Log($"[CharBoss] 皮肤补跑：{method.DeclaringType?.Name} 抛异常（忽略）: " +
                                   (e.InnerException?.Message ?? e.Message));
                    }
                }
            }

            _invoked += ok;
            if (ok > 0 && _invoked <= 8)
            {
                Logger.Log($"[CharBoss] 皮肤补跑：对这只 Boss 补跑了 {ok}/{tried} 个「别的模组的 " +
                           $"NCreature._Ready 后置补丁」（触发点={why}，影子玩家已绑好 ⇒ 皮肤 mod 的角色判据成立）");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 皮肤补跑失败（忽略）: " + e.Message);
        }
    }

    private static MethodInfo? ReadyMethod()
    {
        try
        {
            if (!_readySearched)
            {
                _readySearched = true;
                _readyMethod = typeof(NCreature).GetMethod("_Ready",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (_readyMethod == null) Logger.Log("[CharBoss] 皮肤补跑：找不到 NCreature._Ready");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 皮肤补跑：取 _Ready 失败: " + e.Message);
        }
        return _readyMethod;
    }

    /// <summary>
    /// 取 Harmony 给这个方法的补丁信息。
    /// ⚠ 这个 Harmony 版本里 <c>Harmony.GetPatchInfo</c> 是 **internal** ⇒ 走反射。
    /// </summary>
    private static HarmonyLib.Patches? PatchInfoOf(MethodBase original)
    {
        try
        {
            if (!_getPatchInfoSearched)
            {
                _getPatchInfoSearched = true;
                _getPatchInfo = typeof(Harmony).GetMethod("GetPatchInfo",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(MethodBase) }, null);
                if (_getPatchInfo == null) Logger.Log("[CharBoss] 皮肤补跑：找不到 Harmony.GetPatchInfo");
            }
            return _getPatchInfo?.Invoke(null, new object[] { original }) as HarmonyLib.Patches;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 皮肤补跑：读补丁信息失败: " + e.Message);
            return null;
        }
    }

    /// <summary>从一条 Harmony <c>Patch</c> 里取出它的目标方法（不同 Harmony 版本字段名不同，两个都试）。</summary>
    private static MethodInfo? PatchMethodOf(object? patch)
    {
        try
        {
            if (patch == null) return null;
            var t = patch.GetType();
            var prop = t.GetProperty("PatchMethod",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop?.GetValue(patch) is MethodInfo fromProp) return fromProp;
            var field = t.GetField("patchMethod",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field?.GetValue(patch) as MethodInfo;
        }
        catch { return null; }
    }
}
