# apply-overrides.ps1 -- 把 overrides.json 的人工订正套到 wiki-dump 导出的数据上。
#
# 顺序：wiki-dump（反射导出 data.json）→ 本脚本（套订正，原地改写 data.json）→
#       make-markdown.ps1 / make-xlsx.ps1 / build-site.ps1（三份产物）
#
# 只依赖 Windows PowerShell 5.1。文件必须是 UTF-8 with BOM。
# 用法：powershell -ExecutionPolicy Bypass -File tools\wiki\apply-overrides.ps1

$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not (Test-Path (Join-Path $Root 'tools\wiki\data.json'))) { $Root = 'D:\Sts2ModProject\Downfall Expansion Vanilla Characters as Bosses' }
$dataPath = Join-Path $Root 'tools\wiki\data.json'
$ovPath = Join-Path $Root 'tools\wiki\overrides.json'

$utf8 = New-Object System.Text.UTF8Encoding($false)
$data = ConvertFrom-Json ([System.IO.File]::ReadAllText($dataPath, [System.Text.Encoding]::UTF8))
$ov = ConvertFrom-Json ([System.IO.File]::ReadAllText($ovPath, [System.Text.Encoding]::UTF8))

# ── 通用：清掉 [gold]/[red]/[img] 这类标记，能量图标换成「能量」 ──
function Clean-Markup([string]$s) {
    if ($null -eq $s -or $s.Length -eq 0) { return $s }
    # 连续的能量图标：N 个 ⇒ 「N 能量」，1 个 ⇒ 「能量」
    $icon = '\[img\]res://images/packed/sprite_fonts/[a-z_]*energy_icon\.png\[/img\]'
    $s = [regex]::Replace($s, "($icon)+", {
        param($m)
        $n = ([regex]::Matches($m.Value, '\[' + 'img\]')).Count
        if ($n -le 1) { return '能量' } else { return ($n.ToString() + '能量') }
    })
    $s = [regex]::Replace($s, '\[/?img\]', '')
    # 其它命名标记（gold / red / blue / purple / orange …）只去标签留文字
    $s = [regex]::Replace($s, '\[/?(gold|red|blue|purple|orange|green|white|grey|gray|jade|light_blue|dark_red)\]', '')
    return $s
}

# ── 卡组条目工具 ──
function Split-DeckLabel([string]$entry) {
    # "打击+（漩涡）" ⇒ @{ Base = "打击"; Suffix = "+（漩涡）" }
    $idx = $entry.IndexOfAny([char[]]@('+', '（'))
    if ($idx -lt 0) { return @{ Base = $entry; Suffix = '' } }
    return @{ Base = $entry.Substring(0, $idx); Suffix = $entry.Substring($idx) }
}

function Remove-FromDeck($deck, [string]$spec) {
    # spec: "电击" / "电击*2" / "灵魂*"
    $count = 1
    $name = $spec
    if ($spec -match '^(.*)\*(\d+)$') { $name = $Matches[1]; $count = [int]$Matches[2] }
    if ($spec -match '^(.*)\*$') { $name = $Matches[1]; $count = -1 }   # -1 = 删所有以此为前缀的
    $out = New-Object System.Collections.ArrayList
    $removed = 0
    foreach ($e in @($deck)) {
        $base = (Split-DeckLabel ([string]$e)).Base
        $hit = $false
        if ($count -lt 0) { if ($base.StartsWith($name)) { $hit = $true } }
        elseif ($base -eq $name -and $removed -lt $count) { $hit = $true }
        if ($hit) { $removed++; continue }
        [void]$out.Add([string]$e)
    }
    return @{ Deck = @($out); Removed = $removed }
}

# ── 逐设计套订正 ──
$applied = New-Object System.Collections.ArrayList

