using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// **所有「形态」特效**都要朝左（面向玩家）：群蛇形态、恶魔形态、回响形态、死神形态、虚空形态…
///
/// 用户 2026-10-26：「开完群蛇形态后，这张牌的特效方向是反的（应该朝左朝向玩家）」→
/// 接着「把其它形态牌也顺手改一下」。
///
/// 原因：她的立绘是靠 <see cref="BossVisuals.FaceThePlayer"/> 把 **`Visuals.Body` 的 `scale.x` 取负**
/// 来镜像的；而形态特效走另一条路：
/// <code>
/// // 反编译（五个形态的 Create 签名完全一致）
/// public static NXxxFormVfx Create(Creature target) {
///     var vfx = PreloadManager.Cache.GetScene(_scenePath).Instantiate&lt;NXxxFormVfx&gt;();
///     vfx.Initialize(target.Player);
///     creatureNode.Visuals.AddFormVfx(vfx);   // ← 唯一入口
/// }
/// // NCreatureVisuals.AddFormVfx:
/// //   FormVfxHolder.FreeChildren(); FormVfxHolder.AddChildSafely(formVfx); formVfx.Position = Vector2.Zero;
/// </code>
/// 而 `NCreatureVisuals.FormVfxHolder` 是 `Visuals` 下一个**独立的 `Control`**，不是 `Body` 的子节点
/// ⇒ 不继承 `Body` 的镜像，特效仍然朝右、背对玩家。
///
/// 做法：只挂**唯一入口** `NCreatureVisuals.AddFormVfx` —— 五个形态（以及以后原版新增的）一次全覆盖：
///   · 从 `NCreatureVisuals` 往上找到 `NCreature`，用它公开的 `Entity` 判断是不是我们的 Boss；
///   · 是的话把**这个特效节点自己的** `scale.x` 取负（它的 `Position` 被强制成 `Zero`，
///     子节点都相对它定位，就地镜像最省事）；
///   · 再在 <c>BossFrameTick</c> 的 4Hz 慢车道幂等兜一次（特效被重建/重置后也能翻回来）。
///
/// 翻转不会被抹掉：`NSpineSpriteBoneFollower._Process` 每帧只写 `GlobalPosition`，不碰 `scale`。
/// 只对我们的 Boss 生效 —— 玩家自己开形态完全不受影响。
/// </summary>
[HarmonyPatch]
public static class FormVfxFacingPatch
{
    private static int _logs;

    /// <summary>
    /// ★ 双版本（v0.107 / v0.111）：`NCreatureVisuals.AddFormVfx` + `Nodes.Vfx.Forms` 这套
    ///   **只有新版才有**（旧版整个「形态特效」入口都不在）。
    ///   所以：类型不写死、改成运行时解析；旧版探测不到就整类跳过（只是外观，跳过不影响玩法）。
    /// </summary>
    [HarmonyPrepare]
    public static bool Prepare()
        // ⚠ 必须和 TargetMethod() 用**同一个判据**：只要方法不在就返回 false，
        //   否则 TargetMethod() 返回 null 而 Harmony 仍去补丁 → 抛 HarmonyException。
        => Core.GameCompat.MethodExists("MegaCrit.Sts2.Core.Nodes.Combat.NCreatureVisuals", "AddFormVfx");

    internal static System.Reflection.MethodBase? TargetMethod()
        => Core.GameCompat.DeclaredMethod(typeof(NCreatureVisuals), "AddFormVfx");

