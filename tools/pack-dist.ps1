# pack-dist.ps1 -- 收尾打包：把「刚构建出来的东西」同步到 workshop\content 和 .dist\
#
# 用户 2026-11-05：「打包时在这里也放一份 D:\Sts2ModProject\...\.dist」
#
# 一次调用做三件事，保证三份内容永远一致：
#   1) 从游戏 mods 目录（install-mod.bat 刚装进去的那份，含新构建的 pck）
#      同步到 workshop\content\CharacterBosses\  ==> 上传工坊用的就是它；
#   2) 镜像到 .dist\CharacterBosses\                 ==> 本地留一份成品；
#   3) 压成 .dist\CharacterBosses-v<版本>.zip        ==> zip 内顶层是 CharacterBosses\，
#      解压到 <游戏>\mods\ 就能用（发给连不上创意工坊的朋友）。
#
# 版本号读 src\CharacterBosses.json 的 version（唯一真相）。
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File tools\pack-dist.ps1
#   powershell -ExecutionPolicy Bypass -File tools\pack-dist.ps1 -NoZip
#   powershell -ExecutionPolicy Bypass -File tools\pack-dist.ps1 -NoWorkshop
#   powershell -ExecutionPolicy Bypass -File tools\pack-dist.ps1 -Source "D:\...\mods\CharacterBosses"
#
# install-mod.bat 在最末尾会自动调用一次（[5/5] 步）；失败只提示，不影响安装。
[CmdletBinding()]
param(
    [string]$Source,
    [switch]$NoZip,
    [switch]$NoWorkshop
)

$ErrorActionPreference = 'Stop'

$Root     = Split-Path -Parent $PSScriptRoot
$Workshop = Join-Path $Root 'workshop\content\CharacterBosses'
$Dist     = Join-Path $Root '.dist'
$Mirror   = Join-Path $Dist 'CharacterBosses'

# 源目录：优先游戏 mods 目录（新鲜构建 + 新 pck），没有就退回 workshop\content
if (-not $Source) {
    $fallback = 'D:\SteamLibrary\steamapps\common\Slay the Spire 2\mods\CharacterBosses'
    if (Test-Path $fallback) { $Source = $fallback }
}
if (-not $Source -or -not (Test-Path $Source)) {
    if (Test-Path $Workshop) { $Source = $Workshop }
    else { Write-Host '[dist] 找不到任何可打包的目录（game mods / workshop\content）' -ForegroundColor Red; exit 1 }
}
Write-Host ('[dist] 源目录: ' + $Source)

# 版本号
$version = '0.0.0'
try {
    $json = [System.IO.File]::ReadAllText((Join-Path $Root 'src\CharacterBosses.json'))
    $m = [regex]::Match($json, '"version"\s*:\s*"([^"]+)"')
    if ($m.Success) { $version = $m.Groups[1].Value }
} catch { }
Write-Host ('[dist] 版本 ' + $version)

# 只搬这几个文件（游戏 mods 目录里还有 frames\ 等非工坊内容）
$names = @(
    'CharacterBosses.dll',
    'CharacterBosses.Impl107.dll',
    'CharacterBosses.Impl111.dll',
    'CharacterBosses.json',
    'CharacterBosses.pck',
    '安装说明.txt'
)
$missing = @()
foreach ($n in $names) {
    if (-not (Test-Path (Join-Path $Source $n))) {
        if ($n -eq '安装说明.txt') { continue }
        $missing += $n
    }
}
if ($missing.Count -gt 0) {
    Write-Host ('[dist] 源目录缺少: ' + ($missing -join ', ')) -ForegroundColor Red
    exit 1
}

function Copy-Set([string]$target, [string]$label) {
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    foreach ($n in $names) {
        $p = Join-Path $Source $n
        if (Test-Path $p) { Copy-Item $p (Join-Path $target $n) -Force }
    }
    # 安装说明.txt 以仓库里的 docs\安装说明.txt 为正本（它不是构建产物，游戏目录里那份可能是旧的）
    $doc = Join-Path $Root 'docs\安装说明.txt'
    if (Test-Path $doc) { Copy-Item $doc (Join-Path $target '安装说明.txt') -Force }
    $files = Get-ChildItem $target -File
    Write-Host ('[dist] ' + $label + '：' + $files.Count + ' 个文件')
    foreach ($f in $files) { Write-Host ('         ' + $f.Name + '  ' + $f.Length + ' B') }
}

# 1) workshop\content（上传工坊用的那一份）
if (-not $NoWorkshop) {
    $srcFull = (Resolve-Path $Source).Path
    $wsFull = $null
    if (Test-Path $Workshop) { $wsFull = (Resolve-Path $Workshop).Path }
    if ($srcFull -ne $wsFull) { Copy-Set $Workshop 'workshop\content\CharacterBosses 已更新' }
    else { Write-Host '[dist] 源目录就是 workshop\content，跳过第 1 步' }
}

# 2) .dist 镜像
Copy-Set $Mirror '.dist\CharacterBosses 已更新'

if ($NoZip) { Write-Host '[dist] -NoZip：跳过压缩'; exit 0 }

# 3) 压缩（zip 内顶层文件夹 = CharacterBosses）
$zip = Join-Path $Dist ('CharacterBosses-v' + $version + '.zip')
if (Test-Path $zip) { Remove-Item $zip -Force }

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $Mirror,
    $zip,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $true)

$size = (Get-Item $zip).Length
Write-Host ('[dist] 已生成 ' + (Split-Path -Leaf $zip) + '  (' + [math]::Round($size / 1MB, 2) + ' MB)')
Write-Host ('[dist] 目录: ' + $Dist) -ForegroundColor Green