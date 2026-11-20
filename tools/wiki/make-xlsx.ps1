# make-xlsx.ps1 -- 把 tools\wiki\data.json 生成一份便于核验的 Excel 总表。
#
# 输出：玩家角色当Boss——Boss意图表.v2.xlsx
#   工作表「总表」    ：每个设计一行（楼层/角色/设计/Key/控制台指令/血量/循环/卡组/遗物/特殊效果/恐惧版/进阶改动/能力统计）
#   工作表「剧本·N层」：每个设计的逐回合（Key/设计/回合/打出/不出/备注）
#   工作表「说明」    ：数据来源、血量公式、生成时间
#
# 只依赖 Windows PowerShell 5.1 + System.IO.Compression（xlsx 就是 zip + XML）。
# 用法：powershell -ExecutionPolicy Bypass -File tools\wiki\make-xlsx.ps1

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not (Test-Path (Join-Path $Root 'tools\wiki\data.json'))) { $Root = 'D:\Sts2ModProject\Downfall Expansion Vanilla Characters as Bosses' }

$dataPath = Join-Path $Root 'tools\wiki\data.json'
$outPath = Join-Path $Root '玩家角色当Boss——Boss意图表.v2.xlsx'

$data = ConvertFrom-Json ([System.IO.File]::ReadAllText($dataPath, [System.Text.Encoding]::UTF8))

# ── 角色基础血量（血量公式的第一项）───────────────────────
$StartHp = @{
    'IRONCLAD_BOSS' = 80; 'SILENT_BOSS' = 70; 'DEFECT_BOSS' = 75; 'NECROBINDER_BOSS' = 66
    'REGENT_BOSS' = 75; 'FOOL_BOSS' = 75; 'WATCHER_DIVINITY' = 72
}

# ── 工具函数（必须在使用前定义）──────────────────────────
function Get-BossHp([string]$defKey, [int]$actNo, [int]$actBaseHp, [int]$firstBaseHp, [int]$variantBonus) {
    $start = 80
    if ($StartHp.ContainsKey($defKey)) { $start = $StartHp[$defKey] }
    $extra = 0
    if ($actBaseHp -gt $firstBaseHp) { $extra = $actBaseHp - $firstBaseHp }
    return $start + $extra + 120 + 180 * ($actNo - 1) + $variantBonus
}

function Esc([string]$s) {
    if ($null -eq $s) { return '' }
    $s = $s.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;')
    return $s
}

function Is-NotPlayed($c) {
    foreach ($f in @('Burned', 'HoldInstead', 'NotDrawn', 'DiscardInstead', 'DiscardInsteadWhenMultiplayer', 'DiscardInsteadSingleOnly', 'DisplayOnly')) {
        if ($c.PSObject.Properties.Name -contains $f) { return $true }
    }
    return $false
}

function Join-Cards($cards, [bool]$notPlayed) {
    if ($null -eq $cards) { return '' }
    $parts = New-Object System.Collections.ArrayList
    foreach ($c in $cards) {
        if ((Is-NotPlayed $c) -ne $notPlayed) { continue }
        $label = [string]$c.name
        if ($c.PSObject.Properties.Name -contains 'Upgraded') { $label = $label + '+' }
        $cost = -1
        if ($c.PSObject.Properties.Name -contains 'CostOverride') { $cost = [int]$c.CostOverride }
        if ($c.PSObject.Properties.Name -contains 'CostThisTurn') { $cost = [int]$c.CostThisTurn }
        if ($cost -ge 0) { $label = $label + '（' + $cost + '费）' }
        if (($c.PSObject.Properties.Name -contains 'ExtraAoe') -and [int]$c.ExtraAoe -gt 0) { $label = $label + '〔群攻 ' + $c.ExtraAoe + '〕' }
        if (($c.PSObject.Properties.Name -contains 'DamageOverride') -and [int]$c.DamageOverride -gt 0) { $label = $label + '〔伤 ' + $c.DamageOverride + '〕' }
        if (($c.PSObject.Properties.Name -contains 'BlockOverride') -and [int]$c.BlockOverride -gt 0) { $label = $label + '〔格挡 ' + $c.BlockOverride + '〕' }
        if ($c.PSObject.Properties.Name -contains 'PrintsToDrawPile') { $label = $label + '〔印 ' + ($c.PrintsToDrawPile -join '/') + '〕' }
        if ($c.PSObject.Properties.Name -contains 'ReturnsToHand') { $label = $label + '〔回收 ' + ($c.ReturnsToHand -join '/') + '〕' }
        if ($c.PSObject.Properties.Name -contains 'ExhaustWith') { $label = $label + '〔消耗 ' + ($c.ExhaustWith -join '/') + '〕' }
        if ($c.PSObject.Properties.Name -contains 'Sly') { $label = $label + '〔奇巧〕' }
        if ($c.PSObject.Properties.Name -contains 'Retain') { $label = $label + '〔保留〕' }
        if ($c.PSObject.Properties.Name -contains 'AutoPlayed') { $label = $label + '〔自动〕' }
        if ($c.PSObject.Properties.Name -contains 'FreeThisTurn') { $label = $label + '〔免费〕' }
        if ($c.PSObject.Properties.Name -contains 'SpendsAllEnergy') { $label = $label + '〔吃掉全部能量〕' }
        [void]$parts.Add($label)
    }
    return ($parts -join '、')
}

