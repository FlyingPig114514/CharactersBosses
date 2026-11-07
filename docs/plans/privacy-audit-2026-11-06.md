# 隐私 / 凭据自查（2026-11-06，仓库已有 5 个提交、工坊已发 v0.2.451）

> **仓库决定（2026-11-06）：保持 Private，不公开。** ⇒ 第二、三节的脱敏/移出仓库都是可选（想清爽可以做，但不算泄露）。\n> 第一节（飞书 app_secret 已随工坊 DLL 公开）与仓库公开与否无关，**仍然要处理**。\n>\n> 起因：用户问「目前上传的东西中还有可能包含隐私数据吗？」——查了**已提交到 GitHub 的文件**和**已发到工坊的 DLL**。

## 一、🔴 最高优先：飞书 app_secret 内嵌在 DLL 里（等于已经公开）

`src/Hooks/Core/BossConfig.cs` 第 172–175 行有 4 个 Base64 常量：

| 常量 | 内容 | 敏感度 |
|---|---|---|
| `BuiltInAppIdB64` | 飞书应用 app_id | 低 |
| `BuiltInAppSecretB64` | **飞书应用 app_secret** | **高**（可换 `tenant_access_token`，进而读写你的多维表格） |
| `BuiltInAppTokenB64` | 多维表格 app_token | 中 |
| `BuiltInTableIdB64` | 数据表 id | 低 |

**已亲自验证**：把常量按 **UTF-16**（.NET 的 `#US` 字符串堆编码）在 DLL 里搜索 ——
`workshop\content\CharacterBosses\CharacterBosses.Impl{107,111}.dll` **都命中**，
`app_id` / `app_secret` / `app_token` 三个都在。
⇒ 这两个 DLL 就是**发到创意工坊的公开包**，任何人 `strings` 一下就能拿到 app_secret。

### 为什么会这样
A6「上传对战数据」是**由每个玩家的客户端直接调飞书接口**的，所以凭据必须内嵌在客户端里才「零配置」。
但**任何内嵌在客户端的密钥都等于公开**（DLL 是明文可读的），客户端内嵌密钥这条路本质上不安全。

### 建议（按顺序）
1. **立刻重置飞书 App Secret**（开放平台 → 应用 → 凭证与基础信息 → 重置）。重置后旧客户端会 401，
   代码里已有日志 `若是刚重置过 app_secret，说明这个客户端还是旧 secret ⇒ 更新模组`。
2. 换方案，三选一：
   - **自建中转**（最稳）：客户端只往你自己的一个 HTTP 端点 POST，密钥留在服务端；
   - **群机器人 webhook**：也是密钥，但权限只到「往一个群发消息」，泄露后果小得多，且可随时换；
   - **完全去掉内置凭据**：只在作者自己的机器上用「模组配置」里那几项（`feishuAppId/appSecret/...`）填，
     玩家端的 A6 只写本机文件。**代价**：玩家数据不再自动汇总。
3. 无论哪种，**别再往源码/二进制里写长期密钥**；本地配置走 `%APPDATA%` 或环境变量。

> ⚠ 注意：git 历史里已经有这个常量了（第 1 个提交就有）。**删代码 / 改历史都不能让它变回秘密**
> —— 真正的补救只有「重置密钥」。所以顺序是：先重置，再改代码。

## 二、🟡 他人 SteamID / 玩家昵称（私有仓库里可接受，转公开前必须处理）

| 文件 | 命中 |
|---|---|
| `.dshwolf/STATUS.md` | `7656119xxxxxxxxx` × 7 |
| `docs/plans/desync-2026-11-06-review.md` | `7656119xxxxxxxxx` × 2 |
| `src/Hooks/Patches/DivergenceStateProbe.cs` | 注释里 × 1（历史玩家 ID） |
| `.dshwolf/buglog.json` | × 1 |
| `docs/plans/desync-2026-11-06-next.md` | 玩家昵称 `虚无度日`、`紫影龙` |

SteamID 可以反查到个人主页。**私有仓库**里放着没大问题（只有你和你授权的协作者能看），
但**一旦改公开**就等于披露他人账号 —— 那之前请把这几处换成 `<玩家A>` / `<玩家B>`。

## 三、🟡 `.dshwolf/`（AI 代理的记忆与日志）也在仓库里

9 个文件：`STATUS.md` / `memory.md` / `buglog.json` / `cerebrum.md` / `anatomy*.md` 等，
里面是开发过程的内部笔记（含从玩家日志里摘的片段、SteamID）。
它不是「项目内容」，公开前建议整体移出仓库（`git rm --cached -r .dshwolf` + 写进 `.gitignore`）。

## 四、🟢 已经确认**没有**进仓库的东西

- 玩家发来的 `mod_log*.txt`（`.gitignore` 已屏蔽，实测未收录）
- 屏幕录像 `*.mp4`（4 GB+，未收录）
- `references/` `.refs/`（游戏本体 `sts2.dll`）、`.recon/`（反编译产物）
- `sts2-mod-uploader-main/`（别人的仓库）
- `.dist/` `workshop/content/` `bin/` `obj/` `*.pck`（构建产物）
- 本机绝对路径之外的其它凭据：扫描 `password` / `api_key` / `Bearer` / `@gmail` 等模式，除飞书那套外无命中

## 五、一句话结论

**有，且最严重的一条不在仓库里而是在已发布的 DLL 里：飞书 app_secret。**
其余（SteamID / 昵称 / 代理笔记）只影响「将来要不要公开仓库」这个决定。

## 六、转公开前的 checklist

- [ ] 飞书 App Secret 已重置，且新方案不内嵌长期密钥
- [ ] SteamID / 玩家昵称已脱敏（4 个文件）
- [ ] `.dshwolf/` 已移出仓库
- [ ] `git log -p` 抽查一遍历史里没有别的密钥（历史不会因为删文件而变干净）
