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

# ★ 打包护栏（2026-11-08，玩家那次不同步的**真凶不在代码里，在打包里**）：
#   玩家（beta / 111 端）发来的日志构建横幅是 `v0.2.502`，而他装的就是「刚打包的那一版」。
#   查下来 `.dist\CharacterBosses-v0.2.503.zip` 里 **107=0.2.503、111=0.2.502**：
#   打包前那一次 install-mod.bat 复制 Impl111 失败（游戏开着 ⇒ 文件被占用，copy 报错被 >nul 吞掉），
#   于是包里的 beta 实现还是旧 DLL ⇒ 0.2.503 的多人阵容修复**从来没送到 beta 玩家手里**。
#   ⇒ 打包前必须自己验一遍：两份实现的版本串都要等于清单版本，否则直接失败（不再产出混版包）。
function Get-ImplVersion([string]$path) {
    try {
        $bytes = [System.IO.File]::ReadAllBytes($path)
        # ⚠ 2026-11-09 修正：**不能**把整份文件按 UTF-16 解一次再正则 —— #US 堆里每串的偏移
        #   不一定是偶数，整份解码会错位，于是「明明有 v0.2.515」却被读成「读不到」
        #   （差点误判部署失败）。现在直接在**字节层**找 UTF-16LE 的 'v<主>.<次>.' 前缀。
        $probe = 'v' + $version
        if (Test-BytesContain -hay $bytes -text $probe) { return $version }

        # 版本对不上时，尽量把找到的那个版本号报出来（字节层扫描 'v0.2.' 后面 3 个字符）
        $prefix = [System.Text.Encoding]::Unicode.GetBytes('v')
        $dot = [System.Text.Encoding]::Unicode.GetBytes('.')
        for ($i = 0; $i + 12 -lt $bytes.Length; $i += 2) {
            if (-not ($bytes[$i] -eq $prefix[0] -and $bytes[$i + 1] -eq 0)) { continue }
            # 形如 v 数字 . 数字 . 数字数字数字（UTF-16LE：每个字符 2 字节，第 2 字节为 0）
            if ($bytes[$i + 3] -ne 0 -or $bytes[$i + 5] -ne 0) { continue }
            if (-not ([char]$bytes[$i + 2] -match '\d')) { continue }
            if (-not ([char]$bytes[$i + 4] -eq '.')) { continue }
            $sb = New-Object System.Text.StringBuilder
            $k = $i
            while ($k + 1 -lt $bytes.Length -and $bytes[$k + 1] -eq 0 -and ([char]$bytes[$k] -match '[0-9a-zA-Z\.]')) {
                [void]$sb.Append([char]$bytes[$k]); $k += 2
                if ($sb.Length -gt 14) { break }
            }
            $txt = $sb.ToString()
            if ($txt -match '^v\d+\.\d+\.\d+') { return $txt.TrimStart('v') }
        }
        $a = [System.Text.Encoding]::ASCII.GetString($bytes)
        $m = [regex]::Match($a, 'v(\d+\.\d+\.\d+)')
        if ($m.Success) { return $m.Groups[1].Value }
    } catch { }
    return ''
}

# 字节层子串查找（needle 按 UTF-16LE 编码）
function Test-BytesContain([byte[]]$hay, [string]$text) {
    $needle = [System.Text.Encoding]::Unicode.GetBytes($text)
    if ($needle.Length -eq 0 -or $hay.Length -lt $needle.Length) { return $false }
    for ($i = 0; $i -le $hay.Length - $needle.Length; $i++) {
        if ($hay[$i] -ne $needle[0]) { continue }
        $ok = $true
        for ($j = 1; $j -lt $needle.Length; $j++) {
            if ($hay[$i + $j] -ne $needle[$j]) { $ok = $false; break }
        }
        if ($ok) { return $true }
    }
    return $false
}

$badVersion = @()
foreach ($n in @('CharacterBosses.Impl107.dll', 'CharacterBosses.Impl111.dll')) {
    $p = Join-Path $Source $n
    if (-not (Test-Path $p)) { continue }
    $v = Get-ImplVersion $p
    Write-Host ('[dist] ' + $n + ' 版本串: ' + ($(if ($v) { 'v' + $v } else { '<读不到>' })))
    if ($v -ne $version) { $badVersion += ($n + '=' + $(if ($v) { 'v' + $v } else { '<读不到>' })) }
}
if ($badVersion.Count -gt 0) {
    Write-Host ('[dist] ✗ 拒绝打包：源目录的实现版本与清单版本 v' + $version + ' 不一致 ⇒ ' + ($badVersion -join ', ')) -ForegroundColor Red
    Write-Host '[dist]   最常见原因：游戏正开着，install-mod.bat 复制 Impl*.dll 时文件被占用（copy 失败被 >nul 吞掉）。' -ForegroundColor Yellow
    Write-Host '[dist]   ⇒ 先完全退出游戏，再重跑 install-mod.bat（两个目标各编一次），然后再打包。' -ForegroundColor Yellow
    exit 1
}

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