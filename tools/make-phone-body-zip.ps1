param(
    [string]$GameDir = 'D:\SteamLibrary\steamapps\common\Slay the Spire 2',
    [string]$Out     = '',
    [string]$Ver     = 'v0.114.0'
)
# 把 PC 版「游戏本体」（不含 exe、不含 mods）打成一个 **不压缩** 的 zip，
# 供手机启动器「导入本体」使用。目录结构照抄启动器自己生成的 payload：
#   SlayTheSpire2.pck / data_sts2_windows_x86_64/ / controller_config/ / release_info.json
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = Split-Path -Parent $PSScriptRoot
if (-not $Out) { $Out = Join-Path $root ".tmp\phone-dl\SlayTheSpire2-$Ver.zip" }
$outDir = Split-Path -Parent $Out
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }
if (Test-Path $Out) { Remove-Item $Out -Force }

$items = @(
    'SlayTheSpire2.pck',
    'data_sts2_windows_x86_64',
    'controller_config',
    'release_info.json'
)

$zip = [System.IO.Compression.ZipFile]::Open($Out, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($it in $items) {
        $src = Join-Path $GameDir $it
        if (-not (Test-Path $src)) { Write-Host ("SKIP (missing): " + $it); continue }
        $files = @()
        if (Test-Path $src -PathType Container) {
            $files = Get-ChildItem $src -Recurse -File
        } else {
            $files = @(Get-Item $src)
        }
        $base = (Get-Item $GameDir).FullName
        $n = 0
        foreach ($f in $files) {
            $rel = $f.FullName.Substring($base.Length).TrimStart('\')
            $entry = $zip.CreateEntry($rel.Replace('\', '/'), [System.IO.Compression.CompressionLevel]::NoCompression)
            $entry.LastWriteTime = $f.LastWriteTime
            $in = [System.IO.File]::OpenRead($f.FullName)
            try {
                $outStream = $entry.Open()
                try { $in.CopyTo($outStream, 1MB) } finally { $outStream.Dispose() }
            }
            finally { $in.Dispose() }
            $n++
        }
        Write-Host ("added {0} file(s): {1}" -f $n, $it)
    }
}
finally { $zip.Dispose() }

$i = Get-Item $Out
Write-Host ("OK {0}  {1:N1} MB" -f $Out, ($i.Length / 1MB))
