# 用飞书「企业自建应用」自动创建 CharacterBosses 数据表（多维表格）
# =====================================================================
# 为什么用这个脚本（用户 2026-10-30 卡在这里）：
#   飞书「分享 → 添加协作者」的搜索框**只搜人/部门/用户组，搜不到自建应用**，
#   所以「把应用加为表格协作者」这一步在很多版本里根本做不到。
#   正确做法反过来：**让应用自己去建这张表** —— 应用就是所有者，之后所有写入畅通，
#   你只需要再把自己加成协作者用来查看。
#
# 前置（在 https://open.feishu.cn/app 里做好）：
#   1. 自建应用 →「权限管理」里加 **多维表格** 权限（bitable:app，或 查看、评论、编辑和管理多维表格）
#   2. 「版本管理与发布」→ 创建并发布版本（不发布的话 token 能拿到但接口会拒绝）
#   3. 记下 App ID / App Secret
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File tools\feishu-create-bitable.ps1 -AppId cli_xxx -AppSecret xxx
#   成功后它会打印 app_token / table_id，把这两个值填进
#   %APPDATA%\SlayTheSpire2\mod_configs\CharacterBosses\charbosses_config.cfg
#   （feishuAppId / feishuAppSecret / feishuBitableAppToken / feishuTableId）
#
#   -OpenAll        顺便把表格分享给「组织内可阅读」以外的东西？不需要，默认不动分享设置
#   -Name "xxx"     自定义多维表格名字（默认 CharacterBosses 数据）

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$AppId,
    [Parameter(Mandatory = $true)][string]$AppSecret,
    [string]$Name = 'CharacterBosses 数据',
    [string]$TableName = 'boss_fights'
)

$ErrorActionPreference = 'Stop'
$base = 'https://open.feishu.cn/open-apis'

# Windows PowerShell 5.1 默认用 TLS 1.0/1.1，连 open.feishu.cn 会报
# 「The underlying connection was closed」⇒ 强制 TLS 1.2
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch { }

# 飞书返回体是 UTF-8，而 PS 5.1 的控制台默认按 GBK 解码 curl 的 stdout
# ⇒ 报错里的中文会变成「搴旂敤灏氭湭寮€閫?」这种乱码（用户 2026-10-30 截图里就是）。这里统一成 UTF-8。
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
try { $OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }

function Post-Json([string]$url, $body, [string]$token) {
    # ⚠ 为什么用 curl.exe（本轮连踩三个坑）：
    #   ① `Invoke-RestMethod -Body [byte[]]` 会把 Content-Type 变成 form-urlencoded ⇒ 飞书回 10003；
    #   ② `System.Net.Http.HttpClient` 在 PS 5.1 里要先 Add-Type 才认（报「找不到类型」）；
    #   ③ 就算 `-Body` 传字符串 + `-ContentType`，PS 5.1 在部分环境仍会把 body 发坏 ⇒ 依旧 10003。
    #   curl.exe 是 Windows 自带的：JSON 先写进临时文件，`--data-binary @文件` 原样发送，
    #   返回体原样读回。这样再报「参数非法」就真的只可能是凭据/租户问题，而不是我们的工具。
    $json = $body | ConvertTo-Json -Depth 12 -Compress
    $tmp = [System.IO.Path]::GetTempFileName()
    try {
        [System.IO.File]::WriteAllText($tmp, $json, (New-Object System.Text.UTF8Encoding($false)))
        $curl = Join-Path $env:SystemRoot 'System32\curl.exe'
        if (-not (Test-Path $curl)) { $curl = 'curl.exe' }

        $curlArgs = @('-s', '-S', '-X', 'POST', $url,
                      '-H', 'Content-Type: application/json; charset=utf-8',
                      '--data-binary', "@$tmp")
        if ($token) { $curlArgs += @('-H', "Authorization: Bearer $token") }

        $out = & $curl @curlArgs 2>&1 | Out-String
        if ([string]::IsNullOrWhiteSpace($out)) {
            Write-Host '请求没有返回内容（网络/代理？）' -ForegroundColor Red
            throw 'empty response'
        }
        try { return ($out | ConvertFrom-Json) }
        catch {
            Write-Host ('飞书返回的不是 JSON：{0}' -f $out.Trim()) -ForegroundColor Red
            throw
        }
    }
    finally {
        try { Remove-Item $tmp -Force -ErrorAction SilentlyContinue } catch { }
    }
}

# curl.exe 发 GET（带 Bearer），返回 JSON 对象
function Invoke-CurlGet([string]$url, [string]$token) {
    $curl = Join-Path $env:SystemRoot 'System32\curl.exe'
    if (-not (Test-Path $curl)) { $curl = 'curl.exe' }
    $args = @('-s', '-S', '-X', 'GET', $url, '-H', 'Content-Type: application/json; charset=utf-8')
    if ($token) { $args += @('-H', "Authorization: Bearer $token") }
    $out = & $curl @args 2>&1 | Out-String
    return $out
}
# ── 1. 换 tenant_access_token ──
# 用户 2026-10-30：手抄 secret 出现 10003 invalid param —— 这里去掉两端空白，
# 并把「长度」打出来（secret 本身不打印），方便一眼看出是不是复制短了/多了。
$AppId = $AppId.Trim()
$AppSecret = $AppSecret.Trim()
Write-Host '[1/3] 获取 tenant_access_token ...'
Write-Host ("      app_id = {0}（长度 {1}）；app_secret 长度 {2}" -f $AppId, $AppId.Length, $AppSecret.Length)
$tok = Post-Json "$base/auth/v3/tenant_access_token/internal" @{ app_id = $AppId; app_secret = $AppSecret } $null
if (-not $tok.tenant_access_token) {
    Write-Host ("失败：code={0} msg={1}" -f $tok.code, $tok.msg) -ForegroundColor Red
    if ($tok.code -eq 10003 -or $tok.code -eq 10014) {
        Write-Host '  → 基本可以断定 App Secret 抄错了（10003=参数非法 / 10014=app_secret 无效）。' -ForegroundColor Yellow
        Write-Host '    别手打：回到「凭证与基础信息」，点 App Secret 右边的复制按钮，或先点小眼睛再整段复制。' -ForegroundColor Yellow
    }
    Write-Host '  → 其它原因：应用不是「企业自建应用」；App ID 属于另一个应用；应用未发布。'
    exit 1
}
$token = $tok.tenant_access_token
Write-Host '      ok'

