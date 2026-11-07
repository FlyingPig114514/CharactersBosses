# 飞书中转（relay）部署状态 / 决策记录

写于 2026-11-07。承接 `docs/plans/feishu-relay.md`。

## 1. 目标

App Secret 不能再硬编码进 mod（≤0.2.460 已公开泄露）。玩家端只持有一个**只能写入、不能改表**的中转地址，
Secret 只存在于中转服务器上。

## 2. 已经做完的部分（可复用，不是白干）

- `tools/relay/feishu-relay.js`：Workers 风格的 handler。白名单字段、按批合并、可选 `RELAY_TOKEN` 头校验、
  上游 4xx 与网络错误分开返回。`wrangler deploy --dry-run` 通过（4.32 KiB / gzip 1.69 KiB）。
- `tools/relay/wrangler.toml`：`name = "charbosses-relay"`，`RELAY_REQUIRED = "1"`。
- Cloudflare 账号侧：wrangler OAuth 登录成功；`workers.dev` 子域已注册为 `flyingpig114514`。
- mod 侧：`BossConfig` 里 5 个硬编码飞书常量已删除，只剩 `private const string BuiltInRelayUrl = "";`，
  现在玩家上传走「配置里自己填」，不会外泄任何凭据。

## 3. 卡住的地方（两个，第二个是致命的）

1. `wrangler deploy` 返回 `code: 10034 You need to verify your email address to use Workers`。
   账号 `（作者邮箱已脱敏）` 没验证邮箱。需要人去 163 邮箱点验证链接，我无法代做。
2. **就算验证了也没用**：`*.workers.dev` 在中国大陆被墙。用户截图的
   `welcome.developers.workers.dev` 直接 `ERR_CONNECTION_TIMED_OUT` 就是证据。
   中转地址是给**玩家**（主要在国内）用的，玩家连不上 = 这个方案对绝大多数玩家等于不存在。

结论：Cloudflare Workers 这条路在国内玩家场景下基本作废，不要为了它去验证邮箱/绑域名硬推。

## 4. 备选方案

| 方案 | 国内可达 | 需要什么 | 数据形态 | 工作量 |
|---|---|---|---|---|
| A. Cloudflare Workers | ❌ 被墙 | 验证邮箱 + 可选自定义域名 | 结构化写表 | 已完成 90%，但玩家用不了 |
| B. 腾讯云函数 SCF（Node.js + HTTP 触发器） | ✅ | 腾讯云账号 + 实名 + API 密钥（我来部署） | 结构化写表，现有统计脚本不用改 | 中：handler 从 `fetch(request)` 改成 `event/context` 入口，约 40 行胶水 |
| C. 飞书群自定义机器人 webhook | ✅ | 群里加一个机器人，拿 hook URL | **只能发消息**，不是表格行；统计要改成读群消息或再转发 | 小：mod 直接 POST，连中转都不用 |
| D. 不做中转，本地 only | ✅ | 无 | 玩家不发，作者自己看本地日志 | 零；最彻底地解决泄密，但拿不到玩家数据 |

安全性排序（从好到差）：B ≈ C > A > 裸 Secret。
C 的泄露后果只是「别人能往你的群灌垃圾消息」，这是客户端遥测的标准做法（类似 Sentry DSN），可接受；
B 泄露后果是「别人能往中转灌假数据」，加个 `RELAY_TOKEN` 就够。

## 4.1 费用与实名（查证过的，2026-11-07）

腾讯云 SCF 官方计费页原文：

> 腾讯云云函数免费额度和计费方式已于 2022 年 6 月 1 日 0:00（北京时间）起正式调整。
> 新用户**前三个月**将获得更多用量的免费额度，**第四个月开始不再享受免费额度**。
> 免费额度不包含 **Web 函数响应流量**。

