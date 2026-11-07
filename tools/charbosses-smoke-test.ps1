# CharacterBosses 冒烟测试：一条命令走完「安装 → 开调试开关 → 开局 → 打到 Boss → 自动打几回合 → 打印日志」
#
#   powershell -ExecutionPolicy Bypass -File tools\charbosses-smoke-test.ps1
#   powershell -ExecutionPolicy Bypass -File tools\charbosses-smoke-test.ps1 -Boss silent -Turns 3
#   powershell -ExecutionPolicy Bypass -File tools\charbosses-smoke-test.ps1 -NoLaunch   # 游戏已开着，只做开局+驱动
#
# -Boss 就是写进 charbosses_debug.txt 的内容：
#   cycle（默认，每场普通战斗轮换一个角色）/ ironclad / silent / defect / necrobinder / regent
#
# 前置：Steam 已启动并登录（否则游戏起不来）。

param(
    [string]$GameDir = 'D:\SteamLibrary\steamapps\common\Slay the Spire 2',
    [string]$Boss = 'cycle',
    [int]$Turns = 4,
    [int]$MaxSteps = 20,
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Continue'
$workspace = Split-Path -Parent $PSScriptRoot
$modDir    = Join-Path $GameDir 'mods\CharacterBosses'
$logFile   = Join-Path $env:APPDATA 'SlayTheSpire2\logs\mod_log.txt'
$MenuUrl   = 'http://localhost:8081/api/v1/menu'
$GameUrl   = 'http://localhost:15526/api/v1/singleplayer'
$SteamOk   = $false

function Say($msg)  { Write-Host "[test] $msg" }
function Warn($msg) { Write-Host "[test] $msg" -ForegroundColor Yellow }

function MenuApi($body) {
    try { Invoke-RestMethod $MenuUrl -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 20 }
    catch { @{ error = $_.Exception.Message } }
}
function MenuState { try { Invoke-RestMethod $MenuUrl -TimeoutSec 10 } catch { @{ screen = 'ERR' } } }
function GameApi($body) {
    try { Invoke-RestMethod $GameUrl -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 20 }
    catch { @{ error = $_.Exception.Message } }
}
function GameState { try { Invoke-RestMethod $GameUrl -TimeoutSec 10 } catch { $null } }

# STS2MCP 的动作名随版本变过（实测 combat_end_turn 已失效、end_turn 才对），
# 所以这里按候选名挨个试，成功一次就记住，避免写死。
$script:PlayAction = $null
$script:EndAction  = $null

function PlayCard($index, $target) {
    $names = if ($script:PlayAction) { @($script:PlayAction) } else { @('combat_play_card', 'play_card') }
    foreach ($n in $names) {
        $r = GameApi ("{{""action"":""{0}"",""card_index"":{1},""target"":""{2}""}}" -f $n, $index, $target)
        if ($r.status -eq 'ok') { $script:PlayAction = $n; return $r }
        if ($r.error -and $r.error -notmatch 'Unknown action') { return $r }
    }
    return $null
}

function EndTurn {
    $names = if ($script:EndAction) { @($script:EndAction) } else { @('end_turn', 'combat_end_turn') }
    foreach ($n in $names) {
        $r = GameApi ("{{""action"":""{0}""}}" -f $n)
        if ($r.status -eq 'ok') { $script:EndAction = $n; return $r }
        if ($r.error -and $r.error -notmatch 'Unknown action') { return $r }
    }
    return $null
}

# ── 1. 确保 mod 处于「已启用 + 已安装最新版」 ─────────────────────────────
Say '启用 mod（若在 mods_disabled 里）'
& (Join-Path $PSScriptRoot 'charbosses-mod.ps1') -Action enable -GameDir $GameDir 2>&1 | ForEach-Object { Say $_ }

if (-not $NoLaunch) {
    Say '关闭已运行的游戏'
    & (Join-Path $PSScriptRoot 'close_sts2.ps1') 2>&1 | Out-Null
    Start-Sleep -Seconds 5
}

Say '构建并安装到游戏目录'
cmd /c "`"$(Join-Path $workspace 'install-mod.bat')`" `"$GameDir`"" 2>&1 |
    Select-String -Pattern 'Build succeeded|PCK created|ERROR' | ForEach-Object { Say $_.Line.Trim() }

if (-not (Test-Path $modDir)) { Warn "安装失败：$modDir 不存在"; exit 1 }

Say "写调试开关：charbosses_debug.txt = '$Boss'"
Set-Content -Path (Join-Path $modDir 'charbosses_debug.txt') -Value $Boss -Encoding ASCII

# ── 2. 启动游戏 ──────────────────────────────────────────────────────────
if (-not $NoLaunch) {
    Remove-Item $logFile -Force -ErrorAction SilentlyContinue
    Say '启动游戏'
    & (Join-Path $PSScriptRoot 'launch_sts2.ps1') 2>&1 | Out-Null

    for ($i = 0; $i -lt 24; $i++) {
        Start-Sleep -Seconds 7
        if ((MenuState).screen -eq 'MAIN_MENU') { $SteamOk = $true; break }
    }
    if (-not $SteamOk) { Warn '等不到主菜单（Steam 没登录？）；仍然尝试继续'; }
    else { Say '已到主菜单' }
}

# ── 3. 开新局 ────────────────────────────────────────────────────────────
$s = MenuState
if ($s.screen -eq 'MODAL') { MenuApi '{"action":"confirm_modal"}' | Out-Null; Start-Sleep 2; $s = MenuState }
if (($s.available_actions) -contains 'abandon_run') {
    MenuApi '{"action":"abandon_run"}' | Out-Null; Start-Sleep 2
    if ((MenuState).screen -eq 'MODAL') { MenuApi '{"action":"confirm_modal"}' | Out-Null; Start-Sleep 3 }
}
if ((MenuState).screen -eq 'MAIN_MENU') {
    MenuApi '{"action":"open_character_select"}' | Out-Null; Start-Sleep 4
    MenuApi '{"action":"embark"}' | Out-Null; Start-Sleep 8
}

# ── 4. 驱动到第一场战斗 ─────────────────────────────────────────────────
Say '驱动到第一场战斗'
for ($i = 0; $i -lt $MaxSteps; $i++) {
    $g = GameState
    if (-not $g) { Start-Sleep 3; continue }
    Say "  step $i`: state=$($g.state_type)"

    switch ($g.state_type) {
        'event'       { GameApi '{"action":"choose_event_option","index":0}' | Out-Null; Start-Sleep 3 }
        'card_select' { GameApi '{"action":"select_card","index":0}' | Out-Null; Start-Sleep 1
                        GameApi '{"action":"confirm_selection"}' | Out-Null; Start-Sleep 3 }
        'map'         { GameApi '{"action":"choose_map_node","index":0}' | Out-Null; Start-Sleep 10 }
        'combat'      { }
        'boss'        { }
        'elite'       { }
        default       { }
    }
    if ($g.state_type -in @('combat', 'boss', 'elite')) { break }
    if ($g.state_type -in @('game_over', 'menu')) { break }
}

$g = GameState
if (-not $g.battle) { Warn "没进战斗（state=$($g.state_type)）"; }
else {
    Say "进入战斗：敌人 = $($g.battle.enemies[0].name) HP=$($g.battle.enemies[0].hp)/$($g.battle.enemies[0].max_hp)"
}

# ── 5. 自动打几回合，逼 Boss 出牌 ───────────────────────────────────────
for ($t = 1; $t -le $Turns; $t++) {
    for ($c = 0; $c -lt 6; $c++) {
        $g = GameState
        if (-not $g.battle -or -not $g.player.hand) { break }
        $playable = $g.player.hand | Where-Object { $_.can_play } | Select-Object -First 1
        if (-not $playable) { break }
        $target = $g.battle.enemies[0].combat_id
        PlayCard $playable.index $target | Out-Null
        Start-Sleep -Milliseconds 900
    }
    Say "turn $t`: 结束回合"
    EndTurn | Out-Null
    Start-Sleep -Seconds 8

    $g = GameState
    if ($g.state_type -eq 'game_over' -or -not $g.battle) { Warn "战斗结束/离开（state=$($g.state_type)）"; break }
    Say ("  -> 玩家 HP {0}/{1}，敌人 HP {2}，回合 {3}" -f $g.player.hp, $g.player.max_hp, $g.battle.enemies[0].hp, $g.battle.round)
}

# ── 6. 打印关键日志 ─────────────────────────────────────────────────────
Write-Host ''
Write-Host '================ [CharBoss] / [Hook] 日志 ================' -ForegroundColor Cyan
if (Test-Path $logFile) {
    Get-Content $logFile | Select-String -Pattern 'CharBoss|Hook|hand ui' | Select-Object -Last 60 |
        ForEach-Object { $_.Line }
} else {
    Warn "没有日志文件：$logFile"
}
Write-Host '=========================================================' -ForegroundColor Cyan
Write-Host '检查要点：' -ForegroundColor Cyan
Write-Host '  1) [Hook] act N boss slot fixed / normal encounter -> 换对了角色吗'
Write-Host '  2) [CharBoss] shadow player ready: <KEY> act=N deck(N): ...  卡组是不是你设计的那套'
Write-Host '  3) [CharBoss] planned N card(s): [...] 与 hand ui: N card(s)  计划与手牌栏是否一致'
Write-Host '  4) [CharBoss] PLAY xxx -> 名字   每张牌有没有真的打出去'
Write-Host '  5) 有没有 "play ... FAILED" 或 "PrepareTurn failed"'
