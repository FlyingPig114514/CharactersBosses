# 喂给飞书 AI 建表的原话（CharacterBosses 数据表）

> 用途：飞书「模板中心 → 多维表格（AI 搭建）」里，把下面 **【给 AI 的需求】** 整段粘进去即可。
> 关键是**字段名必须和我们代码发送的 JSON 键完全一致**（大小写、下划线都一样），
> 否则 `bitable/records/batch_create` 会写不进去（`mod_log.txt` 会报「飞书多维表格写入失败」）。

## 换了飞书账户（新租户）之后要重做哪些东西

**要重做**：多维表格、企业自建应用、它的 app_id/app_secret、以及新的 URL（app_token/table_id）。
旧租户里的东西新账户看不到也用不了；确认不用了就把旧表和旧应用删掉（旧应用删掉后 secret 自动失效，无妨）。

**不用重做**：模组代码、下面那段给 AI 的需求原话（可以一模一样再用一次）。

### 新租户 10 分钟清单

1. 用新账户（建议「免费团队版、成员只有你」）登录 <https://open.feishu.cn/app>，能进开发者后台即可；
2. 新建多维表格（**AI 建表**或**空白表手动加列**都行，见下面两种做法）；
3. 建「企业自建应用」→ 权限加 **多维表格可读写（`bitable:app`）** → 发布 → 拿 **App ID / App Secret**；
4. 多维表格右上「分享」→ **把这个应用加为协作者（可编辑）** ← 最容易漏；
5. 复制新 URL 里的 `<app_token>` 与 `<table_id>`，连同 App ID / Secret 一起填进
   `%APPDATA%\SlayTheSpire2\mod_configs\CharacterBosses\charbosses_config.cfg`；
6. 重启游戏 → A5「记录本地对战数据」+ A6「上传游玩数据给作者」→ 打一场 Boss 战，
   看 `mod_log.txt` 里有没有 `飞书多维表格：表内字段 [...]` 和 `数据上传成功：本次 N 条`。

### 建表的两种做法

- **做法 A（推荐，稳）**：新建**空白多维表格**，按下面表里的 16 个字段名逐个「+ 添加字段」，类型照抄。
- **做法 B（快，但可能要收拾）**：把下面【给 AI 的需求】原话粘进 AI 建表。飞书 AI 常会自己改名/多加列
  （例如把 `result` 写成「胜局标记」），此时要么按日志提示把列名改回来，要么就让它缺着 ——
  v0.2.236 起模组会**先读表内字段再写**，表里没有的列自动跳过并记
  `飞书多维表格：表里没有这些列 ⇒ 本次跳过 [...]`，补上就自动生效。

---

## 【给 AI 的需求】（直接复制这段）

帮我搭建一个多维表格，用来分析一个游戏模组的 Boss 战数据（做强度平衡和 bug 修复）。

数据表命名：`boss_fights`。

**字段必须严格按下面的名称和类型创建（名称逐字一致，区分大小写）**：

| 字段名 | 类型 | 说明 |
|---|---|---|
| `at` | 文本 | 记录时间，ISO 格式，如 `2026-10-30T15:20:11Z` |
| `kind` | 文本 | 记录类型：`boss_fight` / `diag` / `consent` |
| `mod` | 文本 | 模组版本号 |
| `result` | 单选 | 战斗结果，仅两个选项：`win` / `lose` |
| `act` | 数字 | 第几幕（1–4） |
| `encounter` | 文本 | 遭遇 id，如 `REGENT_STAR_BOSS_ENCOUNTER` |
| `lineup` | 文本 | 这一场的 Boss 阵容，逗号分隔 |
| `boss` | 文本 | Boss 的怪物 id，如 `REGENT_BOSS` |
| `ascension` | 数字 | 进阶等级（0–10） |
| `players` | 数字 | 玩家数（1–4） |
| `bossMaxHp` | 数字 | Boss 最大生命 |
| `bossHp` | 数字 | 战斗结束时 Boss 剩余生命 |
| `turns` | 数字 | 这场 Boss 战打了多少回合 |
| `seed` | **文本** | 本局种子（19 位整数，必须用文本存，否则会丢精度） |
| `cfg` | 文本 | 当时的设置快照（一段 JSON） |
| `line` | 文本 | 模组自己的报错行（仅 `kind=diag` 时有内容） |

**请帮我建好这些视图**：
1. `全部数据`：按 `at` 倒序；
2. `Boss 胜率`：按 `boss` 分组，统计 `result` 的 win / lose 数量与胜率；
3. `按进阶分层`：按 `ascension` + `boss` 分组统计胜率；
4. `按幕分布`：按 `act` 分组统计样本量；
5. `问题日志`：只显示 `kind = diag` 的记录，按 `at` 倒序。

**再帮我做一个仪表盘**（命名 `CharacterBosses 平衡面板`）：
- 柱状图：每只 Boss 的 win / lose 数量；
- 饼图：整体胜率；
- 柱状图：进阶 10 时每只 Boss 的胜率（按 `ascension = 10` 筛选）；
- 数字卡：总样本量、胜率、平均回合数。

---

## 建好之后（3 步接到模组）

1. **拿表格 id**：打开这张多维表格，URL 形如
   `https://xxx.feishu.cn/base/`**`BASCxxxxxx`**`?table=`**`tblxxxxxx`**
   → `BASC…` = `app_token`，`tbl…` = `table_id`。
2. **建应用拿密钥**（<https://open.feishu.cn/app>）：创建「企业自建应用」→
   权限里加 **多维表格可读写**（`bitable:app`）→ 发布应用 → 「凭证与基础信息」里取 **App ID / App Secret**。
   （不是管理员时，需要管理员把应用「可用范围」放开到你，并把应用加到这张表的协作者。）
3. **填配置**：`%APPDATA%\SlayTheSpire2\mod_configs\CharacterBosses\charbosses_config.cfg`

```json
"feishuAppId": "cli_xxxxxxxx",
"feishuAppSecret": "xxxxxxxx",
"feishuBitableAppToken": "BASCxxxxxx",
"feishuTableId": "tblxxxxxx"
```

重启游戏 → A5 打开「记录本地对战数据」、A6 打开「上传游玩数据给作者」→ 打完一场 Boss 战就会写进表格。

> 只想先试水？用【飞书群机器人】更快：群设置 → 群机器人 → 自定义机器人 → 复制 webhook，
> 填 `"feishuBotUrl": "https://open.feishu.cn/open-apis/bot/v2/hook/xxxx"` 即可，5 分钟就能看到消息。

---

## 排错

| 现象 | 原因 / 处理 |
|---|---|
| `mod_log.txt` 里 `飞书多维表格写入失败：…` | 字段名/类型和上表不一致；或应用没有 `bitable:app` 权限；或应用没被加为该表协作者 |
| `飞书 token 获取失败` | `feishuAppId` / `feishuAppSecret` 填错，或应用未发布 |
| 表格里 `seed` 变成 `8.4E+18` | 该列被建成「数字」了 —— 改成文本（我们就是按文本发的） |
| 只有 `consent` 没有 `boss_fight` | A5 开了但没打完一场**本模组**的 Boss 战（小怪战不记录） |
