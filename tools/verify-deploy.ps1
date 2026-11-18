# verify-deploy.ps1 -- 装完之后自检：两份实现的版本串必须与清单版本一致
#
# 为什么需要（2026-11-13 又踩了一次）：
#   游戏开着的时候 `install-mod.bat` 里的 `copy /Y ... >nul` 会因文件被占用而**静默失败**
#   （>nul 把错误吞掉了），于是游戏目录里出现「Impl107 是新版、Impl111 还是旧版」的混版状态。
#   v0.2.502（玩家联机不同步）和 v0.2.591（本次）都是这个原因。
#   `pack-dist.ps1` 打包时有一道同样的护栏，但那只在打包时跑；日常装机也要立刻知道。
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File tools\verify-deploy.ps1 -ModDir "<游戏>\mods\CharacterBosses"
#   退出码：0 = 一致；1 = 不一致或缺文件
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ModDir,
    [string]$ExpectedVersion
)

$ErrorActionPreference = 'Stop'

# 从程序集里读「构建版本」：`BuildInfo.Tag` 的那条字符串以 `v<版本> / <日期>` 开头，
# 后面还串着一整串历史（「另含 v0.2.591 / …」）⇒ **只认第一处**，否则旧版本号会误判成通过。
#
# ★★ v0.2.800（2026-11-19 装机时踩到）：**绝不要把整份文件按 UTF-16 解一次再正则**。
#   `#US` 字符串堆里每串的偏移**不一定是偶数**（实测 `CharacterBosses.Impl113.dll.bin` 的
#   Tag 落在第 1312443 字节 = 奇数）⇒ 整份解码错位 ⇒ 明明有 `v0.2.800` 却报「读不到」
#   ⇒ 白白怀疑「部署版本不一致」，害得人去重装。`tools\pack-dist.ps1` 早就改成字节层查找了，
#   这里同步改成同一套：**先按字节层找「期望版本」**（这才是本脚本真正要回答的问题），
#   只有找不到时才尽力把「实际读到的版本」报出来（正反两个奇偶各解一次）。
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

function Read-Utf16FirstVersion([byte[]]$hay) {
    for ($skip = 0; $skip -lt 2; $skip++) {
        if ($hay.Length -le ($skip + 8)) { continue }
        $len = $hay.Length - $skip
        $len = $len - ($len % 2)
        $decoded = [System.Text.Encoding]::Unicode.GetString($hay, $skip, $len)
        $m = [regex]::Match($decoded, 'v(\d+\.\d+\.\d+) / ')
        if ($m.Success) { return $m.Groups[1].Value }
    }
    return ''
}

if (-not (Test-Path $ModDir)) {
    Write-Host ('[verify] 找不到模组目录: ' + $ModDir) -ForegroundColor Red
    exit 1
}

if (-not $ExpectedVersion) {
    # 清单就在模组目录里
    $manifest = Join-Path $ModDir 'CharacterBosses.json'
    if (Test-Path $manifest) {
        $json = [System.IO.File]::ReadAllText($manifest)
        $m = [regex]::Match($json, '"version"\s*:\s*"([^"]+)"')
        if ($m.Success) { $ExpectedVersion = $m.Groups[1].Value }
    }
}
if (-not $ExpectedVersion) {
    Write-Host '[verify] 读不到期望版本（清单里没有 version 字段）' -ForegroundColor Yellow
    exit 1
}

# ★ v0.2.733（2026-11-17）：构建目标早就从「107 / 111」改成「111（正式）/ 113（测试）」，
#   这里的白名单却还守着 Impl107 ⇒ 每次装机都误报「缺文件 ⇒ 部署版本不一致」。
#   现在按 loader 真正会加载的那两份核。
$bad = @()
foreach ($name in @('CharacterBosses.Impl111.dll.bin', 'CharacterBosses.Impl113.dll.bin')) {
    $p = Join-Path $ModDir $name
    if (-not (Test-Path $p)) {
        # 兼容旧安装方式（.dll）
        $alt = Join-Path $ModDir ($name -replace '\.bin$', '')
        if (Test-Path $alt) { $p = $alt; $name = Split-Path $alt -Leaf }
        else { $bad += ($name + '=缺文件'); continue }
    }
    $bytes = [System.IO.File]::ReadAllBytes($p)
    # ★ 判据 = 「字节层里有 `v<期望版本>`」（不受字符串堆奇偶偏移影响，见文件头的说明）
    if (Test-BytesContain $bytes ('v' + $ExpectedVersion)) { continue }
    $found = Read-Utf16FirstVersion $bytes
    if ($found -eq $ExpectedVersion) { continue }
    if (-not $found) { $found = '读不到' }
    $bad += ($name + '=v' + $found)
}

if ($bad.Count -eq 0) {
    Write-Host ('[verify] 两份实现的版本串都是 v' + $ExpectedVersion + '  OK') -ForegroundColor Green
    exit 0
}

Write-Host ('[verify] ✗ 部署版本不一致（期望 v' + $ExpectedVersion + '）：' + ($bad -join ', ')) -ForegroundColor Red
Write-Host '[verify]   最常见原因：游戏正开着，copy 被文件占用（install-mod.bat 里的 copy 报错被 >nul 吞掉）。' -ForegroundColor Yellow
Write-Host '[verify]   ⇒ 完全退出游戏，重跑 install-mod.bat。' -ForegroundColor Yellow
exit 1
