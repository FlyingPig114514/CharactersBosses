# 铁甲战士 Boss 的语音（v0.2.539，2026-11-10）

> 用户需求（原话）：
> 「所有铁甲战士boss死亡的音效都改成 STS_VO_Ironclad_2a、2b、2c 这 3 个中随机一个」
> 「所有铁甲战士每回合首次打出攻击牌都同时播放 STS_VO_Ironclad_1a、1b、1c 这 3 个中随机一个」
> 「把这六个音效放进模组内部」

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
| `src/Hooks/Core/BossVoice.cs`（新） | 载入 + 缓存 6 个 `AudioStream`，随机播一句；挂到 `NAudioManager` → `NRunMusicController` → 场景根，总线设 `SFX`，`Finished` 后 `QueueFree` |
| `src/Hooks/Monsters/CharBossMonster.cs` `PlayOne` | **AutoPlay 之前**：`IroncladBoss` && `AttacksPlayed == 0` && 这张是攻击牌 ⇒ 随机喊 `1a/1b/1c`（与她出牌同时出声） |
| `src/Hooks/Monsters/CharacterBosses.cs` `IroncladBoss` | `public override bool HasDeathSfx => false;` ⇒ 压掉原版死亡音 |
| `src/Hooks/Patches/IroncladBossDeathVoicePatch.cs`（新） | `NCreature.StartDeathAnim(bool)` 的 **Postfix** ⇒ 是铁甲战士 boss 就随机喊 `2a/2b/2c`（同一只只喊一次） |

`IroncladBoss` 是所有铁甲战士变体的基类（怒 / 打击 / 出血 / 杂耍 / 易伤 / 巨石），所以「所有」自动覆盖。

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

1. 启动日志里找 `[CharBoss] 语音：本回合首张攻击牌 → STS_VO_Ironclad_1x（IroncladBoss）`
   —— 每回合最多一条（打第一张攻击牌时）。
2. 打死她时找 `[CharBoss] 语音：死亡 → STS_VO_Ironclad_2x（IroncladAngerBoss 等变体类名）`。
3. 若出现 `语音：载入失败（PCK 里没有这个文件？）res://CharacterBosses/audio/voice/…`
   ⇒ PCK 没打进去（重跑 `install-mod.bat`，确认日志是 `Packed 54 extra file(s)`）。
4. 语音是**纯本机音效**，不参与联机同步（音效不同步只是「有人多听到一句」，无副作用）。
