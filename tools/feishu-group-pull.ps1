<#
=============================================================================
 feishu-group-pull.ps1 —— 从**飞书群里**把玩家上传的数据捞回来，写进多维表格
=============================================================================

背景（为什么有这个脚本）：
  v0.2.462 起，玩家端不再持有飞书 app_secret，只持有一个**群自定义机器人 webhook**
  （`https://open.feishu.cn/open-apis/bot/v2/hook/xxx`，零鉴权，最坏结果只是别人能往那个群发消息）。
  玩家的模组把每条记录（原始 JSON 一行）作为群消息发进那个群。

  这个脚本在**作者本机**跑：用作者自己的自建应用凭据（只存在本机 cfg 里，不进 DLL）
  读群消息 → 解析出 JSON 行 → 批量写回原来的多维表格。效果和「玩家直连多维表格」一样，
  只是多了一次「定时拉取」。

前置（一次性）：
  1. 飞书开放平台给应用开通权限：`im:chat:readonly` + `im:message.history:readonly`，
     然后**创建版本并发布**（个人版即时生效）。
  2. 建一个群（就自己一个人也行）：
     · 加「自定义机器人」→ 安全设置选**自定义关键词**，关键词填 `CharacterBosses` → 拿到 webhook 地址；
     · 再加「应用机器人」（你自己的自建应用）→ 脚本才读得到这个群的历史消息。
  3. 把 webhook 地址给作者填进 `BossConfig.BuiltInRelayUrl`（或玩家自己在 cfg 里填 `feishuBotUrl`）。

用法：
  # 先干跑，看看能捞到多少条、认不认得表
  powershell -ExecutionPolicy Bypass -File tools\feishu-group-pull.ps1 -DryRun

  # 真跑：读群消息 + 写回多维表格
  powershell -ExecutionPolicy Bypass -File tools\feishu-group-pull.ps1

  # 只导出成本地 JSONL/CSV，不写表
  powershell -ExecutionPolicy Bypass -File tools\feishu-group-pull.ps1 -NoBitable

  # 指定群 / 指定起始时间
  powershell -ExecutionPolicy Bypass -File tools\feishu-group-pull.ps1 -ChatName "CharacterBosses" -Since 2026-11-01

参数：
  -ChatName   群名字里包含这段文字就选它（默认 `CharacterBosses`；只在第一次需要，之后用状态文件里记的 chat_id）
  -ChatId     直接指定 oc_xxx
  -Since      从这个时间点开始拉（默认用状态文件里上次拉到的位置；没有就拉最近 7 天）
  -DryRun     只打印，不写表、不落状态
  -NoBitable  不写多维表格，只导出本地文件
  -Limit      最多拉多少页（每页 50 条），默认 200 页

状态文件：`.feishu-group-state.json`（记 chat_id / 上次时间戳 / 已收记录指纹，防重复写表）
导出文件：`docs\plans\telemetry-import\records-<时间>.jsonl` 与 `.csv`
=============================================================================
#>
[CmdletBinding()]
param(
    [string]$ChatName = 'CharacterBosses',
    [string]$ChatId = '',
    [string]$Since = '',
    [switch]$DryRun,
    [switch]$NoBitable,
    [int]$Limit = 200
)

$ErrorActionPreference = 'Stop'
$root = (Get-Location).Path
if (-not (Test-Path (Join-Path $root 'src'))) {
    # 允许从任意目录调用：脚本在 tools\ 下，上一级就是根
    $root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
}
$statePath = Join-Path $root '.feishu-group-state.json'
$outDir = Join-Path $root 'docs\plans\telemetry-import'

function Info($m) { Write-Host $m -ForegroundColor Cyan }
function Warn($m) { Write-Host $m -ForegroundColor Yellow }
function Fail($m) { Write-Host $m -ForegroundColor Red }

# ── 1. 读本机配置里的飞书凭据 ──────────────────────────────────────────────
function Get-Config {
    $candidates = @(
        (Join-Path $env:APPDATA 'SlayTheSpire2\mod_configs\CharacterBosses\charbosses_config.cfg')
    )
    foreach ($p in $candidates) {
        if (Test-Path $p) {
            try { return (Get-Content $p -Raw -Encoding UTF8 | ConvertFrom-Json) } catch { Warn "配置读不出来：$p" }
        }
    }
    throw "找不到 charbosses_config.cfg（$($candidates -join ' / ')）"
}

$cfg = Get-Config
foreach ($need in 'feishuAppId', 'feishuAppSecret', 'feishuBitableAppToken', 'feishuTableId') {
    if ([string]::IsNullOrWhiteSpace($cfg.$need)) { throw "配置缺少 $need，先在模组配置里填好（或直接从游戏里跑一次让 cfg 生成）" }
}

