#if STS2_111   // 观者 Boss 只在这个构建里
using System;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Helpers;
using CharacterBosses.UI;

namespace CharacterBosses.Core;

/// <summary>
/// **塔1「时间扭曲」触发时的特效**（用户 2026-11-18 提，v0.2.999 首版，v0.2.1000 按实机反馈重做）。
///
/// ## 塔1 到底播了什么（反编译 `SlayTheSpire/desktop-1.0.jar` 得到，不是猜的）
/// <code>
/// // com/megacrit/cardcrawl/powers/TimeWarpPower.class（onAfterUseCard，计数归零那一刻）
/// AbstractDungeon.actionManager.callEndTurnEarlySequence();             // 回合直接结束
/// AbstractDungeon.effectsQueue.add(new BorderFlashEffect(Color.GOLD));  // ① 屏幕四周金色辉光
/// AbstractDungeon.topLevelEffectsQueue.add(new TimeWarpTurnEndEffect()); // ② 屏幕中央那个大钟
/// for (AbstractMonster m : AbstractDungeon.getMonsters().monsters)
///     addToBot(new ApplyPowerAction(m, m, new StrengthPower(m, STR_AMT), STR_AMT)); // ③ 力量 +2
///
/// // com/megacrit/cardcrawl/vfx/combat/TimeWarpTurnEndEffect.class
/// img = AbstractPower.atlas.findRegion("128/time");   // ← 钟 = 「时间扭曲」能力图标那张金怀表
///
/// // com/megacrit/cardcrawl/vfx/BorderFlashEffect.class
/// img = ImageMaster.BORDER_GLOW_2;    // ← vfx/vfx.atlas 里 1280×720 的满屏「四周辉光」图
/// Gdx.gl.glBlendFunc(GL_ONE, GL_ONE); // 叠加混合；颜色 = Color.GOLD，alpha 用即 Interpolation 淡出
/// </code>
///
/// ## v0.2.1000 按用户实机反馈改了什么（原话：「你那个黄光很明显和1代的不一样，并且也没有钟的动画出现」）
/// · **黄光**：v0.2.999 是自己拼了 4 条纯色 `ColorRect`（硬边、没有渐变）⇒ 和塔1 完全不像。
///   现在改成塔1 的那张图本身：`vfx/vfx.atlas` 的 `borderGlow2`（`xy 2,604 size 1280,720`）裁出来
///   ⇒ `res://CharacterBosses/images/vfx/border_glow_2.png`，铺满全屏 + **叠加混合**（`Color.GOLD` 色调），
///   与塔1 `BorderFlashEffect(Color.GOLD, additive=true)` 同一条路子。
/// · **钟不出现了**：v0.2.999 用 `Sprite2D` + 一条 `SetParallel(true)`/`Chain()` 的复合 Tween
///   （起始 `modulate.a=0`、位置在屏幕下方）—— 实机里黄光出来了、钟一个像素都没有，
///   说明那条 tween 没推进（节点停在「屏幕外 + 全透明」的初始状态）。
///   现在改成：
///     ① 用 **`TextureRect`（Control）**（金边那条路已经证明 Control 在这个宿主里能正常渲染）；
///     ② 每条属性各用一条**最简单的顺序 Tween**（`position` / `rotation` / `scale` / `modulate:a`
///        各一条，全程不用 `SetParallel` / `Chain`）；
///     ③ **初始状态就是「看得见」的**：不透明、满尺寸、贴在屏幕底边露出一截 ——
///        万一 tween 又不推进，屏幕上会留下一个静止的金怀表（能一眼看出来），而不是像上次那样一片空白；
///     ④ 每帧不需要我们自己的任何虚回调（`_Process` / `_Draw` 在本模组自建节点上根本不派发）。
///
/// ## v0.2.1000 按实机反馈的第二轮调整（原话：「钟在屏幕中间转的时候卡了一下，并且钟的大小似乎有点大」）
/// · **卡了一下**：0.3.000 的旋转 tween 在悬停段用的是 `TweenInterval` ⇒ 整整 0.40s **不转**。
///   现在旋转全程匀速（三段都 Linear，4.7 rad/s ≈ 0.75 圈/秒，合计约 1 圈），
///   到达也不再带 `Back` 回弹（回弹叠加会像第二下停顿），悬停段改成「上浮 10px 再落回」；
/// · **有点大**：钟的尺寸从 0.40 屏短边收到 **0.30 屏短边**（1920×1080 下贴图矩形 345px、
///   实际可见的怀表约 230px）。
///
/// ## 资源（都随 PCK 发布，见 `tools\art\pck_extras.txt`）
/// · `res://CharacterBosses/images/powers/time_warp.png` —— 塔1 `powers/powers.atlas` 的 `128/time`
///   （能力图标 / 塔1 特效**同一份**像素）；
/// · `res://CharacterBosses/images/vfx/border_glow_2.png` —— 塔1 `vfx/vfx.atlas` 的 `borderGlow2`；
/// · `res://CharacterBosses/audio/STS_SFX_TimeWarp.ogg` —— jar 内 `audio/sound/STS_SFX_TimeWarp_v2.ogg`
///   （能力音效键 `POWER_TIME_WARP`，与本文件逐字节一致）。
/// </summary>
internal static class TimeWarpFx
{
    private const string SfxPath = "res://CharacterBosses/audio/STS_SFX_TimeWarp.ogg";
    private const string ClockPath = "res://CharacterBosses/images/powers/time_warp.png";
    private const string GlowPath = "res://CharacterBosses/images/vfx/border_glow_2.png";