function Merge-Counts($list) {
    if ($null -eq $list) { return '' }
    $order = New-Object System.Collections.ArrayList
    $counts = @{}
    foreach ($x in $list) {
        if ([string]::IsNullOrEmpty($x)) { continue }
        if (-not $counts.ContainsKey($x)) { $counts[$x] = 0; [void]$order.Add($x) }
        $counts[$x] = $counts[$x] + 1
    }
    $parts = @()
    foreach ($k in $order) {
        if ($counts[$k] -gt 1) { $parts += ($k + ' ×' + $counts[$k]) } else { $parts += $k }
    }
    return ($parts -join '、')
}

function Loop-Text($script) {
    if ($null -eq $script) { return '' }
    if ($script.loopFrom -gt 0 -and $script.loopLength -gt 0) {
        $end = $script.loopFrom + $script.loopLength - 1
        return ('第 ' + $script.loopFrom + '~' + $end + ' 回合循环')
    }
    return '不循环'
}

function Col-Name([int]$n) {
    $s = ''
    while ($n -gt 0) {
        $m = ($n - 1) % 26
        $s = [string][char](65 + $m) + $s
        $n = [int](($n - $m) / 26)
    }
    return $s
}

function Sheet-Xml($rows, [int[]]$widths) {
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>')
    [void]$sb.Append('<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">')
    if ($widths) {
        [void]$sb.Append('<cols>')
        for ($i = 0; $i -lt $widths.Length; $i++) {
            $n = $i + 1
            [void]$sb.Append('<col min="' + $n + '" max="' + $n + '" width="' + $widths[$i] + '" customWidth="1"/>')
        }
        [void]$sb.Append('</cols>')
    }
    [void]$sb.Append('<sheetData>')
    $rowIdx = 0
    foreach ($row in $rows) {
        $rowIdx = $rowIdx + 1
        [void]$sb.Append('<row r="' + $rowIdx + '">')
        $colIdx = 0
        foreach ($cell in $row) {
            $colIdx = $colIdx + 1
            $ref = (Col-Name $colIdx) + $rowIdx
            $val = ''
            if ($null -ne $cell) { $val = [string]$cell }
            if ($val -match '^-?\d+$' -and $val.Length -lt 10) {
                [void]$sb.Append('<c r="' + $ref + '"><v>' + $val + '</v></c>')
            }
            elseif ($val.Length -gt 0) {
                [void]$sb.Append('<c r="' + $ref + '" t="inlineStr"><is><t xml:space="preserve">' + (Esc $val) + '</t></is></c>')
            }
        }
        [void]$sb.Append('</row>')
    }
    [void]$sb.Append('</sheetData></worksheet>')
    return $sb.ToString()
}

function Add-ZipText($zip, [string]$name, [string]$text) {
    $entry = $zip.CreateEntry($name, [System.IO.Compression.CompressionLevel]::Optimal)
    $stream = $entry.Open()
    $writer = New-Object System.IO.StreamWriter($stream, (New-Object System.Text.UTF8Encoding($false)))
    $writer.Write($text)
    $writer.Flush()
    $writer.Dispose()
    $stream.Dispose()
}

