# make-share-package.ps1 -- assemble a SELF-CONTAINED (offline) scaffold package.
#
# Why (2026-11-21): the friend who received StS2-Mod-Scaffold-Share could not work at all --
# tools\ only had 5 .ps1 files (no dotnet SDK / Godot / ilspy-cli), references\ was empty,
# .env did not exist, so nothing could be compiled and no .pck could be built. His agent also
# reported that outbound network inside its sandbox fails, so install.ps1 (which downloads the
# toolchain) is useless there.
#
# This script copies the pieces that are actually required to build + install a mod, out of THIS
# project, into the share folder:
#   references\sts2.dll          <- game install (data_sts2_windows_x86_64\sts2.dll)
#   tools\dotnet\                <- local .NET SDK 10 (840 MB)
#   tools\godot\                 <- Godot 4.5.1 mono (221 MB)
#   tools\pck_builder\           <- build_pck.gd
#   tools\ilspy-cli\             <- decompiler used by the agent
#   .env                         <- GAME_DIR
#   START-HERE-OFFLINE.md        <- offline instructions
#   tools\doctor.ps1             <- self-check
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File tools\make-share-package.ps1
#   ... -SkipToolchain          (only the small bits; the friend must install .NET+Godot himself)
#   ... -Target "D:\some\other\dir"
#
# ASCII only on purpose: PowerShell 5.1 reads a BOM-less .ps1 as ANSI.
[CmdletBinding()]
param(
    [string]$Target = 'D:\Sts2ModProject\StS2-Mod-Scaffold-Share',
    [string]$GameDir = 'D:\SteamLibrary\steamapps\common\Slay the Spire 2',
    [switch]$SkipToolchain
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
Write-Host ("[share] project  = " + $Root)
Write-Host ("[share] target   = " + $Target)

if (-not (Test-Path $Target)) { New-Item -ItemType Directory -Path $Target -Force | Out-Null }

function Ensure-Dir([string]$p) {
    if (-not (Test-Path $p)) { New-Item -ItemType Directory -Path $p -Force | Out-Null }
}

function Copy-FileSafe([string]$from, [string]$to) {
    if (-not (Test-Path $from)) { Write-Host ("  [missing] " + $from) -ForegroundColor Yellow; return $false }
    Ensure-Dir (Split-Path -Parent $to)
    Copy-Item $from $to -Force
    Write-Host ("  [copy] " + (Split-Path -Leaf $to) + "  " + [math]::Round((Get-Item $to).Length / 1MB, 1) + " MB")
    return $true
}

# ---- 1. references -------------------------------------------------------
Write-Host "[share] 1/5 references"
Ensure-Dir (Join-Path $Target 'references')
$gameDll = Join-Path $GameDir 'data_sts2_windows_x86_64\sts2.dll'
$ok = Copy-FileSafe $gameDll (Join-Path $Target 'references\sts2.dll')
if (-not $ok) {
    Write-Host "  !! sts2.dll not found under $GameDir -- pass -GameDir <your game folder>" -ForegroundColor Red
}
foreach ($n in @('0Harmony.dll', 'GodotSharp.dll', 'BaseLib.dll')) {
    Copy-FileSafe (Join-Path $Root "references\$n") (Join-Path $Target "references\$n") | Out-Null
}

# ---- 2. toolchain --------------------------------------------------------
Write-Host "[share] 2/5 tools\pck_builder + ilspy-cli"
Ensure-Dir (Join-Path $Target 'tools')
robocopy (Join-Path $Root 'tools\pck_builder') (Join-Path $Target 'tools\pck_builder') /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
robocopy (Join-Path $Root 'tools\ilspy-cli')  (Join-Path $Target 'tools\ilspy-cli')  /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
Write-Host "  [copy] pck_builder + ilspy-cli done"

# ---- 2b. PRIVACY: strip local-machine traces -----------------------------
# obj\ under a hand-built tool carries THIS machine's username and absolute NuGet paths
# (C:\Users\<me>\..., project folder names) and *.pdb embeds the same -> never ship them.
$ilspyObj = Join-Path $Target 'tools\ilspy-cli\obj'
if (Test-Path $ilspyObj) { Remove-Item $ilspyObj -Recurse -Force; Write-Host "  [privacy] removed tools\ilspy-cli\obj" }
Get-ChildItem (Join-Path $Target 'tools') -Recurse -File -Include '*.pdb' -ErrorAction SilentlyContinue |
    ForEach-Object { Remove-Item $_.FullName -Force; Write-Host ("  [privacy] removed " + $_.Name) }

if (-not $SkipToolchain) {
    Write-Host "[share] 3/5 tools\dotnet (840 MB, this takes a while)"
    robocopy (Join-Path $Root 'tools\dotnet') (Join-Path $Target 'tools\dotnet') /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    Write-Host "[share] 4/5 tools\godot (221 MB)"
    robocopy (Join-Path $Root 'tools\godot') (Join-Path $Target 'tools\godot') /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
}
else {
    Write-Host "[share] 3-4/5 SKIPPED toolchain (-SkipToolchain)" -ForegroundColor Yellow
}

# ---- 3. .env ------------------------------------------------------------
Write-Host "[share] 5/5 .env + docs"
$envPath = Join-Path $Target '.env'
# PRIVACY: ship the generic default path, not THIS machine's layout.
$envText = "GAME_DIR=C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2`r`n"
[System.IO.File]::WriteAllText($envPath, $envText, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "  [write] .env  (generic default path; the friend edits it or passes it to install-mod.bat)"

$startHere = @'
# START HERE (offline edition)

This folder is a **self-contained** Slay the Spire 2 mod scaffold: no download is needed.
Everything the build touches is already inside it.

## 1. Check the package (10 seconds)

```powershell
powershell -ExecutionPolicy Bypass -File tools\doctor.ps1
```

It prints OK/`MISSING` for each required piece:
`references\sts2.dll`, `tools\dotnet\dotnet.exe`, `tools\godot\Godot_v4.5.1-stable_mono_win64.exe`,
`tools\pck_builder\build_pck.gd`, `.env`.

If `references\sts2.dll` is missing, copy it out of your own game install:

```powershell
copy "<your game>\data_sts2_windows_x86_64\sts2.dll" references\sts2.dll
```

## 2. Point it at your game folder

Either edit `.env`, or just run `install-mod.bat "<your game folder>"`.
Default it looks for `C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2`.

## 3. Build + install

```powershell
install-mod.bat
```

It uses the bundled SDK (`tools\dotnet`) and Godot (`tools\godot`) -- nothing is fetched.

## 4. Make a new mod out of this scaffold

```powershell
powershell -ExecutionPolicy Bypass -File tools\rename-scaffold.ps1 -NewModName "MyMod"
```

Then read `rules.md` (must read) and `HOWTO-new-mod.md`.

## 5. If your AI agent says "no network"

That is expected: the agent's sandbox may block outbound traffic. Do not run `install.ps1`
in that case -- it is only needed when the toolchain is NOT bundled. With this package you can
build, pack and install completely offline.

## What is NOT bundled (optional)

| piece | why | how to get |
|---|---|---|
| `tools\STS2MCP` / `tools\STS2MenuControl` | in-game automation for testing | only needed if the agent should play the game; clone from GitHub when you have network |
| `tools\ILSpy-Mcp` | decompiler MCP server | `tools\ilspy-cli` is bundled, that is enough for reading `sts2.dll` |
'@
[System.IO.File]::WriteAllText((Join-Path $Target 'START-HERE-OFFLINE.md'), $startHere,
    (New-Object System.Text.UTF8Encoding($false)))

$doctor = @'
# doctor.ps1 -- verify the offline scaffold package has everything needed to build.
# ASCII only on purpose.
$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
$bad = 0

function Check([string]$label, [string]$path, [switch]$IsDir) {
    $exists = if ($IsDir) { Test-Path $path -PathType Container } else { Test-Path $path -PathType Leaf }
    if ($exists) {
        $size = ''
        if (-not $IsDir) { $size = '  ' + [math]::Round((Get-Item $path).Length / 1MB, 1) + ' MB' }
        Write-Host ("  OK      " + $label + $size) -ForegroundColor Green
    }
    else {
        Write-Host ("  MISSING " + $label + "   -> " + $path) -ForegroundColor Red
        $script:bad++
    }
}

Write-Host "== offline scaffold self-check =="
Check 'references\sts2.dll'      (Join-Path $root 'references\sts2.dll')
Check 'references\0Harmony.dll'  (Join-Path $root 'references\0Harmony.dll')
Check 'references\GodotSharp.dll' (Join-Path $root 'references\GodotSharp.dll')
Check 'tools\dotnet\dotnet.exe'  (Join-Path $root 'tools\dotnet\dotnet.exe')
Check 'tools\godot\Godot_v4.5.1-stable_mono_win64.exe' (Join-Path $root 'tools\godot\Godot_v4.5.1-stable_mono_win64.exe')
Check 'tools\pck_builder\build_pck.gd' (Join-Path $root 'tools\pck_builder\build_pck.gd')
Check 'src\Sts2ModScaffold.csproj' (Join-Path $root 'src\Sts2ModScaffold.csproj')

$envFile = Join-Path $root '.env'
if (Test-Path $envFile) {
    $line = (Get-Content $envFile | Where-Object { $_ -match '^\s*GAME_DIR\s*=' } | Select-Object -First 1)
    if ($line) {
        $g = ($line -split '=', 2)[1].Trim()
        Write-Host ("  .env     GAME_DIR = " + $g)
        if (Test-Path (Join-Path $g 'data_sts2_windows_x86_64\sts2.dll')) {
            Write-Host "  OK      game install found" -ForegroundColor Green
        }
        else { Write-Host "  WARN    game install not found at that GAME_DIR (pass the path to install-mod.bat)" -ForegroundColor Yellow }
    }
}
else { Write-Host "  WARN    .env missing (GAME_DIR unknown; install-mod.bat will ask)" -ForegroundColor Yellow }

Write-Host ""
if ($bad -eq 0) { Write-Host "ALL GOOD -- run install-mod.bat" -ForegroundColor Green }
else { Write-Host ("$bad required piece(s) missing -- see above") -ForegroundColor Red }
'@
Ensure-Dir (Join-Path $Target 'tools')
[System.IO.File]::WriteAllText((Join-Path $Target 'tools\doctor.ps1'), $doctor,
    (New-Object System.Text.UTF8Encoding($false)))

$total = (Get-ChildItem $Target -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
Write-Host ("[share] done -> " + $Target + "  (" + [math]::Round($total / 1GB, 2) + " GB)")
