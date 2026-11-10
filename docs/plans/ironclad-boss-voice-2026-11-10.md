# 铁甲战士 Boss 的语音（v0.2.539 → v0.2.540，2026-11-10）

> 用户需求（原话，分两批）：
> 1. 「所有铁甲战士boss死亡的音效都改成 STS_VO_Ironclad_2a、2b、2c 这 3 个中随机一个」
> 2. 「所有铁甲战士每回合首次打出攻击牌都同时播放 STS_VO_Ironclad_1a、1b、1c 这 3 个中随机一个」
> 3. 「把这六个音效放进模组内部」
> 4. （第二批，改第 2 条的触发方式）「音效稍微有点早，等他的攻击牌造成伤害的时候再放音效。
>    而且音效有点太频繁了，改成每回合第一次打出以下牌的时候放：痛击、完美打击、突破、全身撞击、
>    巨石、彼岸咆哮、闪电霹雳、欺凌」

## 1. 这 6 句是从哪来的

一代崩坠（StS1 `Downfall.jar`）的 `charbosses` 包给铁甲战士 boss 覆写了
`CharBossIronclad.onPlayAttackCardSound`，里面 `SoundMaster.playSound(..., Sounds.VO_IRONCLAD_1A…)`。
但**语音文件本体不在崩坠 jar 里**，在塔 1 原版 `desktop-1.0.jar` 的 `audio/sound/vo/`：

- `VO_IRONCLAD_1A/1B/1C` → `STS_VO_Ironclad_1a/1b/1c.ogg`（原版游戏**从来没用过**这三句，崩坠挖出来做 boss 吆喝）
- `VO_IRONCLAD_2A/2B/2C` → `STS_VO_Ironclad_2a/2b/2c.ogg`

提取脚本：`tools\extract-sts1-charboss-voices.ps1`（产物在 `.assets-sts1-downfall\14-角色Boss语音…\`，仅供试听）。

## 2. 打进模组

```
tools\art\audio\voice\STS_VO_Ironclad_{1a,1b,1c,2a,2b,2c}.ogg      ← 源文件（6 个，约 0.26 MB）
tools\art\pck_extras.txt  追加 6 行 res://CharacterBosses/audio/voice/<name>.ogg|<绝对路径>
```

打包后 PCK 里就能查到 `res://CharacterBosses/audio/voice/…`，和一代 BGM 走的是同一条管线
（`AudioStreamOggVorbis.LoadFromFile`，见 `Core/BossBgm.cs`）。

> ⚠ `tools/` 在 `.gitignore` 里（`/tools/*`）⇒ 这 6 个 ogg **不进 git 仓库**，和那三首一代 BGM 一样，
> 只存在于本机 + 打出来的 PCK 里。要从零重建需要先跑一次语音提取脚本。
> 实测打包日志：`Packed 54 extra file(s)`（原来 48 + 这 6 个）。

## 3. 代码

| 位置 | 干什么 |
| --- | --- |
| `src/Hooks/Core/BossVoice.cs`（新） | 载入 + 缓存 6 个 `AudioStream`，随机播一句；挂到 `NAudioManager` → `NRunMusicController` → 场景根，总线设 `SFX`，音量 `VolumeDb = -0.5`（v0.2.541 起：原来是 -4，用户「有点小，调大 50%」⇒ 振幅 ×1.5），`Finished` 后 `QueueFree`；内含「会喊的 8 张牌」清单 |
| `src/Hooks/Patches/IroncladBossAttackVoicePatch.cs`（新） | ①`CardCmd.AutoPlay` 前缀：打出那 8 张之一 ⇒ 挂「待播」标记；②`Hook.AfterDamageGiven` 前缀：**伤害落地那一刻**才真播（每回合最多一次，多段牌只响第一段） |
| `src/Hooks/Monsters/CharBossMonster.cs` | 加了 `VoiceArmed` / `VoiceFiredThisTurn` 两个字段（`PrepareTurn` 清零）；`PlayOne` 里**不再**出牌前播（第一批的写法已删） |
| `src/Hooks/Monsters/CharacterBosses.cs` `IroncladBoss` | `public override bool HasDeathSfx => false;` ⇒ 压掉原版死亡音 |
| `src/Hooks/Patches/IroncladBossDeathVoicePatch.cs`（新） | `NCreature.StartDeathAnim(bool)` 的 **Postfix** ⇒ 是铁甲战士 boss 就随机喊 `2a/2b/2c`（同一只只喊一次） |

