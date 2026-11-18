#if STS2_111   // 观者 Boss 只在这个构建里
using System;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Helpers;

namespace CharacterBosses.Core;

/// <summary>
/// **塔1「时间扭曲」的时钟特效 + 音效**（用户 2026-11-18：三层 3 号「红蓝观」的特殊效果
/// 「节制」/ 恐惧模式的「时间扭曲」计数满 12 时要播塔1那个钟）。
///
/// · 音效：StS1 的 `audio/sound/STS_SFX_TimeWarp_v2.ogg` → 抽出成
///   `tools/art/audio/STS_SFX_TimeWarp.ogg`，由 `tools/art/pck_extras.txt` 打进 PCK，
///   运行时 `res://CharacterBosses/audio/STS_SFX_TimeWarp.ogg`（和 BossVoice 同一条管线）。
/// · 动画：StS1 那个钟是**程序化绘制**的（jar 里没有贴图），所以这里自己画：
///   屏幕中央一个半透明表盘 + 12 个刻度 + 指针快速转一圈 + 淡入淡出（约 1.4 秒）。
/// </summary>
internal static class TimeWarpFx
{
    private const string ResPath = "res://CharacterBosses/audio/STS_SFX_TimeWarp.ogg";
    private static AudioStream? _stream;
    private static bool _logged;

    /// <summary>播一次「时间扭曲」：音效 + 屏幕中央的钟。</summary>
    internal static void Play(Creature? at)
    {
        try { PlaySound(); } catch (Exception e) { Logger.Log("[CharBoss] 时间扭曲：音效播放失败: " + e.Message); }
        try { SpawnClock(); } catch (Exception e) { Logger.Log("[CharBoss] 时间扭曲：时钟特效失败: " + e.Message); }
    }

    private static void PlaySound()
    {
        try
        {
            if (_stream == null)
            {
                try { _stream = AudioStreamOggVorbis.LoadFromFile(ResPath); } catch { _stream = null; }
                if (_stream == null) { try { _stream = ResourceLoader.Load<AudioStream>(ResPath); } catch { _stream = null; } }
                if (_stream == null)
                {
                    if (!_logged) { Logger.Log("[CharBoss] 时间扭曲：PCK 里没有 " + ResPath + "（音效缺失）"); _logged = true; }
                    return;
                }
            }
            Node? host = null;
            try { var audio = NAudioManager.Instance; if (GodotObject.IsInstanceValid(audio)) host = audio; } catch { host = null; }
            if (host == null) { try { host = NCombatRoom.Instance; } catch { host = null; } }
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

    private static void SpawnClock()
    {
        try
        {
            Node? host = null;
            try { host = NCombatRoom.Instance; } catch { host = null; }
            if (host == null) { try { host = NCombatRoom.Instance; } catch { host = null; } }
            if (host == null) return;

            var clock = new TimeWarpClock();
            clock.Name = "CharBossTimeWarpClock";
            host.AddChildSafely(clock);
        }
        catch (Exception e) { Logger.Log("[CharBoss] 时间扭曲：时钟节点失败: " + e.Message); }
    }
}

/// <summary>
/// 画在屏幕中央的那个「时间扭曲」钟：半透明表盘 + 12 个刻度 + 三根指针，1.4 秒内指针转一圈并淡出。
/// 全程 `_Draw()` 程序化绘制（StS1 原本也是这么画的，jar 里没有贴图可用）。
/// </summary>
public partial class TimeWarpClock : Control
{
    private const float Life = 1.4f;
    private float _t;
    private Vector2 _center;
    private float _radius;

    public override void _Ready()
    {
        try
        {
            MouseFilter = MouseFilterEnum.Ignore;
            ZIndex = 400;
            var vp = GetViewportRect().Size;
            _center = vp * 0.5f;
            _radius = Mathf.Min(vp.X, vp.Y) * 0.28f;
            SetAnchorsPreset(LayoutPreset.FullRect);
            SetDeferred(Control.PropertyName.Position, new Vector2(0, 0));
        }
        catch { /* 忽略 */ }
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        if (_t >= Life)
        {
            try { QueueFree(); } catch { /* 忽略 */ }
            return;
        }
        try { QueueRedraw(); } catch { /* 忽略 */ }
    }

    public override void _Draw()
    {
        try
        {
            float k = Mathf.Clamp(_t / Life, 0f, 1f);
            float alpha = k < 0.25f ? k / 0.25f : (k > 0.8f ? (1f - k) / 0.2f : 1f);
            alpha = Mathf.Clamp(alpha, 0f, 1f);

            var face = new Color(0.95f, 0.85f, 0.45f, 0.10f * alpha);
            var rim = new Color(0.98f, 0.90f, 0.55f, 0.85f * alpha);
            var hand = new Color(1f, 0.97f, 0.85f, 0.95f * alpha);

            // 表盘
            DrawCircle(_center, _radius, face);
            DrawArc(_center, _radius, 0f, Mathf.Tau, 96, rim, 4f * Mathf.Max(0.5f, _radius / 120f), true);

            // 12 个刻度
            for (int i = 0; i < 12; i++)
            {
                float a = Mathf.Tau * i / 12f - Mathf.Pi / 2f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                DrawLine(_center + dir * (_radius * 0.82f), _center + dir * (_radius * 0.96f),
                         new Color(rim.R, rim.G, rim.B, 0.7f * alpha), 3f, true);
            }

            // 指针：整圈转 1 圈（时间被「扭曲」）
            float spin = k * Mathf.Tau * 1.0f;
            DrawHand(spin, _radius * 0.55f, 6f, hand);                 // 时针
            DrawHand(spin * 4f, _radius * 0.80f, 4f, hand);            // 分针
            DrawHand(spin * 12f, _radius * 0.90f, 2f, new Color(1f, 0.55f, 0.45f, alpha)); // 秒针（发红）
            DrawCircle(_center, 6f, hand);
        }
        catch { /* 忽略 */ }
    }

    private void DrawHand(float angle, float len, float width, Color color)
    {
        var dir = new Vector2(Mathf.Cos(angle - Mathf.Pi / 2f), Mathf.Sin(angle - Mathf.Pi / 2f));
        DrawLine(_center, _center + dir * len, color, width, true);
    }
}
#endif
