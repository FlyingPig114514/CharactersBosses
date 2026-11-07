# 用 GitHub 做版本管理（本模组的具体做法）

> 现状（2026-11-06 已完成）：本地仓库已 `git init -b main`，首次提交 `9aa722e`
> 「初始提交（v0.2.451 基线）」，530 个文件 / 11 MB，并打了标签 **v0.2.451**。
> **已推送到 <https://github.com/FlyingPig114514/CharactersBosses>（私有仓库）**：3 个提交 + 标签 `v0.2.451`。
> 本机凭据已存进 Windows 凭据管理器（Git Credential Manager），以后 `git push` 不会再弹授权窗。
> ⚠ 注意：在这个 agent 的沙箱 shell 里，非提权的 `git push` 会因 schannel 拿不到证书存储而失败
> （`SEC_E_NO_CREDENTIALS`）—— 需要提权执行；你在自己的终端里跑则完全正常。

## 一、仓库里放什么、不放什么

放（都在仓库里了）：
- `src/` —— 全部 C# 源码（含 `src/Hooks/…`）
- `loader/` —— 双版本外壳
- `content/localization/` —— 16 语言文案
- `docs/` —— 安装说明 + 设计稿 + 各 Boss / 联机排查记录
- `tools/*.ps1`、`install-mod.bat`、`uninstall-mod.bat`、`*.csproj`、`AGENTS.md`、`rules.md`、`README*.md`
- `workshop/`（除 `content/`）—— `workshop.json`（标题/描述/changeNote）、`mod_id.txt`、`image.png`、`previews/`

不放（`.gitignore` 已屏蔽，共 ~5 GB）：
| 目录/文件 | 为什么 |
|---|---|
| `tools/dotnet/`（1.2 GB） | 本机 .NET SDK，能重装 |
| `屏幕录制 *.mp4`（4 GB+） | 素材录像；发版截图只用到导出的帧 |
| `.recon/` `.refs/` `references/` | 反编译产物 + 游戏 DLL（**有版权，绝不能进公开仓库**） |
| `.build/` `.tmp/` `.dist/` `bin/` `obj/` `*.pck` | 构建产物 |
| `workshop/content/` | 打包出来的 dll/pck（`pack-dist.ps1` 重新生成） |
| `sts2-mod-uploader-main/` | 别人的独立仓库 |
| `mod_log*.txt` | 本机/玩家日志 |
| `Modding-Tutorial/` | 脚手架克隆下来的教程 |

> ⚠ 版权：本模组用了《杀戮尖塔2》与崩坠（Downfall）的美术素材。**代码公开没问题，美术素材别上传**
> —— 所以 `references/`、`.refs/` 和打包产物这类含素材/官方 DLL 的东西一律忽略。想更保险就把仓库设 **private**。

## 二、把仓库推到 GitHub（一次性）

**方式 A：网页建仓库（不用装任何工具，推荐）**
1. 打开这个**预填好参数**的链接：<https://github.com/new?name=CharactersBosses&visibility=private>
   直接点最下面「Create repository」，**不要**勾 Add README / .gitignore / license（本地已有）。
2. 建好后在本目录执行（remote 已配好，只要两行）：

```powershell
git push -u origin main
git push origin --tags
```

> 远端已设为 <https://github.com/FlyingPig114514/CharactersBosses.git>；
> 想换名字就 `git remote set-url origin <新地址>`。

**方式 B：装 GitHub CLI 后一条命令**

```powershell
winget install GitHub.cli
gh auth login                 # 选 GitHub.com → HTTPS → 浏览器登录
gh repo create CharactersBosses --private --source . --remote origin --push
git push origin --tags
```

（GitHub 会要求用 Personal Access Token 当密码；用 `gh auth login` 的话它会自动配好凭据。）

## 三、日常怎么用（重点）

**一个「验证过的构建」= 一个提交**，提交信息里写版本号：

```powershell
# 1. 改代码 → 编译 → 部署到游戏 → 进游戏验证
cmd /c install-mod.bat "D:\SteamLibrary\steamapps\common\Slay the Spire 2"

# 2. 验证 OK 就提交（别攒着）
git add -A
git commit -m "fix(defect): 事件机槽位摆错导致三层永远出能力机（v0.2.447）"

# 3. 想同步到 GitHub
git push
```

**发版时**（三个地方的版本号要对齐，缺一个就会像以前那样「改了但没生效」）：

| 位置 | 例子 |
|---|---|
| `src/CharacterBosses.json` 的 `version` | `0.2.451` |
| `src/Hooks/Core/BuildInfo.cs` 的 `Tag` | `v0.2.451 / 2026-11-06（…改了什么…）` |
| git 标签 / 工坊 `changeNote` | `v0.2.451` |

```powershell
git tag -a v0.2.452 -m "v0.2.452：一句话说明"
git push origin main --tags
```

**发布 zip 走 GitHub Releases，不要提交进仓库**：
GitHub 网页 → Releases → Draft a new release → 选刚推的 tag → 把 `.dist\CharacterBosses-v0.2.452.zip` 拖进去当附件 → 发布。
（单个附件上限 2 GB，我们 13 MB，随便放。）

## 四、版本管理到底能帮上什么（这次真的用得上的场景）

1. **「改了但没生效」**：`git log --oneline` 一眼看出上一次部署是哪次提交，
   再对 `BuildInfo.Tag` —— 这次会话里就吃过「打包/编译静默没生效」的亏。
2. **回归对比**：`git diff v0.2.416 HEAD -- src/Hooks/Core/BossLineups.cs`
   —— 查「某个 bug 是哪一版引入的」直接看 diff。
3. **回滚单个文件到某版本**：`git checkout v0.2.416 -- src/Hooks/Core/BossDefs.cs`
4. **二分定位**（最有价值）：
   ```powershell
   git bisect start
   git bisect bad HEAD
   git bisect good v0.2.416
   # 每次它切到一个提交，你编译+进游戏验证，然后 git bisect good / bad
   ```
5. **玩家报的 bug 能对上版本**：让他发 `mod_log.txt` 的第一行
   `===== CharacterBosses 构建：v0.2.451 …`，直接 `git show v0.2.451` 看当时那版代码。

## 五、几个注意点

- **别把游戏目录 `mods/CharacterBosses/` 纳入版本管理**：它是部署产物，源头是 `src/`。
- **换行**：仓库里已有 `.gitattributes`；建议顺手 `git config --global core.autocrlf true`
  （Windows 上避免整文件 CRLF/LF 抖动）和 `git config --global core.quotepath false`（中文路径不再显示成 `\346\230\216`）。
- **提交身份**：本仓库暂时用占位身份 `feizhu <feizhu@users.noreply.github.com>`。
  想和 GitHub 账号对上，改一次即可（建议设成你 GitHub 的 noreply 邮箱，防止泄露真实邮箱）：
  ```powershell
  git config user.name  "FlyingPig114514"
  git config user.email "你的GitHub数字ID+用户名@users.noreply.github.com"
  ```
- **多人协作**才需要分支 + PR；一个人开发直接在 `main` 上提交 + 打标签就够了。
  真要大改（例如重写某个 Boss）可以先 `git switch -c feat/xxx`，验完再 `git switch main && git merge --no-ff feat/xxx`。
