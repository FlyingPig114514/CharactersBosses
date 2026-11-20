# make-markdown.ps1 -- 把 tools\wiki\data.json 生成「人类可读」的 Markdown 百科表。
#
# 输出：docs\wiki\boss-table.md  （GitHub 原生渲染成表格，不需要开 GitHub Pages）
#
# 结构与 Excel 版一致：一段一个设计（41 套），含
#   控制台指令 / 血量 / 能量 / 手牌 / 循环 / 卡组（合并计数）/ 遗物 / 特殊效果（普通 + 恐惧版）/
#   进阶与恐惧升级 / 逐回合「回合 · 打出 · 不出 · 备注」表格
#
# 用法：powershell -ExecutionPolicy Bypass -File tools\wiki\make-markdown.ps1

$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not (Test-Path (Join-Path $Root 'tools\wiki\data.json'))) { $Root = 'D:\Sts2ModProject\Downfall Expansion Vanilla Characters as Bosses' }

$dataPath = Join-Path $Root 'tools\wiki\data.json'
$outPath = Join-Path $Root 'docs\wiki\boss-table.md'

$data = ConvertFrom-Json ([System.IO.File]::ReadAllText($dataPath, [System.Text.Encoding]::UTF8))

# 角色基础血量（与 make-xlsx.ps1 / build-site.ps1 保持一致）
$StartHp = @{
    'IRONCLAD_BOSS' = 80; 'SILENT_BOSS' = 70; 'DEFECT_BOSS' = 75; 'NECROBINDER_BOSS' = 66
    'REGENT_BOSS' = 75; 'FOOL_BOSS' = 75; 'WATCHER_DIVINITY' = 72
}

function Get-Hp([string]$defKey, [int]$actNo, [int]$actBaseHp, [int]$firstBaseHp, [int]$variantBonus) {
    $start = 80
    if ($StartHp.ContainsKey($defKey)) { $start = $StartHp[$defKey] }
    $extra = 0
    if ($actBaseHp -gt $firstBaseHp) { $extra = $actBaseHp - $firstBaseHp }
    return $start + $extra + 120 + 180 * ($actNo - 1) + $variantBonus
}

# Markdown 转义：竖线会破坏表格，其余保持原样
function MdText([string]$s) {
    if ($null -eq $s) { return '' }
    $s = $s.Replace('|', '\|')
    $s = $s -replace '[\[\]]', ''
    return $s.Trim()
}

function Is-NotPlayed($c) {
    foreach ($f in @('Burned', 'HoldInstead', 'NotDrawn', 'DiscardInstead', 'DiscardInsteadWhenMultiplayer', 'DiscardInsteadSingleOnly', 'DisplayOnly')) {
        if ($c.PSObject.Properties.Name -contains $f) { return $true }
    }
    return $false
}

function Card-Label($c) {
    $label = [string]$c.name
    if ($c.PSObject.Properties.Name -contains 'Upgraded') { $label = $label + '+' }
    $cost = -1
    if ($c.PSObject.Properties.Name -contains 'CostOverride') { $cost = [int]$c.CostOverride }
    if ($c.PSObject.Properties.Name -contains 'CostThisTurn') { $cost = [int]$c.CostThisTurn }
    if ($cost -ge 0) { $label = $label + '（' + $cost + '费）' }
    if (($c.PSObject.Properties.Name -contains 'ExtraAoe') -and [int]$c.ExtraAoe -gt 0) { $label = $label + '〔群攻' + $c.ExtraAoe + '〕' }
    if (($c.PSObject.Properties.Name -contains 'DamageOverride') -and [int]$c.DamageOverride -gt 0) { $label = $label + '〔伤' + $c.DamageOverride + '〕' }
    if (($c.PSObject.Properties.Name -contains 'BlockOverride') -and [int]$c.BlockOverride -gt 0) { $label = $label + '〔格挡' + $c.BlockOverride + '〕' }
    if ($c.PSObject.Properties.Name -contains 'PrintsToDrawPile') { $label = $label + '〔印' + ($c.PrintsToDrawPile -join '/') + '〕' }
    if ($c.PSObject.Properties.Name -contains 'ReturnsToHand') { $label = $label + '〔回收' + ($c.ReturnsToHand -join '/') + '〕' }
    if ($c.PSObject.Properties.Name -contains 'ExhaustWith') { $label = $label + '〔消耗' + ($c.ExhaustWith -join '/') + '〕' }
    if ($c.PSObject.Properties.Name -contains 'Sly') { $label = $label + '〔奇巧〕' }
    if ($c.PSObject.Properties.Name -contains 'Retain') { $label = $label + '〔保留〕' }
    if ($c.PSObject.Properties.Name -contains 'AutoPlayed') { $label = $label + '〔自动〕' }
    if ($c.PSObject.Properties.Name -contains 'FreeThisTurn') { $label = $label + '〔免费〕' }
    if ($c.PSObject.Properties.Name -contains 'SpendsAllEnergy') { $label = $label + '〔吃掉全部能量〕' }
    return (MdText $label)
}