# ── 2. 飞书 API 小工具 ────────────────────────────────────────────────────
$script:token = $null

function Get-Token {
    if ($script:token) { return $script:token }
    $body = @{ app_id = $cfg.feishuAppId; app_secret = $cfg.feishuAppSecret } | ConvertTo-Json -Compress
    $r = Invoke-RestMethod -Uri 'https://open.feishu.cn/open-apis/auth/v3/tenant_access_token/internal' `
        -Method Post -ContentType 'application/json; charset=utf-8' -Body $body -TimeoutSec 30
    if ($r.code -ne 0) { throw "取 tenant_access_token 失败：code=$($r.code) $($r.msg)（app_secret 是不是又重置了？）" }
    $script:token = $r.tenant_access_token
    return $script:token
}

function Api($method, $url, $bodyObj) {
    $headers = @{ Authorization = "Bearer $(Get-Token)" }
    $args = @{ Uri = $url; Method = $method; Headers = $headers; TimeoutSec = 40 }
    if ($bodyObj) {
        $args.Body = ($bodyObj | ConvertTo-Json -Depth 12 -Compress)
        $args.ContentType = 'application/json; charset=utf-8'
    }
    try {
        return Invoke-RestMethod @args
    }
    catch {
        # PS 5.1 的 400 响应体有时读不出来 ⇒ 三个来源依次试：ErrorDetails → 响应流 → 异常文本
        $body = ''
        if ($_.ErrorDetails -and $_.ErrorDetails.Message) { $body = $_.ErrorDetails.Message }
        $resp = $_.Exception.Response
        if (-not $body -and $resp) {
            try {
                $sr = New-Object System.IO.StreamReader -ArgumentList $resp.GetResponseStream()
                $body = $sr.ReadToEnd()
            } catch { }
        }
        if (-not $body) { $body = $_.Exception.Message }

        $hint = ''
        if ($body -match '230027' -or $body -match 'im:message\.group_msg') {
            $hint = "`n  => 还差一个**群消息读取**权限：im:message.group_msg（飞书把它当敏感权限单独管）。" +
                    "开通后**重新创建版本并发布**：`n  " +
                    "https://open.feishu.cn/app/APP_ID_REDACTED/auth?q=im:message.group_msg&op_from=openapi&token_type=tenant"
        } elseif ($body -match '99991672' -or $body -match 'scopes is required' -or $body -match '应用尚未开通') {
            $hint = "`n  => 这是**权限没开通**（不是脚本的问题）。去飞书开放平台给应用开通 im:chat:readonly + " +
                    "im:message.history:readonly，然后**创建版本并发布**。一键开通链接：`n  " +
                    "https://open.feishu.cn/app/APP_ID_REDACTED/auth?q=im:chat:readonly,im:message.history:readonly&op_from=openapi&token_type=tenant"
        } elseif ($body -match '230002' -or $body -match 'not in the chat') {
            $hint = "`n  => 应用机器人不在这个群里：群设置 → 群机器人 → 添加机器人 → 选你的自建应用。"
        }
        if (-not $body) { $body = "(响应体为空，错误码 $([int]$resp.StatusCode))" }
        throw "飞书 API $method $url 失败：$body$hint"
    }
}

# ── 3. 状态文件 ───────────────────────────────────────────────────────────
$state = @{ chat_id = ''; last_ts = 0; seen = @() }
if (Test-Path $statePath) {
    try {
        $loaded = Get-Content $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($loaded.chat_id) { $state.chat_id = $loaded.chat_id }
        if ($loaded.last_ts) { $state.last_ts = [int64]$loaded.last_ts }
        if ($loaded.seen) { $state.seen = @($loaded.seen) }
    } catch { Warn "状态文件坏了，忽略：$statePath" }
}
$seenSet = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($h in $state.seen) { [void]$seenSet.Add([string]$h) }