    [HarmonyPostfix]
    public static void Postfix(NCreatureVisuals __instance, object[] __args)
    {
        try
        {
            var formVfx = (__args != null && __args.Length > 0 ? __args[0] : null) as Node2D;
            if (formVfx == null || !GodotObject.IsInstanceValid(formVfx)) return;

            var creature = CreatureOf(__instance);
            if (creature?.Monster is not CharBossMonster boss) return;   // 玩家/别的敌人：保持原样

            if (FaceLeft(formVfx, DesiredSign(__instance)))
            {
                if (_logs++ < 8)
                {
                    Logger.Log($"[CharBoss] 形态特效已水平镜像（朝左面向玩家）：{formVfx.GetType().Name} " +
                               $"@ {boss.Id.Entry}，scale={formVfx.Scale}");
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 形态特效翻转失败（忽略）: " + e.Message);
        }
    }

    /// <summary>从 `NCreatureVisuals` 往上找 `NCreature`，再取它公开的 `Entity`（最多找 4 层）。</summary>
    private static Creature? CreatureOf(NCreatureVisuals? visuals)
    {
        try
        {
            Node? node = visuals;
            for (int depth = 0; node != null && depth < 4; depth++)
            {
                if (node is NCreature creatureNode) return creatureNode.Entity;
                node = node.GetParent();
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    /// <summary>
    /// 把节点水平镜像（只动 scale.x，幂等）。返回 true = 这次真的翻了。
    ///
    /// ★ v0.2.763（用户 2026-11-18：「群蛇形态等形态牌的特效在 boss 身上是反的，
    ///   之前统一修过，新版本更新后好像就都坏了」）：
    ///   以前只看**节点自己**的 `scale.x &lt; 0` 就认为「已经翻过」。可是新版本把
    ///   `FormVfxHolder` 挪进了被她镜像过的 `Body` 子树里 ⇒ 特效**已经**被父级翻过一次，
    ///   我们再翻一次就正好抵消 ⇒ 「反的」。
    ///
    ///   现在改成看**最终生效的方向**（`GlobalScale.X`，把父级镜像也算进去）：
    ///   · 净方向已经是负（父级已经把它镜像朝左）⇒ 什么都不做；
    ///   · 净方向是正 ⇒ 翻自己的 `scale.x`，让**净方向**变负。
    ///   这样无论原版把 Holder 放在 Body 里还是放在 Body 外，结果都一致。
    /// </summary>
    /// <summary>
    /// **本体当前的净朝向**（±1）—— 形态特效要跟它同向，而不是写死朝左。
    ///
    /// ★ v0.2.877（用户 2026-11-22：「boss 使用形态牌获得的特效还是反的」）：
    ///   上一版假设「特效必须朝左」，靠 `GlobalScale.X &lt; 0` 判断是否已翻。可 `FormVfxHolder`
    ///   现在既可能在被她镜像过的 `Body` 子树里、也可能在外面，某些形态的原生朝向本身又是反的
    ///   ⇒ 单看一个方向会翻错。改成**跟 `Visuals.Body` 的净方向对齐**：本体翻哪边、特效就跟哪边。
    /// </summary>
    private static float DesiredSign(NCreatureVisuals? visuals)
    {
        try
        {
            var body = AccessTools.Property(visuals!.GetType(), "Body")?.GetValue(visuals) as Node2D
                       ?? AccessTools.Field(visuals!.GetType(), "Body")?.GetValue(visuals) as Node2D;
            if (body != null && GodotObject.IsInstanceValid(body) && Math.Abs(body.GlobalScale.X) > 0.001f)
                return body.GlobalScale.X < 0f ? -1f : 1f;
        }
        catch { /* 忽略 */ }
        return -1f;    // 拿不到本体 ⇒ 沿用旧口径（朝左）
    }

    private static bool FaceLeft(Node2D node, float desiredSign = -1f)
    {
        try
        {
            if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree()) return false;

            float net = 1f;
            try { net = node.GlobalScale.X; }        // 含所有父级
            catch { net = node.Scale.X; }
            // ★ v0.2.877：不再写死「朝左」，而是和**本体当前的净方向**比（见 DesiredSign）。
            float want = desiredSign < 0f ? -1f : 1f;
            if ((net < 0f ? -1f : 1f) == want) return false;   // 已经和本体同向 ⇒ 别重复翻
            if (Math.Abs(net) < 0.001f) return false;

            var scale = node.Scale;
            float x = Math.Abs(scale.X) < 0.001f ? 1f : Math.Abs(scale.X);
            node.Scale = new Vector2(want * x, scale.Y);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// 兜底（由 <c>BossFrameTick</c> 的 4Hz 慢车道调用）：形态特效如果被重建/重置，重新翻一次。
    /// </summary>
    internal static void EnsureFacing(CharBossMonster? boss)
    {
        try
        {
            if (!Core.GameCompat.HasFormVfx) return;   // 旧版没有「形态特效」这套节点
            var node = NCombatRoom.Instance?.GetCreatureNode(boss?.Creature);
            var visuals = node?.Visuals;
            if (visuals == null) return;
            // `FormVfxHolder` 也是新版才有的成员 ⇒ 反射取，旧版取不到就跳过
            var holder = AccessTools.Property(visuals.GetType(), "FormVfxHolder")?.GetValue(visuals) as Node;
            if (holder == null || !GodotObject.IsInstanceValid(holder)) return;

            foreach (var child in holder.GetChildren())
            {
                if (child is Node2D vfx && GodotObject.IsInstanceValid(vfx)) FaceLeft(vfx, DesiredSign(visuals));
            }
        }
        catch { /* 忽略 */ }
    }
}
