# CharacterBosses 创意工坊发布记录

> 与 `docs/plans/workshop-upload.md`（那个是 **CriticalCinematic** 项目的）区分开，本文件专记**本模组**。

## 关键信息

| 项 | 值 |
|---|---|
| 工坊物品 id | **3830639796**（存在 `workshop\mod_id.txt`，别删） |
| 游戏 AppID | 2868840（上传器目录里的 `steam_appid.txt`） |
| 账号 | 飞猪 |
| 工作区目录 | `workshop\`（`workshop.json` + `image.png` + `previews\` + `content\CharacterBosses\`） |
| 依赖 | BaseLib `3737335127`（`workshop.json.dependencies`） |
| 可见性 | 由 Steam 网页侧维护（`workshop.json` 里不写 ⇒ 上传不会改动） |

## 打包

> ★ **用户 2026-11-06 定的规则**：只有**明确说「打包」/「上传」时**才生成 `.dist` 里那份 zip。
> 日常 `install-mod.bat` **不再自动打包**（以前每次部署都打一份，`.dist` 堆了 94 个中间版本 zip，
> 全是带 bug 的中间态、没有价值）。现在要打包必须显式带 `pack`：

```powershell
install-mod.bat pack                       # 安装 + 打包（默认游戏路径）
install-mod.bat "D:\...\Slay the Spire 2" pack   # 指定游戏路径 + 打包
install-mod.bat                            # 只安装，不打包
```

**只打包、不重新安装**（改完已经部署过时用这个，最快）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\pack-dist.ps1
```

产物：
- `workshop\content\CharacterBosses\` —— 工坊上传的实际内容（6 个文件）
- `.dist\CharacterBosses-v<版本>.zip` —— 给「工坊连不上」的玩家手动安装用
- `.dist\` 现在只保留**已发布过**的 zip（0.2.406 / v0.2.416 / v0.2.451）。

打包前务必确认 **`docs\安装说明.txt` 的版本号与实际版本一致**（它是 zip 里的说明文件，
pack-dist 会把它原样复制进包里）。本次已把标题行与「本包是 vX」两处更新到 v0.2.451。

## 上传

```powershell
cd sts2-mod-uploader-main\sts2-mod-uploader-main
$env:DOTNET_ROLL_FORWARD = "LatestMajor"     # 上传器是 net8.0，本机只有 .NET 9/10 运行时
& ".\bin\Release\net8.0\ModUploader.exe" upload -w "D:\...\workshop"
```

- 直接跑预编译的 `bin\Release\net8.0\ModUploader.exe`（需要同目录的 `steam_api64.dll`）。
  用 `dotnet run` 也可以，但要能编译 net8.0 目标。
- **Steam 必须先启动并登录**（上传器走 Steamworks 原生 API，读的是已登录会话）。
- 成功标志：控制台最后一行
  `Successfully uploaded '玩家角色Boss / Characters Bosses' to the workshop with id 3830639796!`
  以及 `C:\Program Files (x86)\Steam\logs\workshop_log.txt` 末尾
  `[...] Upload finished for workshop item 3830639796 : OK`

### 踩到的坑：`k_EResultFail`（2026-11-06 首次尝试）

现象：内容上传到 100% 之后 `k_EItemUpdateStatusInvalid` + `Result: k_EResultFail`。
真因在 **Steam 自己的日志**里（上传器的日志只有一句 EResultFail，看不出原因）：

```
[.../logs/workshop_log.txt]
[2026-11-06 21:25:52] [AppID 2868840] Upload workshop item 3830639796 failed (Timeout uploading manifest (size 1777))
```

⇒ **Steam 后端上传 manifest 超时**，与内容无关（13 MB 远低于工坊 100 MB 上限）。
**隔 20 秒重试一次就成功了**（21:27:46 完成）。以后再遇到 EResultFail：
先看 `workshop_log.txt` 那一行的括号内原因，再决定是重试还是查内容（预览图 >1MB、描述超 8000 字等）。

## 已发布版本

| 版本 | 时间 | 备注 |
|---|---|---|
| v0.2.513 | 2026-11-08 22:21 | 新增「蛇咬猎」（共 30 个：一层 10／二层 9／三层 11）；斗蛐蛐适配（30 档各暴露成独立怪物类）；修 Boss 杂交 / 面板不显示 / 飞镖预览偏低 / 多人不同步 |
| v0.2.451 | 2026-11-06 21:27 | 新增「电球机」「仆从储」（共 27 个）；三层事件机首次真正可遇到；意图/联机一批修复 |
| v0.2.416 | 2026-11-05 | 三层「状态机」、Boss 改成「按层数 + 同角色编号」 |
