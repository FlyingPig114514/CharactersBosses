# 检查「代码里的 Boss 类名」与「content 里的本地化 key」是否对得上
#
#   powershell -ExecutionPolicy Bypass -File tools\check-loc-keys.ps1
#
# 为什么需要它：怪物/遭遇的本地化 key 是由**类名**推出来的
#     class IroncladBoss        → ModelId.Entry = "IRONCLAD_BOSS"        → monsters.json 要 "IRONCLAD_BOSS.name"
#     class IroncladBossEncounter → "IRONCLAD_BOSS_ENCOUNTER"            → encounters.json 要 ".title" / ".loss"
# 所以你一旦重命名类名（或加第 6 个 Boss），本地化 key 不会报错、只会**静默变成生 key**
# （游戏里显示成 "monsters.IRONCLAD_BOSS.name" 这种）。这个脚本在构建前跑一遍就能立刻发现。
#
# 只读源码与 content 目录，不碰游戏。

param(
    [string]$MonsterDir = 'src\Hooks\Monsters',
    [string]$EncounterFile = 'src\Hooks\Encounters\CharBossEncounters.cs',
    [string]$ContentDir = 'content\localization',
    [string[]]$Languages = @('eng', 'zhs')
)

$ErrorActionPreference = 'Stop'

# 和游戏 StringHelper.Slugify 同样的规则（注意：PS 5.1 的 -replace 会把 '$1_$2' 弄坏，必须用 .NET）
function Get-Slug([string]$name) {
    return ([regex]::Replace($name, '([a-z0-9])([A-Z])', '${1}_${2}')).ToUpperInvariant()
}
if ((Get-Slug 'IroncladVulnerableBoss') -ne 'IRONCLAD_VULNERABLE_BOSS') {
    Write-Host "slug 规则自检失败：$(Get-Slug 'IroncladVulnerableBoss')" -ForegroundColor Red; exit 1
}

# 怪物类：继承 CharBossMonster 的（排除抽象基类本身）
$monsters = @()
Get-ChildItem $MonsterDir -Filter *.cs -Recurse | ForEach-Object {
    $text = [IO.File]::ReadAllText($_.FullName, [Text.Encoding]::UTF8)
    foreach ($m in [regex]::Matches($text, 'class\s+(\w+)\s*:\s*CharBossMonster')) {
        $monsters += $m.Groups[1].Value
    }
}

# 遭遇类：继承 CharBossEncounterBase<T> 的
$encounters = @()
$encText = [IO.File]::ReadAllText((Resolve-Path $EncounterFile).Path, [Text.Encoding]::UTF8)
foreach ($m in [regex]::Matches($encText, 'class\s+(\w+)\s*:\s*CharBossEncounterBase<')) {
    $encounters += $m.Groups[1].Value
}

Write-Host "发现 $($monsters.Count) 个 Boss 怪物类：$($monsters -join ', ')"
Write-Host "发现 $($encounters.Count) 个 Boss 遭遇类：$($encounters -join ', ')"
if ($monsters.Count -eq 0 -or $encounters.Count -eq 0) {
    Write-Host "解析不到类名，检查正则/文件路径" -ForegroundColor Red; exit 1
}

$problems = 0
foreach ($lang in $Languages) {
    $monsterTable = Get-Content (Join-Path $ContentDir "$lang\monsters.json") -Raw -Encoding UTF8 | ConvertFrom-Json
    $encTable     = Get-Content (Join-Path $ContentDir "$lang\encounters.json") -Raw -Encoding UTF8 | ConvertFrom-Json
    $monsterKeys  = $monsterTable.PSObject.Properties.Name
    $encKeys      = $encTable.PSObject.Properties.Name

    Write-Host ""
    Write-Host "===== $lang =====" -ForegroundColor Cyan
    foreach ($cls in $monsters) {
        $slug = Get-Slug $cls
        foreach ($field in @('name', 'moves.CHARBOSS_PLAY.title')) {
            $key = "$slug.$field"
            if ($monsterKeys -contains $key) { Write-Host "  OK   $key" -ForegroundColor Green }
            else { Write-Host "  ✗ 缺 $key（类 $cls）" -ForegroundColor Red; $problems++ }
        }
    }
    foreach ($cls in $encounters) {
        $slug = Get-Slug $cls
        foreach ($field in @('title', 'loss')) {
            $key = "$slug.$field"
            if ($encKeys -contains $key) { Write-Host "  OK   $key" -ForegroundColor Green }
            else { Write-Host "  ✗ 缺 $key（类 $cls）" -ForegroundColor Red; $problems++ }
        }
    }

    # 反向检查：content 里有、代码里没人用的 key（多半是改名后的残留）
    foreach ($key in $monsterKeys) {
        $slug = ($key -replace '\.(name|moves\..*)$', '')
        if ($monsters | Where-Object { (Get-Slug $_) -eq $slug }) { continue }
        Write-Host "  ! 多余 key（代码里没有对应类）: $key" -ForegroundColor Yellow
    }
    foreach ($key in $encKeys) {
        $slug = ($key -replace '\.(title|loss)$', '')
        if ($encounters | Where-Object { (Get-Slug $_) -eq $slug }) { continue }
        Write-Host "  ! 多余 key（代码里没有对应类）: $key" -ForegroundColor Yellow
    }
}

Write-Host ""
if ($problems -gt 0) {
    Write-Host "发现 $problems 个缺失的本地化 key —— 游戏里会显示成生 key。" -ForegroundColor Red
    exit 1
}
Write-Host "本地化 key 与代码类名一致 ✓（多余 key 不算错，只提示）" -ForegroundColor Green
