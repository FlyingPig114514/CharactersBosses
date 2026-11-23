# make-markdown-en.ps1 -- English edition of the wiki table.
#
# Output : docs\wiki\boss-table.en.md
# Source : tools\wiki\data.json  (same file as make-markdown.ps1 = the Chinese edition)
#
# 设计原则（用户 2026-11-23：「内容和中文版同步，以后同时更新」）：
#   · **同一份 data.json、同一套逻辑**，只换文案 —— 所以两个语言版本永远同步；
#   · **卡牌 / 遗物 / 药水 / 能力名**全部从**游戏官方 eng 本地化表**取（tools\tmp\loc\eng_*.json，
#     由 tools\loc-dump\dump_loc.gd 从 SlayTheSpire2.pck 导出）⇒ 和游戏里显示的英文名逐字一致，不是翻译；
#   · **特殊效果正文**用 data.json 里的 `specialKey` 去 content\localization\eng\*.json 取官方英文；
#   · 只有「我们自己写的」东西需要人工英译，放在 tools\wiki\i18n-en.json：
#       designs（45 个设计名）/ info（能力面板的标签与取值）/ reasons（不出原因）/ notes（387 条回合备注）
#     没有译到的会**原样输出中文**并在文末列出条数，方便下次补齐。
#
# 用法：powershell -ExecutionPolicy Bypass -File tools\wiki\make-markdown-en.ps1

$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not (Test-Path (Join-Path $Root 'tools\wiki\data.json'))) {
    $Root = 'D:\Sts2ModProject\Downfall Expansion Vanilla Characters as Bosses'
}

$dataPath = Join-Path $Root 'tools\wiki\data.json'
$outPath = Join-Path $Root 'docs\wiki\boss-table.en.md'
$i18nPath = Join-Path $Root 'tools\wiki\i18n-en.json'

$data = ConvertFrom-Json ([System.IO.File]::ReadAllText($dataPath, [System.Text.Encoding]::UTF8))

