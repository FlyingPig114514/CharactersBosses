# 单独提取「角色当 Boss」（崩坠 charbosses 包）用到的**语音 / 音效**。
#
# 为什么不能从 Downfall.jar 里提：那套 boss 的「打攻击牌叫一声」是
# `CharBossIronclad.onPlayAttackCardSound` / `CharBossSilent.onPlayAttackCardSound` 里
# `SoundMaster.playSound(..., Sounds.VO_IRONCLAD_1A …)` —— 语音**文件本体**在塔 1 原版
# `desktop-1.0.jar` 的 `audio/sound/vo/`。崩坠自己的 jar 里一个角色语音都没有。
#
#   铁甲战士：VO_IRONCLAD_1A/1B/1C/2A/2B/2C   （塔 1 原版做了 6 句，但原版游戏**从来没播过**，
#                                              崩坠拿来做 boss 的吆喝 —— 这也是「为什么原版没听过」）
#   静默猎手：VO_SILENT_1A/1B/2A/2B           （同上，原版未使用）
#   观者    ：VO_GREMLINCALM_1A/1B/2A/2B      （观者没有语音资源 ⇒ 借小地精「平静」的语音）
#   隐士    ：VO_GREMLINCALM_1A/1B            （同上）+ 自己的枪声/装弹/旋转（这个在崩坠 jar 里）
#   商人3幕 ：VO_NEOW_2B                      （借涅奥的语音）
#   隐士3幕Doomsday / 鸦信徒面具遗物：VO_CULTIST_1A/1B/1C（塔 1 原版鸦信徒）
#   故障机器人：**没有**语音（不覆盖 onPlayAttackCardSound，走原版默认攻击音）
#
# 用法（PowerShell 5.1 即可，本文件已存成 UTF-8 with BOM，否则中文目录名会乱）：
#   powershell -ExecutionPolicy Bypass -File tools\extract-sts1-charboss-voices.ps1
#
# 产物：.assets-sts1-downfall\14-角色Boss语音(取自塔1原版VO)\...

