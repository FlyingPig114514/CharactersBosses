using System;
using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace CharacterBosses.Patches;

/// <summary>
/// **地图上那颗 Boss 徽章**：没有专属美术的档位，别再画成「原版 TheKinBoss 的剪影」，改成**角色自己的徽记**。
///
/// ## 背景（用户 2026-10-29 实机反馈）
/// <c>「地图 boss 节点提示写静默猎手，但图标是铁甲战士的头像 —— 这不对吧，上面是战士的头像」</c>
///
/// 徽章贴图的来源（已反编译 <c>NBossMapPoint._Ready</c>）：
/// <code>
/// enc = (Point == Map.SecondBossMapPoint) ? Act.SecondBossEncounter : Act.BossEncounter;
/// PlaceholderImage.Texture   = Cache.GetAsset&lt;Texture2D&gt;(enc.BossNodePath + ".png");
/// PlaceholderOutline.Texture = Cache.GetAsset&lt;Texture2D&gt;(enc.BossNodePath + "_outline.png");
/// </code>
/// 我们的遭遇只有少数档位有专属图（silent_shiv / silent_poison / soul_bone / defect_*），
/// 其余走 <see cref="Encounters.CharBossEncounterBase{TMonster}.BossNodePath"/> 的兜底 =
/// **TheKinBoss 的占位图**（那张是三只披甲武士的剪影，看着就像「战士的头像」）。
/// 于是第三幕「炼药猎 + 巨石战」两颗徽章长得一模一样、且都和名字对不上（日志实证：
/// <c>结算/顶栏图标缺图 → 改用原版占位图标（...silent_brewer_boss_encounter.png → ...the_kin_boss.png）</c>）。
///
/// ## 现在的做法
/// 只对**我们自己的、且没有专属美术**的遭遇动手：把徽章换成
/// <see cref="CharacterModel.IconTexture"/>（原版顶栏那个 88×88 角色徽记，带透明底）的**白色剪影**，
/// 衬底换成白色圆盘（和手工图标 soul_bone 同一思路：挡住从徽章里穿过去的路线虚线）。
/// 白色 + 原版的 <c>SelfModulate</c> 染色 ⇒ 观感与原版 Boss 徽章一致（深色剪影 + 地图底色衬底）。
///
/// 专属美术有的档位**一个像素都不动**；别人的遭遇（没开替换 / 原版 Boss）也一个像素都不动。
/// </summary>
[HarmonyPatch(typeof(NBossMapPoint), nameof(NBossMapPoint._Ready))]
internal static class BossMapPointCharacterIconPatch
{
    [HarmonyPostfix]
    public static void Postfix(NBossMapPoint __instance)
    {
        try
        {
            if (__instance == null || !GodotObject.IsInstanceValid(__instance)) return;

            var traverse = Traverse.Create(__instance);
            var runState = traverse.Field("_runState").GetValue<IRunState>();
            if (runState == null) return;

            var act = runState.Act;
            if (act == null) return;

            // 和原版同一套规则：这颗是「第二个 Boss」的地图点就取第二槽。
            MapPoint? point = null;
            try { point = __instance.Point; } catch { /* 拿不到就按第一只 */ }

            var image = __instance.GetNodeOrNull<TextureRect>("%PlaceholderImage");
            if (image == null) return;

            // ⚠⚠ 2026-10-30 用户实测：「进阶 10 第三幕第二场是**根除骨**，可第二颗徽章画的却是**储君**」。
            //   根因：以前两颗节点都拿 `[本槽, 另一槽]` 两个候选去挑，而挑中的条件是
            //   「这个遭遇**没有**专属美术」—— 第二场的根除骨恰好**有**专属美术（`soul_bone`），
            //   于是第一轮就被 `UsesPlaceholderArt` 否掉，接着回落到**第一场的储君**
            //   ⇒ 第二颗徽章被画成储君，两颗长得一模一样。
            //
            //   现在改成**先确定「这颗节点是哪一格」，再只认那一格的遭遇**：
            //     ① 先看节点当前显示的贴图路径和哪个遭遇的 `BossNodePath` 对得上（最可靠，直接来自同步数据）；
            //     ② 对不上再用 `Point` 判第二颗；
            //     ③ 仍然判不出来，才退回「按占位图猜」的老办法。
            //   —— 一旦确定是哪一格，就**只**用那一格的遭遇，绝不跨槽回落。
            string currentPath = string.Empty;
            try { currentPath = image.Texture?.ResourcePath ?? string.Empty; } catch { currentPath = string.Empty; }

            bool isSecond = SamePoint(point, SafeSecondBossMapPoint(runState));
            // 两颗节点可能共用同一张占位图（都用 TheKinBoss 占位）⇒ 先按「这颗大概是第几颗」排好比对顺序，
            // 这样同名贴图时不会把第二颗认成第一颗。
            EncounterModel? byArt = isSecond
                ? MatchByArt(currentPath, act.SecondBossEncounter, act.BossEncounter)
                : MatchByArt(currentPath, act.BossEncounter, act.SecondBossEncounter);

            EncounterModel? encounter = byArt;
            if (encounter == null)
            {
                encounter = isSecond ? act.SecondBossEncounter : act.BossEncounter;
                // 第二颗节点但第二槽还是空的（进阶 10 的地图绘制比「第二槽被替换」更早）
                // ⇒ 用**确定性阵容**推出第二场的主 Boss 再取它的遭遇。
                if (encounter == null && isSecond)
                {
                    try
                    {
                        var secondKey = Core.BossLineups.For(act.Index, 1);
                        if (secondKey != null && secondKey.Length > 0)
                        {
                            var viaLineup = Core.BossKeys.EncounterOf(secondKey[0]);
                            if (viaLineup != null)
                            {
                                encounter = viaLineup;
                                Logger.Log($"[CharBoss] 地图徽章：第二场 Boss 槽还是空的 ⇒ 按阵容推出 " +
                                           $"{secondKey[0]}（{viaLineup.Id.Entry}）");
                            }
                        }
                    }
                    catch { /* 推不出来就保持原样 */ }
                }
            }

            if (encounter == null) return;
            if (!OurEncounterIconPaths.IsOurEncounter(encounter.Id)) return;

            // 诊断（每「遭遇 + 第几颗」一次）：两颗节点可能共用同一张占位图，这时只能靠 `Point` 判第几颗，
            // 一旦判错就会画成另一颗的角色 —— 这行日志能立刻看出来（用户 2026-10-30 就是这样抓到的）。
            _ = PickedOnce(encounter, isSecond, byArt != null);

            // 这一格的遭遇**自己有专属美术**、而且节点现在画的正是它 ⇒ 一个像素都不动
            if (!UsesPlaceholderArt(encounter) && MatchByArt(currentPath, encounter) != null) return;

            var character = TopBarBossIconFallbackPatch.CharacterFor(encounter);
            var emblem = CharacterEmblem.Silhouette(character);
            if (emblem == null) return;

            image.Texture = emblem;
            image.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;

            // ⚠ **把这颗节点上「别人的美术」全部藏掉**（用户 2026-10-30：「女王的装饰怎么还在？」）。
            //
            // 反编译 + 反查 `res://scenes/ui/boss_map_point.tscn` 得到节点树：
            //   BossMapPoint(Control)
            //     └ SpriteContainer(Node2D)
            //         ├ SpineSprite(SpineSprite, 带 boss_map_point.gdshader)   ← 光效/装饰可能是它
            //         └ PlaceholderImage(TextureRect, 374×350)
            //             └ PlaceholderOutline(TextureRect, show_behind_parent) ← <BossNodePath>_outline.png
            //   之前只把 `PlaceholderOutline` 设成不可见，用户实测那圈尖刺/叶子还在 ⇒
            //   说明它来自别处（Boss 的 Spine 装饰）。现在干脆：**SpriteContainer 下除我们这张图以外，
            //   所有 CanvasItem 一律隐藏**，并把这些节点名记一行日志（下次一眼能看出是谁在画）。
            try
            {
                var container = image.GetParent();
                if (container != null)
                {
                    var hidden = new List<string>();
                    HideExcept(container, image, hidden);
                    if (hidden.Count > 0)
                    {
                        Logger.Log($"[CharBoss] 地图 Boss 徽章：已隐藏节点上的旧美术 [{string.Join(",", hidden)}]");
                    }
                }
            }
            catch { /* 隐藏失败不影响换图 */ }

            CharacterEmblem.LogOnce(encounter, character, isSecond ? "第二颗" : "第一颗");
        }
        catch (Exception e)
        {
            // 画不出徽章就保持原版占位图，绝不把地图搞崩（地图屏崩了整局都进不去）
            Logger.Log("[CharBoss] 地图 Boss 徽章改用角色徽记失败（保持占位图）: " + e.Message);
        }
    }

