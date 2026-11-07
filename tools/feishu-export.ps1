<#
.SYNOPSIS
  把飞书多维表格里的 CharacterBosses 数据导出成 CSV（用 Excel / WPS 直接打开）。

.DESCRIPTION
  为什么需要它（用户 2026-10-30 实测）：
    应用自己建的这张多维表格属于**应用空间**，用浏览器打开经常会遇到
    「https://open.feishu.cn/base/... 不存在」或「没有权限」——
    因为 ① 域名应该是 `https://<你的租户>.feishu.cn/base/...`（open.feishu.cn 是接口域名）；
         ② 你得先被加成协作者。
  **但应用读自己拥有的表永远不需要分享**，所以这条命令随时能把数据捞下来。

  用法：
    powershell -ExecutionPolicy Bypass -File tools\feishu-export.ps1 `
        -AppId cli_xxx -AppSecret xxxx `
        -BitableAppToken BITABLE_APP_TOKEN_REDACTED -TableId TABLE_ID_REDACTED

  可选 -OutCsv "D:\data.csv"（默认存到桌面，文件名带时间戳）。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$AppId,
    [Parameter(Mandatory = $true)][string]$AppSecret,
    [Parameter(Mandatory = $true)][string]$BitableAppToken,
    [Parameter(Mandatory = $true)][string]$TableId,
    [string]$OutCsv = ''
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch { }

$base = 'https://open.feishu.cn/open-apis'
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Invoke-Curl {
    param([ValidateSet('GET', 'POST')][string]$Method, [string]$Url, $Body = $null, [string]$Token = '')
    $curl = Join-Path $env:SystemRoot 'System32\curl.exe'
    if (-not (Test-Path $curl)) { $curl = 'curl.exe' }

    $args = @('-s', '-S', '-X', $Method, $Url, '-H', 'Content-Type: application/json; charset=utf-8')
    if ($Token) { $args += @('-H', "Authorization: Bearer $Token") }
    $tmp = $null
    try {
        if ($null -ne $Body) {
            $tmp = [System.IO.Path]::GetTempFileName()
            [System.IO.File]::WriteAllText($tmp, ($Body | ConvertTo-Json -Depth 10 -Compress), $Utf8NoBom)
            $args += @('--data-binary', "@$tmp")
        }
        $out = & $curl @args 2>&1 | Out-String
    }
    finally {
        if ($tmp) { Remove-Item $tmp -Force -ErrorAction SilentlyContinue }
    }
    if ([string]::IsNullOrWhiteSpace($out)) { throw '飞书没有返回内容（网络/代理？）' }
    return ($out | ConvertFrom-Json)
}

# ── 1. token ──
Write-Host '[1/3] 获取 tenant_access_token ...'
$tok = Invoke-Curl -Method POST -Url "$base/auth/v3/tenant_access_token/internal" `
    -Body @{ app_id = $AppId.Trim(); app_secret = $AppSecret.Trim() }
if ($tok.code -ne 0 -or -not $tok.tenant_access_token) {
    Write-Host ("失败：code={0} msg={1}" -f $tok.code, $tok.msg) -ForegroundColor Red
    exit 1
}
$token = [string]$tok.tenant_access_token
Write-Host '      ok'

# ── 2. 分页取记录 ──
Write-Host '[2/3] 读取记录 ...'
$records = New-Object System.Collections.ArrayList
$pageToken = ''
$page = 0
while ($true) {
    $page++
    $url = "$base/bitable/v1/apps/$BitableAppToken/tables/$TableId/records?page_size=500"
    if ($pageToken) { $url += "&page_token=$([uri]::EscapeDataString($pageToken))" }
    $res = Invoke-Curl -Method GET -Url $url -Token $token
    if ($res.code -ne 0) {
        Write-Host ("失败：code={0} msg={1}" -f $res.code, $res.msg) -ForegroundColor Red
        Write-Host '  → 91403/1254302：应用没被加为该表协作者（但如果是应用自己建的表则不会）；1254004：app_token 不对。'
        exit 1
    }
    $items = @($res.data.items)
    foreach ($it in $items) { [void]$records.Add($it) }
    Write-Host ("      第 {0} 页：{1} 条（累计 {2}）" -f $page, $items.Count, $records.Count)
    if ($res.data.has_more -and $res.data.page_token) { $pageToken = [string]$res.data.page_token } else { break }
    if ($page -ge 40) { Write-Host '      （已取 40 页，先停）' -ForegroundColor Yellow; break }
}
if ($records.Count -eq 0) {
    Write-Host '表里还没有数据 —— 先在游戏里打一场角色 Boss 战（并且 cfg 里 telemetry/telemetryUpload 都开着）。' -ForegroundColor Yellow
    exit 0
}

# ── 3. 写 CSV（列顺序固定，多余列追加在后面）──
Write-Host '[3/3] 写 CSV ...'
$preferred = @('at', 'kind', 'mod', 'result', 'act', 'encounter', 'lineup', 'boss',
               'ascension', 'players', 'bossMaxHp', 'bossHp', 'turns', 'seed', 'cfg', 'line')

$extra = New-Object System.Collections.Generic.List[string]
foreach ($r in $records) {
    foreach ($p in $r.fields.PSObject.Properties.Name) {
        if ($preferred -notcontains $p -and -not $extra.Contains($p)) { $extra.Add($p) }
    }
}
$columns = @($preferred) + @($extra)

function CellText($value) {
    if ($null -eq $value) { return '' }
    if ($value -is [string]) { return $value }
    if ($value -is [int] -or $value -is [long] -or $value -is [double] -or $value -is [decimal] -or $value -is [bool]) { return [string]$value }
    if ($value -is [System.Collections.IEnumerable]) {
        $parts = @()
        foreach ($each in $value) {
            if ($null -eq $each) { continue }
            if ($each -is [string]) { $parts += $each }
            elseif ($each.PSObject.Properties.Name -contains 'text') { $parts += [string]$each.text }
            elseif ($each.PSObject.Properties.Name -contains 'name') { $parts += [string]$each.name }
            else { $parts += [string]$each }
        }
        return ($parts -join '|')
    }
    if ($value.PSObject.Properties.Name -contains 'text') { return [string]$value.text }
    return [string]$value
}

function CsvEscape([string]$s) {
    if ($null -eq $s) { return '' }
    if ($s -match '[",\r\n]') { return '"' + ($s -replace '"', '""') + '"' }
    return $s
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add((($columns | ForEach-Object { CsvEscape $_ }) -join ','))
foreach ($r in $records) {
    $row = @()
    foreach ($c in $columns) {
        $v = $null
        if ($r.fields.PSObject.Properties.Name -contains $c) { $v = $r.fields.$c }
        $row += (CsvEscape (CellText $v))
    }
    $lines.Add(($row -join ','))
}

if (-not $OutCsv) {
    $desktop = [Environment]::GetFolderPath('Desktop')
    $OutCsv = Join-Path $desktop ("CharacterBosses-数据-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.csv')
}
$enc = New-Object System.Text.UTF8Encoding($true)   # 带 BOM：Excel 打开中文不乱码
[System.IO.File]::WriteAllLines($OutCsv, $lines, $enc)

Write-Host ''
Write-Host '====================== 导出完成 ======================' -ForegroundColor Green
Write-Host ("记录数 = {0}" -f $records.Count)
Write-Host ("CSV    = {0}" -f $OutCsv)
