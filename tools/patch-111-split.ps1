# patch-111-split.ps1 —— 0.111 正式版 / 0.113 测试分支 双构建拆分
#
# 目的：把「愚者 / 恐惧模式 / 0.113 新 API」的东西全部收进 STS2_113，
#       让同一份源码也能对着 **0.111 正式版** 编译（STS2_111 只表示「现代内容」）。
#
# 用法（幂等，可重复跑）：
#   powershell -ExecutionPolicy Bypass -File tools\patch-111-split.ps1
#
# 只做「插 #if STS2_113 / #endif」这种机械包裹；文本锚点全部来自 git 版本，跑完请编两个目标验证。
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Wrap-LineOf([string]$relPath, [string]$needle, [int]$occurrence = 1) {
    $path = Join-Path $root $relPath
    $lines = [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)
    $hit = -1; $seen = 0
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i].IndexOf($needle, [System.StringComparison]::Ordinal) -ge 0) {
            $seen++
            if ($seen -eq $occurrence) { $hit = $i; break }
        }
    }
    if ($hit -lt 0) { Write-Host "  !! 找不到锚点: $relPath :: $needle"; return }
    if ($hit -gt 0 -and $lines[$hit - 1].Trim() -eq '#if STS2_113') { Write-Host "  已包过: $relPath"; return }

    $out = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($i -eq $hit) { $out.Add('#if STS2_113') }
        $out.Add($lines[$i])
        if ($i -eq $hit) { $out.Add('#endif') }
    }
    [System.IO.File]::WriteAllLines($path, $out, $utf8)
    Write-Host ("  包裹 {0} 第 {1} 行：{2}" -f (Split-Path -Leaf $relPath), ($hit + 1), $needle)
}

function Set-Text([string]$relPath, [string]$old, [string]$new) {
    $path = Join-Path $root $relPath
    $t = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
    if ($t.IndexOf($old, [System.StringComparison]::Ordinal) -lt 0) {
        if ($t.IndexOf($new, [System.StringComparison]::Ordinal) -ge 0) { Write-Host "  已改过: $relPath"; return }
        Write-Host "  !! 文本锚点没命中: $relPath"; return
    }
    [System.IO.File]::WriteAllText($path, $t.Replace($old, $new), $utf8)
    Write-Host "  改文本: $relPath"
}