function Join-Cards($cards, [bool]$notPlayed) {
    if ($null -eq $cards) { return '' }
    $parts = @()
    foreach ($c in $cards) {
        if ((Is-NotPlayed $c) -ne $notPlayed) { continue }
        $parts += (Card-Label $c)
    }
    if ($parts.Count -eq 0) { return '—' }
    return ($parts -join '、')
}

function Merge-Counts($list) {
    if ($null -eq $list) { return '' }
    $order = @()
    $counts = @{}
    foreach ($x in $list) {
        if ([string]::IsNullOrEmpty($x)) { continue }
        if (-not $counts.ContainsKey($x)) { $counts[$x] = 0; $order += $x }
        $counts[$x] = $counts[$x] + 1
    }
    $parts = @()
    foreach ($k in $order) {
        if ($counts[$k] -gt 1) { $parts += ($k + ' ×' + $counts[$k]) } else { $parts += $k }
    }
    if ($parts.Count -eq 0) { return '—' }
    return (MdText ($parts -join '、'))
}

function Loop-Text($script) {
    if ($null -eq $script) { return '—' }
    if ($script.loopFrom -gt 0 -and $script.loopLength -gt 0) {
        $end = $script.loopFrom + $script.loopLength - 1
        return ('第 ' + $script.loopFrom + '~' + $end + ' 回合循环')
    }
    return '不循环（打完停在最后一回合）'
}

# ── 收集设计（按幕分组）─────────────────────────────────
$actNames = @{ 1 = '一层'; 2 = '二层'; 3 = '三层' }
$designs = @()

foreach ($boss in $data.bosses) {
    if ($boss.acts.Count -eq 0) { continue }
    $firstBaseHp = [int]$boss.acts[0].baseHp

    foreach ($act in $boss.acts) {
        $actNo = [int]$act.act
        if ($actNo -lt 1 -or $actNo -gt 3) { continue }

        # 主设计（空壳幕跳过：没有剧本也没有卡组）
        if (-not ($null -eq $act.script -and $act.deck.Count -eq 0)) {
            $designs += [pscustomobject]@{
                Act = $actNo; Char = $boss.character; Name = [string]$act.name; Key = [string]$boss.key
                Encounter = [string]$act.encounter; Hp = (Get-Hp $boss.key $actNo ([int]$act.baseHp) $firstBaseHp 0)
                Energy = $act.energy; Hand = $act.handSize; Kind = '主设计'
                Deck = $act.deck; Relics = $act.relics
                Special = $act.special; SpecialDread = $act.specialDread
                Extras = @(); Script = $act.script; Info = $boss.info
            }
        }

        foreach ($v in $act.variants) {
            $extras = @()
            foreach ($x in $v.ascensionUpgrades) { if ($x) { $extras += ('进阶 ' + $x) } }
            foreach ($x in $v.ascensionRelics) { if ($x) { $extras += ('进阶 ' + $x) } }
            foreach ($x in $v.dreadUpgrades) { if ($x) { $extras += ('恐惧升级 ' + $x) } }
            $designs += [pscustomobject]@{
                Act = $actNo; Char = $boss.character; Name = [string]$v.name; Key = [string]$v.key
                Encounter = [string]$v.encounter; Hp = (Get-Hp $boss.key $actNo 0 $firstBaseHp ([int]$v.hpBonus))
                Energy = $act.energy; Hand = $act.handSize; Kind = '变体'
                Deck = $v.deck; Relics = $v.relics
                Special = $v.special; SpecialDread = $v.specialDread
                Extras = $extras; Script = $v.script; Info = $boss.info
            }
        }
    }
}

# ── 写 Markdown ─────────────────────────────────────────
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('# 玩家角色当Boss · Boss 意图表（41 套设计）')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('> 本表**由脚本从模组本体自动导出**（`tools/wiki-dump` 反射编译好的模组 DLL 里的 BossDefs/BossScript），')
[void]$sb.AppendLine('> 和游戏里实际跑的那一份完全同源，不存在手抄过期。导出时间：**' + $data.generatedAt + '**（' + $data.generatedFrom + '）。')
[void]$sb.AppendLine('>')
[void]$sb.AppendLine('> **血量** = 角色基础血量 + 该幕额外 + 涅奥的祝福 120 + 每进一幕 180×(幕−1) + 变体额外')
[void]$sb.AppendLine('> （铁甲战士 80 / 静默猎手 70 / 故障机器人 75 / 亡灵契约师 66 / 储君 75 / 愚者 75 / 观者 72）')
[void]$sb.AppendLine('>')
[void]$sb.AppendLine('> **读法**：「打出」= 剧本排队要打的牌（顺序即真实出牌顺序）；「不出」= 被她自己烧掉 / 丢弃 / 保留没打 / 只显示意图的牌。')
[void]$sb.AppendLine('> 牌打到最后没费的那几张仍列在「打出」里，每回合的**备注**会写明是哪几张没费。')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('## 目录')
[void]$sb.AppendLine('')

foreach ($a in @(1, 2, 3)) {
    $inAct = @($designs | Where-Object { $_.Act -eq $a })
    if ($inAct.Count -eq 0) { continue }
    [void]$sb.AppendLine('**' + $actNames[$a] + '**（' + $inAct.Count + ' 套）：' + (($inAct | ForEach-Object { '[' + $_.Name + '](#' + $_.Encounter + ')' }) -join ' · '))
    [void]$sb.AppendLine('')
}