[CmdletBinding()]
param(
    [string]$BaseJar = 'D:\SteamLibrary\steamapps\common\SlayTheSpire\desktop-1.0.jar',
    [string]$ModJar  = 'D:\SteamLibrary\steamapps\workshop\content\646570\1610056683\Downfall.jar',
    [string]$Out     = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (-not $Out) { $Out = Join-Path (Split-Path -Parent $PSScriptRoot) '.assets-sts1-downfall' }
if (-not (Test-Path -LiteralPath $BaseJar)) { throw "找不到塔 1 原版包：$BaseJar" }
if (-not (Test-Path -LiteralPath $ModJar))  { throw "找不到崩坠包：$ModJar" }

$root = Join-Path $Out '14-角色Boss语音(取自塔1原版VO)'
if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
New-Item -ItemType Directory -Force -Path $root | Out-Null

# ── 谁用哪几句（VO 键 → 塔1原版文件名）──────────────────────────────────────
$wanted = @(
    @{ Group = '铁甲战士-Ironclad'; Key = 'VO_IRONCLAD_1A'; File = 'audio/sound/vo/STS_VO_Ironclad_1a.ogg' }
    @{ Group = '铁甲战士-Ironclad'; Key = 'VO_IRONCLAD_1B'; File = 'audio/sound/vo/STS_VO_Ironclad_1b.ogg' }
    @{ Group = '铁甲战士-Ironclad'; Key = 'VO_IRONCLAD_1C'; File = 'audio/sound/vo/STS_VO_Ironclad_1c.ogg' }
    @{ Group = '铁甲战士-Ironclad'; Key = 'VO_IRONCLAD_2A'; File = 'audio/sound/vo/STS_VO_Ironclad_2a.ogg' }
    @{ Group = '铁甲战士-Ironclad'; Key = 'VO_IRONCLAD_2B'; File = 'audio/sound/vo/STS_VO_Ironclad_2b.ogg' }
    @{ Group = '铁甲战士-Ironclad'; Key = 'VO_IRONCLAD_2C'; File = 'audio/sound/vo/STS_VO_Ironclad_2c.ogg' }
    @{ Group = '静默猎手-Silent';   Key = 'VO_SILENT_1A';   File = 'audio/sound/vo/STS_VO_Silent_1a.ogg' }
    @{ Group = '静默猎手-Silent';   Key = 'VO_SILENT_1B';   File = 'audio/sound/vo/STS_VO_Silent_1b.ogg' }
    @{ Group = '静默猎手-Silent';   Key = 'VO_SILENT_2A';   File = 'audio/sound/vo/STS_VO_Silent_2a.ogg' }
    @{ Group = '静默猎手-Silent';   Key = 'VO_SILENT_2B';   File = 'audio/sound/vo/STS_VO_Silent_2b.ogg' }
    @{ Group = '观者-Watcher(借小地精)'; Key = 'VO_GREMLINCALM_1A'; File = 'audio/sound/vo/STS_VO_GremlinCalm_1a.ogg' }
    @{ Group = '观者-Watcher(借小地精)'; Key = 'VO_GREMLINCALM_1B'; File = 'audio/sound/vo/STS_VO_GremlinCalm_1b.ogg' }
    @{ Group = '观者-Watcher(借小地精)'; Key = 'VO_GREMLINCALM_2A'; File = 'audio/sound/vo/STS_VO_GremlinCalm_2a.ogg' }
    @{ Group = '观者-Watcher(借小地精)'; Key = 'VO_GREMLINCALM_2B'; File = 'audio/sound/vo/STS_VO_GremlinCalm_2b.ogg' }
    @{ Group = '隐士-Hermit';       Key = 'VO_GREMLINCALM_1A'; File = 'audio/sound/vo/STS_VO_GremlinCalm_1a.ogg' }
    @{ Group = '隐士-Hermit';       Key = 'VO_GREMLINCALM_1B'; File = 'audio/sound/vo/STS_VO_GremlinCalm_1b.ogg' }
    @{ Group = '商人-Merchant(借涅奥)'; Key = 'VO_NEOW_2B';   File = 'audio/sound/vo/STS_VO_Neow_2b.ogg' }
    @{ Group = '隐士3幕与鸦信徒遗物'; Key = 'VO_CULTIST_1A'; File = 'audio/sound/vo/STS_VO_CrowCultist_1a.ogg' }
    @{ Group = '隐士3幕与鸦信徒遗物'; Key = 'VO_CULTIST_1B'; File = 'audio/sound/vo/STS_VO_CrowCultist_1b.ogg' }
    @{ Group = '隐士3幕与鸦信徒遗物'; Key = 'VO_CULTIST_1C'; File = 'audio/sound/vo/STS_VO_CrowCultist_1c.ogg' }
)

$rows = @()
$zip = [System.IO.Compression.ZipFile]::OpenRead($BaseJar)
try {
    foreach ($w in $wanted) {
        $e = $zip.Entries | Where-Object { $_.FullName -eq $w.File } | Select-Object -First 1
        if (-not $e) { Write-Warning ("原版包里没有 {0}（键 {1}）" -f $w.File, $w.Key); continue }
        $dir = Join-Path $root $w.Group
        if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($e, (Join-Path $dir $e.Name), $true)
        $rows += [pscustomobject]@{ 分组 = $w.Group; VO键 = $w.Key; 文件 = $e.Name; KB = [math]::Round($e.Length / 1KB, 1) }
    }
}
finally { $zip.Dispose() }

# 隐士自己的枪声/装弹/旋转在崩坠 jar 里（不是原版），一并复制过来方便一起听
$hermitSfx = @('hermit_gun.ogg', 'hermit_gun2.ogg', 'hermit_gun3.ogg', 'hermit_reload.ogg', 'hermit_spin.ogg')
$zip = [System.IO.Compression.ZipFile]::OpenRead($ModJar)
try {
    $dir = Join-Path (Join-Path $root '隐士-Hermit') '崩坠自带音效'
    foreach ($n in $hermitSfx) {
        $e = $zip.Entries | Where-Object { $_.FullName -eq "hermitResources/audio/$n" } | Select-Object -First 1
        if (-not $e) { Write-Warning "崩坠包里没有 $n"; continue }
        if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($e, (Join-Path $dir $n), $true)
        $rows += [pscustomobject]@{ 分组 = '隐士-Hermit'; VO键 = '(崩坠自带)'; 文件 = $n; KB = [math]::Round($e.Length / 1KB, 1) }
    }
}
finally { $zip.Dispose() }

# ── 对照表 ─────────────────────────────────────────────────────────────────
$md = @'
# 角色当 Boss 的语音 / 音效

这些是「把可玩角色做成 boss」时，**boss 打出攻击牌时喊的那一声**（`onPlayAttackCardSound`）。

## 为什么语音文件不在崩坠的 jar 里

崩坠的 `charbosses` 只写了**调用**：`SoundMaster.playSound(..., Sounds.VO_IRONCLAD_1A)`，
文件本体在塔 1 原版 `desktop-1.0.jar` 的 `audio/sound/vo/`，本目录的文件是从那里取出来的
（崩坠自己的 jar 里一个角色语音都没有，只有下面隐士那 5 个音效）。

## 每个 boss 用哪几句

| boss | VO 键 | 文件 | 说明 |
| --- | --- | --- | --- |
| 铁甲战士 | `VO_IRONCLAD_1A/1B/1C/2A/2B/2C` | `STS_VO_Ironclad_1a/1b/1c/2a/2b/2c.ogg` | 塔 1 做了 6 句，**原版游戏从来没用过**；崩坠拿来做 boss 攻击牌的吆喝 |
| 静默猎手 | `VO_SILENT_1A/1B/2A/2B` | `STS_VO_Silent_1a/1b/2a/2b.ogg` | 同上，原版未使用 |
| 观者 | `VO_GREMLINCALM_1A/1B/2A/2B` | `STS_VO_GremlinCalm_1a/1b/2a/2b.ogg` | 观者没有语音资源 ⇒ 借小地精「平静」的语音 |
| 隐士 | `VO_GREMLINCALM_1A/1B` | `STS_VO_GremlinCalm_1a/1b.ogg` | 同上；另外隐士有崩坠自带的 `hermit_gun*` / `hermit_reload` / `hermit_spin` |
| 商人（3 幕） | `VO_NEOW_2B` | `STS_VO_Neow_2b.ogg` | 借涅奥的语音 |
| 隐士 3 幕 Doomsday / 鸦信徒面具遗物 | `VO_CULTIST_1A/1B/1C` | `STS_VO_CrowCultist_1a/1b/1c.ogg` | 塔 1 原版鸦信徒 |
| 故障机器人 | （无） | — | 不覆盖 `onPlayAttackCardSound`，走原版默认的攻击音 |

证据（都在 `Downfall.jar` 的 class 常量池里）：

```
charbosses/bosses/Ironclad/CharBossIronclad.class   : onPlayAttackCardSound + VO_IRONCLAD_1A…2C
charbosses/bosses/Silent/CharBossSilent.class       : onPlayAttackCardSound + VO_SILENT_1A…2B
charbosses/bosses/Watcher/CharBossWatcher.class     : VO_GREMLINCALM_1A…2B
charbosses/bosses/Hermit/CharBossHermit.class       : VO_GREMLINCALM_1A/1B
charbosses/bosses/Merchant/CharBossMerchant.class   : VO_NEOW_2B
charbosses/bosses/Hermit/NewAge/ArchetypeAct3DoomsdayNewAge.class : VO_CULTIST_1A/1B/1C
```

重新生成：`powershell -ExecutionPolicy Bypass -File tools\extract-sts1-charboss-voices.ps1`
'@
Set-Content -LiteralPath (Join-Path $root '对照表.md') -Value $md -Encoding utf8

Write-Host ''
Write-Host "完成：$($rows.Count) 个音频 → $root"
$rows | Sort-Object 分组, 文件 | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
