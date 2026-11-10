# upload-workshop.ps1 -- upload workshop\ to Steam, with the native-DLL problem already solved.
#
# WHY THIS WRAPPER EXISTS (2026-10-31):
#   Running the uploader as `dotnet bin\Release\net8.0\ModUploader.dll upload ...` fails with
#     System.DllNotFoundException: Unable to load DLL 'steam_api64' ... (0x8007007E)
#   because the process image is tools\dotnet\dotnet.exe, so Windows' "application directory"
#   for DLL search is tools\dotnet\ -- NOT the folder that contains steam_api64.dll.
#   Verified with a probe: NativeLibrary.Load("steam_api64") FAILS by name, works by full path.
#   Two working fixes (both verified):
#     a) run the apphost  bin\Release\net8.0\ModUploader.exe  (its own folder IS the app dir)
#        + DOTNET_ROOT so the apphost finds the .NET 8 runtime under tools\dotnet
#     b) prepend bin\Release\net8.0 to PATH and keep using `dotnet ....dll`
#   This script does (a).
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File tools\upload-workshop.ps1
#   powershell -ExecutionPolicy Bypass -File tools\upload-workshop.ps1 -Args 'remove'
#   powershell -ExecutionPolicy Bypass -File tools\upload-workshop.ps1 -Help
#
# Steam must be running and logged in. On success the uploader writes mod_id.txt in the
# workspace (workshop\mod_id.txt) -- keep it, updates are matched by that id.
[CmdletBinding()]
param(
    # ⚠ 2026-11-10：这里**必须是绝对路径** —— 传 `..\..\workshop` 时 Steamworks 会报
    #   `k_EResultFileNotFound`（相对路径带着 `..`，Steam 那边解析不到），上传直接失败。
    #   空 = 由下面按脚本位置算成绝对路径；想换目录再显式传 -UploaderArgs。
    [string[]]$UploaderArgs = @(),
    [switch]$Help
)

$ErrorActionPreference = 'Stop'

$Root     = Split-Path -Parent $PSScriptRoot
$Proj     = Join-Path $Root 'sts2-mod-uploader-main\sts2-mod-uploader-main'
$Exe      = Join-Path $Proj 'bin\Release\net8.0\ModUploader.exe'
$Dll      = Join-Path $Proj 'bin\Release\net8.0\ModUploader.dll'
$DotnetRoot = Join-Path $Root 'tools\dotnet'

if (-not (Test-Path $Exe) -or -not (Test-Path $Dll)) {
    Write-Host 'Uploader is not built yet -- building it now...' -ForegroundColor Yellow
    $dotnet = Join-Path $Root 'tools\dotnet\dotnet.exe'
    if (-not (Test-Path $dotnet)) { Write-Host ("missing " + $dotnet) -ForegroundColor Red; exit 1 }
    Push-Location $Proj
    try {
        $out = & $dotnet build 'ModUploader.csproj' -c Release -v q 2>&1
        $errs = $out | Where-Object { $_ -match ': error ' }
        if ($errs) { $errs | ForEach-Object { Write-Host $_ -ForegroundColor Red }; exit 1 }
    }
    finally { Pop-Location }
}

$native = Join-Path $Proj 'bin\Release\net8.0\steam_api64.dll'
if (-not (Test-Path $native)) {
    $from = Join-Path $Proj 'steam\steam_api64.dll'
    if (Test-Path $from) {
        Copy-Item $from $native -Force
        Write-Host ('copied steam_api64.dll next to the uploader (' + $native + ')')
    }
    else { Write-Host 'WARNING: steam_api64.dll not found in bin or steam\ -- upload will fail' -ForegroundColor Yellow }
}
$appId = Join-Path $Proj 'bin\Release\net8.0\steam_appid.txt'
if (-not (Test-Path $appId) -and (Test-Path (Join-Path $Proj 'steam\steam_appid.txt'))) {
    Copy-Item (Join-Path $Proj 'steam\steam_appid.txt') $appId -Force
}

# apphost needs to know where the private .NET runtime lives
$env:DOTNET_ROOT = $DotnetRoot

if ($UploaderArgs.Count -eq 0) {
    $wsAbs = (Resolve-Path (Join-Path $Root 'workshop')).Path
    $UploaderArgs = @('upload', '-w', $wsAbs)
}

Push-Location $Proj
try {
    if ($Help) { & $Exe 'upload' '--help'; exit $LASTEXITCODE }
    Write-Host ('running: ModUploader.exe ' + ($UploaderArgs -join ' '))
    Write-Host ('workspace: ' + (Join-Path $Proj ($UploaderArgs[2])))
    & $Exe @UploaderArgs
    $code = $LASTEXITCODE
    Write-Host ''
    if ($code -eq 0) {
        Write-Host 'UPLOAD OK' -ForegroundColor Green
        $idFile = Join-Path $Root 'workshop\mod_id.txt'
        if (Test-Path $idFile) { Write-Host ('workshop item id: ' + ([System.IO.File]::ReadAllText($idFile).Trim())) }
    }
    else { Write-Host ('uploader exited with code ' + $code) -ForegroundColor Yellow }
    exit $code
}
finally { Pop-Location }
