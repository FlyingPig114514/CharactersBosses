<#
.SYNOPSIS
  飞书凭证 / 多维表格 / 群机器人的「一条命令自检」。

.DESCRIPTION
  为什么需要它：
    · `tools\feishu-create-bitable.ps1` 报 `code=10003 msg=invalid param` 时，
      90% 是「app_id 与 app_secret 不是同一个应用」（或者 secret 被重置过）。
      光看报错分不出来，所以这里把每一步的**原始返回**打出来。
    · 全部用 curl.exe + 临时 JSON 文件发请求：Windows PowerShell 5.1 的
      `[System.Net.Http.HttpClient]` 经常加载不到，`-Body` 传字符串还会把 JSON 弄坏（以前的坑）。

  用法（三条互不影响，想测哪条给哪条的参数）：

    # ① 只验证 app_id + app_secret（换 tenant_access_token）
    powershell -ExecutionPolicy Bypass -File tools\feishu-check.ps1 `
        -AppId cli_xxxxxxxx -AppSecret xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx

    # ② 顺便把「这个应用能看到的第一个多维表格」的 app_token / table_id / 字段都列出来
    powershell -ExecutionPolicy Bypass -File tools\feishu-check.ps1 `
        -AppId cli_xxxxxxxx -AppSecret xxxxxxxx `
        -BitableAppToken X8n7b9Eg3GaDTWRsfJ9ncJntH -TableId tblazV2VYw2EZRjg

    # ③ 只测群机器人 webhook（不需要 app_id / secret）
    powershell -ExecutionPolicy Bypass -File tools\feishu-check.ps1 `
        -BotHookUrl https://open.feishu.cn/open-apis/bot/v2/hook/xxxxxxxx

  成功长什么样：
    · ① 返回 `code: 0` + `tenant_access_token: t-...`（说明凭证没问题，10003 是别的参数）
    · ② 打出表的列表 + 每个字段的 field_name / type
    · ③ 返回 `{"code":0,"msg":"success"}`（并且飞书群里会收到一条测试消息）
#>
[CmdletBinding()]
param(
    [string]$AppId,
    [string]$AppSecret,
    [string]$BitableAppToken,
    [string]$TableId,
    [string]$BotHookUrl
)

$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$Domain = 'https://open.feishu.cn'
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Write-Section([string]$text) {
    Write-Host ''
    Write-Host ('─' * 68) -ForegroundColor DarkGray
    Write-Host $text -ForegroundColor Cyan
    Write-Host ('─' * 68) -ForegroundColor DarkGray
}

# curl.exe 收发 JSON（临时文件传 body，避免 PS 5.1 把 JSON 弄成表单）
function Invoke-CurlJson {
    param(
        [ValidateSet('GET', 'POST')][string]$Method,
        [string]$Url,
        $Body = $null,
        [string]$Bearer = ''
    )

    $tmp = [System.IO.Path]::GetTempFileName()
    try {
        $args = @('-s', '-S', '-X', $Method, $Url, '-H', 'Content-Type: application/json; charset=utf-8')
        if ($Bearer) { $args += @('-H', "Authorization: Bearer $Bearer") }
        if ($null -ne $Body) {
            [System.IO.File]::WriteAllText($tmp, ($Body | ConvertTo-Json -Compress -Depth 8), $Utf8NoBom)
            $args += @('--data-binary', "@$tmp")
        }
        $raw = & curl.exe @args 2>&1
        return ($raw -join "`n").Trim()
    }
    finally {
        Remove-Item $tmp -Force -ErrorAction SilentlyContinue
    }
}

function ConvertTo-JsonSafe([string]$text) {
    try { return ($text | ConvertFrom-Json) } catch { return $null }
}

# ─────────────────────────────────────────────────────────────
# ③ 群机器人（最简单：没有 app / secret / 权限）
# ─────────────────────────────────────────────────────────────
if ($BotHookUrl) {
    Write-Section '③ 群机器人 webhook 自检'
    if ($BotHookUrl -notmatch '^https://open\.feishu\.cn/open-apis/bot/v2/hook/') {
        Write-Host '⚠ 这个地址不像飞书自定义机器人的 webhook（应以 https://open.feishu.cn/open-apis/bot/v2/hook/ 开头）' -ForegroundColor Yellow
    }
    $body = @{ msg_type = 'text'; content = @{ text = 'CharacterBosses 自检消息：webhook 通了 ✅（收到这条就说明模组能往这里发数据）' } }
    $resp = Invoke-CurlJson -Method POST -Url $BotHookUrl -Body $body
    Write-Host "返回：$resp"
    $obj = ConvertTo-JsonSafe $resp
    if ($obj -and ($obj.code -eq 0 -or $obj.StatusCode -eq 0)) {
        Write-Host '✅ 群机器人可用：把这条 webhook 填进 charbosses_config.cfg 的 feishuBotUrl 即可。' -ForegroundColor Green
    }
    else {
        Write-Host '❌ 群机器人不通：检查是不是复制漏了、或者机器人被移出群。' -ForegroundColor Red
    }
}