# ── 2. 建多维表格（应用即所有者） ──
Write-Host "[2/3] 创建多维表格「$Name」 ..."
$app = Post-Json "$base/bitable/v1/apps" @{ name = $Name } $token
if ($app.code -ne 0 -or -not $app.data.app.app_token) {
    Write-Host ("失败：code={0} msg={1}" -f $app.code, $app.msg) -ForegroundColor Red
    Write-Host '  → 多半是没给应用「多维表格」权限，或版本没发布。'
    exit 1
}
$appToken = $app.data.app.app_token
Write-Host ("      app_token = {0}" -f $appToken)

# ── 3. 建数据表 + 16 个字段 ──
# 字段类型：1=多行文本 2=数字 3=单选
$fields = @(
    @{ field_name = 'at';          type = 1 },
    @{ field_name = 'kind';        type = 1 },
    @{ field_name = 'mod';         type = 1 },
    @{ field_name = 'result';      type = 3; property = @{ options = @(@{ name = 'win' }, @{ name = 'lose' }) } },
    @{ field_name = 'act';         type = 2 },
    @{ field_name = 'encounter';   type = 1 },
    @{ field_name = 'lineup';      type = 1 },
    @{ field_name = 'boss';        type = 1 },
    @{ field_name = 'ascension';   type = 2 },
    @{ field_name = 'players';     type = 2 },
    @{ field_name = 'bossMaxHp';   type = 2 },
    @{ field_name = 'bossHp';      type = 2 },
    @{ field_name = 'turns';       type = 2 },
    @{ field_name = 'seed';        type = 1 },
    @{ field_name = 'cfg';         type = 1 },
    @{ field_name = 'line';        type = 1 }
)

Write-Host "[3/3] 创建数据表「$TableName」与 16 个字段 ..."
$table = Post-Json "$base/bitable/v1/apps/$appToken/tables" @{
    table = @{
        name                 = $TableName
        default_view_name    = '全部数据'
        fields               = $fields
    }
} $token
if ($table.code -ne 0 -or -not $table.data.table_id) {
    Write-Host ("失败：code={0} msg={1}" -f $table.code, $table.msg) -ForegroundColor Red
    exit 1
}
$tableId = $table.data.table_id

# ── 4. 问一下这张表真正的 URL（应用建出来的表在**应用的空间**里，
#      地址必须是「你的租户域名」，`https://open.feishu.cn/base/...` 是**接口域名**，
#      浏览器打开会说「不存在」—— 用户 2026-10-30 就踩了这个）。
Write-Host ''
Write-Host '[4/4] 取这张表的真实链接 ...'
$realUrl = ''
try {
    $meta = (Invoke-CurlGet "$base/bitable/v1/apps/$appToken" $token) | ConvertFrom-Json
    if ($meta.code -eq 0 -and $meta.data.app.url) { $realUrl = [string]$meta.data.app.url }
} catch { }
if (-not $realUrl) { $realUrl = "https://<你的租户>.feishu.cn/base/$appToken?table=$tableId" }

Write-Host ''
Write-Host '====================== 完成了 ======================' -ForegroundColor Green
Write-Host ("app_token = {0}" -f $appToken)
Write-Host ("table_id  = {0}" -f $tableId)
Write-Host ("表格链接  = {0}" -f $realUrl) -ForegroundColor Green
Write-Host ''
Write-Host '⚠ 注意：这张表的所有者是**应用**（应用空间里），所以：' -ForegroundColor Yellow
Write-Host '   · 链接域名必须是 `https://<你的租户>.feishu.cn/base/...`；' -ForegroundColor Yellow
Write-Host '     用 `open.feishu.cn` 打开会说「不存在」（那是接口域名，不是文档域名）。' -ForegroundColor Yellow
Write-Host '   · 你自己要能看，得先把自己加成协作者，或者把表改成「组织内可阅读/可编辑」：' -ForegroundColor Yellow
Write-Host '     直接跑 tools\feishu-export.ps1 也能把数据导成 CSV（应用读自己的表不需要分享）。' -ForegroundColor Yellow
Write-Host ''
Write-Host '把下面这段填进 %APPDATA%\SlayTheSpire2\mod_configs\CharacterBosses\charbosses_config.cfg：'
Write-Host ''
Write-Host ('  "feishuAppId": "{0}",' -f $AppId)
Write-Host '  "feishuAppSecret": "<你的 secret>",'
Write-Host ('  "feishuBitableAppToken": "{0}",' -f $appToken)
Write-Host ('  "feishuTableId": "{0}"' -f $tableId)
Write-Host ''
Write-Host '提醒：这张表的所有者是**应用**，你要看数据就点上面的链接，用你的账号打开并' -ForegroundColor Yellow
Write-Host '      「添加自己为协作者（可管理）」，否则你自己可能没有编辑/查看权限。' -ForegroundColor Yellow
