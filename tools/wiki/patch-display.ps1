# 一次性补丁：让三份产物符合用户 2026-11-20 的显示口径
#   ① 去掉「代码登记卡组」
#   ② 卡组括号里只留张数
#   ③ 不再区分「主设计 / 变体」，改成 1号 / 2号 / 3号
$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$utf8 = New-Object System.Text.UTF8Encoding($true)

# ── xlsx ──────────────────────────────────────────────
$xlsxPath = Join-Path $Root 'tools\wiki\make-xlsx.ps1'
$x = [System.IO.File]::ReadAllText($xlsxPath, [System.Text.Encoding]::UTF8)

$oldHead = @'
@('楼层', '角色', '设计', 'Key', '控制台指令', '血量', '能量', '手牌', '循环', '卡组', '代码登记卡组', '遗物', '特殊效果', '恐惧版特殊效果', '进阶/恐惧改动', '能力统计')
'@
$newHead = @'
@('楼层', '编号', '角色', '设计', 'Key', '控制台指令', '血量', '能量', '手牌', '循环', '卡组', '遗物', '特殊效果', '恐惧版特殊效果', '进阶/恐惧改动', '能力统计')
'@
if ($x.Contains($oldHead)) { $x = $x.Replace($oldHead, $newHead); Write-Output 'xlsx 表头已改' } else { Write-Output 'xlsx 表头锚点未命中' }

$oldMainRow = @'
            (Deck-Cell $act.deck $act.deckNote), (Merge-Counts $act.deckRegistered), (Merge-Counts $act.relics),
'@
$newMainRow = @'
            [string]$act.numberLabel, (Merge-Counts $act.deck), (Merge-Counts $act.relics),
'@
if ($x.Contains($oldMainRow)) { $x = $x.Replace($oldMainRow, $newMainRow); Write-Output 'xlsx 主设计行已改' } else { Write-Output 'xlsx 主设计行锚点未命中' }

$oldVarRow = @'
                (Deck-Cell $v.deck $v.deckNote), (Merge-Counts $v.deckRegistered), (Merge-Counts $v.relics),
'@
$newVarRow = @'
                [string]$v.numberLabel, (Merge-Counts $v.deck), (Merge-Counts $v.relics),
'@
if ($x.Contains($oldVarRow)) { $x = $x.Replace($oldVarRow, $newVarRow); Write-Output 'xlsx 变体行已改' } else { Write-Output 'xlsx 变体行锚点未命中' }

$oldWidth = @'
$sheetXml['总表'] = Sheet-Xml $rowsSum @(6, 10, 26, 24, 42, 8, 6, 6, 26, 62, 52, 34, 66, 66, 44, 50)
'@
$newWidth = @'
$sheetXml['总表'] = Sheet-Xml $rowsSum @(6, 6, 10, 26, 24, 42, 8, 6, 6, 26, 62, 34, 66, 66, 44, 50)
'@
if ($x.Contains($oldWidth)) { $x = $x.Replace($oldWidth, $newWidth); Write-Output 'xlsx 列宽已改' } else { Write-Output 'xlsx 列宽锚点未命中' }

[System.IO.File]::WriteAllText($xlsxPath, $x, $utf8)

# ── 网页模板 ──────────────────────────────────────────
$tplPath = Join-Path $Root 'tools\wiki\template.html'
$h = [System.IO.File]::ReadAllText($tplPath, [System.Text.Encoding]::UTF8)

$oldReg = @'
      if (design.deckRegistered && design.deckRegistered.length &&
          design.deckRegistered.slice().sort().join('|') !== design.deck.slice().sort().join('|')) {
        h.push('</div><div class="sub2">代码登记卡组（BossDefs，可能已过期，仅供对照）：</div><div class="chips">');
        h.push(plainChipsHTML(design.deckRegistered, 'card dim2'));
      }
'@
if ($h.Contains($oldReg)) { $h = $h.Replace($oldReg, ''); Write-Output '网页：对照区块已删' } else { Write-Output '网页：对照区块锚点未命中' }

$oldNote = @'
      h.push('<div class="sect"><h3>卡组<span class="cnt">' + design.deck.length + ' 张</span>' +
        (design.deckNote ? '<span class="cnt dim">' + esc(design.deckNote) + '</span>' : '') + '</h3><div class="chips">');
'@
$newNote = @'
      h.push('<div class="sect"><h3>卡组<span class="cnt">' + design.deck.length + ' 张</span></h3><div class="chips">');
'@
if ($h.Contains($oldNote)) { $h = $h.Replace($oldNote, $newNote); Write-Output '网页：卡组口径已删' } else { Write-Output '网页：卡组口径锚点未命中' }

$oldBadge = @'
    var meta = design.character + ' · 第 ' + design.act + ' 幕'
      + (design.isVariant ? '<span class="badge">变体</span>' : '<span class="badge">主设计</span>');
'@
$newBadge = @'
    var meta = design.character + ' · 第 ' + design.act + ' 幕'
      + '<span class="badge">' + esc(design.numberLabel || (design.isVariant ? '变体' : '主设计')) + '</span>';
'@
if ($h.Contains($oldBadge)) { $h = $h.Replace($oldBadge, $newBadge); Write-Output '网页：编号徽标已改' } else { Write-Output '网页：编号徽标锚点未命中' }

$oldDeckField = @'
    deck: src.deck || [],
'@
$newDeckField = @'
    deck: src.deck || [],
    numberLabel: src.numberLabel || '',
'@
if ($h.Contains($oldDeckField)) { $h = $h.Replace($oldDeckField, $newDeckField); Write-Output '网页：numberLabel 字段已加' } else { Write-Output '网页：numberLabel 锚点未命中' }

[System.IO.File]::WriteAllText($tplPath, $h, $utf8)
Write-Output '补丁完成'