# ── 4. 选群 ───────────────────────────────────────────────────────────────
$chat = $ChatId
if ([string]::IsNullOrWhiteSpace($chat)) { $chat = $state.chat_id }
if ([string]::IsNullOrWhiteSpace($chat)) {
    Info "列出机器人所在的群，找名字里含「$ChatName」的…"
    $pageToken = ''
    $found = @()
    do {
        $u = "https://open.feishu.cn/open-apis/im/v1/chats?page_size=50"
        if ($pageToken) { $u += "&page_token=$pageToken" }
        $r = Api 'GET' $u
        if ($r.code -ne 0) { throw "列群失败：code=$($r.code) $($r.msg)（是不是没开通 im:chat:readonly 权限？）" }
        foreach ($it in $r.data.items) { $found += $it }
        $pageToken = if ($r.data.has_more) { $r.data.page_token } else { '' }
    } while ($pageToken)

    if ($found.Count -eq 0) {
        throw "应用机器人不在任何群里。请在群里「添加机器人 → 选择你的自建应用」，然后重跑（顺便确认 im:chat:readonly 已开通并发布）"
    }
    Info ("找到的群：" + (($found | ForEach-Object { "$($_.name)($($_.chat_id))" }) -join '；'))
    $pick = $found | Where-Object { $_.name -like "*$ChatName*" } | Select-Object -First 1
    if (-not $pick) {
        if ($found.Count -eq 1) { $pick = $found[0]; Warn "名字没匹配上，但只有一个群，就用它：$($pick.name)" }
        else { throw "有 $($found.Count) 个群但都不含「$ChatName」，用 -ChatName 或 -ChatId 指定" }
    }
    $chat = $pick.chat_id
    Info "选中群：$($pick.name)（$chat）"
}

# ── 5. 拉群消息（分页，按时间升序） ────────────────────────────────────────
$nowTs = [int64]([datetimeoffset]::UtcNow.ToUnixTimeSeconds())
$startTs = 0
if ($Since) {
    $startTs = [int64]([datetimeoffset]::Parse($Since, [System.Globalization.CultureInfo]::InvariantCulture).ToUnixTimeSeconds())
} elseif ($state.last_ts -gt 0) {
    $startTs = [int64]$state.last_ts - 120        # 留 2 分钟重叠，靠指纹去重
} else {
    $startTs = $nowTs - 7 * 24 * 3600
}
Info ("拉取窗口：{0:yyyy-MM-dd HH:mm} ~ {1:yyyy-MM-dd HH:mm}（UTC）" -f
    ([datetimeoffset]::FromUnixTimeSeconds($startTs).UtcDateTime),
    ([datetimeoffset]::FromUnixTimeSeconds($nowTs).UtcDateTime))

$messages = @()
$pageToken = ''
$pages = 0
do {
    $u = "https://open.feishu.cn/open-apis/im/v1/messages?container_id_type=chat&container_id=$chat" +
         "&start_time=$startTs&end_time=$nowTs&sort_type=ByCreateTimeAsc&page_size=50"
    if ($pageToken) { $u += "&page_token=$pageToken" }
    $r = Api 'GET' $u
    if ($r.code -ne 0) {
        throw "读群消息失败：code=$($r.code) $($r.msg)" +
              "（没开通 im:message.history:readonly，或应用机器人不在这个群里）"
    }
    foreach ($it in $r.data.items) { $messages += $it }
    $pageToken = if ($r.data.has_more) { $r.data.page_token } else { '' }
    $pages++
} while ($pageToken -and $pages -lt $Limit)

Info "群消息 $($messages.Count) 条（$pages 页）"

# ── 6. 解析 JSON 行 ───────────────────────────────────────────────────────
$records = New-Object System.Collections.ArrayList
$newHashes = New-Object System.Collections.ArrayList
foreach ($m in $messages) {
    if ($m.msg_type -ne 'text') { continue }
    $text = $null
    try { $text = ($m.body.content | ConvertFrom-Json).text } catch { continue }
    if ([string]::IsNullOrWhiteSpace($text)) { continue }
    foreach ($line in ($text -split "`n")) {
        $line = $line.Trim()
        if (-not $line.StartsWith('{')) { continue }
        try { $obj = $line | ConvertFrom-Json } catch { continue }
        if (-not $obj.kind) { continue }
        $fp = [string]$obj.kind + '|' + [string]$obj.seed + '|' + [string]$obj.at + '|' + [string]$obj.encounter +
              '|' + [string]$obj.result + '|' + [string]$obj.turns
        if ($seenSet.Contains($fp)) { continue }
        [void]$seenSet.Add($fp)
        [void]$newHashes.Add($fp)
        [void]$records.Add([pscustomobject]@{ fp = $fp; obj = $obj })
    }
}

Info "新记录 $($records.Count) 条（已处理过的跳过）"
if ($records.Count -eq 0) {
    if (-not $DryRun) {
        $state.chat_id = $chat
        $state.last_ts = $nowTs
        ($state | ConvertTo-Json -Depth 5) | Set-Content -Path $statePath -Encoding UTF8
    }
    return
}