foreach ($a in @(1, 2, 3)) {
    $inAct = @($designs | Where-Object { $_.Act -eq $a })
    if ($inAct.Count -eq 0) { continue }

    [void]$sb.AppendLine('---')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('## ' + $actNames[$a] + '（' + $inAct.Count + ' 套）')
    [void]$sb.AppendLine('')

    foreach ($d in $inAct) {
        [void]$sb.AppendLine('<a id="' + $d.Encounter + '"></a>')
        [void]$sb.AppendLine('')
        [void]$sb.AppendLine('### ' + $d.Name + '　`' + $d.Key + '`')
        [void]$sb.AppendLine('')
        [void]$sb.AppendLine('- **角色 / 幕**：' + $d.Char + ' · 第 ' + $d.Act + ' 幕 · ' + $d.Kind)
        [void]$sb.AppendLine('- **控制台**：`fight ' + $d.Encounter + '_BOSS_ENCOUNTER`')
        [void]$sb.AppendLine('- **血量**：**' + $d.Hp + '** ｜ **能量**：' + $d.Energy + ' ｜ **手牌**：' + $d.Hand + ' ｜ **循环**：' + (Loop-Text $d.Script))
        if ($d.Info) {
            [void]$sb.AppendLine('- **能力统计**：' + (MdText ($d.Info -replace "`r?`n", ' ｜ ')))
        }
        [void]$sb.AppendLine('- **卡组**（' + $d.Deck.Count + ' 张）：' + (Merge-Counts $d.Deck))
        [void]$sb.AppendLine('- **遗物**：' + (Merge-Counts $d.Relics))
        if ($d.Extras.Count -gt 0) {
            [void]$sb.AppendLine('- **进阶 / 恐惧改动**：' + (MdText ($d.Extras -join '；')))
        }
        [void]$sb.AppendLine('')
        if ($d.Special) {
            [void]$sb.AppendLine('**特殊效果**')
            [void]$sb.AppendLine('')
            foreach ($line in (($d.Special -replace "`r`n", "`n") -split "`n")) {
                if ($line.Trim().Length -gt 0) { [void]$sb.AppendLine('> ' + (MdText $line)) }
            }
            [void]$sb.AppendLine('')
        }
        if ($d.SpecialDread) {
            [void]$sb.AppendLine('**恐惧模式加强**')
            [void]$sb.AppendLine('')
            foreach ($line in (($d.SpecialDread -replace "`r`n", "`n") -split "`n")) {
                if ($line.Trim().Length -gt 0) { [void]$sb.AppendLine('> ' + (MdText $line)) }
            }
            [void]$sb.AppendLine('')
        }

        if ($null -ne $d.Script) {
            [void]$sb.AppendLine('| 回合 | 打出 | 不出 | 备注 |')
            [void]$sb.AppendLine('|:---:|---|---|---|')
            $i = 0
            foreach ($turn in $d.Script.turns) {
                $i = $i + 1
                $note = [string]$turn.note
                $note = [regex]::Replace($note, '^\d+\.\s*', '')
                [void]$sb.AppendLine('| ' + $i + ' | ' + (Join-Cards $turn.cards $false) + ' | ' + (Join-Cards $turn.cards $true) + ' | ' + (MdText $note) + ' |')
            }
            [void]$sb.AppendLine('')
        }
    }
}

[void]$sb.AppendLine('---')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('## 说明')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('- 本文件由 `tools/wiki/make-markdown.ps1` 从 `tools/wiki/data.json` 生成，**请勿手改**；')
[void]$sb.AppendLine('  改模组代码后依次跑 `tools/wiki-dump`（导出 JSON）→ 本脚本（出表）→ `tools/wiki/build-site.ps1`（出网页）。')
[void]$sb.AppendLine('- 网页版（可搜索、可折叠、带卡组 chip）：`docs/wiki/index.html`；')
[void]$sb.AppendLine('  也可启用 GitHub Pages（Settings → Pages → main / docs）后访问 <https://flyingpig114514.github.io/CharactersBosses/>。')
[void]$sb.AppendLine('- Excel 版（总表 + 三层剧本 + 说明，便于筛选排序）：`玩家角色当Boss——Boss意图表.v2.xlsx`（由 `tools/wiki/make-xlsx.ps1` 生成）。')
[void]$sb.AppendLine('- 本表**不含**卡牌的具体数值（伤害/格挡/费用以游戏内卡面为准），只有剧本显式覆盖过的地方（如「遗传算法 16 格挡」）才带数字。')

$dir = Split-Path -Parent $outPath
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
[System.IO.File]::WriteAllText($outPath, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))

$lines = ($sb.ToString() -split "`n").Count
Write-Output ('OK: ' + $outPath)
Write-Output ('  设计 ' + $designs.Count + ' 套；Markdown ' + $lines + ' 行；' + [math]::Round((Get-Item $outPath).Length / 1024, 1) + ' KB')
