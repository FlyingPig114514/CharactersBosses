# 飞书中转站：把 app_secret 从客户端里拿掉（2026-11-07）

## 为什么必须改

现在 `src/Hooks/Core/BossConfig.cs` 里有 4 个 base64 常量（app_id / **app_secret** / app_token / table_id），
它们**会被编译进 DLL**，而 DLL 是发到创意工坊的公开文件 —— 任何人 `strings` / 反编译就能拿到 app_secret
（我实测过：按 UTF-16 在两份 `CharacterBosses.Impl*.dll` 里都能搜到）。
base64 只是不让一眼看见，**不是加密**。

结论：**任何内嵌在客户端的长期密钥都等于公开**。正确做法是客户端只 POST 到一个中转站，
密钥存在中转站的服务端环境变量里。

> ⚠ 顺序很重要：**先去飞书开放平台重置 App Secret**（旧 secret 已在公开的 DLL 里，视为已泄露），
> 再部署中转站，再发版。删代码/改 git 历史都救不回已泄露的密钥。

## 中转站长什么样

`tools/relay/feishu-relay.js` —— 一个 Cloudflare Worker（免费额度足够：每天 10 万次请求）：

```
玩家客户端  --POST {"mod":…, "records":[ {...}, … ]}-->  Worker
Worker      --带 app_secret 换 tenant_access_token-->      open.feishu.cn
Worker      --batch_create-->                              你的多维表格
```

Worker 里做的事：校验方法/大小/条数 → 可选校验 `?t=` 串 → 按**字段白名单**清洗（表里没有的字段直接丢弃，
避免整批被飞书拒收）→ 分成「对局表 / 杂项表」两批 → 写进飞书。返回 `{"ok":true,"written":N}`。

## 部署（Cloudflare Worker，两条路选一条）

### A. 网页版（完全不装工具，最快）

1. 登录 <https://dash.cloudflare.com/> → **Workers & Pages** → **Create** → **Worker** → 起名 `charbosses-relay` → Deploy。
2. 点 **Edit code**，把 `tools/relay/feishu-relay.js` 全文粘进去 → **Deploy**。
3. 回到该 Worker → **Settings → Variables and Secrets**，加这几个（**App Secret 一定要用 Secret 类型**）：

   | 名字 | 值 |
   |---|---|
   | `FEISHU_APP_ID` | `cli_xxx`（飞书开放平台 → 你的应用 → 凭证与基础信息） |
   | `FEISHU_APP_SECRET` | **重置后**的新 secret |
   | `BITABLE_APP_TOKEN` | 多维表格 URL 里 `/base/<这一段>` |
   | `TABLE_ID` | 数据表 id，`tblXXXX` |
   | `DIAG_TABLE_ID` | 可选，杂项表 id |
   | `RELAY_TOKEN` | 可选，随便一串随机字符（下面拼进 URL） |

4. 部署完拿到地址形如 `https://charbosses-relay.<你的子域>.workers.dev`。
   若设了 `RELAY_TOKEN`，**客户端要用的完整 URL** 是：
   `https://charbosses-relay.<你的子域>.workers.dev/?t=<RELAY_TOKEN>`

### B. 命令行（wrangler）

```powershell
cd tools\relay
npx wrangler login
npx wrangler deploy
npx wrangler secret put FEISHU_APP_ID
npx wrangler secret put FEISHU_APP_SECRET     # 粘贴新 secret
npx wrangler secret put BITABLE_APP_TOKEN
npx wrangler secret put TABLE_ID
npx wrangler secret put DIAG_TABLE_ID         # 可选
npx wrangler secret put RELAY_TOKEN           # 可选
```

自检（应该返回 `{"ok":true,...}` 或 4xx 的明确报错，而不是 5xx）：

```powershell
curl -s -X POST "https://<你的 worker>/?t=<RELAY_TOKEN>" -H "content-type: application/json" `
  -d '{\"mod\":\"manual-test\",\"records\":[{\"kind\":\"diag\",\"level\":\"info\",\"message\":\"relay smoke test\"}]}'
```

## 模组侧要怎么改（我下一步做）

1. **删掉** `BossConfig.cs` 里那 4 个 base64 常量与它们的回填逻辑（app_id / app_secret / app_token / table_id）；
2. 加一个 **`BuiltInRelayUrl`**（明文 URL，不含密钥），`BackfillBuiltInEndpoints` 改成「把它填进 `telemetryUrl`」；
3. 玩家端因此只走「通用 URL」上传路径（`UploadToGenericUrl`）；飞书那条路只在**你自己机器**的
   `charbosses_config.cfg` 里填齐四项时才走（作者本机直连，不需要中转）。**这样 DLL 里就再也不含任何密钥。**

## 其它要注意

- 建议在 Cloudflare 后台加一条 **Rate limiting**（例如每 IP 每分钟 10 次）—— `RELAY_TOKEN` 是能挡住随手扫描，
  但它同样在 DLL 里（只是位置显眼），真正抗滥用的还是限流 + 字段白名单 + 条数上限。
- 表被灌垃圾也别慌：`tools\feishu-export.ps1` 定期导 CSV 备份，必要时清表（`tools\feishu-clear-table.ps1`）。
- 部署完记得把新的 Worker URL 写进 `BuiltInRelayUrl` 再发版；老客户端（旧 secret）上传会 401，更新即可。