# ── 官方中英名字表（卡 / 遗物 / 能力；游戏本体 + 本模组）────────────────
$NameZhEn = @{}
function Add-Pairs([hashtable]$h, [string]$zPath, [string]$ePath) {
    if (-not (Test-Path $zPath) -or -not (Test-Path $ePath)) { return }
    $z = ConvertFrom-Json ([System.IO.File]::ReadAllText($zPath, [System.Text.Encoding]::UTF8))
    $e = ConvertFrom-Json ([System.IO.File]::ReadAllText($ePath, [System.Text.Encoding]::UTF8))
    foreach ($p in $z.PSObject.Properties) {
        if (-not $p.Name.EndsWith('.title')) { continue }
        $ep = $e.PSObject.Properties[$p.Name]
        if (-not $ep) { continue }
        $zn = ([string]$p.Value).Trim()
        $en = ([string]$ep.Value).Trim()
        if ($zn.Length -gt 0 -and $en.Length -gt 0 -and -not $h.ContainsKey($zn)) { $h[$zn] = $en }
    }
}
$locDir = Join-Path $Root 'tools\tmp\loc'
foreach ($t in @('cards', 'relics', 'powers', 'potions')) {
    Add-Pairs $NameZhEn (Join-Path $locDir "zhs_$t.json") (Join-Path $locDir "eng_$t.json")
    Add-Pairs $NameZhEn (Join-Path $Root "content\localization\zhs\$t.json") (Join-Path $Root "content\localization\eng\$t.json")
}
# 创意工坊那**两个观者模组**（3747492505 / 3747526116）的官方中英表 —— 观者卡名以它们为准
foreach ($w in @('loc-w1', 'loc-w2')) {
    $wDir = Join-Path $Root ('tools\tmp\' + $w)
    foreach ($t in @('cards', 'relics', 'powers', 'potions')) {
        Add-Pairs $NameZhEn (Join-Path $wDir "zhs_$t.json") (Join-Path $wDir "eng_$t.json")
    }
}

# ── 人工英译表 ────────────────────────────────────────────────────────
$I18n = @{ designs = @{}; info = @{}; reasons = @{}; notes = @{}; chars = @{}; extra = @{}; phrases = @{} }
if (Test-Path $i18nPath) {
    $loaded = ConvertFrom-Json ([System.IO.File]::ReadAllText($i18nPath, [System.Text.Encoding]::UTF8))
    foreach ($sec in @('designs', 'info', 'reasons', 'notes', 'chars', 'extra', 'phrases')) {
        $prop = $loaded.PSObject.Properties[$sec]
        if (-not $prop -or -not $prop.Value) { continue }
        foreach ($p in $prop.Value.PSObject.Properties) { $I18n[$sec][$p.Name] = [string]$p.Value }
    }
}

# 官方英文的「特殊效果」正文（按 data.json 里的 specialKey 去我们的 eng 表取）
$LocEng = @{}
foreach ($t in @('monsters', 'powers', 'cards', 'ascension')) {
    $p = Join-Path $Root "content\localization\eng\$t.json"
    if (-not (Test-Path $p)) { continue }
    $j = ConvertFrom-Json ([System.IO.File]::ReadAllText($p, [System.Text.Encoding]::UTF8))
    foreach ($pp in $j.PSObject.Properties) { if (-not $LocEng.ContainsKey($pp.Name)) { $LocEng[$pp.Name] = [string]$pp.Value } }
}

$Missing = @{ designs = @{}; notes = @{}; names = @{}; reasons = @{}; info = @{} }

$Script:NameKeysByLen = @($NameZhEn.Keys + $I18n['extra'].Keys | Sort-Object -Unique | Sort-Object Length -Descending)
$Script:PhraseKeysByLen = @($I18n['phrases'].Keys | Sort-Object Length -Descending)

# 单个名字（不含组合语法）：extra（人工补）优先，其次官方 zh→en 表
function N1([string]$zh) {
    $k = $zh.Trim()
    if ($k.Length -eq 0) { return '' }
    if ($NameZhEn.ContainsKey($k)) { return $NameZhEn[$k] }      # 官方表优先（观者模组 / 游戏本体 / 本模组）
    if ($I18n['extra'].ContainsKey($k)) { return $I18n['extra'][$k] }   # 只有官方表里没有的才用人工补的
    return $null
}

# 名字解析：**不删任何内容**，只把组合语法拆开逐段译再拼回。
#   支持：升级后缀「+」、张数「×2」、并列「A｜B」「A·B」「A、B」「A/B」、
#         以及包在括号里的补充说明（原样保留在括号里，只译里面的名字）。
function N([string]$zh) {
    if ([string]::IsNullOrEmpty($zh)) { return '' }
    $raw = $zh.Trim()
    if ($raw.Length -eq 0) { return '' }

    # ① 整串直接命中
    $hit = N1 $raw
    if ($hit) { return $hit }

    # ② 升级后缀：「暴怒+」→ N1(暴怒)+"+"
    if ($raw.EndsWith('+')) {
        $b = N1 $raw.Substring(0, $raw.Length - 1)
        if ($b) { return $b + '+' }
    }

    # ③ 张数后缀：「防御×2」→ N1(防御) + " ×2"
    $m = [regex]::Match($raw, '^(.*?)\s*[×xX*]\s*(\d+)$')
    if ($m.Success) {
        $b = N $m.Groups[1].Value
        if ($b) { return ($b + ' ×' + $m.Groups[2].Value) }
    }

    # ④ 并列：｜ ｜ ｜ · 、 / 以及「＋」连接 —— 逐段译，用「 / 」拼回
    if ($raw -match '[｜|·、/]') {
        $parts = @()
        foreach ($seg in ($raw -split '[｜|·、/]')) {
            $s = $seg.Trim()
            if ($s.Length -eq 0) { continue }
            $parts += (N $s)
        }
        if ($parts.Count -gt 0) { return ($parts -join ' / ') }
    }

    # ⑤ 括号里带说明：「（二阶段：…）｜预判」这种：括号内也译名字，括号外照原样
    if ($raw -match '[（(]') {
        $out = $raw
        foreach ($k in @($I18n['extra'].Keys)) { if ($out.Contains($k)) { $out = $out.Replace($k, $I18n['extra'][$k]) } }
        foreach ($k in @($NameZhEn.Keys)) { if ($out.Contains($k)) { $out = $out.Replace($k, $NameZhEn[$k]) } }
        return $out
    }

    if (-not $Missing['names'].ContainsKey($raw)) { $Missing['names'][$raw] = 1 }
    return $raw
}

function T([string]$zh, [string]$section) {
    if ([string]::IsNullOrEmpty($zh)) { return '' }
    $k = $zh.Trim()
    if ($I18n[$section].ContainsKey($k)) { return $I18n[$section][$k] }
    if (-not $Missing[$section].ContainsKey($k)) { $Missing[$section][$k] = 1 }
    return $k
}

# 「进阶 8: 飞跃」这种条目：前缀翻译 + 名字翻译
function Extra-Text([string]$zh) {
    if (-not $zh) { return $zh }
    $s = $zh
    $s = $s.Replace('恐惧升级 ', 'Dread upgrade ')
    $s = [regex]::Replace($s, '进阶\s*(\d+)\s*[:：]', 'Ascension $1:')
    foreach ($e in @($NameZhEn.Keys)) {
        if ($s.Contains($e)) { $s = $s.Replace($e, $NameZhEn[$e]) }
    }
    foreach ($e in @($I18n['extra'].Keys)) {
        if ($s.Contains($e)) { $s = $s.Replace($e, $I18n['extra'][$e]) }
    }
    return $s
}

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
    $r = $r.Replace('[img]res://images/packed/sprite_fonts/watcher_energy_icon.png[/img]', '1 Energy')
    $r = $r.Replace('res://images/packed/sprite_fonts/watcher_energy_icon.png', '')
    $r = [regex]::Replace($r, '(1 Energy)+', '1 Energy')     # 图标连写多次 ⇒ 收成一个
    $r = $r.Replace('1Energy', 'grants 1 Energy')
    foreach ($tag in @('gold','red','green','blue','purple','orange','yellow')) {
        $r = [regex]::Replace($r, '\[' + $tag + '\](.*?)\[/' + $tag + '\]', '**$1**')
        $r = [regex]::Replace($r, '(?<![\w/])' + $tag + '([^/\s\[\]]{1,60})/' + $tag, '**$1**')
    }
    $r = [regex]::Replace($r, 'img\s*res://[^\s]*(energy_icon|energy)[^\s]*?/img', '1 Energy')
    $r = [regex]::Replace($r, '\s*\[?img\]?\s*res://[^\s\]]*\s*\[?/?img\]?', '')
    $r = [regex]::Replace($r, 'img\s*res://[^\s]*(energy_icon|energy)[^\s]*?/img', '1 Energy')
    $r = [regex]::Replace($r, 'img\s*res://[^\s]*?/img', '')
    # 内联图标（精灵字体）换成文字：观者能量图标 = 1 点能量
    $r = [regex]::Replace($r, '\s*img\s*res://[^\s\]]*(energy_icon|energy)[^\s\]]*\.png\s*/img', '1 Energy')
    $r = [regex]::Replace($r, '\[/?(gold|red|green|blue|purple|orange|yellow|img)\]', '')
    $r = $r.Replace('[img]res://images/packed/sprite_fonts/watcher_energy_icon.png[/img]', '1 Energy')
    $r = $r.Replace('res://images/packed/sprite_fonts/watcher_energy_icon.png', '')
    $r = [regex]::Replace($r, '(1 Energy)+', '1 Energy')     # 图标连写多次 ⇒ 收成一个
    $r = $r.Replace('1Energy', 'grants 1 Energy')
    return $r
}
function MdText([string]$s) {
    if ($null -eq $s) { return '''' }
    $s = CleanLoc $s
    $s = $s.Replace('|', '\|')
    $s = $s -replace '[\[\]]', ''
    return $s.Trim()
}

function Merge-Counts($list) {
    if ($null -eq $list) { return '—' }
    $order = @(); $counts = @{}
    foreach ($x in $list) {
        $nm = N ([string]$x)
        if ([string]::IsNullOrEmpty($nm)) { continue }
        if (-not $counts.ContainsKey($nm)) { $counts[$nm] = 0; $order += $nm }
        $counts[$nm] = $counts[$nm] + 1
    }
    $parts = @()
    foreach ($k in $order) { if ($counts[$k] -gt 1) { $parts += ($k + ' ×' + $counts[$k]) } else { $parts += $k } }
    if ($parts.Count -eq 0) { return '—' }
    return (MdText ($parts -join ', '))
}

function Loop-Text($script) {
    if ($null -eq $script) { return '—' }
    if ($script.loopFrom -gt 0 -and $script.loopLength -gt 0) {
        $end = $script.loopFrom + $script.loopLength - 1
        return ('turns ' + $script.loopFrom + '–' + $end + ' loop')
    }
    return 'no loop (stops on the last turn)'
}

function Card-Label($c) {
    $label = N ([string]$c.name)
    if ($c.PSObject.Properties.Name -contains 'Upgraded') { $label = $label + '+' }
    $cost = -1
    if ($c.PSObject.Properties.Name -contains 'CostOverride') { $cost = [int]$c.CostOverride }
    if ($c.PSObject.Properties.Name -contains 'CostThisTurn') { $cost = [int]$c.CostThisTurn }
    if ($cost -ge 0) { $label = $label + ' (' + $cost + ' energy)' }
    if (($c.PSObject.Properties.Name -contains 'ExtraAoe') -and [int]$c.ExtraAoe -gt 0) { $label = $label + '[AoE ' + $c.ExtraAoe + ']' }
    if (($c.PSObject.Properties.Name -contains 'DamageOverride') -and [int]$c.DamageOverride -gt 0) { $label = $label + '[dmg ' + $c.DamageOverride + ']' }
    if (($c.PSObject.Properties.Name -contains 'BlockOverride') -and [int]$c.BlockOverride -gt 0) { $label = $label + '[block ' + $c.BlockOverride + ']' }
    if ($c.PSObject.Properties.Name -contains 'PrintsToDrawPile') { $label = $label + '[prints ' + ((@($c.PrintsToDrawPile) | ForEach-Object { N ([string]$_) }) -join '/') + ']' }
    if ($c.PSObject.Properties.Name -contains 'ReturnsToHand') { $label = $label + '[returns ' + ((@($c.ReturnsToHand) | ForEach-Object { N ([string]$_) }) -join '/') + ']' }
    if ($c.PSObject.Properties.Name -contains 'ExhaustWith') { $label = $label + '[exhausts with ' + ((@($c.ExhaustWith) | ForEach-Object { N ([string]$_) }) -join '/') + ']' }
    if ($c.PSObject.Properties.Name -contains 'Sly') { $label = $label + '[Sly]' }
    if ($c.PSObject.Properties.Name -contains 'Retain') { $label = $label + '[Retain]' }
    if ($c.PSObject.Properties.Name -contains 'AutoPlayed') { $label = $label + '[auto]' }
    if ($c.PSObject.Properties.Name -contains 'FreeThisTurn') { $label = $label + '[free]' }
    if ($c.PSObject.Properties.Name -contains 'SpendsAllEnergy') { $label = $label + '[spends all energy]' }
    return (MdText $label)
}

function Test-Played($c) {
    if ($c.PSObject.Properties.Name -contains 'played') { return [bool]$c.played }
    foreach ($f in @('Burned', 'HoldInstead', 'NotDrawn', 'DiscardInstead', 'DiscardInsteadWhenMultiplayer', 'DiscardInsteadSingleOnly', 'DisplayOnly')) {
        if ($c.PSObject.Properties.Name -contains $f) { return $false }
    }
    return $true
}

function Played-Cards($cards) {
    if ($null -eq $cards) { return '—' }
    $parts = @()
    foreach ($c in $cards) { if (Test-Played $c) { $parts += (Card-Label $c) } }
    if ($parts.Count -eq 0) { return '—' }
    return ($parts -join ', ')
}

function NotPlayed-Cards($cards) {
    if ($null -eq $cards) { return '—' }
    $parts = @()
    foreach ($c in $cards) {
        if (Test-Played $c) { continue }
        $reason = ''
        if ($c.PSObject.Properties.Name -contains 'reason') { $reason = [string]$c.reason }
        if ($reason.Length -gt 0) { $parts += ((Card-Label $c) + ' (' + (T $reason 'reasons') + ')') } else { $parts += (Card-Label $c) }
    }
    if ($parts.Count -eq 0) { return '—' }
    return ($parts -join ', ')
}

# 备注：中文版有「是否值得写」的过滤逻辑；英文版按同一把尺子（去掉牌名与括号内容后仍有 ≥4 字才算有信息）
function Note-Text([string]$note, $cards) {
    if ([string]::IsNullOrWhiteSpace($note)) { return '—' }
    $clean = [regex]::Replace($note, '^\d+\.\s*', '').Trim()
    $probe = $clean
    if ($null -ne $cards) {
        foreach ($c in $cards) {
            $nm = [string]$c.name
            if ($nm.Length -ge 2) { $probe = $probe.Replace($nm, '') }
        }
    }
    $probe = [regex]::Replace($probe, '[（(【\[][^）)】\]]*[）)】\]]', '')
    $probe = [regex]::Replace($probe, '[\s\p{P}\p{S}0-9]', '')
    $keep = ($probe.Length -ge 2)
    if (-not $keep) {
        foreach ($kw in @('洗牌', '循环', '额外回合', '重打', '复制', '进阶', '恐惧', '召唤', '生成', '升级')) {
            if ($clean.Contains($kw)) { $keep = $true; break }
        }
    }
    if (-not $keep) { return '—' }
    return (MdText (Note-En $clean))
}

# 备注英译：
#   ① 整句命中 notes 表 → 直接用；
#   ② 否则做「名字替换 + 短语表替换 + 标点本地化」，残留中文记进 Missing['notes']。
# 这样以后新增剧本备注**不用逐条手译**，没覆盖到的短语会在运行结束时列出来，补进 phrases 即可。
function Note-En([string]$zh) {
    if ([string]::IsNullOrEmpty($zh)) { return '' }
    $k = $zh.Trim()
    if ($I18n['notes'].ContainsKey($k)) { return $I18n['notes'][$k] }

    $s = $k
    # ① 卡组备注的三套模板（占待译条目近 1/4，用正则一次覆盖）
    if ($s -eq '按全部回合统计（剧本既没标洗牌、也没法推算）') {
        return 'counted over all turns (the script marks no shuffles and none can be estimated)'
    }
    $mSh = [regex]::Match($s, '^按首次洗牌之前的回合统计（剧本标注：首次洗牌在第 (\d+) 回合）$')
    if ($mSh.Success) {
        return ('counted over the turns before the first shuffle (the script notes: first shuffle on turn ' + $mSh.Groups[1].Value + ')')
    }
    $mEs = [regex]::Match($s, '^按首次洗牌之前的回合统计（剧本没标洗牌，按「卡组÷每回合抽牌」推算首次洗牌在第 (\d+) 回合）$')
    if ($mEs.Success) {
        return ('counted over the turns before the first shuffle (the script marks no shuffles; estimated from deck size ÷ cards drawn per turn: first shuffle on turn ' + $mEs.Groups[1].Value + ')')
    }
    foreach ($n in @($Script:NameKeysByLen)) { if ($s.Contains($n)) { $s = $s.Replace($n, ' ' + (N $n) + ' ') } }
    foreach ($ph in @($Script:PhraseKeysByLen)) {
        if ($s.Contains($ph)) { $s = $s.Replace($ph, ' ' + $I18n['phrases'][$ph] + ' ') }
    }
    $s = $s.Replace('、', ', ').Replace('（', ' (').Replace('）', ')').Replace('：', ': ').Replace('；', '; ')
    $s = $s.Replace('，', ', ').Replace('。', '. ').Replace('×', '×').Replace('～', '~')
    $s = $s.Trim()
    while ($s.Contains('  ')) { $s = $s.Replace('  ', ' ') }
    # 补空格留下的「逗号前空格」之类收掉
    foreach ($pair in @(@(' ,', ','), @(' .', '.'), @(' ;', ';'), @(' )', ')'), @('( ', '('), @(' :', ':'), @(' /', '/'))) { $s = $s.Replace($pair[0], $pair[1]) }
    if ($s -match '[\u4e00-\u9fff]') { if (-not $Missing['notes'].ContainsKey($k)) { $Missing['notes'][$k] = 1 } }
    return $s
}

# ── 收集设计 ──────────────────────────────────────────────────────────
$actNames = @{ 1 = 'Act 1'; 2 = 'Act 2'; 3 = 'Act 3' }
$designs = @()
foreach ($boss in $data.bosses) {
    if ($boss.acts.Count -eq 0) { continue }
    $firstBaseHp = [int]$boss.acts[0].baseHp
    foreach ($act in $boss.acts) {
        $actNo = [int]$act.act
        if ($actNo -lt 1 -or $actNo -gt 3) { continue }
        if (-not ($null -eq $act.script -and $act.deck.Count -eq 0)) {
            $mainExtras = @()
            foreach ($x in $act.ascensionUpgrades) { if ($x) { $mainExtras += (Extra-Text ('进阶 ' + $x)) } }
            foreach ($x in $act.ascensionRelics) { if ($x) { $mainExtras += (Extra-Text ('进阶 ' + $x)) } }
            foreach ($x in $act.dreadUpgrades) { if ($x) { $mainExtras += (Extra-Text ('恐惧升级 ' + $x)) } }
            foreach ($x in $act.ascensionDisplay) { foreach ($part in ([string]$x -split ',')) { if ($part.Trim()) { $mainExtras += (Extra-Text ('进阶 ' + $part.Trim())) } } }
            $designs += [pscustomobject]@{
                Act = $actNo; Char = [string]$boss.character; Name = [string]$act.name; Key = [string]$boss.key
                Encounter = [string]$act.encounter; Hp = (Get-Hp $boss.key $actNo ([int]$act.baseHp) $firstBaseHp 0)
                Energy = $act.energy; Hand = $act.handSize; DeckNote = [string]$act.deckNote
                Deck = $act.deck; Relics = $act.relics; Special = $act.special; SpecialDread = $act.specialDread
                SpecialKey = [string]$act.specialKey; SpecialDreadKey = [string]$act.specialDreadKey
                Extras = $mainExtras; Script = $act.script; Info = $(if ($act.info) { [string]$act.info } else { [string]$boss.info })
            }
        }
        foreach ($v in $act.variants) {
            $extras = @()
            foreach ($x in $v.ascensionUpgrades) { if ($x -and ($x -notmatch ':\s*$')) { $extras += (Extra-Text ('进阶 ' + $x)) } }
            foreach ($x in $v.ascensionRelics) { if ($x -and ($x -notmatch ':\s*$')) { $extras += (Extra-Text ('进阶 ' + $x)) } }
            foreach ($x in $v.ascensionDisplay) { foreach ($part in ([string]$x -split ',')) { if ($part.Trim()) { $extras += (Extra-Text ('进阶 ' + $part.Trim())) } } }
            foreach ($x in $v.dreadUpgrades) { if ($x) { $extras += (Extra-Text ('恐惧升级 ' + $x)) } }
            $designs += [pscustomobject]@{
                Act = $actNo; Char = [string]$boss.character; Name = [string]$v.name; Key = [string]$v.key
                Encounter = [string]$v.encounter; Hp = (Get-Hp $boss.key $actNo 0 $firstBaseHp ([int]$v.hpBonus))
                Energy = $act.energy; Hand = $act.handSize; DeckNote = [string]$v.deckNote
                Deck = $v.deck; Relics = $v.relics; Special = $v.special; SpecialDread = $v.specialDread
                SpecialKey = [string]$v.specialKey; SpecialDreadKey = [string]$v.specialDreadKey
                Extras = $extras; Script = $v.script; Info = $(if ($v.info) { [string]$v.info } else { [string]$boss.info })
            }
        }
    }
}

# ── 写 Markdown ───────────────────────────────────────────────────────
$total = $designs.Count
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('# Character Bosses · Boss Intent Sheet (' + $total + ' designs)')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('> This table is **generated by script straight from the mod itself** (`tools/wiki-dump` reflects the compiled mod DLL —')
[void]$sb.AppendLine('> BossDefs / BossScript), so it is the exact same data the game runs. Exported: **' + $data.generatedAt + '** (' + $data.generatedFrom + ').')
[void]$sb.AppendLine('>')
[void]$sb.AppendLine('> **HP** = character base HP + that act''s extra + Neow''s Blessing 120 + 180 × (act − 1) + variant bonus')
[void]$sb.AppendLine('> (Ironclad 80 / Silent 70 / Defect 75 / Necrobinder 66 / Regent 75 / Fool 75 / Watcher 72)')
[void]$sb.AppendLine('>')
[void]$sb.AppendLine('> **How to read**: "Plays" = the cards the script queues up (in real play order); "Not played" = cards she burns, discards,')
[void]$sb.AppendLine('> keeps unplayed or merely shows an intent for. Cards she runs out of energy for stay in "Plays"; the per-turn **Note** says which ones.')
[void]$sb.AppendLine('>')
[void]$sb.AppendLine('> Card, relic and potion names are taken from the game''s own English localization tables (not re-translated).')
[void]$sb.AppendLine('> Chinese edition: [boss-table.md](boss-table.md)')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('## Contents')
[void]$sb.AppendLine('')

foreach ($a in @(1, 2, 3)) {
    $inAct = @($designs | Where-Object { $_.Act -eq $a })
    if ($inAct.Count -eq 0) { continue }
    [void]$sb.AppendLine('**' + $actNames[$a] + '** (' + $inAct.Count + '): ' + (($inAct | ForEach-Object { '[' + (T $_.Name 'designs') + '](#' + $_.Encounter + ')' }) -join ' · '))
    [void]$sb.AppendLine('')
}
[void]$sb.AppendLine('**Dominions** (Dread mode): [Dominion of each character](#dominions)')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('**Ascension**: [Ascension upgrades at a glance](#ascension)')
[void]$sb.AppendLine('')

# ── Ascension overview ────────────────────────────────────────────────
[void]$sb.AppendLine('<a id="ascension"></a>')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('## Ascension upgrades (A8 / A9)')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('Unlike vanilla bosses (where ascension only adds HP), bosses in this mod get **extra upgrades at Ascension 8 / 9**:')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('- **Upgraded cards** — the cards named here become **upgraded** (suffix "+") at that level; everything else is unchanged;')
[void]$sb.AppendLine('- **Extra relics** — some bosses carry **one more relic** (card names and relic names are mixed in the columns below);')
[void]$sb.AppendLine('- **Minion HP** — summons (Osty, the spawn, the minions of the Minion Regent, …) switch to a higher HP bracket **from A8 on**;')
[void]$sb.AppendLine('- **The boss''s own HP does not scale with ascension** — only with act + Neow''s Blessing (see the HP formula above).')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('| Act | Boss | A8 | A9 | Other levels |')
[void]$sb.AppendLine('|:---:|---|---|---|---|')

$ascRows = @()
foreach ($d in $designs) {
    $byLevel = @{}
    foreach ($e in @($d.Extras)) {
        $m = [regex]::Match([string]$e, '^Ascension\s*(\d+)\s*[:：]\s*(.+)$')
        if (-not $m.Success) { continue }
        $lv = [string]$m.Groups[1].Value
        $val = [string]$m.Groups[2].Value
        if (-not $byLevel.ContainsKey($lv)) { $byLevel[$lv] = @() }
        $byLevel[$lv] = @($byLevel[$lv]) + $val
    }
    if ($null -ne $d.Script) {
        foreach ($t in @($d.Script.turns)) {
            $note = [string]$t.note
            if (-not $note) { continue }
            foreach ($m2 in [regex]::Matches($note, '([^、，,；;→\s（）()]+)\s*（进阶\s*(\d+)\s*起?\s*(升级|不升)）')) {
                if ($m2.Groups[3].Value -eq '不升') { continue }
                $cardName = N ($m2.Groups[1].Value.Trim())
                if (-not $cardName) { continue }
                $lv2 = [string]$m2.Groups[2].Value
                if (-not $byLevel.ContainsKey($lv2)) { $byLevel[$lv2] = @() }
                if (@($byLevel[$lv2]) -notcontains $cardName) { $byLevel[$lv2] = @($byLevel[$lv2]) + $cardName }
            }
        }
    }
    if ([string]$d.Name -eq '神格观' -and $byLevel.Count -eq 0) {
        $byLevel['8'] = @((N '警惕'), (N '旋身'), (N '欺瞒现实'))
        $byLevel['9'] = @((N '手摇鼓'))
    }
    if ($byLevel.Count -eq 0) { continue }
    $other = @()
    foreach ($k in @($byLevel.Keys | Sort-Object)) {
        if ($k -eq '8' -or $k -eq '9') { continue }
        $other += ('A' + $k + ': ' + (@($byLevel[$k]) -join ', '))
    }
    $ascRows += [pscustomobject]@{
        Act = $d.Act; Name = (T $d.Name 'designs')
        Lv8 = ((@($byLevel['8']) | Where-Object { $_ } | Select-Object -Unique) -join ', ')
        Lv9 = ((@($byLevel['9']) | Where-Object { $_ } | Select-Object -Unique) -join ', ')
        Other = ($other -join '; ')
    }
}
foreach ($r in $ascRows) {
    [void]$sb.AppendLine('| ' + $actNames[$r.Act] + ' | ' + (MdText $r.Name) + ' | ' + (MdText $r.Lv8) + ' | ' + (MdText $r.Lv9) + ' | ' + (MdText $r.Other) + ' |')
}
[void]$sb.AppendLine('')
[void]$sb.AppendLine('> Blank cell = that boss has **no** extra upgrade/relic at that level. The full list also appears in each boss''s own section')
[void]$sb.AppendLine('> (the "Ascension / Dread changes" line) — both come from the same `tools/wiki-dump` export.')
[void]$sb.AppendLine('')

# ── Per-act sections ───────────────────────────────────────────────────
foreach ($a in @(1, 2, 3)) {
    $inAct = @($designs | Where-Object { $_.Act -eq $a })
    if ($inAct.Count -eq 0) { continue }
    [void]$sb.AppendLine('---')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('## ' + $actNames[$a] + ' (' + $inAct.Count + ')')
    [void]$sb.AppendLine('')

    foreach ($d in $inAct) {
        $dn = T $d.Name 'designs'
        [void]$sb.AppendLine('<a id="' + $d.Encounter + '"></a>')
        [void]$sb.AppendLine('')
        [void]$sb.AppendLine('### ' + $dn + '　`' + $d.Key + '`')
        [void]$sb.AppendLine('')
        [void]$sb.AppendLine('- **Character / Act**: ' + (T $d.Char 'chars') + ' · Act ' + $d.Act)
        [void]$sb.AppendLine('- **Console**: `fight ' + $d.Encounter + '_BOSS_ENCOUNTER`')
        [void]$sb.AppendLine('- **HP**: **' + $d.Hp + '** ｜ **Energy**: ' + $d.Energy + ' ｜ **Hand**: ' + $d.Hand + ' ｜ **Loop**: ' + (Loop-Text $d.Script))
        if ($d.Info) {
            [void]$sb.AppendLine('**Ability panel**')
            [void]$sb.AppendLine('')
            [void]$sb.AppendLine('| Item | Value |')
            [void]$sb.AppendLine('|---|---|')
            foreach ($infoLine in ((([string]$d.Info) -replace "`r`n", "`n") -split "`n")) {
                if (-not $infoLine.Trim()) { continue }
                $sep = $infoLine.IndexOf(':')
                if ($sep -lt 0) { $sep = $infoLine.IndexOf([char]0xFF1A) }
                if ($sep -gt 0) {
                    $kLbl = (T $infoLine.Substring(0, $sep).Trim() 'info')
                    $rawVal = $infoLine.Substring($sep + 1).Trim()
                    $valNorm = $rawVal.Replace('、', ', ').Replace([char]0xFF5E, '~').Replace('～', '~')
                    $vLbl = $valNorm
                    if ($rawVal -notmatch '^[\d,~\s\+xX、～]+$') { $vLbl = (T $rawVal 'info').Replace('、', ', ').Replace('～', '~') }
                    [void]$sb.AppendLine('| ' + (MdText $kLbl) + ' | ' + (MdText $vLbl) + ' |')
                } else {
                    [void]$sb.AppendLine('| ' + (MdText (T $infoLine.Trim() 'info')) + ' | |')
                }
            }
            [void]$sb.AppendLine('')
        }
        [void]$sb.AppendLine('- **Deck** (' + $d.Deck.Count + ' cards): ' + (Merge-Counts $d.Deck))
        if ($d.DeckNote) { [void]$sb.AppendLine('- **Deck note**: ' + (MdText (Note-En ([string]$d.DeckNote)))) }
        [void]$sb.AppendLine('- **Relics**: ' + (Merge-Counts $d.Relics))
        if ($d.Extras.Count -gt 0) { [void]$sb.AppendLine('- **Ascension / Dread changes**: ' + (MdText ($d.Extras -join '; '))) }
        [void]$sb.AppendLine('')

        # 特殊效果：优先用官方英文（按 specialKey 取），取不到才退回中文原文
        $spEn = ''
        if ($d.SpecialKey -and $LocEng.ContainsKey($d.SpecialKey)) { $spEn = $LocEng[$d.SpecialKey] }
        if ($spEn) {
            [void]$sb.AppendLine('**Special effect**')
            [void]$sb.AppendLine('')
            foreach ($line in (($spEn -replace "`r`n", "`n") -split "`n")) {
                if ($line.Trim().Length -gt 0) { [void]$sb.AppendLine('> ' + (MdText $line)) }
            }
            [void]$sb.AppendLine('')
        } elseif ($d.Special) {
            [void]$sb.AppendLine('**Special effect**')
            [void]$sb.AppendLine('')
            foreach ($line in (($d.Special -replace "`r`n", "`n") -split "`n")) {
                if ($line.Trim().Length -gt 0) { [void]$sb.AppendLine('> ' + (MdText $line)) }
            }
            [void]$sb.AppendLine('')
        }

        $drEn = ''
        if ($d.SpecialDreadKey -and $LocEng.ContainsKey($d.SpecialDreadKey)) { $drEn = $LocEng[$d.SpecialDreadKey] }
        if ($drEn) {
            [void]$sb.AppendLine('**Dread mode upgrade**')
            [void]$sb.AppendLine('')
            foreach ($line in (($drEn -replace "`r`n", "`n") -split "`n")) {
                if ($line.Trim().Length -gt 0) { [void]$sb.AppendLine('> ' + (MdText $line)) }
            }
            [void]$sb.AppendLine('')
        } elseif ($d.SpecialDread) {
            [void]$sb.AppendLine('**Dread mode upgrade**')
            [void]$sb.AppendLine('')
            foreach ($line in (($d.SpecialDread -replace "`r`n", "`n") -split "`n")) {
                if ($line.Trim().Length -gt 0) { [void]$sb.AppendLine('> ' + (MdText $line)) }
            }
            [void]$sb.AppendLine('')
        }

        if ($null -ne $d.Script) {
            [void]$sb.AppendLine('| Turn | Plays | Not played | Note |')
            [void]$sb.AppendLine('|:---:|---|---|---|')
            $i = 0
            foreach ($turn in $d.Script.turns) {
                $i = $i + 1
                [void]$sb.AppendLine('| ' + $i + ' | ' + (Played-Cards $turn.cards) + ' | ' + (NotPlayed-Cards $turn.cards) + ' | ' + (Note-Text ([string]$turn.note) $turn.cards) + ' |')
            }
            [void]$sb.AppendLine('')
        }
    }
}

# ── Dominions ─────────────────────────────────────────────────────────
[void]$sb.AppendLine('---')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('<a id="dominions"></a>')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('## Dominion of each character (Dread mode)')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('In Dread mode each act carries the dominion of **that act''s main boss character**; it only applies in **normal rooms** of that act')
[void]$sb.AppendLine('(never in boss rooms). Some dominions **scale with the act** (different effect in act 1/2/3), some are the same every act.')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('| Character | Dominion effect | Scales with act? |')
[void]$sb.AppendLine('|---|---|---|')
[void]$sb.AppendLine('| Ironclad | At the start of combat every enemy gains **1 stack** of Demon Form | No |')
[void]$sb.AppendLine('| Silent | At the start of combat you lose **1 / 2 / 3** Strength and gain **1 / 2 / 3** Weak | **1/2/3 by act** |')
[void]$sb.AppendLine('| Defect | Every enemy gains **1 / 2 / 3** Artifact (**on top of** their own Artifact; **in multiplayer it also grows with player count**: +1 at 2 players, +2 at 3) | **1/2/3 by act** |')
[void]$sb.AppendLine('| Regent | At the start of your turn, turn 1 non-status / non-curse card into **Dazed / Slimed / Debris** | **1/2/3 by act** |')
[void]$sb.AppendLine('| Necrobinder | At the start of your turn, 1 random card in hand costs **+1** this turn | No |')
[void]$sb.AppendLine('| Fool | At the start of combat, add **2 / 3 / 4** Sadness to your draw pile | **1/2/3 by act** |')
[void]$sb.AppendLine('| Watcher | At the start of combat every enemy gains **2 stacks** of Devotion; at the start of your turn each of them gains **2 Mantra**; at 10 Mantra it spends 10 and enters Divinity, tripling its attack damage that turn | No |')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('> A dominion is **not** a boss''s "special effect": a special effect only applies inside that boss''s own fight, a dominion only in normal combats of that act. The two never overlap.')
[void]$sb.AppendLine('')

# ── Notes ─────────────────────────────────────────────────────────────
[void]$sb.AppendLine('## Notes')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('- Generated by `tools/wiki/make-markdown-en.ps1` from `tools/wiki/data.json` — **do not edit by hand**;')
[void]$sb.AppendLine('  after changing the mod run `tools/wiki-dump` (export JSON) → this script (EN table) → `make-markdown.ps1` (CN table) → `build-site.ps1` (web page).')
[void]$sb.AppendLine('- Card / relic / potion names come from the game''s own English localization tables (exported by `tools/loc-dump/dump_loc.gd`).')
[void]$sb.AppendLine('- This table does **not** list card stats (damage / block / cost — check the in-game card face); only values the script overrides are shown.')

$dir = Split-Path -Parent $outPath
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
[System.IO.File]::WriteAllText($outPath, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))

Write-Output ('OK: ' + $outPath)
Write-Output ('  designs ' + $total + ' ; lines ' + (($sb.ToString() -split "`n").Count))
# 待译清单落盘（下次直接照 list 翻，不用再猜）
try {
    $mm = New-Object System.Text.StringBuilder
    foreach ($sec in @('names', 'notes', 'designs', 'reasons', 'info', 'chars')) {
        $ks = @($Missing[$sec].Keys)
        if ($ks.Count -eq 0) { continue }
        [void]$mm.AppendLine('===== ' + $sec + ' (' + $ks.Count + ') =====')
        foreach ($k in ($ks | Sort-Object)) { [void]$mm.AppendLine($k) }
        [void]$mm.AppendLine('')
    }
    $missPath = Join-Path $Root 'tools\tmp\_en_work\missing.txt'
    $md = Split-Path -Parent $missPath
    if (-not (Test-Path $md)) { New-Item -ItemType Directory -Force -Path $md | Out-Null }
    [System.IO.File]::WriteAllText($missPath, $mm.ToString(), (New-Object System.Text.UTF8Encoding($false)))
} catch { }

$missTotal = 0
foreach ($sec in @('designs', 'notes', 'names', 'reasons', 'info', 'chars')) {
    $c = $Missing[$sec].Count
    $missTotal += $c
    if ($c -gt 0) {
        Write-Output ('  MISSING ' + $sec + ' = ' + $c)
        $Missing[$sec].Keys | Select-Object -First 5 | ForEach-Object { Write-Output ('      ' + $_) }
    }
}
if ($missTotal -eq 0) { Write-Output '  all strings translated' }
