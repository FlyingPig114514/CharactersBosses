# build-site.ps1 -- generate docs\wiki\index.html from tools\wiki\data.json + template.html
#
# Target runtime: Windows PowerShell 5.1  (powershell -ExecutionPolicy Bypass -File ...)
#
# Rules this script follows on purpose (each one is a bug that used to bite):
#   * the file itself is stored as UTF-8 with BOM, so PS 5.1 parses ASCII+Chinese safely;
#     the script body stays pure ASCII to be safe either way.
#   * read/write with [System.IO.File]::ReadAllText / WriteAllText and an explicit
#     UTF8Encoding. Never Get-Content/Set-Content on Chinese content (PS 5.1 decodes
#     UTF-8 as ANSI/GBK and wrecks the file).
#   * inject with String.Replace, never with -replace (the JSON is full of "$").
#   * the data.json text is injected verbatim (no ConvertTo-Json round trip) so Chinese
#     stays Chinese and the payload is byte-identical to the exported file.
#   * every computed HP value is injected as a tiny separate ASCII-only map.
#
# Output: <repo>\docs\wiki\index.html  (single file, no CDN, works from file://)

[CmdletBinding()]
param(
    [string]$DataPath = '',
    [string]$TemplatePath = '',
    [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'

$Utf8NoBom = New-Object System.Text.UTF8Encoding -ArgumentList $false
$MarkerData = '/*__BOSS_DATA__*/'
$MarkerHp = '/*__BOSS_HP__*/'

function Fail([string]$Message) {
    Write-Host ('ERROR: ' + $Message) -ForegroundColor Red
    exit 1
}

function Info([string]$Message) {
    Write-Host $Message -ForegroundColor Gray
}

function Ok([string]$Message) {
    Write-Host $Message -ForegroundColor Green
}

# ---------------------------------------------------------------- paths ----

$scriptRoot = $PSScriptRoot
if ([string]::IsNullOrEmpty($scriptRoot)) {
    $scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
}
if ([string]::IsNullOrEmpty($scriptRoot)) { Fail 'cannot resolve the script directory' }

$repoRoot = Split-Path -Parent (Split-Path -Parent $scriptRoot)

if ([string]::IsNullOrEmpty($DataPath)) { $DataPath = Join-Path $scriptRoot 'data.json' }
if ([string]::IsNullOrEmpty($TemplatePath)) { $TemplatePath = Join-Path $scriptRoot 'template.html' }
if ([string]::IsNullOrEmpty($OutputPath)) { $OutputPath = Join-Path $repoRoot 'docs\wiki\index.html' }

if (-not (Test-Path -LiteralPath $DataPath)) { Fail ('data file not found: ' + $DataPath) }
if (-not (Test-Path -LiteralPath $TemplatePath)) { Fail ('template not found: ' + $TemplatePath) }

$outDir = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $outDir)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
    Info ('created directory: ' + $outDir)
}

# ------------------------------------------------- encounter key reference ----
# The "fight <KEY>" hint on every card now comes from the data source itself:
# acts[].encounter / variants[].encounter hold the encounter id (e.g. IRONCLAD_ANGER) and the
# console command is "fight " + that id + "_BOSS_ENCOUNTER". The definition key (IRONCLAD_BOSS)
# is NOT the encounter id, so it is only a fallback for when the field is missing. When the mod's
# own localization table is present every derived key is cross-checked and misses are warned.

function FightKeyOf([string]$Encounter, [string]$Key) {
    if (-not [string]::IsNullOrEmpty($Encounter)) { return $Encounter + '_BOSS_ENCOUNTER' }
    if ($Key.EndsWith('_BOSS')) { return $Key + '_ENCOUNTER' }
    return $Key + '_BOSS_ENCOUNTER'
}

$warnings = New-Object System.Collections.Generic.List[string]
$encSet = New-Object 'System.Collections.Generic.HashSet[string]'
$encPath = Join-Path $repoRoot 'content\localization\zhs\encounters.json'
if (Test-Path -LiteralPath $encPath) {
    try {
        $encText = [System.IO.File]::ReadAllText($encPath, [System.Text.Encoding]::UTF8)
        $encObj = ConvertFrom-Json -InputObject $encText
        foreach ($prop in $encObj.PSObject.Properties) {
            $propName = [string]$prop.Name
            if ($propName.EndsWith('.title')) { $encSet.Add($propName.Substring(0, $propName.Length - 6)) | Out-Null }
        }
        Info ('read encounter keys: ' + $encSet.Count)
    } catch {
        $null = $encSet.Clear()
        $warnings.Add('could not read encounter list ' + $encPath + ': ' + $_.Exception.Message) | Out-Null
    }
}

# --------------------------------------------------------------- read in ----

$rawJson = [System.IO.File]::ReadAllText($DataPath, [System.Text.Encoding]::UTF8)
if ([string]::IsNullOrWhiteSpace($rawJson)) { Fail ('data file is empty: ' + $DataPath) }
Info ('read data.json: ' + $rawJson.Length + ' chars')

