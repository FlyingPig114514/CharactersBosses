# check-localization.ps1 -- 校验 content\localization\<lang>\*.json 与 zhs 是否「键集合 + 占位符 + 行数」一致。
#
# 用法（在项目根目录）：
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\check-localization.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\check-localization.ps1 -Langs jpn,kor
#
# 检查项：
#   1. 5 张表都能被 JSON 解析；
#   2. 键集合与 zhs 完全相同（缺键 / 多键都会报）；
#   3. 每条文案里的占位符（{character} / {Boss} / {Loss} / {Reduction} …）集合与 zhs 一致；
#   4. 每条文案里 [gold]/[blue] 开闭标签数量一致、\n 数量一致；
#   5. 非中日韩语言里不应残留汉字（zht/jpn 允许，hong 混排会很难看）。
[CmdletBinding()]
param(
    [string[]]$Langs,
    [string]$Root
)

$ErrorActionPreference = 'Stop'
if (-not $Root) { $Root = Split-Path -Parent $PSScriptRoot }
if (-not $Root) { $Root = (Get-Location).Path }
$loc = Join-Path $Root 'content\localization'
if (-not (Test-Path $loc)) { Write-Host "找不到 $loc" -ForegroundColor Red; exit 1 }

$tables = @('monsters', 'powers', 'encounters', 'relics', 'intents')
$all = Get-ChildItem $loc -Directory | Select-Object -ExpandProperty Name
if ($Langs) { $all = $all | Where-Object { $Langs -contains $_ } }

$cjkOk = @('zhs', 'zht', 'jpn')
$problems = 0

function Placeholders([string]$s) {
    $set = @()
    foreach ($m in [regex]::Matches($s, '\{[A-Za-z_][A-Za-z0-9_]*\}')) { $set += $m.Value }
    return ($set | Sort-Object -Unique) -join '|'
}
function CountOf([string]$s, [string]$needle) { return ([regex]::Matches($s, [regex]::Escape($needle))).Count }

foreach ($lang in $all) {
    if ($lang -eq 'zhs') { continue }
    $line = "$lang :"
    foreach ($t in $tables) {
        $basePath = Join-Path $loc "zhs\$t.json"
        $path = Join-Path $loc "$lang\$t.json"
        if (-not (Test-Path $path)) { $line += "  [$t] 缺失"; $problems++; continue }
        try { $base = Get-Content $basePath -Raw -Encoding UTF8 | ConvertFrom-Json }
        catch { Write-Host "zhs/$t.json 自己有语法错误: $_" -ForegroundColor Red; $problems++; continue }
        $engPath = Join-Path $loc "eng\$t.json"
        $e = $null
        if (Test-Path $engPath) { try { $e = Get-Content $engPath -Raw -Encoding UTF8 | ConvertFrom-Json } catch { $e = $null } }
        try { $cur = Get-Content $path -Raw -Encoding UTF8 | ConvertFrom-Json }
        catch { $line += "  [$t] JSON 解析失败"; $problems++; continue }

        $bk = $base.PSObject.Properties.Name
        $ck = $cur.PSObject.Properties.Name
        $missing = @($bk | Where-Object { $ck -notcontains $_ })
        $extra = @($ck | Where-Object { $bk -notcontains $_ })
        if ($missing.Count -gt 0) { $line += "  [$t] 缺键 $($missing.Count)"; $problems++ }
        if ($extra.Count -gt 0) { $line += "  [$t] 多键 $($extra.Count)"; $problems++ }

        foreach ($k in $bk) {
            if ($ck -notcontains $k) { continue }
            $zs = [string]$base.$k
            $cs = [string]$cur.$k
            $es = ''
            if ($e -and ($e.PSObject.Properties.Name -contains $k)) { $es = [string]$e.$k }
            if ($zs -eq '') { continue }
            $allowed = @( (Placeholders $zs), (Placeholders $es) ) | Where-Object { $_ -ne '' } | ForEach-Object { $_ -split '\|' } | Sort-Object -Unique
            $used = @( (Placeholders $cs) -split '\|' ) | Where-Object { $_ -ne '' }
            $bad = @($used | Where-Object { $allowed -notcontains $_ })
            if ($bad.Count -gt 0) { $line += "  [$t/$k] 未知占位符 $($bad -join ',')"; $problems++ }
            # 说明：zhs 用 {Pronoun}（他/她/它）、eng 用 {Pronoun2}（his/her/its），
            # 两者都是模组自己注册的变量 ⇒ 只查「用了没注册的占位符」，不要求各语言用同一个。
            if ((CountOf $zs "`n") -ne (CountOf $cs "`n")) { $line += "  [$t/$k] 换行数不一致"; $problems++ }
            if ((CountOf $cs "[gold]") -ne (CountOf $cs "[/gold]")) { $line += "  [$t/$k] [gold] 未闭合"; $problems++ }
            if ((CountOf $cs "[blue]") -ne (CountOf $cs "[/blue]")) { $line += "  [$t/$k] [blue] 未闭合"; $problems++ }
            if ($cjkOk -notcontains $lang -and $cs -match '[\u4e00-\u9fff]') {
                $line += "  [$t/$k] 残留汉字"; $problems++
            }
            if ($cs.Trim() -eq '') { $line += "  [$t/$k] 空文案"; $problems++ }
        }
    }
    if ($line -eq "$lang :") { Write-Host ("{0} OK（5 张表与 zhs 一致）" -f $lang) -ForegroundColor Green }
    else { Write-Host $line -ForegroundColor Yellow }
}

if ($problems -eq 0) { Write-Host "`n全部通过：$($all.Count) 个语言目录" -ForegroundColor Green }
else { Write-Host "`n共 $problems 处问题" -ForegroundColor Red; exit 1 }
