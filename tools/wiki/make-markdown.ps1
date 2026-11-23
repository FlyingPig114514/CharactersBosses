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
# 本地化标记清洗（中英两版共用逻辑）：
#   [gold]真言[/gold] / gold真言/gold  →  **真言**
#   [red]…[/red] / red…/red            →  **…**
#   imgres://xxx.png/img              →  删掉（markdown 里画不出图标）
#   其余 [tag]…[/tag]                 →  **…**
# ⚠ 必须在 MdText 去方括号**之前**调用，否则会变成「gold真言/gold」这种残渣。
function CleanLoc([string]$s) {
    if ([string]::IsNullOrEmpty($s)) { return $s }
    # 先按**字面**把精灵字体内联图标换掉（正则容易把它连着的 img 标签一起吞掉）
    $r = $s
    # ★ 2026-11-23：「数字 + 能量图标」连写（例如「你在下回合失去1[img]⚡[/img]」）时，
    #   如果只把图标换成「1 点能量」就会变成「失去11 点能量」（疾风观的特殊效果就是这样）。
    #   所以先把「数字+图标」整段换成「N 点能量」。
    $r = [regex]::Replace($r, '(\d+)\s*\[img\]res://[^\[]*energy_icon[^\[]*\[/img\]', '$1 点能量')
    $r = $r.Replace('[img]res://images/packed/sprite_fonts/watcher_energy_icon.png[/img]', '1 点能量')
    $r = $r.Replace('res://images/packed/sprite_fonts/watcher_energy_icon.png', '')
    $r = [regex]::Replace($r, '(1 点能量)+', '1 点能量')     # 图标连写多次 ⇒ 收成一个
    $r = $r.Replace('1点能量', '获得 1 点能量')
    foreach ($tag in @('gold','red','green','blue','purple','orange','yellow')) {
        $r = [regex]::Replace($r, '\[' + $tag + '\](.*?)\[/' + $tag + '\]', '**$1**')
        $r = [regex]::Replace($r, '(?<![\w/])' + $tag + '([^/\s\[\]]{1,60})/' + $tag, '**$1**')
    }
    $r = [regex]::Replace($r, 'img\s*res://[^\s]*(energy_icon|energy)[^\s]*?/img', '1 点能量')
    $r = [regex]::Replace($r, '\s*\[?img\]?\s*res://[^\s\]]*\s*\[?/?img\]?', '')
    $r = [regex]::Replace($r, 'img\s*res://[^\s]*(energy_icon|energy)[^\s]*?/img', '1 点能量')
    $r = [regex]::Replace($r, 'img\s*res://[^\s]*?/img', '')
    # 内联图标（精灵字体）换成文字：观者能量图标 = 1 点能量
    $r = [regex]::Replace($r, '\s*img\s*res://[^\s\]]*(energy_icon|energy)[^\s\]]*\.png\s*/img', '1 点能量')
    $r = [regex]::Replace($r, '\[/?(gold|red|green|blue|purple|orange|yellow|img)\]', '')
    $r = $r.Replace('[img]res://images/packed/sprite_fonts/watcher_energy_icon.png[/img]', '1 点能量')
    $r = $r.Replace('res://images/packed/sprite_fonts/watcher_energy_icon.png', '')
    $r = [regex]::Replace($r, '(1 点能量)+', '1 点能量')     # 图标连写多次 ⇒ 收成一个
    $r = $r.Replace('1点能量', '获得 1 点能量')
    return $r
}
function MdText([string]$s) {
    if ($null -eq $s) { return '''' }
    $s = CleanLoc $s
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

# 这一回合**真打出来**的牌：数据导出器已经按「队列顺序 + 每张牌实际费用 + 该回合能量」算好了 played。
function Played-Cards($cards) {
    if ($null -eq $cards) { return '—' }
    $parts = @()
    foreach ($c in $cards) {
        if (-not (Test-Played $c)) { continue }
        $parts += (Card-Label $c)
    }
    if ($parts.Count -eq 0) { return '—' }
    return ($parts -join '、')
}

# 这一回合**没打**的牌：没费 / 被烧掉 / 被丢弃 / 留在手里 / 仅显示意图
function NotPlayed-Cards($cards) {
    if ($null -eq $cards) { return '—' }
    $parts = @()
    foreach ($c in $cards) {
        if (Test-Played $c) { continue }
        $reason = ''
        if ($c.PSObject.Properties.Name -contains 'reason') { $reason = [string]$c.reason }
        if ($reason.Length -gt 0) { $parts += ((Card-Label $c) + '（' + $reason + '）') }
        else { $parts += (Card-Label $c) }
    }
    if ($parts.Count -eq 0) { return '—' }
    return ($parts -join '、')
}

# 老数据没有 played 字段时退回按标记判断，保证脚本不会崩
function Test-Played($c) {
    if ($c.PSObject.Properties.Name -contains 'played') { return [bool]$c.played }
    return -not (Is-NotPlayed $c)
}

# 备注只在「除了牌名之外还有额外信息」时才写（用户口径：一般情况下备注是空的，主要看前两栏）
function Note-IfUseful([string]$note, $cards) {
    if ([string]::IsNullOrWhiteSpace($note)) { return '—' }
    $clean = [regex]::Replace($note, '^\d+\.\s*', '').Trim()
    $probe = $clean
    if ($null -ne $cards) {
        foreach ($c in $cards) {
            $nm = [string]$c.name
            if ($nm.Length -ge 2) { $probe = $probe.Replace($nm, '') }
        }
    }
    # 去掉括号里的补充说明（「（群攻）」「（被坚毅烧掉）」这种已经体现在前两栏了）
    $probe = [regex]::Replace($probe, '[（(【\[][^）)】\]]*[）)】\]]', '')
    $probe = [regex]::Replace($probe, '[\s\p{P}\p{S}0-9]', '')
    # 剩下的还是有信息量（≥4 个字），或者出现了「洗牌 / 循环 / 额外回合」这类关键提示 ⇒ 保留备注
    $keep = ($probe.Length -ge 4)
    if (-not $keep) {
        foreach ($kw in @('洗牌', '循环', '额外回合', '重打', '复制', '进阶', '恐惧', '召唤', '生成', '升级')) {
            if ($clean.Contains($kw)) { $keep = $true; break }
        }
    }
    if (-not $keep) { return '—' }
    return (MdText $clean)
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
            # ★ v0.2.874（用户 2026-11-22：「wiki 上好像没有写进阶增强」）：
            #   以前这里 Extras 恒为空 —— wiki-dump 只导出了**变体**那两张进阶表，
            #   于是黑球机 / 愤怒战 / 灾厄骨 / 碎屑储… 这些主设计的进阶升级与额外遗物在 wiki 上一个字都没有。
            #   现在 dump 也导出了按幕的两张表，这里照变体那套拼进同一行。
            $mainExtras = @()
            foreach ($x in $act.ascensionUpgrades) { if ($x) { $mainExtras += ('进阶 ' + $x) } }
            foreach ($x in $act.ascensionRelics) { if ($x) { $mainExtras += ('进阶 ' + $x) } }
            foreach ($x in $act.dreadUpgrades) { if ($x) { $mainExtras += ('恐惧升级 ' + $x) } }
            foreach ($x in $act.ascensionDisplay) { foreach ($part in ([string]$x -split ',')) { if ($part.Trim()) { $mainExtras += ('进阶 ' + $part.Trim()) } } }
            $designs += [pscustomobject]@{
                Act = $actNo; Char = $boss.character; Name = [string]$act.name; Key = [string]$boss.key
                Encounter = [string]$act.encounter; Hp = (Get-Hp $boss.key $actNo ([int]$act.baseHp) $firstBaseHp 0)
                Energy = $act.energy; Hand = $act.handSize; Kind = '主设计'; NumberLabel = [string]$act.numberLabel; DeckExtraNote = [string]$act.deckNote
                Deck = $act.deck; Relics = $act.relics
                DeckNote = $act.deckNote; DeckRegistered = $act.deckRegistered
                Special = $act.special; SpecialDread = $act.specialDread
                Extras = $mainExtras; Script = $act.script; Info = $(if ($act.info) { [string]$act.info } else { [string]$boss.info })
            }
        }

        foreach ($v in $act.variants) {
            $extras = @()
            foreach ($x in $v.ascensionUpgrades) { if ($x -and ($x -notmatch ':\s*$')) { $extras += ('进阶 ' + $x) } }
            foreach ($x in $v.ascensionRelics) { if ($x -and ($x -notmatch ':\s*$')) { $extras += ('进阶 ' + $x) } }
            foreach ($x in $v.ascensionDisplay) { foreach ($part in ([string]$x -split ',')) { if ($part.Trim()) { $extras += ('进阶 ' + $part.Trim()) } } }
            foreach ($x in $v.dreadUpgrades) { if ($x) { $extras += ('恐惧升级 ' + $x) } }
            $designs += [pscustomobject]@{
                Act = $actNo; Char = $boss.character; Name = [string]$v.name; Key = [string]$v.key
                Encounter = [string]$v.encounter; Hp = (Get-Hp $boss.key $actNo 0 $firstBaseHp ([int]$v.hpBonus))
                Energy = $act.energy; Hand = $act.handSize; Kind = '变体'; NumberLabel = [string]$v.numberLabel; DeckExtraNote = [string]$v.deckNote
                Deck = $v.deck; Relics = $v.relics
                DeckNote = $v.deckNote; DeckRegistered = $v.deckRegistered
                Special = $v.special; SpecialDread = $v.specialDread
                Extras = $extras; Script = $v.script; Info = $(if ($v.info) { [string]$v.info } else { [string]$boss.info })
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

# 用户 2026-11-21：「在目录那里写上不同角色的支配（有的支配要分 1、2、3 层，有的不用）」
[void]$sb.AppendLine('**支配**（恐惧模式）：[各角色的支配](#dominions)')
[void]$sb.AppendLine('')

# ★ 用户 2026-11-22：「github 的 wiki 上好像没有写进阶增强」⇒ 目录里加一条，并单独出一节
[void]$sb.AppendLine('**进阶**：[进阶增强一览](#ascension)')
[void]$sb.AppendLine('')

# ── 进阶增强一览（从各设计的 ascensionUpgrades / ascensionRelics 聚合）─────
[void]$sb.AppendLine('<a id="ascension"></a>')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('## 进阶增强（进阶 8 / 9）')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('本模组的 Boss 在**进阶 8 / 进阶 9** 会有额外增强（和原版 Boss「进阶只加血」不是一个路子）：')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('- **升级牌**：卡组与剧本里点名的牌在该档变成**升级版**（后面带「+」），其余牌不变；')
[void]$sb.AppendLine('- **额外遗物**：部分 Boss 会**多一件遗物**（下表里「牌名」与「遗物名」混排，遗物就是多出来的那件）；')
[void]$sb.AppendLine('- **召唤物血量**：Boss 召出来的仆从（奥斯提 / 子嗣 / 仆从储的那几只等）**进阶 8 起**改用更高的血量档；')
[void]$sb.AppendLine('- **Boss 本体的血量不随进阶变化** —— 只按「幕」+ 涅奥的祝福算（见开头的血量公式）。')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('| 幕 | Boss | 进阶 8 | 进阶 9 | 其它档位 |')
[void]$sb.AppendLine('|:---:|---|---|---|---|')

$ascRows = @()
# ★ v0.2.874：观者那 6 套 + 炼药猎的进阶已经**搬进 `BossVariant.AscensionUpgrades` 表**（单一数据源），
#   所以这里跟其它设计一样纯自动导出 —— 不再有手抄表。
foreach ($d in $designs) {
    $byLevel = @{}
    foreach ($e in @($d.Extras)) {
        $m = [regex]::Match([string]$e, '^进阶\s*(\d+)\s*[:：]\s*(.+)$')
        if (-not $m.Success) { continue }
        $lv = [string]$m.Groups[1].Value
        $val = [string]$m.Groups[2].Value
        if (-not $byLevel.ContainsKey($lv)) { $byLevel[$lv] = @() }
        $byLevel[$lv] = @($byLevel[$lv]) + $val
    }

    # ★ v0.2.874（用户：「这些 boss 我肯定都设计进阶升级了」——确实有）：
    #   炼药猎 + 6 只观者在 dump 进程里拿不到**结构化剧本卡**（观者那几套要等观者 mod 的类型解析、
    #   炼药猎的剧本在静态初始化期也没铺开）⇒ dump 退回按「备注文本」拆牌，那些卡上没有
    #   `UpgradeMinAscension` 字段。但剧本备注里本来就写着「（进阶8起升级）」「（进阶9不升）」，
    #   这里按同一条口径把它们回收进来（与结构化那条路合并去重）。
    if ($null -ne $d.Script) {
        foreach ($t in @($d.Script.turns)) {
            $note = [string]$t.note
            if (-not $note) { continue }
            foreach ($m2 in [regex]::Matches($note, '([^、，,；;→\s（）()]+)\s*（进阶\s*(\d+)\s*起?\s*(升级|不升)）')) {
                if ($m2.Groups[3].Value -eq '不升') { continue }
                $cardName = $m2.Groups[1].Value.Trim()
                if (-not $cardName) { continue }
                $lv2 = [string]$m2.Groups[2].Value
                if (-not $byLevel.ContainsKey($lv2)) { $byLevel[$lv2] = @() }
                if (@($byLevel[$lv2]) -notcontains $cardName) { $byLevel[$lv2] = @($byLevel[$lv2]) + $cardName }
            }
        }
    }

    # ★ v0.2.875：神格观是 `Watcher` **主定义**（不是变体），而 dump 进程里观者卡类型全是 null
    #   ⇒ 按幕表的条目被 null 过滤掉、`AscensionDisplay` 也没跟着主设计导出来。
    #   这里只给这一只补上（数据本体在 BossDefs 的 AscensionUpgradesByAct / AscensionRelicsByAct）。
    if ([string]$d.Name -eq '神格观' -and $byLevel.Count -eq 0) {
        $byLevel['8'] = @('警惕', '旋身', '欺瞒现实')
        $byLevel['9'] = @('手摇鼓')
    }

    if ($byLevel.Count -eq 0) { continue }

    $other = @()
    foreach ($k in @($byLevel.Keys | Sort-Object)) {
        if ($k -eq '8' -or $k -eq '9') { continue }
        $other += ('进阶 ' + $k + '：' + (@($byLevel[$k]) -join '、'))
    }

    $ascRows += [pscustomobject]@{
        Act   = $d.Act
        Name  = $d.Name
        Lv8   = ((@($byLevel['8']) | Where-Object { $_ } | Select-Object -Unique) -join '、')
        Lv9   = ((@($byLevel['9']) | Where-Object { $_ } | Select-Object -Unique) -join '、')
        Other = ($other -join '；')
    }
}
foreach ($r in $ascRows) {
    [void]$sb.AppendLine('| ' + $actNames[$r.Act] + ' | ' + (MdText $r.Name) + ' | ' + (MdText $r.Lv8) + ' | ' + (MdText $r.Lv9) + ' | ' + (MdText $r.Other) + ' |')
}
[void]$sb.AppendLine('')
[void]$sb.AppendLine('> 表里空白 = 这只 Boss 在该档**没有**额外升级/遗物。每只 Boss 的详细升级名单也写在它自己那一节里')
[void]$sb.AppendLine('> （「**进阶 / 恐惧改动**」一行），两处同源（同一次 `tools/wiki-dump` 导出）。')
[void]$sb.AppendLine('')

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
        [void]$sb.AppendLine('- **角色 / 幕**：' + $d.Char + ' · 第 ' + $d.Act + ' 幕 · ' + $d.NumberLabel)
        [void]$sb.AppendLine('- **控制台**：`fight ' + $d.Encounter + '_BOSS_ENCOUNTER`')
        [void]$sb.AppendLine('- **血量**：**' + $d.Hp + '** ｜ **能量**：' + $d.Energy + ' ｜ **手牌**：' + $d.Hand + ' ｜ **循环**：' + (Loop-Text $d.Script))
        if ($d.Info) {
        # ★ v0.2.883：能力面板按「标签：值」渲染成两列表（与设置界面同一份数据）
        if ($d.Info) {
            [void]$sb.AppendLine('**能力面板**')
            [void]$sb.AppendLine('')
            [void]$sb.AppendLine('| 项目 | 值 |')
            [void]$sb.AppendLine('|---|---|')
            foreach ($infoLine in ((([string]$d.Info) -replace "`r`n", "`n") -split "`n")) {
                if (-not $infoLine.Trim()) { continue }
                $sep = $infoLine.IndexOf(':')
                if ($sep -lt 0) { $sep = $infoLine.IndexOf([char]0xFF1A) }
                if ($sep -gt 0) {
                    [void]$sb.AppendLine('| ' + (MdText $infoLine.Substring(0, $sep)) + ' | ' + (MdText $infoLine.Substring($sep + 1)) + ' |')
                } else {
                    [void]$sb.AppendLine('| ' + (MdText $infoLine.Trim()) + ' | |')
                }
            }
            [void]$sb.AppendLine('')
        }
        }
        # 用户 2026-11-20：卡组括号里只保留张数，不要口径说明；也不再显示「代码登记卡组」对照
        [void]$sb.AppendLine('- **卡组**（' + $d.Deck.Count + ' 张）：' + (Merge-Counts $d.Deck))
        if ($d.PSObject.Properties.Name -contains 'DeckExtraNote' -and $d.DeckExtraNote) {
            [void]$sb.AppendLine('- **卡组备注**：' + (MdText ([string]$d.DeckExtraNote)))
        }
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
                $energyTxt = ' | '   # 能量已经在表头外统一说明；这里保持列数一致
                [void]$sb.AppendLine('| ' + $i + ' | ' + (Played-Cards $turn.cards) + ' | ' + (NotPlayed-Cards $turn.cards) + ' | ' + (Note-IfUseful ([string]$turn.note) $turn.cards) + ' |')
            }
            [void]$sb.AppendLine('')
        }
    }
}

[void]$sb.AppendLine('---')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('<a id="dominions"></a>')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('## 各角色的支配（恐惧模式）')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('恐惧模式下，每一幕的「支配」按**本幕主 Boss 的角色**挂；只在本幕的**普通房间**生效（Boss 房里没有支配）。')
[void]$sb.AppendLine('有的支配**按幕分层**（第 1/2/3 幕效果不同），有的**每幕一样**。')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('| 角色 | 支配效果 | 是否按幕分层 |')
[void]$sb.AppendLine('|---|---|---|')
[void]$sb.AppendLine('| 铁甲战士 | 每场战斗开始时，所有敌人获得 **1 层**「恶魔形态」 | 不分层 |')
[void]$sb.AppendLine('| 静默猎手 | 每场战斗开始时，你失去 **1 / 2 / 3** 点力量，并获得 **1 / 2 / 3** 层虚弱 | **分 1/2/3 层** |')
[void]$sb.AppendLine('| 故障机器人 | 所有敌人获得 **1 / 2 / 3** 层人工制品（**额外叠加**在敌人自带的人工制品之上；**多人时按人数增加**：2 人 +1、3 人 +2） | **分 1/2/3 层** |')
[void]$sb.AppendLine('| 储君 | 你的回合开始时，把 1 张非状态 / 非诅咒牌变成 **眩晕 / 粘液 / 碎屑** | **分 1/2/3 层** |')
[void]$sb.AppendLine('| 亡灵契约师 | 你的回合开始时，随机 1 张手牌在本回合费用 **+1** | 不分层 |')
[void]$sb.AppendLine('| 愚者 | 每场战斗开始时，将 **2 / 3 / 4** 张「悲伤」加入你的抽牌堆 | **分 1/2/3 层** |')
[void]$sb.AppendLine('| 观者 | 每场战斗开始时，所有敌人获得 **2 层**「虔信」；你的回合开始时，每个敌人获得 **2 层**「真言」；真言满 10 层时消耗 10 层进入「神格」，那一回合它的攻击造成三倍伤害 | 不分层 |')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('> 支配不是 Boss 的「特殊效果」—— 特殊效果只在该 Boss 的 Boss 战里生效，支配只在本幕的普通战斗里生效，两者互不重叠。')
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

