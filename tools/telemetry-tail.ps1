# telemetry-tail.ps1 -- show the newest boss_fight records from the local telemetry jsonl
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File tools\telemetry-tail.ps1
#   ... -Count 5          how many records to show (default 3)
#   ... -AllFields        also dump keys that are not in the short list below
#
# ASCII-only on purpose (Windows PowerShell 5.1 reads a BOM-less .ps1 as ANSI).
[CmdletBinding()]
param(
    [int]$Count = 3,
    [switch]$AllFields
)

$ErrorActionPreference = 'Stop'

$file = Join-Path $env:APPDATA 'SlayTheSpire2\mod_configs\CharacterBosses\telemetry\charbosses-telemetry.jsonl'
if (-not (Test-Path $file)) {
    Write-Host ("no telemetry file at " + $file)
    Write-Host 'in game: Mod Config -> CharacterBosses -> A5 must be enabled'
    exit 1
}

$lines = [System.IO.File]::ReadAllLines($file, [System.Text.Encoding]::UTF8)
$fights = @()
foreach ($line in $lines) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    if ($line -notmatch '"kind"\s*:\s*"boss_fight"') { continue }
    try { $fights += ($line | ConvertFrom-Json) } catch { }
}
Write-Host ("file : " + $file)
Write-Host ("lines: " + $lines.Length + " ; boss_fight records: " + $fights.Count)
if ($fights.Count -eq 0) { exit 0 }

$order = @('at', 'result', 'act', 'encounter', 'boss', 'ascension', 'players', 'turns',
           'pMaxHp', 'pHpStart', 'pHpEnd', 'pDmg', 'pHeal', 'pList',
           'bossTotal', 'bossesKilled', 'maxHpTotal', 'hpTotal', 'bossList', 'hpList',
           'role', 'seed')

$skip = $Count
for ($i = [Math]::Max(0, $fights.Count - $skip); $i -lt $fights.Count; $i++) {
    $f = $fights[$i]
    Write-Host ''
    Write-Host ("=== boss_fight #" + ($i + 1) + " ===") -ForegroundColor Cyan
    foreach ($k in $order) {
        if ($null -ne $f.$k) { Write-Host ("  {0,-12} = {1}" -f $k, $f.$k) }
    }
    if ($AllFields) {
        Write-Host '  -- all keys --'
        foreach ($p in $f.PSObject.Properties) {
            if ($order -contains $p.Name) { continue }
            Write-Host ("  {0,-12} = {1}" -f $p.Name, $p.Value)
        }
    }
}
