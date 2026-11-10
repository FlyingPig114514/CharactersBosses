using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Audio;

namespace CharacterBosses.Core;

/// <summary>
/// 「角色当 Boss」的**语音**（用户 2026-11-10 需求）。
///
/// 背景：一代崩坠的 charbosses 给铁甲战士 boss 覆写了 <c>onPlayAttackCardSound</c>，
/// 每次打出攻击牌就随机喊一句 <c>VO_IRONCLAD_1a/1b/1c</c>；死亡时是 <c>2a/2b/2c</c>。
/// 用户要的就是这 6 句，且要**放进我们自己模组内部**。
///
/// 资源位置：`tools\art\audio\voice\STS_VO_Ironclad_{1a,1b,1c,2a,2b,2c}.ogg`
/// 由 `tools\art\pck_extras.txt` 打进 PCK，运行时 `res://CharacterBosses/audio/voice/<name>.ogg`。
/// （和 <see cref="BossBgm"/> 那三首一代 BGM 走同一条管线：`AudioStreamOggVorbis.LoadFromFile`。）
///
/// ⚠ 语音是**纯本机音效**，不参与联机同步：音效不同步只是「你多听到一句」，无副作用。
/// </summary>
public static class BossVoice
{
    private const string ResDir = "res://CharacterBosses/audio/voice/";

    /// <summary>本回合打出这些牌时喊的（崩坠里是 1a/1b/1c）。</summary>
    private static readonly string[] FirstAttack = { "STS_VO_Ironclad_1a", "STS_VO_Ironclad_1b", "STS_VO_Ironclad_1c" };

    /// <summary>死亡时喊的（崩坠里是 2a/2b/2c）。</summary>
    private static readonly string[] Death = { "STS_VO_Ironclad_2a", "STS_VO_Ironclad_2b", "STS_VO_Ironclad_2c" };

    private static readonly Dictionary<string, AudioStream?> Cache = new(StringComparer.Ordinal);
    private static readonly Random Rng = new();
    private static readonly HashSet<string> LoggedMissing = new(StringComparer.Ordinal);

    /// <summary>
    /// **只有这几张牌**会触发攻击语音（用户 2026-11-10 第二批要求：
    /// 「音效有点太频繁了，改成每回合第一次打出以下牌的时候放：痛击、完美打击、突破、全身撞击、巨石、彼岸咆哮、闪电霹雳、欺凌」）。
    /// 条目名就是 `card.Id.Entry`（类名的 SCREAMING_SNAKE）：
    /// 痛击 = `BASH`、完美打击 = `PERFECTED_STRIKE`、突破 = `BREAKTHROUGH`、全身撞击 = `BODY_SLAM`、
    /// 巨石 = `GIANT_ROCK`、彼岸咆哮 = `HOWL_FROM_BEYOND`、闪电霹雳 = `THUNDERCLAP`、欺凌 = `BULLY`。
    /// </summary>
    public static readonly HashSet<string> AttackVoiceCards = new(StringComparer.Ordinal)
    {
        "BASH", "PERFECTED_STRIKE", "BREAKTHROUGH", "BODY_SLAM",
        "GIANT_ROCK", "HOWL_FROM_BEYOND", "THUNDERCLAP", "BULLY",
    };

    /// <summary>
    /// 这几张牌**造成伤害的那一刻**喊一声（用户 2026-11-10：「音效稍微有点早，等他的攻击牌造成伤害的时候再放」）。
    /// 由 `Patches.IroncladBossAttackVoicePatch` 在 `Hook.AfterDamageGiven` 里调。
    /// </summary>
    public static bool PlayAttackVoice(Creature? boss) => Play(FirstAttack, boss, "攻击牌造成伤害");

    /// <summary>
    /// 她**死亡**时喊一声（调用方负责压掉原版死亡音）。
    /// 返回 true = 真的播出去了 —— 调用方只有在 true 时才应该压掉原版音效，否则会变成「两边都没声」。
    /// </summary>
    public static bool PlayDeathVoice(Creature? boss) => Play(Death, boss, "死亡");

    /// <summary>随机挑一句播。返回是否真的播了。</summary>
    private static bool Play(string[] pool, Creature? boss, string why)
    {
        try
        {
            if (pool == null || pool.Length == 0) return false;
            string name = pool[Rng.Next(pool.Length)];
            var stream = Load(name);
            if (stream == null) return false;

            var host = FindHost();
            if (host == null) { Logger.Log("[CharBoss] 语音：找不到可以挂 AudioStreamPlayer 的节点，跳过"); return false; }

            var player = new AudioStreamPlayer { Stream = stream, VolumeDb = -4f, Autoplay = false };
            player.Name = "CharBossVoice_" + name;
            // 「SFX」总线路由 ⇒ 跟随玩家的音效音量设置（原版 `NDebugAudioManager` 也是这么设的）
            try { player.Bus = new StringName("SFX"); } catch { /* 没有这条总线就落回 Master */ }
            host.AddChildSafely(player);   // 原版助手：不在主线程就自动 defer
            // 播完自己收拾掉：这些是一次性音效，留在场景里会越积越多
            player.Connect(AudioStreamPlayer.SignalName.Finished, Callable.From(() =>
            {
                try { player.QueueFree(); } catch { /* 节点可能已经随场景一起没了 */ }
            }));
            player.Play();
            Logger.Log($"[CharBoss] 语音：{why} → {name}（{SafeName(boss)}）");
            return true;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 语音播放失败: " + e.Message);
            return false;
        }
    }

    /// <summary>读进内存并缓存（同一句重复播放不需要重新解码）。</summary>
    private static AudioStream? Load(string name)
    {
        if (Cache.TryGetValue(name, out var cached)) return cached;

        AudioStream? stream = null;
        string path = ResDir + name + ".ogg";
        try { stream = AudioStreamOggVorbis.LoadFromFile(path); }
        catch (Exception e) { Logger.Log($"[CharBoss] 语音：ogg 载入异常 {path} — {e.Message}"); }

        if (stream == null)
        {
            try { stream = ResourceLoader.Load<AudioStream>(path); } catch { stream = null; }
        }

        if (stream == null && LoggedMissing.Add(name))
        {
            Logger.Log($"[CharBoss] 语音：载入失败（PCK 里没有这个文件？）{path}");
        }

        Cache[name] = stream;
        return stream;
    }

    /// <summary>
    /// 找挂载点，优先级：**音频管理器**（原版 `NDebugAudioManager` 就是挂在它下面播一次性音效）
    /// → **音乐控制器**（<see cref="BossBgm"/> 用的那个，跨战斗不会随房间销毁）→ **场景树根**。
    /// 都是长命节点；一次性音效播完自己 QueueFree，不会攒。
    /// </summary>
    private static Node? FindHost()
    {
        try
        {
            var audio = NAudioManager.Instance;
            if (GodotObject.IsInstanceValid(audio)) return audio;
        }
        catch { /* 往下退 */ }

        try
        {
            var direct = NRunMusicController.Instance;
            if (GodotObject.IsInstanceValid(direct)) return direct;
        }
        catch { /* 退回场景树 */ }

        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            return tree?.Root;
        }
        catch { return null; }
    }

    private static string SafeName(Creature? boss)
    {
        try { return boss?.Monster?.GetType().Name ?? boss?.Name ?? "?"; }
        catch { return "?"; }
    }
}
