# 数据收集改道「飞书群机器人」——操作手册

写于 2026-11-07。上游决策见 `docs/plans/feishu-relay-deploy-status.md`。

## 1. 为什么不是 Cloudflare Worker

- `wrangler deploy` 被 `code:10034 You need to verify your email address` 挡住（要人去 163 邮箱点链接）；
- 更致命：`*.workers.dev` 在国内打不开（`welcome.developers.workers.dev` → `ERR_CONNECTION_TIMED_OUT` 实拍）。
  中转地址是给**玩家**用的，玩家连不上就等于不存在。
- 飞书官方「多维表格自动化 webhook 触发」查过了：**基础版不支持**，需专业版 35 元/人/月 ⇒ 不选。

## 2. 现在的架构

```
玩家模组 (v0.2.462+)
   │  只有这一个地址：https://open.feishu.cn/open-apis/bot/v2/hook/xxxx
   │  （零鉴权，最坏结果＝别人能往那个群发垃圾消息）
   ▼
飞书群（作者私人群，一个人也行）
   │  每条消息 = 标题 + 最多 8 条记录的**原始 JSON**
   ▼
作者本机  tools\feishu-group-pull.ps1
   │  用作者自己的自建应用凭据（只在 %APPDATA%\SlayTheSpire2\mod_configs\...\charbosses_config.cfg）
   │  读群消息 → 解析 JSON → 按内容指纹去重
   ▼
原来的多维表格（同一张表、同一批字段）→ 现有统计脚本照旧用
```

关键点：**DLL 里没有任何飞书凭据**（已实测 `CharacterBosses.Impl107/111.dll` 里搜不到 app_id / app_secret / app_token / table_id），
app_secret 只存在作者本机配置里。玩家侧泄露的后果只是「能往一个群发消息」。

## 3. 需要你做的三步（一次性）

### ① 给应用开两个权限并发版

打开（应用 `APP_ID_REDACTED`）：

```
https://open.feishu.cn/app/APP_ID_REDACTED/auth?q=im:chat:readonly,im:message.history:readonly&op_from=openapi&token_type=tenant
```

点「开通权限」→ 左边「版本管理与发布」→ 创建版本 → 申请发布（个人版即时生效）。
顺便确认「应用能力」里**机器人**已添加。

### ② 建群 + 加一个「自定义机器人」（这个是给玩家端用的）

1. 飞书新建一个群（只有你自己也行），名字随便，建议 `CharacterBosses 数据`；
2. 群设置 → 群机器人 → 添加机器人 → **自定义机器人** → 名字随便；
3. 安全设置选**自定义关键词**，关键词填 `CharacterBosses`（模组发的消息第一行就带这个词）；**不要**选签名校验（那会又需要密钥）；
4. 复制 webhook 地址（形如 `https://open.feishu.cn/open-apis/bot/v2/hook/xxxxxxxx-xxxx-...`）发给我。

### ③ 同一个群再加一个「应用机器人」（这个是给我本机脚本用的）

**（已解决，留档说明当初为什么看不见应用）**：用 tenant_access_token 调 `bot/v3/info` 曾返回
`{"code":11205,"msg":"app do not have bot"}` ⇒ 应用没开「机器人」能力，所以群设置的「添加机器人」
列表里根本不会出现它。按顺序做：

1. 打开 <https://open.feishu.cn/app/APP_ID_REDACTED/baseinfo>；
2. 左侧「应用能力」→ **添加应用能力** → 选「**机器人**」→ 添加；
3. 左侧「权限管理」→ 开通这三个（实测缺一个都读不出消息，每个都**必须发布新版本**才生效）：
   - `im:chat:readonly`（列群，用来找到 chat_id）
   - `im:message.history:readonly` / `im:message:readonly`（会话历史）
   - **`im:message.group_msg`** ← 飞书把「获取群组中所有消息」当敏感权限单独管，
     只开前两个时接口仍报 `230027 ext=need scope: im:message.group_msg`。
     一键链接：`https://open.feishu.cn/app/APP_ID_REDACTED/auth?q=im:message.group_msg&op_from=openapi&token_type=tenant`
4. 左侧「版本管理与发布」→ **创建版本**（填个版本号，比如 `1.0.1`，更新说明随便）→ 保存 → **申请发布**
   （个人版即时通过；企业版要找管理员点通过）；
5. 发布通过后回群：群设置 → 群机器人 → 添加机器人 → 这时就能看到你的应用名了。

（只加自定义机器人不够：自定义机器人没有读历史消息的 API 身份。）

## 3.5 已经验证过的事实（2026-11-07）

- webhook `…/hook/00501773-d3c0-4a95-a553-a2042fd88054` 已实测可发：
  带 `CharacterBosses` 的消息返回 `{"code":0,"msg":"success"}`；