try {
    $doc = ConvertFrom-Json -InputObject $rawJson
} catch {
    Fail ('data.json is not valid JSON: ' + $_.Exception.Message)
}

if ($null -eq $doc -or $null -eq $doc.bosses) { Fail 'data.json has no "bosses" array' }

$template = [System.IO.File]::ReadAllText($TemplatePath, [System.Text.Encoding]::UTF8)
if ([string]::IsNullOrWhiteSpace($template)) { Fail ('template is empty: ' + $TemplatePath) }

$idx = $template.IndexOf($MarkerData)
if ($idx -lt 0) { Fail ('template is missing the data marker ' + $MarkerData) }
if ($template.IndexOf($MarkerData, $idx + 1) -ge 0) { Fail ('template contains the data marker more than once') }

$idx = $template.IndexOf($MarkerHp)
if ($idx -lt 0) { Fail ('template is missing the hp marker ' + $MarkerHp) }
if ($template.IndexOf($MarkerHp, $idx + 1) -ge 0) { Fail ('template contains the hp marker more than once') }

# ------------------------------------------------------- hp table + maths ----
# in-game max HP = character base HP
#                + extra hp of this act (this act's baseHp minus the first act's baseHp)
#                + Neow's Blessing: 120 on pickup, 180 for every act entered
#                + variant HpBonus
# The per-character base HP table is written down here on purpose (see task spec).

$hpTable = @{
    'IRONCLAD_BOSS'    = 80
    'SILENT_BOSS'      = 70
    'DEFECT_BOSS'      = 75
    'NECROBINDER_BOSS' = 66
    'REGENT_BOSS'      = 75
    'FOOL_BOSS'        = 75
    'WATCHER_DIVINITY' = 72
}

$blessingPickup = 120
$blessingPerAct = 180

$bosses = @($doc.bosses)
$entries = New-Object System.Collections.Generic.List[string]
$seenKeys = New-Object 'System.Collections.Generic.HashSet[string]'
$encMisses = New-Object 'System.Collections.Generic.HashSet[string]'

$bossCount = 0
$actCount = 0
$designCount = 0
$mainCount = 0
$variantCount = 0
$skippedCount = 0
$mainEmptyCount = 0

foreach ($boss in $bosses) {
    $bossKey = [string]$boss.key
    if ([string]::IsNullOrEmpty($bossKey)) {
        $warnings.Add('a boss entry has an empty key, skipped') | Out-Null
        continue
    }
    $bossCount = $bossCount + 1

    $acts = @($boss.acts)
    if ($acts.Count -eq 0) {
        $warnings.Add($bossKey + ': no acts, skipped') | Out-Null
        continue
    }

    $firstBaseHp = [int]$acts[0].baseHp

    if ($hpTable.ContainsKey($bossKey)) {
        $baseHp = [int]$hpTable[$bossKey]
    } else {
        $baseHp = $firstBaseHp
        $warnings.Add($bossKey + ': not in the base HP table, falling back to its first act baseHp ' + $firstBaseHp) | Out-Null
    }

    $baseEntry = '"b|' + $bossKey + '":' + $baseHp
    if ($seenKeys.Add('b|' + $bossKey)) { $entries.Add($baseEntry) | Out-Null }

    foreach ($act in $acts) {
        $actNo = [int]$act.act

        # act > 3 is an empty debug shell in the export (no script / no deck): not part of the wiki
        if ($actNo -gt 3) {
            $skippedCount = $skippedCount + 1
            continue
        }
        $actCount = $actCount + 1

        $actName = ''
        if ($null -ne $act.name) { $actName = [string]$act.name }
        $actEncounter = ''
        if ($null -ne $act.encounter) { $actEncounter = [string]$act.encounter }
        if ([string]::IsNullOrEmpty($actEncounter)) {
            $warnings.Add($bossKey + ' act ' + $actNo + ': no encounter id, falling back to the definition key') | Out-Null
        }
        $actFight = FightKeyOf $actEncounter $bossKey
        if ($encSet.Count -gt 0 -and -not $encSet.Contains($actFight)) { $encMisses.Add($actFight) | Out-Null }

        $extra = [int]$act.baseHp - $firstBaseHp
        if ($extra -lt 0) { $extra = 0 }

        $actHp = $baseHp + $extra + $blessingPickup + ($blessingPerAct * ($actNo - 1))

        $key = 'a|' + $bossKey + '|' + $actNo
        if ($seenKeys.Add($key)) { $entries.Add('"' + $key + '":' + $actHp) | Out-Null }
        else { $warnings.Add('duplicate act entry: ' + $key) | Out-Null }

        # a main design with no script and an empty deck (watcher acts 1/2) is not a real design:
        # the page renders only its variants, so do not count it either
        $deckCount = 0
        if ($null -ne $act.deck) { $deckCount = @($act.deck).Count }
        $mainHasBody = ($null -ne $act.script) -or ($deckCount -gt 0)

        if ($mainHasBody) {
            $mainCount = $mainCount + 1
            $designCount = $designCount + 1
            Info ('  ' + $bossKey.PadRight(18) + ' act ' + $actNo + '  main      hp=' + $actHp + '  ' + $actName + '  (' + $actFight + ')')
        } else {
            $mainEmptyCount = $mainEmptyCount + 1
            Info ('  ' + $bossKey.PadRight(18) + ' act ' + $actNo + '  main      (empty shell, page renders variants only)')
        }

        $variants = $act.variants
        if ($null -eq $variants) { $variants = @() }
        $variants = @($variants)

        foreach ($variant in $variants) {
            $variantKey = [string]$variant.key
            if ([string]::IsNullOrEmpty($variantKey)) {
                $warnings.Add($bossKey + ' act ' + $actNo + ': a variant has an empty key, skipped') | Out-Null
                continue
            }
            $bonus = 0
            if ($null -ne $variant.hpBonus) { $bonus = [int]$variant.hpBonus }
            if ($bonus -lt 0) { $bonus = 0 }

            $variantHp = $baseHp + $blessingPickup + ($blessingPerAct * ($actNo - 1)) + $bonus

            $variantName = ''
            if ($null -ne $variant.name) { $variantName = [string]$variant.name }
            $variantEncounter = ''
            if ($null -ne $variant.encounter) { $variantEncounter = [string]$variant.encounter }
            if ([string]::IsNullOrEmpty($variantEncounter)) {
                $warnings.Add($variantKey + ': no encounter id, falling back to the definition key') | Out-Null
            }
            $variantFight = FightKeyOf $variantEncounter $variantKey
            if ($encSet.Count -gt 0 -and -not $encSet.Contains($variantFight)) { $encMisses.Add($variantFight) | Out-Null }

            $key = 'v|' + $bossKey + '|' + $actNo + '|' + $variantKey
            if ($seenKeys.Add($key)) { $entries.Add('"' + $key + '":' + $variantHp) | Out-Null }
            else { $warnings.Add('duplicate variant entry: ' + $key) | Out-Null }

            $variantCount = $variantCount + 1
            $designCount = $designCount + 1
            Info ('  ' + $bossKey.PadRight(18) + ' act ' + $actNo + '  variant   hp=' + $variantHp + '  ' + $variantName + '  (' + $variantFight + ')')
        }
    }
}

