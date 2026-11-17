#if STS2_113   // 愚者 / 塔罗牌 只在 0.113 存在（用户 2026-11-14：107 不加这个 Boss）
using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Cards;

namespace CharacterBosses.Patches;

/// <summary>
/// 4 张自造塔罗牌用各自「本家」的卡框颜色（用户 2026-11-15）。
///
/// 反编译结论：
///   · CardModel.FramePath = atlases/ui_atlas.sprites/card/card_frame_{type}_s.tres（所有卡池共用同一张框贴图）；
///   · 颜色来自 CardModel.FrameMaterial => VisualCardPool.FrameMaterial，
///     而 CardPoolModel.FrameMaterial 加载 materials/cards/frames/card_frame_*_mat.tres；
///   · 观者 mod 的紫色框就是它自己加的一份 ShaderMaterial：
///       shader = res://shaders/hsv.gdshader   ← 游戏本体自带
///       h = 0.715  s = 0.65  v = 1.1
///     ⇒ 我们按同样参数新建一份即可，不需要从别人 mod 拿任何美术资源 ⇒「不装模组也显示原本颜色」成立。
///   · 亡命之徒（Hermit）：Hermit.pck / Downfall.pck 里没有 card_frame 材质 ⇒ 它用本体自带的那几份之一。
///     所以装了 Hermit 就直接借它卡池的 FrameMaterial；没装则退回一份黄褐色 HSV 材质。
/// </summary>
[HarmonyPatch(typeof(CardModel), "get_FrameMaterial")]
public static class FoolTarotFrameColorPatch
{
    [HarmonyPostfix]
    public static void Postfix(CardModel __instance, ref Material __result)
    {
        try
        {
            var ours = TarotCardFrame.MaterialFor(__instance);
            if (ours != null) __result = ours;
        }
        catch { /* 纯外观，失败就用原版 */ }
    }
}

/// <summary>
/// 4 张自造塔罗牌的**费用宝石**换成各自本家的颜色（用户 2026-11-15）。
///
/// 反编译 `NCard.Reload()`（private）：
///     _energyIcon.Texture = Model.EnergyIcon;      // ← 宝石贴图一次性在这里赋
/// 所以 postfix 改 `_energyIcon`（以及 `_unplayableEnergyIcon`）的 Texture 即可；
/// 只对 `IsOurs` 的 4 张牌动手，其它任何卡牌（含别的模组的卡）完全不受影响。
/// </summary>
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Cards.NCard), "Reload")]
public static class FoolTarotEnergyGemPatch
{
    private static AccessTools.FieldRef<MegaCrit.Sts2.Core.Nodes.Cards.NCard, TextureRect> _gem;
    private static AccessTools.FieldRef<MegaCrit.Sts2.Core.Nodes.Cards.NCard, TextureRect> _unplayableGem;

    [HarmonyPostfix]
    public static void Postfix(MegaCrit.Sts2.Core.Nodes.Cards.NCard __instance)
    {
        try
        {
            var model = __instance?.Model;
            if (model == null) return;
            var tex = TarotCardFrame.GemTextureFor(model);
            if (tex == null) return;

            _gem ??= AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Nodes.Cards.NCard, TextureRect>("_energyIcon");
            var gem = _gem(__instance);
            if (gem != null) gem.Texture = tex;

            _unplayableGem ??= AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Nodes.Cards.NCard, TextureRect>("_unplayableEnergyIcon");
            var unplayable = _unplayableGem(__instance);
            if (unplayable != null) unplayable.Texture = tex;
        }
        catch { /* 纯外观 */ }
    }
}

internal static class TarotCardFrame
{
    private static Material _watcherPurple;
    private static Material _hermitMaterial;
    private static bool _hermitLooked;

    internal static Material MaterialFor(CardModel card)
    {
        if (card == null) return null;
        if (card is TarotFasting || card is TarotDevaForm) return WatcherPurple();
        if (card is TarotBrawl || card is TarotEternalForm) return HermitMaterial();
        return null;
    }