Write-Host '=== 1) BossDefs：占位角色类型 + 愚者定义收进 113 ==='
Set-Text 'src/Hooks/Core/BossDefs.cs' @'
public static class BossDefs
{
'@ @'
public static class BossDefs
{
    /// <summary>
    /// 观者 Boss 的**占位角色类型**（运行期由 `RefreshWatcherCharacterType` 换成真的观者角色）。
    ///
    /// ★ v0.2.732（用户 2026-11-17，官方主线更新到 0.111 之后）：原来这里写死 `Characters.Fool`（愚者）——
    ///   那是 **0.113 测试分支才有的角色**，0.111 正式版里没有这个类型 ⇒ 0.111 构建编译期就找不到。
    ///   现在按构建分：0.113（beta）继续用 `Fool`；0.111（正式）用 `DeprecatedCharacter`（两版都有）。
    ///   三个定义（`FOOL_BOSS` / `WATCHER_DIVINITY` / `WATCHER_OMEGA` 观测变体）与两处
    ///   「还是不是占位符」的判断（`BossFrameTickPatch` / `CharBossMonster.Descriptor`）都走它。
    /// </summary>
    public static readonly Type PlaceholderCharacterType =
#if STS2_113
        typeof(MegaCrit.Sts2.Core.Models.Characters.Fool);
#else
        typeof(MegaCrit.Sts2.Core.Models.Characters.DeprecatedCharacter);
#endif

'@
Set-Text 'src/Hooks/Core/BossDefs.cs' 'typeof(MegaCrit.Sts2.Core.Models.Characters.Fool)' 'PlaceholderCharacterType'
Wrap-LineOf 'src/Hooks/Core/BossDefs.cs' 'Key = "FOOL_BOSS"'
# 该块自己的 #if 是「#if STS2_111」→ 改成 113（块前面的那一行）
$p = Join-Path $root 'src/Hooks/Core/BossDefs.cs'
$l = [System.IO.File]::ReadAllLines($p, [System.Text.Encoding]::UTF8)
$keyIdx = -1
for ($i = 0; $i -lt $l.Count; $i++) { if ($l[$i] -match 'Key = "FOOL_BOSS"') { $keyIdx = $i; break } }
for ($i = $keyIdx; $i -ge 0; $i--) {
    if ($l[$i] -match '^\s*#if STS2_111') { $l[$i] = $l[$i].Replace('#if STS2_111', '#if STS2_113'); [System.IO.File]::WriteAllLines($p, $l, $utf8); Write-Host ("  FOOL_BOSS 外层 #if → STS2_113（第 {0} 行）" -f ($i + 1)); break }
}
Wrap-LineOf 'src/Hooks/Core/BossDefs.cs' '        Fool,'
Wrap-LineOf 'src/Hooks/Core/BossDefs.cs' '[Fool.Key] = Fool,'

Write-Host '=== 2) 各处引用愚者/0.113 内容的地方 ==='
Wrap-LineOf 'src/Hooks/Core/BossKeys.cs' 'new("FOOL_TAROT"'
Wrap-LineOf 'src/Hooks/Core/BossKeys.cs' 'case "FOOL_TAROT": return ModelDb.Encounter<FoolTarotBossEncounter>();'
Wrap-LineOf 'src/Hooks/Encounters/CharBossEncounters.cs' 'ModelDb.Encounter<FoolTarotBossNormalEncounter>(),'
Wrap-LineOf 'src/Hooks/Encounters/CharBossEncounters.cs' 'ModelDb.Encounter<FoolTarotBossEncounter>(),'
Wrap-LineOf 'src/Hooks/Encounters/CharBossEncounters.cs' 'Add(5, 2, ModelDb.Encounter<FoolTarotBossEncounter>());'
Wrap-LineOf 'src/Hooks/Patches/BossSpecialPowerIconPatch.cs' 'if (power is Powers.FoolDominionPower)'
Wrap-LineOf 'src/Hooks/Powers/DominionPowers.cs' 'internal sealed class FoolDominionPower'
Wrap-LineOf 'src/Hooks/Monsters/CharacterBosses.cs' 'public class FoolBoss : CharBossMonster'
Wrap-LineOf 'src/Hooks/Monsters/TierBossMonsters.Act3.cs' 'public sealed class FoolTarotBoss : FoolBoss'
Wrap-LineOf 'src/Hooks/Patches/BossSpecialPowerIconPatch.cs' 'Models.Characters.Fool'
Wrap-LineOf 'src/Hooks/Patches/EncounterIconPathPatch.cs' 'Models.Characters.Fool'
Wrap-LineOf 'src/Hooks/Monsters/CharBossMonster.Descriptor.cs' 'Def.CharacterType == typeof(MegaCrit.Sts2.Core.Models.Characters.Fool)'
Wrap-LineOf 'src/Hooks/Patches/BossFrameTickPatch.cs' 'Core.BossDefs.Watcher.CharacterType == typeof(MegaCrit.Sts2.Core.Models.Characters.Fool)'
Wrap-LineOf 'src/Hooks/Core/BossDefValidator.cs' '("斋戒", typeof(Cards.TarotFasting)),'

Write-Host '=== 3) 0.111 没有的 API ==='
Set-Text 'src/Hooks/Patches/BossMultiplayerHpPatch.cs' '#if STS2_107' '#if !STS2_113'
Set-Text 'src/Hooks/Relics/NeowBlessing.cs' '#if STS2_107' '#if !STS2_113'
Set-Text 'src/Hooks/Core/SelectionCompat.cs' '#if STS2_107' '#if !STS2_113'
Set-Text 'src/Hooks/Patches/BossSelectorStackPatch.cs' '#if STS2_107' '#if !STS2_113'

Write-Host '=== 完成（请编译 111 与 113 两个目标验证）==='
