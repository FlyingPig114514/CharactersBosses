# 游玩数据收集：后端怎么搭、数据在哪看

> 用户 2026-10-30：「这玩家的 boss 战数据要是不上传我怎么进行平衡呢？…之后我从哪里查这些数据？」
>
> 结论先说：**模组本体只负责「发送」，收数据的地方要你自己提供一个 URL**（免费方案 15 分钟能搭好）。
> 配好后玩家在弹窗里选「开启」就会把匿名数据 POST 过来，你**在 Google 表格 / Cloudflare 后台**里看。

---

## 一、模组怎么发的（协议）

- 触发：每场**本模组的 Boss 战**结束（`boss_fight`），以及我们自己打出的**异常/失败日志行**（`diag`）。
  第一次开启时还有一条 `consent`（表示玩家同意过）。
- 前置：`A5 记录 = 开` **且** `charbosses_config.cfg` 里 `telemetryUrl` 非空 **且** `A6 上传 = 开`
  （玩家在弹窗里选「开启」时会自动勾上 A6）。
- 动作：`HTTP POST`，`Content-Type: application/json`，请求体：

```json
{
  "mod": "v0.2.233 / 2026-10-30（…）",
  "records": [
    {"kind":"boss_fight","result":"win","act":3,"encounter":"REGENT_STAR_BOSS_ENCOUNTER",
     "lineup":"REGENT_STAR","boss":"REGENT_BOSS","ascension":10,"players":1,
     "bossMaxHp":225,"bossHp":0,"turns":9,"seed":8461494873891562058,
     "cfg":{"mixedMatching":true,"sharePool":false,"asc10Act3OneEach":false,"bgmSource":1},
     "at":"2026-10-30T15:20:11Z","mod":"v0.2.233 …"},
    {"kind":"diag","line":"…","act":3,"encounter":"…","at":"…","mod":"…"}
  ]
}
```

- 失败重试：**不丢数据**。要发的记录先写进 `telemetry/upload-queue.jsonl`，POST 成功（2xx）才删；
  失败就留着下次再发（队列上限 400 条，超出丢最旧的）。
- 绝不阻塞游戏：整条链路在后台线程 + 15 秒超时，任何异常都吞掉。

---

## 二、国内可用：飞书（推荐，两种接法都已支持）

> 用户 2026-10-30：「我在国内谷歌表格打不开，能不能用飞书云文档之类的？」——可以，而且**两种都实现了**。

配置都在 `%APPDATA%\SlayTheSpire2\mod_configs\CharacterBosses\charbosses_config.cfg` 里手填（作者配一次），
玩家侧只需要 A5/A6 两个开关，什么都不用填。

### 接法 ①：飞书群机器人（最简单，零鉴权，5 分钟）

1. 飞书里建一个群（只有你自己也行），群设置 →「群机器人」→「添加机器人」→「**自定义机器人**」；
2. 复制它的 Webhook，形如 `https://open.feishu.cn/open-apis/bot/v2/hook/xxxx-xxxx`；
3. 写进配置：
   ```json
   "feishuBotUrl": "https://open.feishu.cn/open-apis/bot/v2/hook/xxxx-xxxx"
   ```
4. **去哪看数据**：就在那个飞书群里，每次上传会来一条文本消息，形如
   ```
   [战斗] win | 第3幕 REGENT_STAR_BOSS_ENCOUNTER | REGENT_BOSS | 进阶10 1人 | 回合9 | HP 0/225 | 种子8461494873891562058
   ```
   （一次最多列 20 条，剩下的写「…还有 N 条」。）

   优点：零成本、手机也能看；缺点：是聊天记录，不适合做统计。

### 接法 ②：飞书多维表格（结构化，能筛选/分组统计 —— 想认真做平衡就用这个）

1. **建表格**：飞书 →「多维表格」→ 新建，数据表列名请**照抄**这些（大小写一致）：
   `at | kind | mod | result | act | encounter | lineup | boss | ascension | players | bossMaxHp | bossHp | turns | seed | cfg | line`
   - 建议把 `act / ascension / players / bossMaxHp / bossHp / turns / seed` 设成 **数字**（我们按数字发，能排序求和）；
   - 其余设 **文本**（`cfg` 是一段 JSON 文本）。
2. **建应用拿密钥**：<https://open.feishu.cn/app> → 创建「企业自建应用」→
   「权限管理」里加 **多维表格** 的读写权限（`bitable:app` 或 记录新增 `bitable:app:readonly` 不够，需要可写）→
   「版本管理与发布」发布应用 → 「凭证与基础信息」里拿到 **App ID / App Secret**。
   - 如果你不是管理员，需要在飞书管理后台把该应用「可用范围」放开到你自己。
3. **拿表格的两个 id**：打开多维表格，URL 形如
   `https://xxx.feishu.cn/base/`**`BASCxxxxx`**`?table=`**`tblxxxxx`** →
   `Bascxxxxx` 是 **app_token**、`tblxxxxx` 是 **table_id**。
4. 写进配置：
   ```json
   "feishuAppId": "cli_xxx",
   "feishuAppSecret": "xxx",
   "feishuBitableAppToken": "BASCxxxxx",
   "feishuTableId": "tblxxxxx"
   ```
5. **去哪看数据**：就是这张多维表格。常用分析：
   - 加一个「仪表盘」→ 饼图/柱图，按 `result` 统计 = 胜率；
   - 「分组统计」：按 `boss` 分组、`result` 计数 → 每只 Boss 胜率；
   - 再按 `ascension`、`players` 分组 → 分层看「进阶 10 单人根除骨是不是太难」；
   - 想导出就用「导出为 CSV/Excel」。

