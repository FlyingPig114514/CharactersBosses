# 生成「各角色卡池清单」文档（给用户挑牌用）
#
#   powershell -ExecutionPolicy Bypass -File tools\gen-card-lists.ps1
#
# 前置：先跑过 tools\extract-card-meta.ps1（产出 .recon\card-metadata.tsv / relic-metadata.tsv）
#
# 输出：docs\plans\cards-<角色>.md —— 费用 / 类型 / 稀有度 / 中文名 / 效果 / C# 类名，
#       （类名就是写进 src\Hooks\Core\BossDefs.cs 里 typeof(...) 用的那个）
# 数据来源：sts2.dll 反编译（费用/类型/稀有度/数值）+ 游戏官方中文本地化（名字/效果文案）。

param(
    [string]$Assembly = 'references\sts2.dll',
    [string]$LocDir   = '.recon\pck\localization\zhs',
    [string]$OutDir   = 'docs\plans'
)

$ErrorActionPreference = 'Stop'
$dotnet = Join-Path $PSScriptRoot 'dotnet\dotnet.exe'
$cli    = Join-Path $PSScriptRoot 'ilspy-cli\bin\Release\net10.0\ilspycli.dll'

$cardTsv  = '.recon\card-metadata.tsv'
$relicTsv = '.recon\relic-metadata.tsv'
foreach ($f in @($cardTsv, $relicTsv)) {
    if (-not (Test-Path $f)) { Write-Host "缺少 $f —— 先跑 tools\extract-card-meta.ps1" -ForegroundColor Red; exit 1 }
}

$cardsLoc  = Get-Content (Join-Path $LocDir 'cards.json')  -Raw -Encoding UTF8 | ConvertFrom-Json
$relicsLoc = Get-Content (Join-Path $LocDir 'relics.json') -Raw -Encoding UTF8 | ConvertFrom-Json

$charMeta = [ordered]@{
    Ironclad   = @{ cn = '铁甲战士';   cls = 'Ironclad' }
    Silent     = @{ cn = '静默猎手';   cls = 'Silent' }
    Defect     = @{ cn = '故障机器人'; cls = 'Defect' }
    Necrobinder= @{ cn = '亡灵契约师'; cls = 'Necrobinder' }
    Regent     = @{ cn = '储君';       cls = 'Regent' }
}
$typeCn    = @{ Attack = '攻击牌'; Skill = '技能牌'; Power = '能力牌'; Status = '状态牌'; Curse = '诅咒牌'; Quest = '任务牌' }
$rarityCn  = @{ Basic = '基础'; Common = '普通'; Uncommon = '罕见'; Rare = '稀有'; Ancient = '远古'
                Event = '事件'; Token = '衍生'; Status = '状态'; Curse = '诅咒'; Quest = '任务'; None = '-' }
# 这些占位符不是数值，直接从描述里删掉，免得文档里一堆 [IfUpgraded]
$skipPlaceholders = @('IfUpgraded', 'InCombat', 'energyPrefix', 'energyIcons')

# 和游戏 StringHelper.Slugify 同样的规则：小写/数字后跟大写处插下划线，再全大写。
# ⚠ PS 5.1 的 -replace 会把 '$1_$2' 这类替换串弄坏（实测 Assassinate -> A_ss_as_si_na_te），
#   必须用 .NET 的 [regex]::Replace。这里直接从类名重算 slug，不依赖 TSV 里的那一列。
function Get-Slug([string]$name) {
    return ([regex]::Replace($name, '([a-z0-9])([A-Z])', '${1}_${2}')).ToUpperInvariant()
}
if ((Get-Slug 'IroncladVulnerableBoss') -ne 'IRONCLAD_VULNERABLE_BOSS') {
    Write-Host "slug 规则自检失败：$(Get-Slug 'IroncladVulnerableBoss')" -ForegroundColor Red; exit 1
}

# 把 "Damage=6;Vulnerable=2" 解析成查表用的哈希（同时给出「去掉 Power 后缀」的别名）
function Get-VarMap([string]$varsText) {
    $map = @{}
    if (-not $varsText) { return $map }
    foreach ($pair in ($varsText -split ';')) {
        if ($pair -match '^([^=]+)=(.+)$') {
            $k = $matches[1]; $v = $matches[2]
            $map[$k] = $v
            if ($k.EndsWith('Power')) { $map[$k.Substring(0, $k.Length - 5)] = $v }
        }
    }
    return $map
}

