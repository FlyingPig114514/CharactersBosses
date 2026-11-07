# CharacterBosses 模组开关
#
# 这个模组会修改游戏内容（替换 Boss 遭遇），联机时会因为内容不一致被拦，
# 所以联机前先 disable，联机后再 enable。
#
#   powershell -ExecutionPolicy Bypass -File tools\charbosses-mod.ps1 -Action disable
#   powershell -ExecutionPolicy Bypass -File tools\charbosses-mod.ps1 -Action enable
#   powershell -ExecutionPolicy Bypass -File tools\charbosses-mod.ps1 -Action status
#
# 做法是把整个 mod 文件夹在 mods\ 和 mods_disabled\ 之间来回搬。
# 游戏只在启动时扫 mods\，所以切换后必须重启游戏才生效。

param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('disable', 'enable', 'status')]
    [string]$Action,

    [string]$GameDir = 'D:\SteamLibrary\steamapps\common\Slay the Spire 2'
)

$ErrorActionPreference = 'Stop'

$modsDir    = Join-Path $GameDir 'mods'
$holderDir  = Join-Path $GameDir 'mods_disabled'
$livePath   = Join-Path $modsDir 'CharacterBosses'
$stashPath  = Join-Path $holderDir 'CharacterBosses'

if (-not (Test-Path $GameDir)) {
    Write-Host "找不到游戏目录: $GameDir" -ForegroundColor Red
    exit 1
}

$running = Get-Process -Name 'SlayTheSpire2' -ErrorAction SilentlyContinue
if ($running -and $Action -ne 'status') {
    Write-Host "警告: 游戏正在运行。mod 只在启动时加载，请先关掉游戏再执行，否则切换不会生效。" -ForegroundColor Yellow
}

switch ($Action) {
    'disable' {
        if (-not (Test-Path $livePath)) {
            if (Test-Path $stashPath) { Write-Host "已经是关闭状态。" -ForegroundColor Green }
            else { Write-Host "没找到 mod 文件夹: $livePath" -ForegroundColor Red; exit 1 }
            break
        }
        if (-not (Test-Path $holderDir)) { New-Item -ItemType Directory -Path $holderDir | Out-Null }
        Move-Item -Path $livePath -Destination $stashPath -Force
        Write-Host "已关闭: $livePath" -ForegroundColor Green
        Write-Host "  -> $stashPath"
    }
    'enable' {
        if (-not (Test-Path $stashPath)) {
            if (Test-Path $livePath) { Write-Host "已经是开启状态。" -ForegroundColor Green }
            else { Write-Host "没找到已关闭的 mod: $stashPath" -ForegroundColor Red; exit 1 }
            break
        }
        if (-not (Test-Path $modsDir)) { New-Item -ItemType Directory -Path $modsDir | Out-Null }
        Move-Item -Path $stashPath -Destination $livePath -Force
        Write-Host "已开启: $livePath" -ForegroundColor Green
    }
    'status' {
        $state = if (Test-Path $livePath) { '开启 (在 mods\)' }
                 elseif (Test-Path $stashPath) { '关闭 (在 mods_disabled\)' }
                 else { '未安装' }
        Write-Host "CharacterBosses: $state"
        if ($running) { Write-Host "游戏正在运行" } else { Write-Host "游戏未运行" }
    }
}
