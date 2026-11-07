# 创意工坊发布 · 准备清单（2026-10-30 准备完毕，待明天上传）

> 上传器已就绪、工作区已就绪。**唯一没做的是真正上传**（需要 Steam 已登录，我这边不能操作 Steam）。
> 上传器用法与硬约束见 `docs/plans/workshop-upload.md`（那份是同类模组的实录，规则通用）。

## 一、已经准备好的东西

```
workshop/
  workshop.json                          ← 标题/描述/可见性/依赖 BaseLib(3737335127)/更新说明
  image.png                              ← 920×920 PNG，131 KB（Steam 限 <1MB）
  content/CharacterBosses/
    CharacterBosses.dll                  ← 762 KB，已核验 v0.2.248
    CharacterBosses.json                 ← 清单（id/name/version/pck_name/affects_gameplay/依赖 BaseLib）
    CharacterBosses.pck                  ← 12.3 MB（本地化 + 美术 + 音乐）
```

- 封面用 `tools\make-workshop-image.ps1` 生成（可重跑；想换图直接覆盖 `workshop\image.png` 即可）。
  当前是**排版封面**（深色底 + 「角色当 Boss」+ 五个角色标签）。若你有更好的实战截图，建议替换成实拍。
- `workshop.json` 里 `visibility` 目前是 **`private`**（先自测，确认无误再改 public）。
- `changeNote` 已写好（首版说明 + 本版改动要点）。

## 二、上传器的环境修复（今天做的，明天不用再弄）

| 问题 | 处理 |
|---|---|
| `global.json` 要求 .NET SDK **9.0.0**，本机只有 10.0.401 ⇒ `dotnet build` 直接报「A compatible .NET SDK was not found」 | 已把 `sts2-mod-uploader-main\sts2-mod-uploader-main\global.json` 的 `version` 改成 `10.0.0`（原文件备份为 `global.json.bak`） |
| csproj 里有 `Reference HintPath=steam\Steamworks.NET.dll`，但仓库只带了 `.nupkg` | 已从 `steam\Steamworks.NET.15.0.1.nupkg` 里取出 `Steamworks.NET.dll`（netstandard2.1）放进 `steam\` |

⚠ 明天第一次构建需要**联网**（NuGet restore：`Steamworks.NET 15.0.1` + `System.CommandLine 3.0.0-preview`）。
我这边沙箱无外网，只验证到「SDK 能解析」这一步。

## 三、明天的步骤

```powershell
cd "D:\Sts2ModProject\Downfall Expansion Vanilla Characters as Bosses"

# 0. 确保 Steam 已启动并登录（上传器靠 Steamworks 提交，没登录会失败）

# 1.（可选）重新同步一次载荷，确保 content 里就是当前构建
Copy-Item src\bin\Release\CharacterBosses.dll  workshop\content\CharacterBosses\ -Force
Copy-Item src\CharacterBosses.json             workshop\content\CharacterBosses\ -Force
Copy-Item .build\CharacterBosses.pck           workshop\content\CharacterBosses\ -Force

# 2. 构建并上传（首次 = 新建工坊条目）
cd sts2-mod-uploader-main\sts2-mod-uploader-main
dotnet build ModUploader.csproj -c Release
dotnet run -- upload -w "..\..\workshop"
```

- 成功后目录里会生成 **`mod_id.txt`** —— **别删**，以后更新靠它定位条目。
- 想先在游戏里自测：把 `workshop\content\CharacterBosses` 整个复制到
  `D:\SteamLibrary\steamapps\common\Slay the Spire 2\mods\` 下（和现在装的一样），或者直接订阅自己那条 private 工坊条目。

## 四、上传前自检（我这边已经核过）

- [x] `CharacterBosses.json`：`id=CharacterBosses`、`version=0.2.248`、`pck_name=CharacterBosses`、
      `has_dll/has_pck=true`、`affects_gameplay=true`、`dependencies=[BaseLib]`
- [x] DLL 内字符串含 `v0.2.248`，且 `0.2.247` 为 0 处（版本没搞混）
- [x] **包内没有任何密钥**：旧飞书 secret（已失效，不再记录）、`APP_ID_REDACTED`、`BITABLE_APP_TOKEN_REDACTED` 在 DLL 与 PCK 里都搜不到
      （DLL 里只有配置键名 `feishuAppSecret` 这个字符串，是无害的字段名）
- [x] 载荷里**不含** `mod_manifest.json`（有个玩家日志里报 `mod_manifest.json missing 'id'` 的 ERROR，
      就是这种无 id 的多余清单文件造成的；我们不带它）
- [x] `image.png` 131 KB < 1MB；文件名与位置符合上传器要求（工作区根目录）

## 五、需要你拍板的三件事

1. **封面**：用现在的排版图，还是换成一张实战截图？（换的话给个时间点/截图，或我按 920×920 重新排）
2. **`previews/`（额外展示图）**：上传器支持工作区里放 `workshop\previews\*.jpg`，
   按文件名增量同步（删掉本地文件会从工坊移除）。**现在没放** —— 要的话我可以从 PCK 里的角色徽记/地图徽记拼几张，
   或你给实战截图。
3. **标签 `tags`**：现在是空。上传后在工坊网页上手动选最稳（不同游戏可用的标签集合不同，填错会被拒/忽略）。

## 六、上传后要做的

1. 打开工坊页面确认：标题/描述/主图/依赖 **BaseLib** 都对；
2. 在游戏里**订阅自己这条**、开一个新档跑一幕，确认 Boss 正常替换（工坊条目与本地目录是两份，别只看本地）；
3. 确认无误后把 `workshop.json` 的 `visibility` 改成 `public`，再 `dotnet run -- upload -w ..\..\workshop` 一次
   （省略的字段不会被清空，`changeNote` 记得同步改成「改为公开」之类）；
4. 以后每次更新：换 `content/` 里的三个文件 → 改 `changeNote` → 同一条命令；
   版本号规则不变（DLL 改动 = `BuildInfo.Tag` 与 `CharacterBosses.json` 的 `version` 同时 +0.0.1）。

## 七、发布时要讲清楚的几件事（已写进工坊描述）

- **依赖 BaseLib**（`3737335127`），没装会加载失败；
- **联机请所有玩家用同一版本**（不同版本会出现「数据不同步」——这次几处联机 bug 都是这么暴露的）；
- **数据收集是可选项**：首次加载弹窗询问，默认只写本机、不含账号信息；
  多人时只有房主上传对局数据（同一局不会重复）。
- 第四幕及以后不参与替换（只做前三幕），与「加幕」的模组共存时不冲突。
