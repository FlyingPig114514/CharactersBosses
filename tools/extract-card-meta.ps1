# 离线提取卡牌/遗物的基础元数据（费用 / 类型 / 稀有度 / 目标类型 / 动态变量数值）
#
#   powershell -ExecutionPolicy Bypass -File tools\extract-card-meta.ps1
#
# 为什么需要它：游戏本地化文件里只有卡牌的 title/description，**没有费用、稀有度、也没有数值**
# （描述里写的是 {Damage:diff()} 这种占位符）。这些信息只存在于卡牌类里：
#     protected StrikeIronclad() : base(1, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy)
#     protected override IEnumerable<DynamicVar> CanonicalVars => ... new DamageVar(6m, ValueProp.Move)
# 所以这里逐个反编译卡牌类（每张只跑一次），把这两处抠出来。
#
# 输出：.recon/card-metadata.tsv  （字符<TAB>类名<TAB>slug<TAB>base 参数<TAB>数值）
#       .recon/relic-metadata.tsv（字符<TAB>类名<TAB>slug<TAB>base 参数）
# 只读 references\sts2.dll，不碰游戏。全套约 5~10 分钟。

param(
    [string]$Assembly = 'references\sts2.dll',
    [string]$OutDir = '.recon'
)

$ErrorActionPreference = 'Continue'
$dotnet = Join-Path $PSScriptRoot 'dotnet\dotnet.exe'
$cli    = Join-Path $PSScriptRoot 'ilspy-cli\bin\Release\net10.0\ilspycli.dll'

if (-not (Test-Path $dotnet)) { Write-Host "找不到 dotnet: $dotnet" -ForegroundColor Red; exit 1 }
if (-not (Test-Path $cli))    { Write-Host "找不到 ilspycli: $cli" -ForegroundColor Red; exit 1 }
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }

# 和游戏 StringHelper.Slugify 同样的规则：小写/数字后跟大写处插下划线，再全大写。
# ⚠ PS 5.1 的 -replace 会把 '$1_$2' 这类替换串弄坏（实测 Assassinate -> A_ss_as_si_na_te），
#   必须用 .NET 的 [regex]::Replace。
function Get-Slug([string]$name) {
    return ([regex]::Replace($name, '([a-z0-9])([A-Z])', '${1}_${2}')).ToUpperInvariant()
}

if ((Get-Slug 'IroncladVulnerableBoss') -ne 'IRONCLAD_VULNERABLE_BOSS') {
    Write-Host "slug 规则自检失败：$(Get-Slug 'IroncladVulnerableBoss')" -ForegroundColor Red; exit 1
}

function Get-PoolTypes([string]$poolFullName) {
    $dump = & $dotnet $cli type $Assembly $poolFullName 2>&1
    $dump | Select-String -Pattern 'ModelDb\.(Card|Relic)<' |
        ForEach-Object { ($_.Line -replace '.*ModelDb\.(Card|Relic)<', '' -replace '>\(\).*', '') } |
        Sort-Object -Unique
}

# 从一次类型反编译里同时抠出 base(...) 那一行和 CanonicalVars 里的数值
function Parse-TypeDump($dump) {
    $baseLine = ''
    $varsText = ''
    $collecting = $false
    # ⚠ 必须扫完整个反编译文本：ILSpy 把构造函数排在属性之后，
    #   而 CanonicalVars 是属性 —— 如果在 CanonicalVars 结束时 break，就永远读不到排在后面的
    #   `: base(费用, 类型, 稀有度, 目标)` 那一行（踩过：费用/类型/稀有度整列变空）。
    foreach ($raw in $dump) {
        $t = $raw.Trim()

        if (-not $baseLine -and $t -match '^:\s*base\(') { $baseLine = $t }

        if (-not $collecting) {
            if ($t -match 'CanonicalVars\s*=>') { $collecting = $true; $varsText = $t }
            continue
        }

        # 收集到语句结束（分号）或下一个成员声明为止，但不跳出循环
        if ($t -match '^(protected|public|private|internal)\s') { $collecting = $false; continue }
        $varsText += ' ' + $t
        if ($varsText.TrimEnd().EndsWith(';')) { $collecting = $false }
    }

    $vars = New-Object System.Collections.Generic.List[string]
    # 泛型形式：new PowerVar<VulnerablePower>(2m) —— 占多数（易伤/虚弱/中毒…都走 PowerVar<T>）
    foreach ($m in [regex]::Matches($varsText, 'new\s+(\w+Var)\s*<(\w+)>\s*\(([^)]*)\)')) {
        $varName = $m.Groups[2].Value -replace 'Power$', ''
        $num = [regex]::Match($m.Groups[3].Value, '-?\d+(\.\d+)?')
        if ($num.Success) { $vars.Add("$varName=$($num.Value)") }
    }
    # 普通形式：new DamageVar(8m, ValueProp.Move)
    foreach ($m in [regex]::Matches($varsText, 'new\s+(\w+Var)\s*\(([^)]*)\)')) {
        $varName = $m.Groups[1].Value -replace 'Var$', ''
        $num = [regex]::Match($m.Groups[2].Value, '-?\d+(\.\d+)?')
        if ($num.Success) { $vars.Add("$varName=$($num.Value)") }
    }
    return @{ base = $baseLine; vars = (($vars | Select-Object -Unique) -join ';') }
}