    // ── 时间轴（秒）。塔1 的 TimeWarpTurnEndEffect 是 1.5s；这里把 1.5s 分成三段动作 ──
    private const float RiseSeconds = 0.42f;   // 从屏幕下方飞上来
    private const float HoldSeconds = 0.40f;   // 在中央旋转悬停
    private const float FallSeconds = 0.62f;   // 掉下去并淡出

    // ── 塔1 BorderFlashEffect 的时长约 0.5s（淡入极快 + pow2Out 淡出）──
    private const float FlashInSeconds = 0.10f;
    private const float FlashOutSeconds = 0.50f;

    private static AudioStream? _stream;
    private static Texture2D? _clock;
    private static Texture2D? _glow;
    private static bool _sfxLogged;
    private static bool _clockLogged;
    private static bool _glowLogged;
    private static int _plays;

    /// <summary>
    /// 播一次「时间扭曲」：音效 + 四周金色辉光（塔1 那张图）+ 屏幕中央那个钟。
    /// <paramref name="at"/> 只用于日志（谁是触发者）；拿不到也不影响播放。
    /// </summary>
    internal static void Play(Creature? at, string why)
    {
        string who = "?";
        try { who = at?.Name ?? "(不认识)"; } catch { /* 只是日志 */ }

        if (_plays++ < 20)
            Logger.Log($"[CharBoss] 时间扭曲特效：{why}（触发者={who}）⇒ 塔1 的金色辉光 + 钟 + 音效");

        try { PlaySound(); } catch (Exception e) { Logger.Log("[CharBoss] 时间扭曲：音效播放失败: " + e.Message); }
        try { SpawnBorderGlow(); } catch (Exception e) { Logger.Log("[CharBoss] 时间扭曲：辉光失败: " + e.Message); }
        try { SpawnClock(); } catch (Exception e) { Logger.Log("[CharBoss] 时间扭曲：时钟特效失败: " + e.Message); }
    }

    // ─────────────────────────────────────────────────────────────
    // 宿主 / 尺寸
    // ─────────────────────────────────────────────────────────────

    /// <summary>特效挂哪儿：优先自建 HUD 层（`CanvasLayer`，战斗 UI 之上），拿不到就退战斗房。</summary>
    private static Node? Host()
    {
        try
        {
            var layer = BossHudLayer.Get();
            if (layer != null && GodotObject.IsInstanceValid(layer)) return layer;
        }
        catch { /* 走兜底 */ }
        try
        {
            var room = NCombatRoom.Instance;
            if (room != null && GodotObject.IsInstanceValid(room)) return room;
        }
        catch { /* 没有战斗房 */ }
        return null;
    }

    /// <summary>屏幕尺寸（拿不到就给 1920×1080，宁可有偏差也不要崩）。</summary>
    private static Vector2 ViewSize()
    {
        try
        {
            if (Engine.GetMainLoop() is SceneTree tree)
            {
                var size = tree.Root.GetVisibleRect().Size;
                if (size.X > 32f && size.Y > 32f) return size;
            }
        }
        catch { /* 忽略 */ }
        return new Vector2(1920f, 1080f);
    }