即：
- **不是长期免费**。第 4 个月起按量后付费：资源使用费 + 调用次数费 + 外网出流量 + Web 函数响应流量。
- 按我们的量级（假设 200 次/天、128MB×200ms）估算，量级在**每月几分钱到几毛钱**，但需要**绑定支付方式**，
  存在欠费/被停的风险，不是真正意义上的 0 元。
- 实名信息只存在腾讯云账号里，**我看不到也拿不到**。我能看到的只有你给的 API 密钥能做的事。
- 密钥有两条路：① 你建一个只授 `QcloudSCFFullAccess` 的**子账号**密钥给我，用完你删掉；
  ② **完全不给我密钥**——我打好 zip + 写逐步点击说明，你自己在控制台传上去（约 6 步，最安全）。
- 飞书侧不需要实名、不需要域名、不需要备案。

对比之下，**飞书群自定义机器人 webhook（方案 C）才是真正 0 元、0 实名、0 平台账号**的方案：
`https://open.feishu.cn/open-apis/bot/v2/hook/<uuid>` 直接 POST，国内可达。

## 5. 最终决定（2026-11-07）：改用**飞书群自定义机器人 webhook**

- 0 元、0 实名、0 第三方账号、0 域名。
- 玩家端只持有一个「只能往一个群发消息」的地址 ⇒ 泄露后果可控（这是客户端遥测的标准做法）。
- 作者本机脚本 `tools/feishu-group-pull.ps1` 用自有应用凭据读群消息 → 写回原多维表格 ⇒ **字段和现在完全一样**。
- 详细操作手册：`docs/plans/feishu-group-bot.md`（含需要用户点的三步 + 定时任务命令）。
- 飞书官方「多维表格自动化 webhook 触发」已查证：基础版不支持（需专业版 35 元/人/月），所以不走这条。

已完成（v0.2.462，已构建并部署到游戏目录）：

- `BossConfig.BuiltInRelayUrl` 现在**两种地址都认**：`/open-apis/bot/v2/hook/…` ⇒ 回填 `feishuBotUrl`；其它 https ⇒ 回填 `telemetryUrl`。
- `Telemetry.UploadToFeishuBot` 重写：每条记录带**原始 JSON 一行**、按 8 条一批全部发出、
  解析飞书 body 的 `code`（飞书拒收也返回 HTTP 200），并修掉旧版「超过 20 条静默丢后半段」的丢数据 bug。
- 实测部署后的 `Impl107/111.dll`：搜得到 `v0.2.462`，搜不到 app_id / app_secret / app_token / table_id。

待用户操作：见 `docs/plans/feishu-group-bot.md` §3（开权限 + 建群 + 两个机器人），拿到 webhook 后填常量、重建、冒烟测试。

## 6. 原备选方案对比（留档）

- 想要「结构化、能自动统计、玩家真能连上」→ **选 B（腾讯云函数）**。
- 想要「今晚就能上线、零基础设施」→ **选 C（群机器人 webhook）**，但要接受统计退化为读消息。
- 只想止损、不追玩家数据 → **选 D**，直接把 `BuiltInRelayUrl` 留空，配置里默认不上传。

无论选哪个，旧的 Feishu App Secret 都建议去飞书开放平台**重置一次**（已在公开 DLL 里出现过）。

## 7. 剩余待办

1. 用户按 `docs/plans/feishu-group-bot.md` §3 做完（开权限 + 建群 + 两个机器人）并给我 webhook 地址。
2. 拿到地址 → 填 `BossConfig.BuiltInRelayUrl` → 重建 + `install-mod.bat` → 冒烟测试（POST 假记录进群 → 脚本捞回来 → 写表成功）。
3. 注册每小时定时任务（命令见 §6 of `feishu-group-bot.md`）。
4. 旧 App Secret 已由用户重置（旧 secret 现在 `code=10014 invalid`，新 secret 验证 `code=0`）；本机 cfg 里的旧值已同步换成新值。
5. 用户说「打包」时才 `tools\pack-dist.ps1` + Workshop 上传，不要提前放 zip。