function Add-TurnRows($target, [string]$key, [string]$name, $script) {
    if ($null -eq $script) { return }
    $i = 0
    foreach ($turn in $script.turns) {
        $i = $i + 1
        $note = [string]$turn.note
        $note = [regex]::Replace($note, '^\d+\.\s*', '')
        [void]$target.Add(@($key, $name, $i, (Join-Cards $turn.cards $false), (Join-Cards $turn.cards $true), $note))
    }
}
# ── 收集数据 ─────────────────────────────────────────────
$summaryRows = New-Object System.Collections.ArrayList
$turn1 = New-Object System.Collections.ArrayList
$turn2 = New-Object System.Collections.ArrayList
$turn3 = New-Object System.Collections.ArrayList

foreach ($boss in $data.bosses) {
    if ($boss.acts.Count -eq 0) { continue }
    $firstBaseHp = [int]$boss.acts[0].baseHp

    foreach ($act in $boss.acts) {
        $actNo = [int]$act.act
        if ($actNo -lt 1 -or $actNo -gt 3) { continue }

        # ① 主设计
        $emptyMain = ($null -eq $act.script -and $act.deck.Count -eq 0)
        if ($emptyMain) { } else {
        $hp = Get-BossHp $boss.key $actNo ([int]$act.baseHp) $firstBaseHp 0
        $name = [string]$act.name
        [void]$summaryRows.Add(@(
            $actNo, $boss.character, $name, $act.encounter, ('fight ' + $act.encounter + '_BOSS_ENCOUNTER'),
            $hp, $act.energy, $act.handSize, (Loop-Text $act.script),
            (Merge-Counts $act.deck), (Merge-Counts $act.relics),
            $act.special, $act.specialDread, ($act.dreadUpgrades -join '、'), $boss.info
        ))
        $target = $turn1
        if ($actNo -eq 2) { $target = $turn2 }
        if ($actNo -eq 3) { $target = $turn3 }
        Add-TurnRows $target ([string]$act.encounter) $name $act.script
        }

        # ② 变体（同一角色的第 2/3/4 套设计）
        foreach ($v in $act.variants) {
            $vHp = Get-BossHp $boss.key $actNo 0 $firstBaseHp ([int]$v.hpBonus)
            $extras = New-Object System.Collections.ArrayList
            foreach ($x in $v.ascensionUpgrades) { if ($x) { [void]$extras.Add('进阶 ' + $x) } }
            foreach ($x in $v.ascensionRelics) { if ($x) { [void]$extras.Add('进阶 ' + $x) } }
            foreach ($x in $v.dreadUpgrades) { if ($x) { [void]$extras.Add('恐惧升级 ' + $x) } }
            [void]$summaryRows.Add(@(
                $actNo, $boss.character, ([string]$v.name), $v.encounter, ('fight ' + $v.encounter + '_BOSS_ENCOUNTER'),
                $vHp, $act.energy, $act.handSize, (Loop-Text $v.script),
                (Merge-Counts $v.deck), (Merge-Counts $v.relics),
                $v.special, $v.specialDread, ($extras -join '；'), $boss.info
            ))
            Add-TurnRows $target ([string]$v.encounter) ([string]$v.name) $v.script
        }
    }
}


# ── 组装工作表 ───────────────────────────────────────────
$sheetNames = @('总表', '剧本·一层', '剧本·二层', '剧本·三层', '说明')
$sheetFiles = @('sheet1.xml', 'sheet2.xml', 'sheet3.xml', 'sheet4.xml', 'sheet5.xml')
$sheetXml = @{}

$rowsSum = New-Object System.Collections.ArrayList
[void]$rowsSum.Add(@('楼层', '角色', '设计', 'Key', '控制台指令', '血量', '能量', '手牌', '循环', '卡组', '遗物', '特殊效果', '恐惧版特殊效果', '进阶/恐惧改动', '能力统计'))
foreach ($r in $summaryRows) { [void]$rowsSum.Add($r) }
$sheetXml['总表'] = Sheet-Xml $rowsSum @(6, 10, 26, 24, 42, 8, 6, 6, 18, 62, 34, 66, 66, 44, 50)

$headTurn = @('Key', '设计', '回合', '打出', '不出', '备注')
$turnSets = @{ '剧本·一层' = $turn1; '剧本·二层' = $turn2; '剧本·三层' = $turn3 }
foreach ($n in @('剧本·一层', '剧本·二层', '剧本·三层')) {
    $rows = New-Object System.Collections.ArrayList
    [void]$rows.Add($headTurn)
    foreach ($r in $turnSets[$n]) { [void]$rows.Add($r) }
    $sheetXml[$n] = Sheet-Xml $rows @(24, 26, 6, 64, 44, 64)
}