# ── 7. 导出本地副本（永远都导，方便事后分析） ──────────────────────────────
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$jsonlPath = Join-Path $outDir "records-$stamp.jsonl"
$csvPath = Join-Path $outDir "records-$stamp.csv"
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$sb = New-Object System.Text.StringBuilder
foreach ($rec in $records) { [void]$sb.AppendLine(($rec.obj | ConvertTo-Json -Depth 6 -Compress)) }
[System.IO.File]::WriteAllText($jsonlPath, $sb.ToString(), $utf8NoBom)
$records | ForEach-Object { $_.obj } | Export-Csv -Path $csvPath -NoTypeInformation -Encoding UTF8
Info "已导出：$jsonlPath"
Info "        $csvPath"

if ($DryRun) {
    Info '（DryRun：不写多维表格、不改状态文件）'
    return
}

# ── 8. 写回多维表格 ───────────────────────────────────────────────────────
if (-not $NoBitable) {
    $appToken = $cfg.feishuBitableAppToken
    $tableId = $cfg.feishuTableId

    # 表里真有哪些字段、什么类型（照着写，飞书不会因为多字段整批拒收）
    $fr = Api 'GET' "https://open.feishu.cn/open-apis/bitable/v1/apps/$appToken/tables/$tableId/fields?page_size=100"
    if ($fr.code -ne 0) { throw "读表字段失败：code=$($fr.code) $($fr.msg)" }
    $fieldType = @{}
    foreach ($f in $fr.data.items) { $fieldType[$f.field_name] = [int]$f.type }
    Info ("表字段 " + $fieldType.Count + " 个")

    $numericKeys = @('act', 'ascension', 'players', 'bossMaxHp', 'bossHp', 'turns', 'bossTotal',
                     'bossesKilled', 'maxHpTotal', 'hpTotal', 'pMaxHp', 'pHpStart', 'pHpEnd', 'pDmg', 'pHeal')

    $toSend = New-Object System.Collections.ArrayList
    $skipped = 0
    foreach ($rec in $records) {
        $fields = @{}
        foreach ($prop in $rec.obj.PSObject.Properties) {
            $name = $prop.Name
            if ($name -eq 'fp') { continue }
            if (-not $fieldType.ContainsKey($name)) { continue }
            $t = $fieldType[$name]
            $v = $prop.Value
            if ($null -eq $v) { continue }
            switch ($t) {
                1  { $fields[$name] = [string]$v }                              # 多行文本
                2  {                                                            # 数字
                    $d = 0.0
                    if ([double]::TryParse([string]$v, [ref]$d)) { $fields[$name] = $d } else { $fields[$name] = [string]$v }
                }
                3  { $fields[$name] = [string]$v }                              # 单选
                5  {                                                            # 日期
                    try {
                        $dto = [datetimeoffset]::Parse([string]$v, [System.Globalization.CultureInfo]::InvariantCulture)
                        $fields[$name] = [int64]$dto.ToUnixTimeMilliseconds()
                    } catch { }
                }
                default {
                    if ($t -eq 1 -or $v -is [string]) { }                        # 其它类型：字符串就照发，复杂结构跳过
                }
            }
        }
        if ($fields.Count -eq 0) { $skipped++; continue }
        [void]$toSend.Add(@{ fields = $fields })
    }
    if ($skipped -gt 0) { Warn "有 $skipped 条一个字段都对不上，跳过" }

    $written = 0
    for ($i = 0; $i -lt $toSend.Count; $i += 100) {
        $chunk = @()
        for ($k = $i; $k -lt [Math]::Min($i + 100, $toSend.Count); $k++) { $chunk += $toSend[$k] }
        $r = Api 'POST' "https://open.feishu.cn/open-apis/bitable/v1/apps/$appToken/tables/$tableId/records/batch_create" @{ records = $chunk }
        if ($r.code -ne 0) { Fail "第 $($i + 1) 批写入失败：code=$($r.code) $($r.msg)"; break }
        $written += $chunk.Count
        Info "已写入 $written / $($toSend.Count)"
    }
    Info "多维表格新增 $written 条"
}

# ── 9. 存状态 ─────────────────────────────────────────────────────────────
$state.chat_id = $chat
$state.last_ts = $nowTs
$merged = New-Object System.Collections.ArrayList
foreach ($h in $state.seen) { [void]$merged.Add([string]$h) }
foreach ($h in $newHashes) { [void]$merged.Add([string]$h) }
$all = @($merged | Select-Object -Unique)
if ($all.Count -gt 20000) { $all = $all[($all.Count - 20000)..($all.Count - 1)] }
$state.seen = $all
($state | ConvertTo-Json -Depth 5) | Set-Content -Path $statePath -Encoding UTF8
Info "状态已保存：$statePath（已收指纹 $($all.Count) 条）"