function Clean-Text([string]$s, $varMap) {
    if (-not $s) { return '' }
    $t = $s -replace '\r?\n', '；'
    $t = $t -replace '\[/?[a-zA-Z][^\]]*\]', ''                      # 去掉 [gold] 这类 bbcode
    # {InCombat: ... {内层} ...} 这种嵌套块整体删掉（贪婪吃到最后一个 }，否则会留下 "次）|}" 残渣）
    $t = [regex]::Replace($t, '\{InCombat:.*\}', '')
    $sub = {
        param($m)
        $name = $m.Groups[1].Value
        if ($skipPlaceholders -contains $name) { return '' }
        if ($varMap.ContainsKey($name)) { return $varMap[$name] }
        # 描述里常写 VulnerablePower / WeakPower，而 CanonicalVars 里的类名是 VulnerableVar / WeakVar
        if ($name.EndsWith('Power') -and $varMap.ContainsKey($name.Substring(0, $name.Length - 5))) {
            return $varMap[$name.Substring(0, $name.Length - 5)]
        }
        return "[$name]"
    }
    $t = [regex]::Replace($t, '\{([A-Za-z0-9_]+):[^}]*\}', $sub)      # {Damage:diff()} -> 数值
    $t = [regex]::Replace($t, '\{([A-Za-z0-9_]+)\}', $sub)
    $t = $t -replace '\|\}', ''                                       # 嵌套占位符的残留尾巴
    $t = $t -replace '[{}]', ''
    $t = $t -replace '。；', '；'
    $t = $t -replace '(；\s*)+', '；'
    $t = $t -replace '\s+', ' '
    return $t.Trim(' ', '；')
}

function Loc-Text($table, [string]$slug, [string]$field) {
    $key = "$slug.$field"
    if ($table.PSObject.Properties.Name -contains $key) { return $table.$key }
    return ''
}

function Parse-Base([string]$baseLine) {
    $r = [ordered]@{ cost = '?'; type = 'None'; rarity = 'None' }
    if ($baseLine -match ':\s*base\(\s*(\d+)\s*,\s*CardType\.(\w+)\s*,\s*CardRarity\.(\w+)') {
        $r.cost = $matches[1]; $r.type = $matches[2]; $r.rarity = $matches[3]
    }
    return $r
}

function Get-Starting([string]$charKey) {
    $dump = & $dotnet $cli type $Assembly "MegaCrit.Sts2.Core.Models.Characters.$($charMeta[$charKey].cls)" 2>&1
    $deck = @(); $relics = @(); $hp = ''
    foreach ($line in $dump) {
        $t = $line.Trim()
        if ($t -match 'StartingHp => (\d+)') { $hp = $matches[1] }
        if ($t -match 'ModelDb\.Card<([A-Za-z0-9_]+)>\(\)') { $deck += $matches[1] }
        if ($t -match 'ModelDb\.Relic<([A-Za-z0-9_]+)>\(\)') { $relics += $matches[1] }
    }
    return @{ deck = $deck; relics = $relics; hp = $hp }
}

$cardRows  = Import-Csv -Path $cardTsv  -Delimiter "`t" -Header char, cls, slug, base, vars
$relicRows = Import-Csv -Path $relicTsv -Delimiter "`t" -Header char, cls, slug, base

function Append-CardTable($sb, $group, $cardsLoc) {
    [void]$sb.AppendLine('| 费用 | 稀有度 | 中文名 | 效果 | 类名 |')
    [void]$sb.AppendLine('|---|---|---|---|---|')
    foreach ($row in ($group | Sort-Object @{ Expression = { [int](Parse-Base $_.base).cost } }, @{ Expression = { $_.cls } })) {
        $b = Parse-Base $row.base
        $slug = Get-Slug $row.cls
        $name = Loc-Text $cardsLoc $slug 'title'
        if (-not $name) { $name = $row.cls }
        $desc = Clean-Text (Loc-Text $cardsLoc $slug 'description') (Get-VarMap $row.vars)
        [void]$sb.AppendLine("| $($b.cost) | $($rarityCn[$b.rarity]) | $name | $desc | ``$($row.cls)`` |")
    }
    [void]$sb.AppendLine()
}