$rowsNote = New-Object System.Collections.ArrayList
[void]$rowsNote.Add(@('项目', '内容'))
[void]$rowsNote.Add(@('数据来源', 'tools\wiki-dump 直接从模组 DLL（' + $data.generatedFrom + '）反射导出：BossDefs.All / BossVariant / BossScript / BossInfoTable'))
[void]$rowsNote.Add(@('导出时间', $data.generatedAt))
[void]$rowsNote.Add(@('血量公式', '角色基础血量 + 该幕额外 + 涅奥的祝福 120 + 每进一幕 180×(幕−1) + 变体 HpBonus'))
[void]$rowsNote.Add(@('基础血量表', '铁甲战士 80 / 静默猎手 70 / 故障机器人 75 / 亡灵契约师 66 / 储君 75 / 愚者 75 / 观者 72'))
[void]$rowsNote.Add(@('打出 / 不出', '带 Burned / HoldInstead / NotDrawn / DiscardInstead* / DisplayOnly 标记的牌算「不出」，其余算「打出」'))
[void]$rowsNote.Add(@('循环', '剧本回合数走完后，从循环起始回合起按循环长度重复；「不循环」= 走完就停在最后一回合'))
[void]$rowsNote.Add(@('恐惧模式', '「恐惧版特殊效果」非空 = 恐惧模式下这段说明/效果更强；「进阶/恐惧改动」列出进阶 8/9 与恐惧模式额外升级的牌'))
[void]$rowsNote.Add(@('重新生成', '改模组代码后依次跑 tools\wiki-dump（导出 JSON）与 tools\wiki\make-xlsx.ps1（生成本表）'))
$sheetXml['说明'] = Sheet-Xml $rowsNote @(18, 130)

# ── 打包 xlsx ────────────────────────────────────────────
$contentTypes = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
'<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">' +
'<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>' +
'<Default Extension="xml" ContentType="application/xml"/>' +
'<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>'
for ($i = 0; $i -lt $sheetFiles.Length; $i++) {
    $contentTypes += '<Override PartName="/xl/worksheets/' + $sheetFiles[$i] + '" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>'
}
$contentTypes += '<Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>'

$rels = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
'<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">' +
'<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>'

$wbSheets = ''
for ($i = 0; $i -lt $sheetNames.Length; $i++) {
    $wbSheets += '<sheet name="' + (Esc $sheetNames[$i]) + '" sheetId="' + ($i + 1) + '" r:id="rId' + ($i + 1) + '"/>'
}
$workbook = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
'<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">' +
'<sheets>' + $wbSheets + '</sheets></workbook>'

$wbRels = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
'<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
for ($i = 0; $i -lt $sheetFiles.Length; $i++) {
    $wbRels += '<Relationship Id="rId' + ($i + 1) + '" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/' + $sheetFiles[$i] + '"/>'
}
$wbRels += '<Relationship Id="rId' + ($sheetFiles.Length + 1) + '" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>'

$styles = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
'<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">' +
'<fonts count="1"><font><sz val="11"/><name val="DengXian"/></font></fonts>' +
'<fills count="1"><fill><patternFill patternType="none"/></fill></fills>' +
'<borders count="1"><border/></borders>' +
'<cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>' +
'<cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs></styleSheet>'

if (Test-Path $outPath) { Remove-Item $outPath -Force }
$zip = [System.IO.Compression.ZipFile]::Open($outPath, 'Create')
Add-ZipText $zip '[Content_Types].xml' $contentTypes
Add-ZipText $zip '_rels/.rels' $rels
Add-ZipText $zip 'xl/workbook.xml' $workbook
Add-ZipText $zip 'xl/_rels/workbook.xml.rels' $wbRels
Add-ZipText $zip 'xl/styles.xml' $styles
for ($i = 0; $i -lt $sheetNames.Length; $i++) {
    Add-ZipText $zip ('xl/worksheets/' + $sheetFiles[$i]) $sheetXml[$sheetNames[$i]]
}
$zip.Dispose()

Write-Output ('OK: ' + $outPath)
Write-Output ('  总表 ' + ($rowsSum.Count - 1) + ' 行；剧本行 一层=' + $turn1.Count + ' 二层=' + $turn2.Count + ' 三层=' + $turn3.Count)
Write-Output ('  大小 ' + [math]::Round((Get-Item $outPath).Length / 1024, 1) + ' KB')