    /// <summary>观者紫（照它自己那份材质的 HSV 参数，用本体自带的 hsv 着色器新建）。</summary>
    private static Material WatcherPurple()
    {
        if (_watcherPurple != null) return _watcherPurple;
        try
        {
            _watcherPurple = MakeHsv(0.715f, 0.65f, 1.1f);
            if (_watcherPurple != null)
                Logger.Log("[CharBoss] 塔罗牌框色：斋戒 / 天人形态 = 观者紫（hsv 0.715 / 0.65 / 1.1）");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 塔罗牌框色（观者紫）失败: " + e.Message); }
        return _watcherPurple;
    }

    /// <summary>亡命之徒：装了 Hermit 就借它的材质，没装用调好的黄褐色。</summary>
    private static Material HermitMaterial()
    {
        if (_hermitLooked) return _hermitMaterial;
        _hermitLooked = true;

        try
        {
            foreach (var pool in ModelDb.AllCardPools)
            {
                try
                {
                    if (pool == null) continue;
                    foreach (var cid in pool.AllCardIds)
                    {
                        string entry = null;
                        try { entry = cid.Entry; } catch { continue; }
                        if (!string.Equals(entry, "HERMIT-BRAWL", StringComparison.Ordinal)
                            && !string.Equals(entry, "BRAWL", StringComparison.Ordinal)) continue;
                        _hermitMaterial = pool.FrameMaterial;
                        Logger.Log("[CharBoss] 塔罗牌框色：斗殴 / 永恒形态 借用 Hermit 卡池材质（" + pool.Title + "）");
                        return _hermitMaterial;
                    }
                }
                catch { /* 下一个池 */ }
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 塔罗牌框色（找 Hermit 卡池）失败: " + e.Message); }

        try
        {
            _hermitMaterial = MakeHsv(0.11f, 0.55f, 1.05f);
            if (_hermitMaterial != null)
                Logger.Log("[CharBoss] 塔罗牌框色：斗殴 / 永恒形态 = 黄褐（没装 Hermit，用自带 hsv 调色）");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 塔罗牌框色（黄褐）失败: " + e.Message); }
        return _hermitMaterial;
    }

    // ── 费用宝石 ────────────────────────────────────────────────────────────────
    // 卡面左上的「费用宝石」贴图来自 CardModel.EnergyIcon => VisualCardPool.EnergyIconPath
    // => EnergyIconHelper.GetPath(EnergyColorName)，也就是 atlases/ui_atlas.sprites/card/energy_<名字>.tres。
    // 观者 / 亡命之徒两个模组都**没有**自带宝石贴图（Watcher.pck 里只有 ui/combat/energy_counters 的
    // 战斗能量球，Hermit.pck 里只有 images/character/energy_icon.png），所以它们的「本家宝石」
    // 在本体里不存在 ⇒ 我们照本体的中性灰宝石 energy_colorless 乘上角色色（DeckEntryCardColor）
    // 生成两张 PNG 打包进本模组（tools\art\tint_png.gd），不依赖任何别的模组。

    private static Texture2D _watcherGem;
    private static Texture2D _hermitGem;
    private static bool _watcherGemLooked;
    private static bool _hermitGemLooked;

    internal static Texture2D GemTextureFor(CardModel card)
    {
        if (card == null) return null;
        if (card is TarotFasting || card is TarotDevaForm) return WatcherGem();
        if (card is TarotBrawl || card is TarotEternalForm) return HermitGem();
        return null;
    }

    private static Texture2D WatcherGem()
    {
        if (_watcherGemLooked) return _watcherGem;
        _watcherGemLooked = true;
        _watcherGem = LoadGem(TarotCardArt.WatcherGem, "观者紫");
        return _watcherGem;
    }

    private static Texture2D HermitGem()
    {
        if (_hermitGemLooked) return _hermitGem;
        _hermitGemLooked = true;
        _hermitGem = LoadGem(TarotCardArt.HermitGem, "亡命之徒黄褐");
        return _hermitGem;
    }

    private static Texture2D LoadGem(string path, string what)
    {
        try
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (!ResourceLoader.Exists(path))
            {
                Logger.Log("[CharBoss] 塔罗牌费用宝石（" + what + "）资源不存在: " + path);
                return null;
            }
            var tex = ResourceLoader.Load<Texture2D>(path);
            if (tex != null) Logger.Log("[CharBoss] 塔罗牌费用宝石（" + what + "）= " + path);
            return tex;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 塔罗牌费用宝石（" + what + "）加载失败: " + e.Message);
            return null;
        }
    }

    /// <summary>用本体自带的 res://shaders/hsv.gdshader 新建材质（和原版卡框同一套着色器）。</summary>
    private static Material MakeHsv(float h, float s, float v)
    {
        try
        {
            var shader = ResourceLoader.Load<Shader>("res://shaders/hsv.gdshader");
            if (shader == null)
            {
                Logger.Log("[CharBoss] 塔罗牌框色：找不到 res://shaders/hsv.gdshader");
                return null;
            }
            var mat = new ShaderMaterial { Shader = shader };
            mat.ResourceLocalToScene = true;
            mat.SetShaderParameter("h", h);
            mat.SetShaderParameter("s", s);
            mat.SetShaderParameter("v", v);
            return mat;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 塔罗牌框色：新建材质失败: " + e.Message);
            return null;
        }
    }
}
#endif