    /// <summary>递归隐藏 <paramref name="root"/> 下所有 CanvasItem（除了 <paramref name="keep"/> 自己），并记下名字。</summary>
    private static void HideExcept(Node root, Node keep, List<string> hidden)
    {
        try
        {
            foreach (var child in root.GetChildren())
            {
                if (child is not Node node) continue;
                if (ReferenceEquals(node, keep)) continue;      // 我们自己那张图：递归进去（它的子节点要藏）但不动它
                if (node is CanvasItem canvas) canvas.Visible = false;
                hidden.Add(node.Name.ToString());
                HideExcept(node, keep, hidden);
            }
        }
        catch { /* 忽略 */ }
    }

    private static readonly HashSet<string> _pickLogs = new(StringComparer.Ordinal);

    /// <summary>诊断：这颗节点被判成哪一格、遭遇是谁、是不是靠贴图判出来的（每组合只报一次）。</summary>
    private static bool PickedOnce(EncounterModel encounter, bool isSecond, bool byArt)
    {
        try
        {
            string entry;
            try { entry = encounter.Id.Entry; } catch { return false; }
            if (!_pickLogs.Add($"{entry}|{isSecond}|{byArt}")) return false;
            Logger.Log($"[CharBoss] 地图 Boss 徽章判定：{entry}（{(isSecond ? "第二颗" : "第一颗")}，" +
                       $"{(byArt ? "按节点现有贴图判定" : "按地图点判定")}）");
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// 这颗节点现在显示的贴图属于哪个遭遇（拿不到返回 null）。
    /// 比对 `<c>BossNodePath + ".png"</c>` 前缀 —— 地图上的贴图路径就是它。
    /// </summary>
    private static EncounterModel? MatchByArt(string resourcePath, params EncounterModel?[] candidates)
    {
        if (string.IsNullOrEmpty(resourcePath)) return null;
        foreach (var cand in candidates)
        {
            if (cand == null) continue;
            try
            {
                string path = cand.BossNodePath;
                if (string.IsNullOrEmpty(path)) continue;
                if (resourcePath.StartsWith(path, StringComparison.Ordinal)) return cand;
            }
            catch { /* 忽略这一只 */ }
        }
        return null;
    }

    /// <summary>两个 <see cref="MapPoint"/> 是不是同一个点（引用相同，或坐标相同 —— 读档后对象会换）。</summary>
    private static bool SamePoint(MapPoint? a, MapPoint? b)
    {
        if (a == null || b == null) return false;
        if (ReferenceEquals(a, b)) return true;
        try
        {
            var ca = Traverse.Create(a).Field("coord").GetValue();
            var cb = Traverse.Create(b).Field("coord").GetValue();
            if (ca == null || cb == null) return false;
            return ca.Equals(cb);
        }
        catch { return false; }
    }

    /// <summary>安全地取 `runState.Map.SecondBossMapPoint`（没有第二只 Boss 时是 null）。</summary>
    private static MapPoint? SafeSecondBossMapPoint(IRunState runState)
    {
        try { return runState.Map?.SecondBossMapPoint; }
        catch { return null; }
    }

    /// <summary>这颗节点现在显示的贴图是不是「原版占位图」（TheKinBoss 那张）。</summary>
    private static bool ShowingPlaceholder(TextureRect image)
    {
        try
        {
            var current = image.Texture;
            if (current == null) return true;
            string path = current.ResourcePath ?? string.Empty;
            if (path.Length == 0) return true;

            var kin = ModelDb.Encounter<MegaCrit.Sts2.Core.Models.Encounters.TheKinBoss>();
            if (kin == null) return false;
            string? kinPath = null;
            try { kinPath = kin.BossNodePath; } catch { /* 忽略 */ }
            if (string.IsNullOrEmpty(kinPath)) return false;
            return path.StartsWith(kinPath!, StringComparison.Ordinal);
        }
        catch { return false; }
    }

    /// <summary>这个遭遇是不是在用「TheKinBoss 占位图」（= 我们没有给它画专属美术）。</summary>
    private static bool UsesPlaceholderArt(EncounterModel encounter)
    {
        try
        {
            string? path = encounter.BossNodePath;
            if (string.IsNullOrEmpty(path)) return true;

            var kin = ModelDb.Encounter<MegaCrit.Sts2.Core.Models.Encounters.TheKinBoss>();
            if (kin != null)
            {
                string? kinPath = null;
                try { kinPath = kin.BossNodePath; } catch { /* 忽略 */ }
                if (!string.IsNullOrEmpty(kinPath) && kinPath == path) return true;
            }

            // 万一以后换了别的占位路径：贴图不存在也算「没有专属美术」。
            return !ResourceLoader.Exists(path + ".png");
        }
        catch { return false; }
    }
}

/// <summary>角色徽记 → 地图徽章贴图（白色剪影 + 白色圆盘衬底），按角色缓存。</summary>
internal static class CharacterEmblem
{
    /// <summary>输出边长。原版占位图是 512，这里 384 足够（徽章显示区约 374×350）。</summary>
    private const int Side = 384;

    /// <summary>
    /// 徽章颜色 —— **必须用原版占位图那种深灰，不能用纯白**。
    ///
    /// 用户 2026-10-30 实测：白色剪影在某些地图状态下**不会被染色**，于是整块白得刺眼、
    /// 和背后的底图完全糊在一起。原版 Boss 徽章贴图本身就是深灰（约 `#5D6870`），不依赖染色。
    /// 这里：剪影比圆盘再深一点，保证「角色形状」能从圆盘上分出来。
    /// </summary>
    private static readonly Color SilhouetteColor = new(0.29f, 0.33f, 0.37f);   // ≈ #4A545E
    private static readonly Color DiscColor = new(0.365f, 0.408f, 0.439f);      // ≈ #5D6870（原版占位图灰）

    private static readonly Dictionary<string, Texture2D?> Cache = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Logged = new(StringComparer.Ordinal);
    private static Texture2D? _disc;

    /// <summary>
    /// 角色徽记的**白色剪影**：RGB 全白、alpha 沿用原图（原版顶栏徽记本来就是透明底）。
    /// 地图会用 <c>ActModel.MapTraveledColor</c> 染色，白剪影染出来就是原版那种深色剪影。
    /// </summary>
    internal static Texture2D? Silhouette(CharacterModel? character)
    {
        if (character == null) return null;

        string key;
        try { key = character.Id.Entry; } catch { return null; }
        if (Cache.TryGetValue(key, out var cached)) return cached;

        Texture2D? result = null;
        try
        {
            var source = character.IconTexture;
            var image = source?.GetImage();
            if (image != null)
            {
                if (image.IsCompressed()) image.Decompress();
                image.Convert(Image.Format.Rgba8);
                if (image.GetWidth() != Side || image.GetHeight() != Side)
                {
                    image.Resize(Side, Side, Image.Interpolation.Cubic);
                }

                for (int y = 0; y < image.GetHeight(); y++)
                {
                    for (int x = 0; x < image.GetWidth(); x++)
                    {
                        float alpha = image.GetPixel(x, y).A;
                        image.SetPixel(x, y, new Color(SilhouetteColor.R, SilhouetteColor.G, SilhouetteColor.B, alpha));
                    }
                }
                result = ImageTexture.CreateFromImage(image);
            }
        }
        catch (Exception e)
        {
            Logger.Log($"[CharBoss] 角色徽记 {key} 生成失败: " + e.Message);
        }

        Cache[key] = result;
        return result;
    }

    /// <summary>白色圆盘衬底（原版那颗 outline 就是「徽章背后的实心盘」，用来挡住路线虚线）。</summary>
    internal static Texture2D? Disc()
    {
        if (_disc != null && GodotObject.IsInstanceValid(_disc)) return _disc;

        try
        {
            var image = Image.CreateEmpty(Side, Side, false, Image.Format.Rgba8);
            float center = Side / 2f;
            float radius = Side / 2f - 6f;
            float radiusSq = radius * radius;

            for (int y = 0; y < Side; y++)
            {
                for (int x = 0; x < Side; x++)
                {
                    float dx = x + 0.5f - center;
                    float dy = y + 0.5f - center;
                    float distanceSq = dx * dx + dy * dy;

                    // 边缘 1.5px 做点抗锯齿，免得圆盘有一圈锯齿
                    float edge = Mathf.Clamp((radiusSq - distanceSq) / (2f * radius * 1.5f), 0f, 1f);
                    if (edge > 0f)
                        image.SetPixel(x, y, new Color(DiscColor.R, DiscColor.G, DiscColor.B, edge));
                }
            }
            _disc = ImageTexture.CreateFromImage(image);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 徽章圆盘衬底生成失败: " + e.Message);
            _disc = null;
        }
        return _disc;
    }

    internal static void LogOnce(EncounterModel encounter, CharacterModel? character, string slot = "")
    {
        try
        {
            string entry = encounter.Id.Entry;
            if (!Logged.Add(entry + "|" + slot)) return;
            Logger.Log($"[CharBoss] 地图 Boss 徽章{slot}：{entry} 没有专属美术 → 改用角色徽记 " +
                       $"{character?.Id.Entry ?? "<未知>"}（原版同色深灰剪影；旧的外圈装饰已隐藏）");
        }
        catch { /* 忽略 */ }
    }
}