# ─────────────────────────────────────────────────────────────
# ① app_id + app_secret → tenant_access_token
# ─────────────────────────────────────────────────────────────
$token = ''
if ($AppId -or $AppSecret) {
    Write-Section '① app_id + app_secret 自检（tenant_access_token）'

    if (-not $AppId -or -not $AppSecret) {
        Write-Host '❌ 两个都要给：-AppId cli_xxx -AppSecret yyy' -ForegroundColor Red
    }
    else {
        Write-Host ("app_id     = {0}（长度 {1}）" -f $AppId, $AppId.Length)
        Write-Host ("app_secret = 长度 {0}（不打印内容）" -f $AppSecret.Length)
        if (-not $AppId.StartsWith('cli_')) { Write-Host '⚠ app_id 一般以 cli_ 开头，请确认没复制成别的东西' -ForegroundColor Yellow }
        if ($AppSecret.Length -ne 32) { Write-Host ("⚠ app_secret 一般是 32 位，你给的是 {0} 位 ⇒ 大概复制漏了/多了字符" -f $AppSecret.Length) -ForegroundColor Yellow }

        $body = @{ app_id = $AppId; app_secret = $AppSecret }
        $resp = Invoke-CurlJson -Method POST -Url "$Domain/open-apis/auth/v3/tenant_access_token/internal" -Body $body
        Write-Host "返回：$resp"

        $obj = ConvertTo-JsonSafe $resp
        if ($obj -and $obj.code -eq 0 -and $obj.tenant_access_token) {
            $token = [string]$obj.tenant_access_token
            Write-Host ("✅ 凭证有效：拿到 tenant_access_token（{0}…，{1} 秒后过期）" -f $token.Substring(0, [Math]::Min(12, $token.Length)), $obj.expire) -ForegroundColor Green
        }
        elseif ($obj -and ($obj.code -eq 10003 -or $obj.code -eq 10014)) {
            Write-Host '' 
            Write-Host '❌ 凭证被拒（10003 invalid param / 10014 app_secret invalid）。按可能性排：' -ForegroundColor Red
            Write-Host '   1. **app_id 和 app_secret 不是同一个应用**（最常见）：'
            Write-Host '      去开放平台 →「开发者后台」→ 选应用 →「凭证与基础信息」，'
            Write-Host '      在**同一个页面**上同时复制 App ID 和 App Secret（App Secret 点「重置」后只显示一次）。'
            Write-Host '   2. 换了账号/租户：旧账号的应用 ID 配新账号的 secret（或反过来）必然 10003。'
            Write-Host '      ⇒ app_id 也要从**当前这个账号**的开发者后台重新复制。'
            Write-Host '   3. 这个应用不是「企业自建应用」（商店应用拿不到 tenant_access_token）。'
        }
        elseif ($obj -and $obj.code -eq 10002) {
            Write-Host '❌ app_id 不存在（10002）：多半是把别的应用的 ID 粘过来了。' -ForegroundColor Red
        }
        else {
            Write-Host '❌ 没拿到 token，见上面的原始返回。' -ForegroundColor Red
        }
    }
}