function Apply-Design($design, $rule, [string]$label) {
    if ($null -eq $rule) { return }
    if ($null -eq $design) { return }
    $notes = New-Object System.Collections.ArrayList

    # 卡组
    if ($rule.deck) {
        if ($rule.deck.raw) {
            $design.deck = @($rule.deck.raw | ForEach-Object { [string]$_ })
            [void]$notes.Add('卡组整份替换')
        } else {
            if ($rule.deck.remove) {
                foreach ($spec in @($rule.deck.remove)) {
                    $r = Remove-FromDeck $design.deck $spec
                    $design.deck = $r.Deck
                    [void]$notes.Add("删 $spec → 实际删 $($r.Removed) 张")
                }
            }
            if ($rule.deck.add) {
                $list = New-Object System.Collections.ArrayList
                foreach ($e in @($design.deck)) { [void]$list.Add([string]$e) }
                foreach ($e in @($rule.deck.add)) { [void]$list.Add([string]$e) }
                $design.deck = @($list)
                [void]$notes.Add("加 $((@($rule.deck.add)) -join '、')")
            }
        }
    }

    if ($null -ne $rule.firstShuffleTurn -and $null -ne $design.script) {
        $design.script.shuffleTurn = [int]$rule.firstShuffleTurn
        $design.script.shuffleGuessed = $false
        [void]$notes.Add("首次洗牌 = 第 $($rule.firstShuffleTurn) 回合")
    }
    if ($null -ne $rule.loopFrom -and $null -ne $design.script) { $design.script.loopFrom = [int]$rule.loopFrom }
    if ($null -ne $rule.loopLength -and $null -ne $design.script) { $design.script.loopLength = [int]$rule.loopLength }
    if ($rule.special) { $design.special = Clean-Markup ([string]$rule.special) }
    if ($rule.appendSpecial) {
        $add = (@($rule.appendSpecial) | ForEach-Object { Clean-Markup ([string]$_) }) -join "`n"
        $design.special = (Clean-Markup ([string]$design.special)) + "`n" + $add
        [void]$notes.Add('特殊效果追加说明')
    }
    if ($rule.specialDread) { $design.specialDread = Clean-Markup ([string]$rule.specialDread) }
    if ($rule.deckNote) { $design.deckNote = [string]$rule.deckNote }

    # 逐回合订正
    if ($rule.turns -and $null -ne $design.script) {
        foreach ($prop in $rule.turns.PSObject.Properties) {
            $turnNo = [int]$prop.Name
            $fix = $prop.Value
            if ($turnNo -lt 1 -or $turnNo -gt @($design.script.turns).Count) {
                [void]$notes.Add("!! 第 $turnNo 回合不存在")
                continue
            }
            $turn = $design.script.turns[$turnNo - 1]
            if ($fix.played) {
                $newCards = New-Object System.Collections.ArrayList
                foreach ($nm in @($fix.played)) { [void]$newCards.Add([pscustomobject]@{ name = [string]$nm; type = ''; cost = 0; played = $true; fromNote = $true }) }
                if ($fix.notPlayed) {
                    foreach ($nm in @($fix.notPlayed)) {
                        $reason = '不出'
                        $n = [string]$nm
                        if ($n -match '^(.*)（(.*)）$') { $n = $Matches[1]; $reason = $Matches[2] }
                        [void]$newCards.Add([pscustomobject]@{ name = $n; type = ''; cost = 0; played = $false; reason = $reason; fromNote = $true })
                    }
                }
                $turn.cards = @($newCards)
                [void]$notes.Add("第 $turnNo 回合打出/不出已重写")
            }
            if ($fix.reorder) {
                $order = @($fix.reorder)
                $cards = @($turn.cards)
                $head = @($cards | Where-Object { $order -contains $_.name })
                $rest = @($cards | Where-Object { $order -notcontains $_.name })
                $sorted = @()
                foreach ($nm in $order) {
                    foreach ($c in $head) { if ($c.name -eq $nm) { $sorted += $c } }
                }
                $turn.cards = @($sorted + $rest)
                [void]$notes.Add("第 $turnNo 回合已按指定顺序重排")
            }
            if ($fix.note) { $turn.note = [string]$fix.note; [void]$notes.Add("第 $turnNo 回合备注已替换") }
        }
    }

    if ($notes.Count -gt 0) { [void]$applied.Add(("{0}: {1}" -f $label, ($notes -join '; '))) }
}

