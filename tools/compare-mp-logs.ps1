# 两端 mod_log 对表工具（联机不同步排查）
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File tools\compare-mp-logs.ps1 `
#       -LogA "房主的 mod_log.txt" -LogB "客人的 mod_log.txt"
#
# 为什么要有这个：本模组的「※阵容输入※」「※多 Boss 生成指纹※」「⚠ 不同步状态摘要」
# 三块日志就是为「两端逐字比对」设计的（见 docs/plans/handover-multiplayer.md）。
# 手工在 3000 万字的两份日志里对表不现实，所以把「去掉时间戳/端身份之后该相同的行」
# 按 key 配成一对，第一处不同就是分叉点。
#
# ⚠ 编码：一律用 .NET 的 UTF8 读写（**不要**用 Get-Content / Set-Content，
#   本机 PS 5.1 会按 ANSI 解码 UTF-8 把中文毁掉，见 .dshwolf/cerebrum.md）。

param(
    [Parameter(Mandatory = $true)][string]$LogA,
    [Parameter(Mandatory = $true)][string]$LogB,
    [string]$Out = ""
)

$ErrorActionPreference = "Stop"
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Read-Lines([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { throw "找不到日志：$path" }
    return [System.IO.File]::ReadLines((Resolve-Path -LiteralPath $path).Path, $utf8)
}

# 去掉 `[HH:MM:SS] ` 前缀与「本机=Host/Client」这种天然两端不同的片段，
# 剩下的部分两端必须完全一致（同一版本、同一阵容、同一状态）。
function Normalize([string]$line) {
    $t = $line -replace '^\[\d\d:\d\d:\d\d\]\s*', ''
    $t = $t -replace '本机=(Host|Client|Singleplayer)', '本机=<端>'
    $t = $t -replace '^\s+', ''
    return $t
}

function Collect([string]$path) {
    $data = @{
        Build     = @()      # 「===== CharacterBosses 构建：…」
        Inputs    = @{}      # ※阵容输入※      key = "第X层第Y场"
        Prints    = @{}      # ※多 Boss 生成指纹※ key = "第X层第Y场"
        Snapshots = @{}      # ⚠ 不同步状态摘要 里的「#N 轮次 M …」 key = "#N"
        Broadcast = @()      # 勾选同步 / 观者 Boss 结论
        Failures  = @()      # 广播失败（同一句只计一次）
        Count     = 0
    }
    $inDump = $false
    foreach ($line in (Read-Lines $path)) {
        $data.Count++
        if ($line.Length -gt 2000) { continue }        # 启动时那几条超长变更日志跳过

        if ($line -match '===== CharacterBosses 构建') { $data.Build += (Normalize $line); continue }

        if ($line -match '※阵容输入※') {
            $key = Get-MapKey $line
            if ($key) { $data.Inputs[$key] = (Normalize $line) }
            $inDump = $false
            continue
        }
        if ($line -match '※多 Boss 生成指纹※') {
            $key = Get-MapKey $line
            if ($key) { $data.Prints[$key] = (Normalize $line) }
            $inDump = $false
            continue
        }
        if ($line -match '不同步状态摘要') { $inDump = $true; continue }
        if ($inDump) {
            if ($line -match '^\s*#(\d+)\s+轮次') { $data.Snapshots["#$($matches[1])"] = (Normalize $line); continue }
            if ($line -match '^\s{2,}\S') { continue }   # 摘要里的其它行（不含快照编号）
            $inDump = $false
        }

        if ($line -match '勾选同步：房主已广播|勾选同步：客人已上报|观者 Boss：房主判定|观者 Boss：客人已上报') {
            $data.Broadcast += (Normalize $line)
            continue
        }
        if ($line -match '勾选同步：广播失败|观者 Boss：上报实现失败') {
            $n = Normalize $line
            if ($data.Failures -notcontains $n) { $data.Failures += $n }
            continue
        }
    }
    return $data
}

function Get-MapKey([string]$line) {
    if ($line -match '第\s*(\d+)\s*层第\s*(\d+)\s*场') { return "L$($matches[1])F$($matches[2])" }
    return $null
}

$nameA = Split-Path -Leaf $LogA
$nameB = Split-Path -Leaf $LogB

Write-Host "[compare] 读 $nameA …"
$a = Collect $LogA
Write-Host "[compare] 读 $nameB …"
$b = Collect $LogB

$report = New-Object System.Collections.Generic.List[string]
function W([string]$s) { $report.Add($s); Write-Host $s }

W "# 两端 mod_log 对表（$nameA  ←→  $nameB）"
W ""
W "| | A | B |"
W "|---|---|---|"
W "| 行数 | $($a.Count) | $($b.Count) |"
W ""
W "## 0. 构建版本（两端必须同一版）"
W ""
W "A: " + (($a.Build | Select-Object -Last 1))
W "B: " + (($b.Build | Select-Object -Last 1))
$buildSame = ($a.Build | Select-Object -Last 1) -eq ($b.Build | Select-Object -Last 1)
W ("⇒ " + $(if ($buildSame) { "一致 ✓" } else { "**不一致 ⇒ 先统一版本再看其它**" }))
W ""

W "## 1. ※阵容输入※（决定 Boss 是哪只：种子/地图/替换开关/候补表/勾选/共用匹配池）"
W ""
$keys = @($a.Inputs.Keys) + @($b.Inputs.Keys) | Sort-Object -Unique
$bad = 0
foreach ($k in $keys) {
    $la = $a.Inputs[$k]; $lb = $b.Inputs[$k]
    if (-not $la) { W "- **$k**：只有 B 有"; $bad++; continue }
    if (-not $lb) { W "- **$k**：只有 A 有"; $bad++; continue }
    if ($la -ne $lb) {
        $bad++
        W "- **$k 不同**："
        W "  - A: $la"
        W "  - B: $lb"
    }
}
if ($bad -eq 0) { W "- 全部一致 ✓（共 $($keys.Count) 场）" }
W ""

W "## 2. ※多 Boss 生成指纹※（这一行不同 ⇒ 场上 Boss 就不同）"
W ""
$keys = @($a.Prints.Keys) + @($b.Prints.Keys) | Sort-Object -Unique
$bad = 0
foreach ($k in $keys) {
    $la = $a.Prints[$k]; $lb = $b.Prints[$k]
    if (-not $la) { W "- **$k**：只有 B 有"; $bad++; continue }
    if (-not $lb) { W "- **$k**：只有 A 有"; $bad++; continue }
    if ($la -ne $lb) { $bad++; W "- **$k 不同**："; W "  - A: $la"; W "  - B: $lb" }
}
if ($bad -eq 0) { W "- 全部一致 ✓（共 $($keys.Count) 场）" }
W ""

W "## 3. ⚠ 不同步状态摘要 · 回合快照（第一处不同就是分叉点）"
W ""
$keys = @($a.Snapshots.Keys) + @($b.Snapshots.Keys) | Sort-Object -Unique
# 快照 key 是 "#N"：按 # 后面的数字排（否则会排成 #1 #10 #11 #2…，
# 那样「第一处不同」就不是真正最早的那一处了）
$keys = @($keys | Sort-Object { [int]($_ -replace '[^0-9]', '') })
if ($keys.Count -eq 0) {
    W "- 两端都**没有**落「⚠ 不同步状态摘要」⇒ 这次联机没触发本模组的摘要（或没检测到不同步）"
} else {
    foreach ($k in $keys) {
        $la = $a.Snapshots[$k]; $lb = $b.Snapshots[$k]
        if (-not $la) { W "- **$k**：只有 B 有"; continue }
        if (-not $lb) { W "- **$k**：只有 A 有"; continue }
        if ($la -ne $lb) { W "- **$k 不同（分叉点）**："; W "  - A: $la"; W "  - B: $lb" }
        else { W "- $k 一致 ✓" }
    }
}
W ""

W "## 4. 勾选同步 / 观者结论（房主→客人这条通道通不通）"
W ""
W ("A：广播/上报结论 " + @($a.Broadcast).Count + " 条，失败 " + @($a.Failures).Count + " 种")
W ("B：广播/上报结论 " + @($b.Broadcast).Count + " 条，失败 " + @($b.Failures).Count + " 种")
foreach ($f in $a.Failures) { W "  - A 失败：$f" }
foreach ($f in $b.Failures) { W "  - B 失败：$f" }
W ""

$outPath = $Out
if (-not $outPath) {
    $outPath = Join-Path (Get-Location) "mp-compare-report.md"
}
[System.IO.File]::WriteAllText((Join-Path (Get-Location) $outPath), ($report -join "`r`n"), $utf8)
Write-Host ""
Write-Host "[compare] 报告已写到 $outPath"
