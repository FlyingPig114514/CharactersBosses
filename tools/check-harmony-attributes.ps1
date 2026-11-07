# Audit: [HarmonyPatch(typeof(X), nameof(X.Y))] targets that do NOT pin parameter types.
# ==================================================================================
# Why (real incident, 2026-10-30):
#   If the target method has overloads (e.g. NRunMusicController has BOTH
#   UpdateTrack() and UpdateTrack(string, float)), Harmony throws
#       HarmonyException: Ambiguous match ... Ambiguous match found for 'Void UpdateTrack()'
#   and PatchAll aborts => NONE of the mod's patches get applied, the game shows
#   "assembly DLL initialization failed" for the mod (see godot.log).
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File tools\check-harmony-attributes.ps1
#   Every listed line must be manually confirmed: does that type declare more than one
#   method with that name? If yes, pin the types:
#       [HarmonyPatch(typeof(X), nameof(X.Y), new Type[0])]             // no params
#       [HarmonyPatch(typeof(X), nameof(X.Y), new[] { typeof(int) })]   // with params
#
# Faster check for one type:
#   tools\dotnet\dotnet.exe tools\ilspy-cli\bin\Release\net10.0\ilspycli.dll members references\sts2.dll <Full.Type.Name>
#   then count lines matching  M ... <Method>(
#
# NOTE: keep this file ASCII-only -- Windows PowerShell reads .ps1 as ANSI unless it has a BOM.

[CmdletBinding()]
param([string]$Root = '')

if ([string]::IsNullOrWhiteSpace($Root)) {
    $here = $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($here)) { $here = (Get-Location).Path }
    $Root = (Resolve-Path (Join-Path $here '..')).Path
}

$pattern = 'HarmonyPatch\(\s*typeof\(([A-Za-z0-9_.]+)\)\s*,\s*nameof\(([A-Za-z0-9_.]+)\)\s*\)'
$hits = @()
Get-ChildItem -Path (Join-Path $Root 'src') -Recurse -Filter *.cs | ForEach-Object {
    $file = $_
    Select-String -Path $file.FullName -Pattern $pattern -AllMatches | ForEach-Object {
        foreach ($m in $_.Matches) {
            $hits += [pscustomobject]@{
                File   = $file.Name
                Line   = $_.LineNumber
                Type   = $m.Groups[1].Value
                Method = $m.Groups[2].Value
            }
        }
    }
}

if ($hits.Count -eq 0) {
    Write-Host 'OK: no unpinned nameof Harmony targets.' -ForegroundColor Green
    exit 0
}

Write-Host ("WARN: {0} unpinned nameof target(s) -- confirm none of them is overloaded:" -f $hits.Count) -ForegroundColor Yellow
$hits | Sort-Object Type, Method | Format-Table File, Line, Type, Method -AutoSize
Write-Host 'If a type declares several methods with that name, pin it with new Type[0] / new[]{typeof(...)}.' -ForegroundColor Gray
exit 0