$designCount = 0
foreach ($boss in $data.bosses) {
    foreach ($act in $boss.acts) {
        if ($act.act -gt 3) { continue }
        $rule = $null
        if ($ov.designs.PSObject.Properties.Name -contains $act.encounter) { $rule = $ov.designs.($act.encounter) }
        if ($null -ne $rule) { Apply-Design $act $rule ([string]$act.name); $designCount++ }

        foreach ($v in $act.variants) {
            $vrule = $null
            if ($ov.designs.PSObject.Properties.Name -contains $v.encounter) { $vrule = $ov.designs.($v.encounter) }
            if ($null -ne $vrule) { Apply-Design $v $vrule ([string]$v.name); $designCount++ }
        }
    }
}

# ── 通用规则：删牌 / 一律算打出 ──
$removedTotal = 0
$forcedTotal = 0
foreach ($boss in $data.bosses) {
    foreach ($act in $boss.acts) {
        if ($act.act -gt 3) { continue }
        $all = @($act) + @($act.variants)
        foreach ($d in $all) {
            if ($null -eq $d) { continue }
            # ① 卡组里删掉通用牌（君王之剑是生成的）
            foreach ($spec in @($ov.global.removeCards)) {
                $r = Remove-FromDeck $d.deck $spec
                $d.deck = $r.Deck
                $removedTotal = $removedTotal + $r.Removed
            }
            # ② 特殊干净化
            if ($d.special) { $d.special = Clean-Markup ([string]$d.special) }
            if ($d.specialDread) { $d.specialDread = Clean-Markup ([string]$d.specialDread) }
            # ③ 一律算打出的牌（回合结束自动打出也算）
            if ($null -ne $d.script) {
                foreach ($turn in @($d.script.turns)) {
                    foreach ($c in @($turn.cards)) {
                        if (@($ov.global.alwaysPlayed) -contains $c.name) {
                            if (-not $c.played) { $c.played = $true; $forcedTotal++ }
                            if ($c.PSObject.Properties.Name -contains 'reason') { $c.PSObject.Properties.Remove('reason') }
                        }
                    }
                }
            }
        }
    }
}


# ── 编号：不再叫「主设计 / 变体」，同一角色内按「幕升序 → 主设计在前 → 变体在后」编成 1号 / 2号 / 3号… ──
$numApplied = 0
foreach ($boss in $data.bosses) {
    foreach ($act in @($boss.acts)) {
        if ($act.act -gt 3) { continue }
        $slot = 0
        $slot = $slot + 1
        $act | Add-Member -NotePropertyName numberLabel -NotePropertyValue (([string]$act.act) + '层' + $slot + '号') -Force
        $numApplied++
        foreach ($v in @($act.variants)) {
            $slot = $slot + 1
            $v | Add-Member -NotePropertyName numberLabel -NotePropertyValue (([string]$act.act) + '层' + $slot + '号') -Force
            $numApplied++
        }
    }
}
Write-Output ("  编号已写入 {0} 个设计" -f $numApplied)
# ⚠ PowerShell 5.1 的 ConvertTo-Json 会把中文写成 \uXXXX 转义（体积膨胀 4 倍、网页内联数据也跟着变大）
#   ⇒ 写盘前把转义还原成真正的 UTF-8 字符。
$json = ConvertTo-Json $data -Depth 24 -Compress   # 紧凑输出：ConvertTo-Json 的缩进会把它撑到 4 倍大
$json = [regex]::Replace($json, '\\u([0-9a-fA-F]{4})', { param($m) [string][char][Convert]::ToInt32($m.Groups[1].Value, 16) })
[System.IO.File]::WriteAllText($dataPath, $json, $utf8)
Write-Output ("OK 订正已套用 → " + $dataPath)
Write-Output ("  设计命中 {0} 个；通用删牌 {1} 张；强制算打出 {2} 张" -f $designCount, $removedTotal, $forcedTotal)
foreach ($line in $applied) { Write-Output ("  · " + $line) }