$hpJson = '{' + ($entries -join ',') + '}'

foreach ($miss in $encMisses) {
    $warnings.Add('encounter key ' + $miss + ' is not in encounters.json') | Out-Null
}

# ------------------------------------------------------------- inject ----

# Escape "<" so the payload can never terminate the <script> element, and so a
# regenerated data.json containing "</script>" stays harmless.
$safeJson = $rawJson.Replace('<', '\u003c')

$html = $template.Replace($MarkerData, $safeJson)
$html = $html.Replace($MarkerHp, $hpJson)

[System.IO.File]::WriteAllText($OutputPath, $html, $Utf8NoBom)

# ---------------------------------------------------------- self check ----

if (-not (Test-Path -LiteralPath $OutputPath)) { Fail ('output was not written: ' + $OutputPath) }
$outInfo = Get-Item -LiteralPath $OutputPath
$outBytes = $outInfo.Length
$outText = [System.IO.File]::ReadAllText($OutputPath, [System.Text.Encoding]::UTF8)

$problems = New-Object System.Collections.Generic.List[string]
if (-not $outText.Contains('IRONCLAD_BOSS')) { $problems.Add('output does not contain IRONCLAD_BOSS') | Out-Null }
if (-not $outText.Contains('WATCHER_DIVINITY')) { $problems.Add('output does not contain WATCHER_DIVINITY') | Out-Null }
if (-not $outText.Contains('<table')) { $problems.Add('output does not contain a <table>') | Out-Null }
if ($outText.Contains('/*__BOSS_DATA__*/')) { $problems.Add('leftover data marker') | Out-Null }
if ($outText.Contains('/*__BOSS_HP__*/')) { $problems.Add('leftover hp marker') | Out-Null }
if ($outBytes -lt 300000) { $problems.Add('output smaller than 300 KB (' + $outBytes + ' bytes)') | Out-Null }

if ($problems.Count -gt 0) {
    foreach ($p in $problems) { Write-Host ('CHECK FAILED: ' + $p) -ForegroundColor Red }
    exit 1
}

foreach ($w in $warnings) { Write-Host ('WARN: ' + $w) -ForegroundColor Yellow }

Ok ('OK  bosses=' + $bossCount + '  acts=' + $actCount + '  designs=' + $designCount + ' (main ' + $mainCount + ' + variant ' + $variantCount + ')')
if ($skippedCount -gt 0) { Info ('    skipped ' + $skippedCount + ' act>3 shell(s), ' + $mainEmptyCount + ' empty main design(s) (variants kept)') }
Ok ('OK  wrote ' + $OutputPath)
Ok ('OK  size ' + $outBytes + ' bytes (' + [math]::Round($outBytes / 1024, 1) + ' KB)')

exit 0
