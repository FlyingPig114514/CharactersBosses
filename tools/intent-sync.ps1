# intent-sync.ps1 -- 把 docs\plans\intent-dump-*.tsv 写进「玩家角色当Boss——Boss意图表.xlsx」
# 需要 Excel；本文件必须 UTF-8 带 BOM（PS 5.1 按 ANSI 读无 BOM 脚本会把中文搞炸）。
# 用法: powershell -ExecutionPolicy Bypass -File tools\intent-sync.ps1 [-DryRun]
# TSV 列: design key act number encounter relics special_name special_text turn_no turn_play turn_skip asc8 asc9 multi collectible
[CmdletBinding()]
param([switch]$DryRun, [string]$Xlsx = '')
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$Root = if ($PSScriptRoot) { Split-Path -Parent $PSScriptRoot } else { (Get-Location).Path }
if (-not $Xlsx) { $Xlsx = Join-Path $Root '玩家角色当Boss——Boss意图表.xlsx' }
$ColStart = @{ SILENT = 1; NECROBINDER = 4; DEFECT = 7; IRONCLAD = 10; REGENT = 13 }

function CharOf([string]$key) {
    if ($key -like 'SILENT_*') { return 'SILENT' }
    if ($key -like 'NECROBINDER_*') { return 'NECROBINDER' }
    if ($key -like 'DEFECT_*') { return 'DEFECT' }
    if ($key -like 'IRONCLAD_*') { return 'IRONCLAD' }
    if ($key -like 'REGENT_*') { return 'REGENT' }
    return $null
}

# 27 档位 + 血量 = 角色基础(80/70/75/66/75) + 120/300/480（事件机 +20、根除骨 +31）
$Roster = @(
    @{k='SILENT_SLY';a=1;n='奇巧猎';hp=190},@{k='DEFECT_DARK';a=1;n='黑球机';hp=195},
    @{k='DEFECT_THUNDER';a=1;n='电球机';hp=195},@{k='NECROBINDER_DOOM';a=1;n='灾厄骨';hp=186},
    @{k='REGENT_DEBRIS';a=1;n='碎屑储君';hp=195},@{k='REGENT_BOMBARD';a=1;n='轰击储';hp=195},
    @{k='IRONCLAD_ANGER';a=1;n='愤怒战';hp=200},
    @{k='SILENT_ENVENOM';a=2;n='涂毒猎';hp=370},@{k='SILENT_SNECKO';a=2;n='蛇眼猎';hp=370},
    @{k='DEFECT_CLAW';a=2;n='物理机';hp=375},@{k='NECROBINDER_SOUL';a=2;n='灵魂骨';hp=366},
    @{k='REGENT_FORGE';a=2;n='铸剑储君';hp=375},@{k='REGENT_KING';a=2;n='王储';hp=375},
    @{k='REGENT_MINION';a=2;n='仆从储';hp=375},@{k='IRONCLAD_BLOOD';a=2;n='放血战';hp=380},
    @{k='IRONCLAD_JUGGLER';a=2;n='杂耍战';hp=380},
    @{k='SILENT_SHIV';a=3;n='刀猎';hp=550},@{k='SILENT_BREWER';a=3;n='炼药猎';hp=550},
    @{k='DEFECT_AI';a=3;n='能力机';hp=555},@{k='DEFECT_EVENT';a=3;n='事件机';hp=575},
    @{k='DEFECT_STATUS';a=3;n='状态机';hp=555},@{k='NECROBINDER_ERADICATE';a=3;n='根除骨';hp=577},
    @{k='NECROBINDER_OSTY';a=3;n='养手骨';hp=546},@{k='REGENT_COLORLESS';a=3;n='无色储君';hp=555},
    @{k='REGENT_STAR';a=3;n='辉星储';hp=555},@{k='IRONCLAD_VULN';a=3;n='易伤战';hp=560},
    @{k='IRONCLAD_BOULDER';a=3;n='巨石战';hp=560}
)

$files = @(Get-ChildItem (Join-Path $Root 'docs\plans') -Filter 'intent-dump-*.tsv' -ErrorAction SilentlyContinue | Sort-Object Name)
if ($files.Count -eq 0) { Write-Host '没找到 docs\plans\intent-dump-*.tsv'; exit 1 }
$rows = @(); foreach ($f in $files) { $rows += (Import-Csv -LiteralPath $f.FullName -Delimiter "`t" -Encoding UTF8) }
Write-Host ("读入 {0} 行，来自 {1}" -f $rows.Count, (($files | ForEach-Object { $_.Name }) -join ', '))

