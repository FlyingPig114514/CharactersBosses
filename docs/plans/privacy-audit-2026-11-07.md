# 隐私 / 凭据自查报告 — 2026-11-07（v0.2.463）

起因：用户 2026-11-07「把这个版本上传到 github 仓库，覆盖之前的，然后自查有没有泄露隐私的风险」。

审计范围：
1. 仓库工作树（已跟踪文件）；
2. git 历史；
3. **已发布的公开产物**（创意工坊包 / 部署 DLL / 玩家端会拿到的文件）——这一层才真正决定风险；
4. 模组上传的数据本身（字段清单）。

结论：**工作树干净，没有活的凭据**；有一条 HIGH 属于"已经发生、已处置"（历史里的旧 secret，已失效），
一条 MEDIUM 已当场修掉（本机路径会随 diag 记录上传），一条 MEDIUM 是设计上接受的风险（webhook 公开）。

---

## 一、严重度排级

### CRITICAL
无。

### HIGH-1（已处置）飞书 app_secret 曾以 base64 内嵌在公开 DLL 里
- 位置（历史）：`git show ffe7f1c:src/Hooks/Core/BossConfig.cs` 里 4 个 base64 常量
  （app_id / **app_secret** / 多维表格 app_token / table_id），v0.2.262~v0.2.460 一直在工坊包里。
- 现状：v0.2.462 起常量已删除，玩家端只剩一个群机器人 webhook；用户已重置 app_secret。
- **实测复核**：历史里那个旧 secret（`elCC…CRc`）现在请求返回 `code=10014 app secret invalid` ⇒ **已失效**。
- 残留风险：仓库历史（commit `ffe7f1c`）仍可解出这个**已失效**的旧值；仓库当前是 Private ⇒ 可接受。
  **若将来要开源这个仓库，必须先用 `git filter-repo` 重写历史**，否则会把旧 secret（以及下面的玩家 SteamID）一起公开。

### HIGH-2（设计上接受）群机器人 webhook 是公开的
- 位置：`src/Hooks/Core/BossConfig.cs:187`、`docs/plans/feishu-group-bot.md`、以及工坊包里的 DLL。
- 拿到这个地址的人能做的事：**只能往「数据收集」这一个群发消息**（实测不带关键词返回 `19024 Key Words Not Found`）。
  不能读表、不能改表、不能换 token —— 这是客户端遥测的标准取舍（类似 Sentry DSN）。
- 缓解：只用于收数据；关键词校验开着；脚本把群消息当**不可信输入**（只认 `kind` 字段、按表里真实字段白名单写）。
- 想换掉：飞书群里新建一个自定义机器人 ⇒ 换 `BuiltInRelayUrl` 重新发版即可，旧地址立刻作废。

### MEDIUM-1（已修，v0.2.463）diag 记录可能带本机用户目录
- 链路：`SafeLogPatch` 把含「失败/异常/Exception/不同步/⚠」的日志转成 `Telemetry.Diagnostic()` → 上传；
  而 .NET 的 IO 异常消息里**常带完整路径**（`Could not find file 'C:\Users\<Windows 用户名>\AppData\…'`）。
- 玩家从没同意过上传「他的电脑用户名」，等于间接泄露一个身份标识。
- 修法：`Diagnostic()` 入队前统一打码 —— `C:\Users\<名>\…` → `C:\Users\<user>\…`，`/home/<名>/…` → `/home/<user>/…`
  （本机数据文件里也一起打码）。见 `src/Hooks/Core/Telemetry.cs` 的 `MaskUserPaths`。

### MEDIUM-2（工作树已脱敏，历史仍在）作者邮箱 / 玩家 SteamID64 曾出现在笔记里
- 命中过：`docs/plans/feishu-relay-deploy-status.md`（作者邮箱）、`.dshwolf/STATUS.md`、
  `docs/plans/desync-2026-11-06-review.md`、`src/Hooks/Patches/DivergenceStateProbe.cs`（其他玩家的 SteamID64）、
  若干 `docs/plans/*.md`（`C:\Users\<用户名>\…` 绝对路径）。
- 已全部替换为 `7656119…` / `C:\Users\<user>\` / `（作者邮箱已脱敏）`。
- 历史提交里仍在 ⇒ 与 HIGH-1 同一条处置（要开源就先重写历史）。

### LOW
- **L1** app_id `APP_ID_REDACTED` 出现在文档与脚本里。app_id 是公开标识，**不是密钥**；单独知道它什么也做不了。
- **L2** `tools/feishu-*.ps1` 的用法示例里写死了多维表格 `app_token` / `table_id`。这不是凭据，
  但能定位到作者的表；**已删除**过时脚本 `tools/rotate-feishu-secret.ps1`（它会把 secret 重新 base64 写回源码，留着是坑）。
- **L3** 每条对局记录含 `seed`（本局种子）。它能重放一局，但不含任何身份信息，是作者做平衡统计所必需 ⇒ 保留。
- **L4** `.dshwolf/` 里有大量内部笔记（含其他玩家的 SteamID，已脱敏）。属于"工作台"，不是产品；仓库 Private。

---

## 二、检查通过的项目（✓）

- ✓ **公开产物**：部署的 `Impl107/111.dll` 里搜得到 `v0.2.463`，搜**不到** app_id / app_secret / app_token / table_id。
- ✓ **遥测字段**（41 个）里没有身份信息：`act / ascension / at / bgmSource / boss* / cfg / encounter / hpList /
  kind / line / lineup / maxHpTotal / mixedMatching / mod / note / pChar / pCharMod / pDmg / pHeal / pHpEnd /
  pHpStart / platform / players / pList / pMaxHp / result / role / seed / sharePool / turns`；
  `platform` 只记 `Win32NT` 这类 OS 名，`role` 只记 `solo/host/guest`；**没有 SteamID、昵称、存档路径**。
- ✓ 上传失败时数据留本机（outbox 只有成功才删）；是否上传由玩家在弹窗 / 配置里决定（默认关）。
- ✓ 本机状态与导出数据不进仓库：`.feishu-group-state.json`、`docs/plans/telemetry-import/`、`*.xlsx`、`.dist/`、
  `workshop/content/` 均已 gitignore，`git ls-files` 里确认 0 命中。
- ✓ 仓库里没有 `.env`、私钥、git 凭据、第三方包的 `postinstall` 之类供应链面（本仓库没有包清单）。
- ✓ 唯一外部输入是飞书群消息：脚本逐行 `ConvertFrom-Json` → 只取白名单字段 → 走飞书 API 写表，
  **不拼 shell、不拼 SQL、不做路径拼接**，因此没有命令注入 / 路径穿越面。
- ✓ 玩家端拿不到任何能写表的凭据（这是本次改道的核心目的）。

---

## 三、建议

1. **工坊是否要重发 v0.2.463**（含 MEDIUM-1 的打码修复）：等你发话再打包上传（按约定我不擅自打包）。
2. **如果要开源仓库**：先 `git filter-repo` 重写历史（去掉旧 secret、邮箱、玩家的 SteamID64），或干脆新建仓库只放当前快照。
3. webhook 视作"公开地址"：泄露了就在飞书群里新建机器人换掉，不必紧张。
4. 群消息一律当不可信输入：别在「数据收集」群里贴别的东西。