function Render-Char([string]$charKey) {
    $meta = $charMeta[$charKey]
    $start = Get-Starting $charKey

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("# $($meta.cn)（$charKey）卡池清单")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('> 自动生成（`tools\gen-card-lists.ps1`）：费用/类型/稀有度/数值来自 `sts2.dll` 反编译，名字/效果来自游戏官方中文本地化。')
    [void]$sb.AppendLine('> 「类名」就是填进 `src/Hooks/Core/BossDefs.cs` 里 `typeof(类名)` 用的名字。')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("- 起始生命：$($start.hp)")
    $deckDesc = ($start.deck | Group-Object | ForEach-Object { if ($_.Count -gt 1) { "$($_.Name)×$($_.Count)" } else { $_.Name } }) -join '、'
    [void]$sb.AppendLine("- 起始卡组（$($start.deck.Count) 张）：$deckDesc")
    [void]$sb.AppendLine("- 起始遗物：$((($start.relics) -join '、'))")
    [void]$sb.AppendLine()

    $mine = $cardRows | Where-Object { $_.char -eq $charKey }
    foreach ($typeName in @('Attack', 'Skill', 'Power', 'Status', 'Curse', 'Quest')) {
        $group = $mine | Where-Object { (Parse-Base $_.base).type -eq $typeName }
        if (-not $group) { continue }
        [void]$sb.AppendLine("## $($typeCn[$typeName])（$($group.Count) 张）")
        [void]$sb.AppendLine()
        Append-CardTable $sb $group $cardsLoc
    }

    $myRelics = $relicRows | Where-Object { $_.char -eq $charKey }
    if ($myRelics) {
        [void]$sb.AppendLine("## 遗物（$($myRelics.Count) 个）")
        [void]$sb.AppendLine()
        [void]$sb.AppendLine('| 中文名 | 效果 | 类名 |')
        [void]$sb.AppendLine('|---|---|---|')
        foreach ($row in ($myRelics | Sort-Object { $_.cls })) {
            $slug = Get-Slug $row.cls
            $name = Loc-Text $relicsLoc $slug 'title'
            if (-not $name) { $name = $row.cls }
            $desc = Clean-Text (Loc-Text $relicsLoc $slug 'description') @{}
            [void]$sb.AppendLine("| $name | $desc | ``$($row.cls)`` |")
        }
        [void]$sb.AppendLine()
    }

    [void]$sb.AppendLine('## 复制即用的骨架')
    [void]$sb.AppendLine()
    [void]$sb.AppendLine('```csharp')
    [void]$sb.AppendLine('DeckByAct = new List<Type>?[]')
    [void]$sb.AppendLine('{')
    [void]$sb.AppendLine('    new List<Type> { /* 第一幕卡组：从上面挑 15~16 张，重复写就是多张 */ },')
    [void]$sb.AppendLine('    new List<Type> { /* 第二幕卡组 */ },')
    [void]$sb.AppendLine('    new List<Type> { /* 第三幕卡组 */ },')
    [void]$sb.AppendLine('    null,')
    [void]$sb.AppendLine('},')
    [void]$sb.AppendLine('RelicsByAct = new List<Type>?[]')
    [void]$sb.AppendLine('{')
    [void]$sb.AppendLine('    new List<Type> { /* 第一幕遗物 1~2 个 */ },')
    [void]$sb.AppendLine('    new List<Type> { /* 第二幕遗物 */ },')
    [void]$sb.AppendLine('    new List<Type> { /* 第三幕遗物 */ },')
    [void]$sb.AppendLine('    null,')
    [void]$sb.AppendLine('},')
    [void]$sb.AppendLine('```')

    $path = Join-Path $OutDir "cards-$($charKey.ToLower()).md"
    [IO.File]::WriteAllText($path, $sb.ToString(), (New-Object Text.UTF8Encoding($false)))
    Write-Host "写入 $path （$($mine.Count) 张卡 / $($myRelics.Count) 个遗物）"
}

foreach ($charKey in $charMeta.Keys) { Render-Char $charKey }

# 无色牌 + 共享遗物各一份
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('# 无色牌（Colorless）清单')
[void]$sb.AppendLine()
[void]$sb.AppendLine('> 自动生成。任何角色都能往卡组里塞无色牌。')
[void]$sb.AppendLine()
$colorless = $cardRows | Where-Object { $_.char -eq 'Colorless' }
foreach ($typeName in @('Attack', 'Skill', 'Power', 'Status', 'Curse', 'Quest')) {
    $group = $colorless | Where-Object { (Parse-Base $_.base).type -eq $typeName }
    if (-not $group) { continue }
    [void]$sb.AppendLine("## $($typeCn[$typeName])（$($group.Count) 张）")
    [void]$sb.AppendLine()
    Append-CardTable $sb $group $cardsLoc
}
[IO.File]::WriteAllText((Join-Path $OutDir 'cards-colorless.md'), $sb.ToString(), (New-Object Text.UTF8Encoding($false)))
Write-Host "写入 docs\plans\cards-colorless.md （$($colorless.Count) 张）"

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('# 共享遗物池（Shared）清单')
[void]$sb.AppendLine()
[void]$sb.AppendLine('> 自动生成。这些遗物不属于某个角色，原版里任何角色开箱/商店都可能拿到，Boss 也可以配。')
[void]$sb.AppendLine()
[void]$sb.AppendLine('| 中文名 | 效果 | 类名 |')
[void]$sb.AppendLine('|---|---|---|')
$sharedRelics = $relicRows | Where-Object { $_.char -eq 'Shared' }
foreach ($row in ($sharedRelics | Sort-Object { $_.cls })) {
    $slug = Get-Slug $row.cls
    $name = Loc-Text $relicsLoc $slug 'title'
    if (-not $name) { $name = $row.cls }
    $desc = Clean-Text (Loc-Text $relicsLoc $slug 'description') @{}
    [void]$sb.AppendLine("| $name | $desc | ``$($row.cls)`` |")
}
[void]$sb.AppendLine()
[IO.File]::WriteAllText((Join-Path $OutDir 'relics-shared.md'), $sb.ToString(), (New-Object Text.UTF8Encoding($false)))
Write-Host "写入 docs\plans\relics-shared.md （$($sharedRelics.Count) 个）"