$records = @()
foreach ($g in ($rows | Group-Object key)) {
    $f0 = $g.Group[0]
    $turns = @($g.Group | Sort-Object { if ($_.turn_no -match '^\d+$') { [int]$_.turn_no } else { 999 } })
    $records += [pscustomobject]@{ design=$f0.design; key=$f0.key; act=[int]$f0.act; number=[int]$f0.number
        enc=$f0.encounter; relics=$f0.relics; spName=$f0.special_name; spText=$f0.special_text
        turns=$turns; asc8=$f0.asc8; asc9=$f0.asc9; multi=$f0.multi; collect=$f0.collectible }
}
Write-Host ("Boss 档位 = {0}" -f $records.Count)
foreach ($r in $records) {
    $miss = @(); if (-not $r.relics) { $miss += '遗物' }; if (-not $r.spName) { $miss += '特效名' }
    if (-not $r.spText) { $miss += '特效文' }; if (-not $r.asc8) { $miss += '进8' }
    if (-not $r.asc9) { $miss += '进9' }; if (-not $r.collect) { $miss += '藏品' }
    Write-Host ("  {0,-20} {1,-24} {2}层{3}号 回合{4,2} 缺:{5}" -f $r.design, $r.key, $r.act, $r.number, $r.turns.Count, (($miss -join ',') -replace '^$','—'))
}
if ($DryRun) { Write-Host 'DRY RUN：未写 Excel。'; exit 0 }

$xl = New-Object -ComObject Excel.Application
$xl.Visible = $false; $xl.DisplayAlerts = $false
$wb = $xl.Workbooks.Open($Xlsx)
function Get-Sheet($name) { foreach ($ws in $wb.Worksheets) { if ($ws.Name -eq $name) { return $ws } }; return $null }

$written = 0
foreach ($r in $records) {
  $global:Step = "begin"
  try {
    $char = CharOf $r.key
    if (-not $char) { Write-Host "!! 认不出角色 $($r.key)"; continue }
    $c = $ColStart[$char]
    $wsName = "$($r.act)层$($r.number)号"
    $ws = Get-Sheet $wsName
    if (-not $ws) { Write-Host "!! 缺工作表 [$wsName] ($($r.design))"; continue }

    $global:Step = "R1名"; $ws.Cells.Item(1,1).Value2 = 'Boss'
    $ws.Cells.Item(1,$c).Value2 = $r.design
    $ws.Cells.Item(2,$c).Value2 = '回合'; $ws.Cells.Item(2,($c+1)).Value2 = '打出'; $ws.Cells.Item(2,($c+2)).Value2 = '不出'
    $global:Step = "清区"; for ($i = 3; $i -le 60; $i++) { for ($j = $c; $j -le ($c+2); $j++) { try { $ws.Cells.Item($i,$j).ClearContents() } catch {} } }

    $global:Step = "回合"; $row = 3
    foreach ($t in $r.turns) {
        $ws.Cells.Item($row,$c).Value2 = $t.turn_no
        $ws.Cells.Item($row,($c+1)).Value2 = $t.turn_play
        $ws.Cells.Item($row,($c+2)).Value2 = $t.turn_skip
        $row++
    }
    $global:Step = "遗物"; $ws.Cells.Item($row,1).Value2 = '遗物'; $ws.Cells.Item($row,($c+1)).Value2 = $r.relics; $row++
    $global:Step = "特效"; $ws.Cells.Item($row,$c).Value2 = $r.spName; $ws.Cells.Item($row,($c+1)).Value2 = $r.spText; $ws.Cells.Item($row,($c+2)).Value2 = $r.collect; $row++
    $global:Step = "进阶"; $ws.Cells.Item($row,1).Value2 = '进阶'; $ws.Cells.Item($row,($c+1)).Value2 = $r.asc8; $ws.Cells.Item($row,($c+2)).Value2 = $r.asc9; $row++
    if ($r.multi -and $r.multi -ne '无') { $ws.Cells.Item($row,1).Value2 = '多人'; $ws.Cells.Item($row,($c+1)).Value2 = $r.multi }
    $written++
  } catch { Write-Host ("!! {0} 写失败（步骤 {1}）: {2}" -f $r.design, $global:Step, $_.Exception.Message) }
}

$wsCmd = Get-Sheet '指令'
if ($wsCmd) {
    $wsCmd.Cells.Clear() | Out-Null
    $wsCmd.Cells.Item(1,1).Value2 = 'Boss'; $wsCmd.Cells.Item(1,2).Value2 = '指令'; $wsCmd.Cells.Item(1,3).Value2 = '血量'
    $row = 2
    foreach ($seg in @(@{t='一层（act 1）-- 7 只';a=1}, @{t='二层（act 2）-- 9 只';a=2}, @{t='三层（act 3）-- 11 只';a=3})) {
        $wsCmd.Cells.Item($row,1).Value2 = $seg.t; $row++
        foreach ($b in ($Roster | Where-Object { $_.a -eq $seg.a })) {
            $wsCmd.Cells.Item($row,1).Value2 = $b.n
            $wsCmd.Cells.Item($row,2).Value2 = 'fight ' + $b.k + '_BOSS_ENCOUNTER'
            $wsCmd.Cells.Item($row,3).Value2 = $b.hp
            $row++
        }
    }
    Write-Host '指令表已重建（7 / 9 / 11）'
} else { Write-Host '!! 找不到「指令」工作表' }

$wb.Save(); $wb.Close($false); $xl.Quit()
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($xl) | Out-Null
Write-Host ("写完成：{0} 个 Boss 档位" -f $written)