# ─────────────────────────────────────────────────────────────
# ② 多维表格：列目录 / 列字段
# ─────────────────────────────────────────────────────────────
if ($BitableAppToken -or $TableId) {
    Write-Section '② 多维表格自检'

    if (-not $token) {
        Write-Host '跳过：上面没拿到 tenant_access_token（先修 ①）。' -ForegroundColor Yellow
    }
    elseif (-not $BitableAppToken) {
        Write-Host '❌ 需要 -BitableAppToken（多维表格 URL 里 /base/ 后面那一段）。' -ForegroundColor Red
        Write-Host '   例：https://xxx.feishu.cn/base/X8n7b9Eg3GaDTWRsfJ9ncJntH?table=tblazV2VYw2EZRjg'
        Write-Host '       → -BitableAppToken X8n7b9Eg3GaDTWRsfJ9ncJntH -TableId tblazV2VYw2EZRjg'
    }
    else {
        # 先把这张表**真正的** URL 打出来：应用建的表在应用空间里，
        # 浏览器要用「你的租户域名」，`open.feishu.cn/base/...` 会显示「不存在」（用户 2026-10-30 踩过）。
        $metaRaw = Invoke-CurlJson -Method GET -Url "$Domain/open-apis/bitable/v1/apps/$BitableAppToken" -Bearer $token
        $meta = ConvertTo-JsonSafe $metaRaw
        if ($meta -and $meta.code -eq 0 -and $meta.data.app) {
            Write-Host ("表格名称：{0}" -f $meta.data.app.name)
            if ($meta.data.app.url) {
                Write-Host ("表格链接（用这个开）：{0}" -f $meta.data.app.url) -ForegroundColor Green
            }
            else {
                Write-Host "表格链接：把下面这段拼到**你的租户域名**后面（浏览器地址栏里看到的 xxx.feishu.cn）：" -ForegroundColor Yellow
                Write-Host ("   https://<你的租户>.feishu.cn/base/{0}?table={1}" -f $BitableAppToken, $TableId)
            }
        }
        else {
            Write-Host ("⚠ 取不到表格信息：{0}" -f $metaRaw) -ForegroundColor Yellow
        }
        $resp = Invoke-CurlJson -Method GET -Url "$Domain/open-apis/bitable/v1/apps/$BitableAppToken/tables?page_size=50" -Bearer $token
        $obj = ConvertTo-JsonSafe $resp
        if (-not $obj -or $obj.code -ne 0) {
            Write-Host "❌ 读表列表失败：$resp" -ForegroundColor Red
            Write-Host ''
            Write-Host '按 code 处理：'
            Write-Host '  · 91403 / 1254302（无权限）：把这个**应用**加成表格协作者 ——'
            Write-Host '    打开多维表格 → 右上角「…」→「添加文档应用」（或「协作者」）→ 搜应用名 → 选「可编辑」。'
            Write-Host '  · 99991663 / 99991661：token 无效（重新跑 ①）。'
            Write-Host '  · 1254004：app_token 不对（要从表格 URL 里取）。'
        }
        else {
            $tables = @($obj.data.items)
            Write-Host ("✅ 应用能看到 {0} 张数据表：" -f $tables.Count) -ForegroundColor Green
            foreach ($t in $tables) { Write-Host ("   · {0}  （table_id = {1}）" -f $t.name, $t.table_id) }

            $targetTable = $TableId
            if (-not $targetTable -and $tables.Count -ge 1) {
                $targetTable = $tables[0].table_id
                Write-Host ("（没给 -TableId，用第一张：{0}）" -f $targetTable) -ForegroundColor DarkGray
            }

            if ($targetTable) {
                $resp2 = Invoke-CurlJson -Method GET -Url "$Domain/open-apis/bitable/v1/apps/$BitableAppToken/tables/$targetTable/fields?page_size=100" -Bearer $token
                $obj2 = ConvertTo-JsonSafe $resp2
                if (-not $obj2 -or $obj2.code -ne 0) {
                    Write-Host "❌ 读字段失败：$resp2" -ForegroundColor Red
                }
                else {
                    $fields = @($obj2.data.items)
                    Write-Host ("✅ 表 {0} 有 {1} 个字段：" -f $targetTable, $fields.Count) -ForegroundColor Green
                    foreach ($f in $fields) { Write-Host ("   · {0}（type {1}）" -f $f.field_name, $f.type) }
                    Write-Host ''
                    Write-Host '模组会**只写表里存在的列**，所以这些列名不用和我给的清单一致；' -ForegroundColor DarkGray
                    Write-Host '想多统计哪一项，就在表里加同名的一列（例如 result / boss / act / turns）。' -ForegroundColor DarkGray
                }
            }
        }
    }
}

if (-not $AppId -and -not $AppSecret -and -not $BotHookUrl -and -not $BitableAppToken -and -not $TableId) {
    Write-Section '没有给任何参数'
    Write-Host '看脚本开头注释里的三种用法（①凭证 ②多维表格 ③群机器人）。'
}

Write-Host ''