$cardPools = [ordered]@{
    Ironclad   = 'MegaCrit.Sts2.Core.Models.CardPools.IroncladCardPool'
    Silent     = 'MegaCrit.Sts2.Core.Models.CardPools.SilentCardPool'
    Defect     = 'MegaCrit.Sts2.Core.Models.CardPools.DefectCardPool'
    Necrobinder= 'MegaCrit.Sts2.Core.Models.CardPools.NecrobinderCardPool'
    Regent     = 'MegaCrit.Sts2.Core.Models.CardPools.RegentCardPool'
    Colorless  = 'MegaCrit.Sts2.Core.Models.CardPools.ColorlessCardPool'
}

$relicPools = [ordered]@{
    Ironclad   = 'MegaCrit.Sts2.Core.Models.RelicPools.IroncladRelicPool'
    Silent     = 'MegaCrit.Sts2.Core.Models.RelicPools.SilentRelicPool'
    Defect     = 'MegaCrit.Sts2.Core.Models.RelicPools.DefectRelicPool'
    Necrobinder= 'MegaCrit.Sts2.Core.Models.RelicPools.NecrobinderRelicPool'
    Regent     = 'MegaCrit.Sts2.Core.Models.RelicPools.RegentRelicPool'
    Shared     = 'MegaCrit.Sts2.Core.Models.RelicPools.SharedRelicPool'
}

$cardRows = New-Object System.Collections.Generic.List[string]
foreach ($char in $cardPools.Keys) {
    $types = Get-PoolTypes $cardPools[$char]
    Write-Host "[cards] $char : $($types.Count) 张"
    foreach ($t in $types) {
        $dump = & $dotnet $cli type $Assembly "MegaCrit.Sts2.Core.Models.Cards.$t" 2>&1
        $parsed = Parse-TypeDump $dump
        $cardRows.Add(("{0}`t{1}`t{2}`t{3}`t{4}" -f $char, $t, (Get-Slug $t), $parsed.base, $parsed.vars))
    }
}
[IO.File]::WriteAllLines((Join-Path $OutDir 'card-metadata.tsv'), $cardRows, (New-Object Text.UTF8Encoding($false)))
Write-Host "[cards] 写入 $($cardRows.Count) 行 -> card-metadata.tsv"

$relicRows = New-Object System.Collections.Generic.List[string]
foreach ($char in $relicPools.Keys) {
    $types = Get-PoolTypes $relicPools[$char]
    Write-Host "[relics] $char : $($types.Count) 个"
    foreach ($t in $types) {
        $dump = & $dotnet $cli type $Assembly "MegaCrit.Sts2.Core.Models.Relics.$t" 2>&1
        $parsed = Parse-TypeDump $dump
        $relicRows.Add(("{0}`t{1}`t{2}`t{3}" -f $char, $t, (Get-Slug $t), $parsed.base))
    }
}
[IO.File]::WriteAllLines((Join-Path $OutDir 'relic-metadata.tsv'), $relicRows, (New-Object Text.UTF8Encoding($false)))
Write-Host "[relics] 写入 $($relicRows.Count) 行 -> relic-metadata.tsv"
Write-Host '完成'