`IroncladBoss` 是所有铁甲战士变体的基类（怒 / 打击 / 出血 / 杂耍 / 易伤 / 巨石），所以「所有」自动覆盖。

### 会喊的那 8 张（`BossVoice.AttackVoiceCards`）

| 中文 | 条目 |
| --- | --- |
| 痛击 | `BASH` |
| 完美打击 | `PERFECTED_STRIKE` |
| 突破 | `BREAKTHROUGH` |
| 全身撞击 | `BODY_SLAM` |
| 巨石 | `GIANT_ROCK` |
| 彼岸咆哮 | `HOWL_FROM_BEYOND` |
| 闪电霹雳 | `THUNDERCLAP` |
| 欺凌 | `BULLY` |

### 为什么「挂待播标记」要挂在 `CardCmd.AutoPlay` 而不是我们自己的 `PlayOne`

「彼岸咆哮」是**回合结束时从消耗牌堆自动打出**的（剧本的 `FreePlayExhaustEntry`），
它不走 `PlayOne`；而所有出牌（含它的 `CardPileCmd.AutoPlayFromDrawPile` /
原版 `Exhaust` 自动打出）都会经过 `CardCmd.AutoPlay` ⇒ 挂那里才能把 8 张全覆盖。
真正出声则交给 `Hook.AfterDamageGiven`（伤害落地），所以「标记」和「发声」是两个点。

## 4. 为什么死亡音效挂在 `StartDeathAnim` 而不是 `SfxCmd.PlayDeath`

反编译 `NCreature.StartDeathAnim(bool shouldRemove)`（107/111 签名一致，同步、返回 float）：

```csharp
if (DeathAnimationTask != null && !DeathAnimationTask.IsCompleted) return 0f;
float num = 0f;
if (_spineAnimator != null) {                       // ← ★ 没有 spine animator 就整段跳过
    if (monster != null && monster.HasDeathSfx) SfxCmd.PlayDeath(Entity.Monster);
    if (Entity.Player != null) SfxCmd.PlayDeath(Entity.Player);
    SetAnimationTrigger("Dead");
    num = GetCurrentAnimationLength();
}
DeathAnimationTask = AnimDie(shouldRemove, ...);
```

- `SfxCmd.PlayDeath` 被 `_spineAnimator != null` 门住，我们的 boss 立绘走角色模型的 `CreateVisuals()`，
  不一定有 spine animator ⇒ 挂那里可能永远不触发；`StartDeathAnim` 只要有生物节点就会被调。
- 必须用 **Postfix**：Prefix 返 `false` 会把 `SetAnimationTrigger("Dead")` / `AnimDie` /
  `DeathAnimationTask` 全跳过 ⇒ **boss 尸体不消失、战斗卡死**（这是本项目踩过的同类坑）。
- 原版那条 FMOD 事件由覆写 `HasDeathSfx => false` 压掉，两件事分开做、互不干扰。
- ⚠ 挂 `CreatureCmd.Kill` 是陷阱：它是 **async**（返回 `Task`），Prefix 必须带 `ref Task __result`
  否则游戏侧拿到 null 直接 NRE。

## 5. 实机验证要点

1. 她打出那 8 张之一时：`[CharBoss] 语音：BASH 属于会喊的 8 张之一 ⇒ 等它造成伤害时再喊`
2. 紧接着伤害落地：`[CharBoss] 语音：攻击牌造成伤害 → STS_VO_Ironclad_1x（IroncladVulnBoss）`
   —— 每回合最多一条；打普通攻击牌（打击/防御…）**不会**有这两行。
3. 打死她时找 `[CharBoss] 语音：死亡 → STS_VO_Ironclad_2x（IroncladAngerBoss 等变体类名）`。
4. 若出现 `语音：载入失败（PCK 里没有这个文件？）res://CharacterBosses/audio/voice/…`
   ⇒ PCK 没打进去（重跑 `install-mod.bat`，确认日志是 `Packed 54 extra file(s)`）。
5. 语音是**纯本机音效**，不参与联机同步（音效不同步只是「有人多听到一句」，无副作用）。

## 6. 已知的小瑕疵

- 「全身撞击」在**格挡为 0** 时会造成 0 点伤害：若那一刻没有触发伤害事件，待播标记会留到本回合下一张牌
  ⇒ 那一声可能响在下一张牌上。其余 7 张都是必定造成伤害的牌，不会出现这种情况；
  标记本身每回合 `PrepareTurn` 也会清零，不会串到下一回合。