> 模组内部走的是官方 API：`POST /open-apis/auth/v3/tenant_access_token/internal` 换 token（缓存 100 分钟），
> 再 `POST /open-apis/bitable/v1/apps/{app}/tables/{table}/records/batch_create`（100 条/批）。
> 失败会在 `mod_log.txt` 里报 `飞书多维表格写入失败：…` 并把数据留在本地队列重试。

### 接法 ③（可选）：自建/Cloudflare

`telemetryUrl` 保持通用 `{"mod":…,"records":[…]}` POST —— 自己写个 Worker / nginx 收下即可（见下文方案 B）。

---

## 附录：Google 表格（国内打不开，留作参考）

1. 打开 <https://sheets.google.com> 新建一个表格，命名为 `CharacterBosses 数据`。
   第一行表头（A1 起）：`at | kind | mod | result | act | encounter | lineup | boss | ascension | players | bossMaxHp | bossHp | turns | seed | cfg | line`
2. 表格里点「扩展程序 → Apps Script」，把下面代码粘进去，保存：

```javascript
function doPost(e) {
  const ss = SpreadsheetApp.getActiveSpreadsheet();
  const sh = ss.getSheetByName('Sheet1') || ss.insertSheet('Sheet1');
  const body = JSON.parse(e.postData.contents);
  (body.records || []).forEach(r => {
    const cfg = r.cfg ? JSON.stringify(r.cfg) : '';
    sh.appendRow([r.at || '', r.kind || '', r.mod || '', r.result || '', r.act || '',
                  r.encounter || '', r.lineup || '', r.boss || '', r.ascension || '',
                  r.players || '', r.bossMaxHp || '', r.bossHp || '', r.turns || '',
                  r.seed || '', cfg, r.line || '']);
  });
  return ContentService.createTextOutput('ok');
}
```

3. 部署：「部署 → 新增部署 → 类型：**Web 应用**」，
   - 执行身分：**我**
   - 谁可以存取：**任何人**（必须是这个，否则游戏发不进来）
   然后复制那个 `https://script.google.com/macros/s/XXXX/exec` 链接。
4. **把链接填进配置**（本机）：
   `%APPDATA%\SlayTheSpire2\mod_configs\CharacterBosses\charbosses_config.cfg`
   里的 `"telemetryUrl": "https://script.google.com/macros/s/XXXX/exec"`
   （改完重启游戏；玩家那边不用动 —— 他们只有 A5/A6 两个开关。）
5. **之后去哪看数据**：就是这张 **Google 表格**（手机装 Sheets App 也能看）。
   常用分析：`数据透视表`：行=`boss`，列=`result`，值=`COUNTA(at)` = 每只 Boss 的胜率；
   再按 `ascension` / `players` 分层，就能看出「进阶 10 单人时根除骨是不是太难」。

> Apps Script 免费额度足够（单日 20,000 次请求），一场 Boss 战才 1 条记录。

---

## 三、Cloudflare Worker（国内 `*.workers.dev` 可能不稳，建议绑自己域名；想自己存 JSON / 量大时用）

```javascript
export default {
  async fetch(request, env) {
    if (request.method !== 'POST') return new Response('ok');
    const body = await request.json();
    const lines = (body.records || []).map(r => JSON.stringify(r)).join('\n') + '\n';
    await env.BUCKET.put(`records/${Date.now()}-${Math.random().toString(36).slice(2)}.jsonl`, lines);
    return new Response('ok');
  }
}
```

- 建一个 R2 桶（`env.BUCKET` 绑定），Worker 路由填任意域名，把该 URL 写进 `telemetryUrl`。
- **去哪看**：Cloudflare 后台 → R2 → 桶里的 `records/*.jsonl`，或本地 `rclone`/`aws s3 cp` 拉下来聚合。
- 也可以用 **D1**（SQLite）：`INSERT INTO records(...)`，然后 `wrangler d1 execute` 查。

---

## 五、不用服务器：玩家手动给

不想搭后端也行：A5 区块里有 **「把数据文件复制到桌面」** 一行，玩家点一下就在桌面得到
`CharacterBosses-telemetry-<时间>.jsonl`，发到群里/创意工坊评论区/你的群文件即可。
适合人数少、先验证可行性。

---

## 六、隐私与合规（写进弹窗文案的那些）

- 只记「对战事实」：胜负、幕/场、进阶、人数、Boss 血量、回合数、种子、我们自己的报错行。
- **不记** SteamID / 昵称 / 存档路径 / IP（POST 目标是你自己的服务，日志里也不落 IP）。
- 默认关；玩家显式同意才记录；关掉后不再写、也不再发。
- 首次加载会用**原版通用弹窗**问一次（文案参考原版「上传游戏数据」那一组），选过的答案会记下来，
  以后不再打扰。

---

## 七、你接下来要做的三步

1. 按方案 A 搭好 Google 表格 + Apps Script，拿到 `.../exec` 链接；
2. 把链接填进 `charbosses_config.cfg` 的 `telemetryUrl`（本机一次即可）；
3. 正常玩几局 → 打开那张表格，就能看到 `boss_fight` 记录；后续版本我可以在配置界面加一个
   「数据汇总」入口（把表格链接直接显示出来），等你说要。