    private static Texture2D? Load(string path, ref Texture2D? cache, ref bool logged, string what)
    {
        if (cache != null && GodotObject.IsInstanceValid(cache)) return cache;
        try
        {
            cache = ResourceLoader.Load<Texture2D>(path);
            if (cache == null && !logged)
            {
                Logger.Log($"[CharBoss] 时间扭曲：PCK 里没有 {path}（{what}缺失）");
                logged = true;
            }
            return cache;
        }
        catch (Exception e)
        {
            if (!logged) { Logger.Log($"[CharBoss] 时间扭曲：读{what}失败: {e.Message}"); logged = true; }
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // ① 音效
    // ─────────────────────────────────────────────────────────────

    private static void PlaySound()
    {
        try
        {
            if (_stream == null)
            {
                try { _stream = ResourceLoader.Load<AudioStream>(SfxPath); } catch { _stream = null; }
                if (_stream == null) { try { _stream = AudioStreamOggVorbis.LoadFromFile(SfxPath); } catch { _stream = null; } }
                if (_stream == null)
                {
                    if (!_sfxLogged) { Logger.Log("[CharBoss] 时间扭曲：PCK 里没有 " + SfxPath + "（音效缺失）"); _sfxLogged = true; }
                    return;
                }
            }
            Node? host = null;
            try { var audio = NAudioManager.Instance; if (GodotObject.IsInstanceValid(audio)) host = audio; } catch { host = null; }
            if (host == null) host = Host();
            if (host == null) return;

            var player = new AudioStreamPlayer { Stream = _stream, VolumeDb = -1f, Autoplay = false };
            player.Name = "CharBossTimeWarpSfx";
            try { player.Bus = new StringName("SFX"); } catch { /* 没这条总线就 Master */ }
            host.AddChildSafely(player);
            player.Connect(AudioStreamPlayer.SignalName.Finished, Callable.From(() =>
            {
                try { player.QueueFree(); } catch { /* 随场景一起没了 */ }
            }));
            player.Play();
        }
        catch (Exception e) { Logger.Log("[CharBoss] 时间扭曲：音效失败（不影响战斗）: " + e.Message); }
    }

    // ─────────────────────────────────────────────────────────────
    // ② 屏幕四周的金色辉光（= 塔1 BorderFlashEffect(Color.GOLD)，用塔1 那张 borderGlow2）
    // ─────────────────────────────────────────────────────────────

    private static void SpawnBorderGlow()
    {
        var tex = Load(GlowPath, ref _glow, ref _glowLogged, "辉光贴图");
        if (tex == null) return;

        Node? host = Host();
        if (host == null) return;

        var size = ViewSize();

        var glow = new TextureRect
        {
            Name = "CharBossTimeWarpGlow",
            Texture = tex,
            Position = Vector2.Zero,
            Size = size,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            // 塔1 用 Color.GOLD 着色 + 叠加混合：白色辉光图 ⇒ 金色光晕
            Modulate = new Color(1f, 0.82f, 0.32f, 0f),
        };
        // 叠加混合（塔1 里是 Gdx.gl.glBlendFunc(GL_ONE, GL_ONE)）
        try
        {
            glow.Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        }
        catch { /* 拿不到材质就退回普通混合，颜色仍然对 */ }

        host.AddChildSafely(glow);

        // 一条最简单的顺序 tween：淡入 → 淡出 → 收走
        var tween = glow.CreateTween();
        tween.TweenProperty(glow, "modulate:a", 0.9f, FlashInSeconds)
             .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(glow, "modulate:a", 0f, FlashOutSeconds)
             .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tween.TweenCallback(Callable.From(() => Free(glow)));

        if (_plays <= 3)
            Logger.Log($"[CharBoss] 时间扭曲：辉光已铺满全屏（图 {tex.GetSize()} ⇒ 屏幕 {size}，叠加混合，tween 有效={tween.IsValid()}）");
    }

    // ─────────────────────────────────────────────────────────────
    // ③ 那个钟（塔1 的贴图；上升 → 旋转悬停 → 落下淡出）
    // ─────────────────────────────────────────────────────────────

    private static void SpawnClock()
    {
        var tex = Load(ClockPath, ref _clock, ref _clockLogged, "钟的贴图");
        if (tex == null) return;

        Node? host = Host();
        if (host == null) return;

        var size = ViewSize();
        float min = Mathf.Min(size.X, size.Y);

        Vector2 texSize = tex.GetSize();                       // 128 × 128
        // ★ v0.2.1000（用户 2026-11-28：「钟的大小似乎有点大」）：0.40 → 0.30 屏短边
        float big = Mathf.Max(1.6f, min * 0.30f / Mathf.Max(texSize.X, 1f));
        Vector2 half = texSize * 0.5f;

        // 视觉中心：先贴在屏幕底边（露出一截，万一 tween 不跑也能一眼看见），最后落到屏幕中间偏上
        var center = new Vector2(size.X * 0.5f, size.Y * 0.42f);
        Vector2 startPos = new Vector2(size.X * 0.5f - half.X, size.Y * 0.98f - half.Y);
        Vector2 hoverPos = center - half;
        Vector2 bobPos = hoverPos - new Vector2(0f, 10f);      // 悬停时轻轻上浮一点，别「死住」
        Vector2 fallPos = new Vector2(size.X * 0.5f - half.X, size.Y * 1.30f - half.Y);

        // ★ v0.2.1000（用户 2026-11-28：「钟在屏幕中间转的时候卡了一下」）：
        //   0.3.000 的旋转 tween 在悬停段用的是 `TweenInterval` ⇒ **整整 0.40s 不转**，
        //   看着就像卡了一下。现在旋转改成**全程匀速**（三段都用 Linear，角速度一致），
        //   位移/缩放的「到达」也不再带 Back 的回弹（回弹叠加会像第二下停顿），
        //   悬停段给一点上浮 + 回落，转与不转都不再出现静止帧。
        const float spinRate = 4.7f;                            // rad/s（约 0.75 圈/秒）
        const float rot0 = -0.55f;
        float rot1 = rot0 + spinRate * RiseSeconds;
        float rot2 = rot1 + spinRate * HoldSeconds;
        float rot3 = rot2 + spinRate * FallSeconds;

        var clock = new TextureRect
        {
            Name = "CharBossTimeWarpClock",
            Texture = tex,
            Position = startPos,
            Size = texSize,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            PivotOffset = half,                                 // 绕中心转/缩放
            Scale = Vector2.One * (big * 0.62f),
            Rotation = rot0,
            Modulate = new Color(1f, 1f, 1f, 1f),               // ★ 一开始就是可见的
        };

        host.AddChildSafely(clock);

        // ⚠ 每条属性各一条**最简单的顺序 tween**（不用 SetParallel / Chain）——
        //   v0.2.999 那条复合 tween 实机里根本没推进，节点停在「屏幕外 + 全透明」。
        var posTw = clock.CreateTween();
        posTw.TweenProperty(clock, "position", hoverPos, RiseSeconds)
             .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        posTw.TweenProperty(clock, "position", bobPos, HoldSeconds * 0.5f)
             .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        posTw.TweenProperty(clock, "position", hoverPos, HoldSeconds * 0.5f)
             .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        posTw.TweenProperty(clock, "position", fallPos, FallSeconds)
             .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        posTw.TweenCallback(Callable.From(() => Free(clock)));

        // 旋转：三段匀速，中间**不停**
        var rotTw = clock.CreateTween();
        rotTw.TweenProperty(clock, "rotation", rot1, RiseSeconds)
             .SetTrans(Tween.TransitionType.Linear);
        rotTw.TweenProperty(clock, "rotation", rot2, HoldSeconds)
             .SetTrans(Tween.TransitionType.Linear);
        rotTw.TweenProperty(clock, "rotation", rot3, FallSeconds)
             .SetTrans(Tween.TransitionType.Linear);

        var scaleTw = clock.CreateTween();
        scaleTw.TweenProperty(clock, "scale", Vector2.One * big, RiseSeconds)
               .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        scaleTw.TweenProperty(clock, "scale", Vector2.One * (big * 0.97f), HoldSeconds)
               .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        scaleTw.TweenProperty(clock, "scale", Vector2.One * (big * 0.6f), FallSeconds)
               .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);

        var alphaTw = clock.CreateTween();
        alphaTw.TweenInterval(RiseSeconds + HoldSeconds);       // 上升 + 悬停期间保持不透明
        alphaTw.TweenProperty(clock, "modulate:a", 0f, FallSeconds * 0.85f)
               .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.In);
        alphaTw.TweenCallback(Callable.From(() =>
        {
            if (_plays <= 20) Logger.Log("[CharBoss] 时间扭曲：钟的动画跑完了");
        }));

        if (_plays <= 3)
            Logger.Log($"[CharBoss] 时间扭曲：钟已创建（贴图 {texSize}，缩放 {big:0.00}×，宿主 {host.GetType().Name}，" +
                       $"inTree={clock.IsInsideTree()}，屏幕 {size}，起点 {startPos} ⇒ 中央 {hoverPos}，" +
                       $"tween 有效={posTw.IsValid()}/{rotTw.IsValid()}/{scaleTw.IsValid()}/{alphaTw.IsValid()}）");
    }

    private static void Free(Node? node)
    {
        try
        {
            if (node != null && GodotObject.IsInstanceValid(node)) node.QueueFree();
        }
        catch { /* 忽略 */ }
    }
}
#endif