- 安全设置**关键词确实生效**：不带关键词的消息返回 `{"code":19024,"msg":"Key Words Not Found"}`
  ⇒ 模组消息头 `CharacterBosses 数据 N 条` 正好匹配，别改这个前缀；
- 读群消息**必须**有 `im:message.group_msg`（敏感权限，单独开、单独发版）：
  只有 `im:message.history:readonly` 时报 `230027 ext=need scope: im:message.group_msg`；
- **端到端跑通**：手工发一条带 JSON 的群消息 → `feishu-group-pull.ps1` 捞出 1 条 → 写进多维表格
  （31 个字段类型全部对：数字是数字、文本是文本、`seed` 是文本没丢精度）→ 复查命中 → 删除测试行确认干净；
- 定时任务 `CharacterBosses-FeishuPull`（每小时）已注册并试跑成功（`LastTaskResult = 0`）；
- ⚠ 用 PowerShell 命令行传 JSON 时别直接 `--data-binary "…中文…"`（PS 5.1 按 GBK 传参会返回 `9499 Bad Request`），
  要写进 UTF-8 **无 BOM** 文件再 `--data-binary "@文件"`（带 BOM 也会 `9499`）。模组用 HttpClient + UTF-8，不受影响。

## 4. 我这边收到 webhook 之后做

1. 把地址填进 `src/Hooks/Core/BossConfig.cs` 的 `BuiltInRelayUrl`（代码会认出 `/open-apis/bot/v2/hook/` ⇒ 回填 `feishuBotUrl`）；
2. 重新构建 + `install-mod.bat` 部署；
3. 冒烟测试：手工 POST 一条假记录进群 → 群里能看到、`feishu-group-pull.ps1` 能捞回来；
4. 顺手注册一个每小时跑的定时任务（见 §6）。

## 5. 脚本用法（`tools\feishu-group-pull.ps1`）

```powershell
# 干跑：只统计能捞到多少条，不写表
powershell -ExecutionPolicy Bypass -File tools\feishu-group-pull.ps1 -DryRun

# 真跑：读群消息 + 写回多维表格
powershell -ExecutionPolicy Bypass -File tools\feishu-group-pull.ps1

# 只导出本地 JSONL/CSV，不写表
powershell -ExecutionPolicy Bypass -File tools\feishu-group-pull.ps1 -NoBitable

# 指定群 / 指定起点
powershell -ExecutionPolicy Bypass -File tools\feishu-group-pull.ps1 -ChatName "CharacterBosses" -Since 2026-11-01
```

- 状态文件 `.feishu-group-state.json`：记住 `chat_id`、上次拉到的时间、已收记录的指纹（防重复写表）；
- 导出目录 `docs\plans\telemetry-import\`：每次跑都会落一份 `records-<时间>.jsonl` / `.csv`，写表失败也不丢数据；
- 字段类型按表里真实字段走（文本/数字/日期各自转换，多出来的字段跳过，不会整批被飞书拒收）。

## 6. 定时任务（可选，让效果和「玩家直连表格」完全等价）

```powershell
schtasks /Create /TN "CharacterBosses-FeishuPull" /SC HOURLY /MO 1 ^
  /TR "powershell -NoProfile -ExecutionPolicy Bypass -File \"D:\Sts2ModProject\Downfall Expansion Vanilla Characters as Bosses\tools\feishu-group-pull.ps1\"" /F
```

日志会直接进 `docs\plans\telemetry-import\`；想停就 `schtasks /Delete /TN "CharacterBosses-FeishuPull" /F`。

## 7. 已知差异 / 限制（写清楚，别自我感动）

| 项 | 以前（玩家直连多维表格） | 现在（群机器人 + 本机拉取） |
|---|---|---|
| 数据字段 | 22 个字段 | **完全一样**（就是模组 outbox 里的原始 JSON） |
| 延迟 | 秒级 | 取决于拉取频率（定时任务 ⇒ 最多 1 小时） |
| 玩家侧泄露风险 | **能拿到 app_secret ⇒ 能改你的表** | 只能往一个群发消息 |
| 失败重试 | 模组本地 outbox 重发 | 一样（模组还是先写 outbox，成功才删） |
| 群消息上限 | — | 一条消息 8 条记录；超过就分多条（旧版「超过 20 条静默丢后半段」的 bug 已修） |
| 飞书返回 HTTP 200 也可能是失败 | — | 已按 body 里的 `code`/`StatusCode` 判断 |

## 8. 回退

- 想换回直连多维表格：玩家在 cfg 里填 `feishuAppId/feishuAppSecret/feishuBitableAppToken/feishuTableId` 即可（模组里那条路径一直在，优先级最高）；
- 想彻底不联网：`BuiltInRelayUrl` 留空 + 弹窗里选「关闭」。
