# 创意工坊上传准备

上传器：`sts2-mod-uploader-main/`（独立仓库，`dotnet run -- upload <工作区目录>`）。
工作区目录 = `workshop/`，需要 `workshop.json` + `image.png` + `content/`（+ 可选 `previews/`）。

## 反编译核实的硬约束（`src/UploadCommand.cs`）

| 事实 | 影响 |
|---|---|
| `workshop.json` 的 `title` / `description` → `SteamUGC.SetItemTitle` / `SetItemDescription` | 工坊页面的标题与描述**与 `mod_manifest.json` 无关**，必须单独写 |
| `image.png` → `SetItemPreview` | 主图必须叫 `image.png`，且 <1MB |
| `previews/` 目录里**每个文件** → `AddItemPreviewFile` | 无扩展名过滤；按文件名做增量更新，本地删掉的会同步移除 |
| 缺 `image.png` 或 `workshop.json` 直接报错退出 | 两者缺一不可 |
| `workshop.json` 里省略的字段上传后保持原样 | 首次上传就要填全 |

`workshop.json` 现填：`visibility: "private"`（先自测）、`dependencies: ["3737335127"]`（= BaseLib，已核对其工坊清单）、`tags` / `contentDescriptors` 空。

## 主图 `image.png`

- 1024×1024 之前试过，现在定稿是 **920×920，705KB**。
- 来源：`屏幕录制 2026-10-14 203039.mp4` 的 **21.0s**，实验体「大力猛扑」+ 天罚虚影镰刀压下的瞬间。
- 裁剪：源 2560×1600 取 `crop=920:920:1405:285`（不缩放），四个元素刚好都在框内 ——
  招式名横幅右缘在 2317（右边界留 2325）、实验体、天罚虚影、蓝色攻击方框。
- 迭代记录：左边界从 1245 收到 1405 是为了切掉头前方的白色电流特效；下边界同量收（保持正方形）。
  那道电流与头部重叠约 50px，所以"只裁到头的轮廓"会剩几缕白丝，必须再进 40px。

## `previews/`（5 张，全部 <1MB）

| 文件 | 来源帧 | 内容 |
|---|---|---|
| `01_player_card_grand_finale.jpg` | 203039 / 1.0s | 玩家卡牌演出：左上「华丽收场+」横幅 + 居中卡片金框 + 玩家蓝框 + 目标金框 |
| `02_queen_you_are_mine.jpg` | 203039 / 14.1s | 女王「你是我的了」：紫幕 + 万针 + 双方框选 + 右上横幅（**13.0s 只有紫幕、还没出针**） |
| `03_test_subject_big_pounce.jpg` | 203039 / 21.0s | 实验体「大力猛扑」+ 天罚虚影镰刀 |
| `04_test_subject_burning_growl.jpg` | **210640 / 5.35s** | 「燃烧之吼」+ 5 张灼烧点亮 + 绿色声波刚起（源改为层级修复后的新录像） |
| `05_slimed_berserker_vomit_ichor.jpg` | **210640 / 12.8s** | 史莱姆狂战士「喷涂脓水」+ 手牌黏液挖洞点亮（同上换源） |

`04` / `05` 换源的原因：原来的两份素材录于**框选层层级修复之前**，画面上框压在牌上面。
新录像 `屏幕录制 2026-10-14 210640.mp4` 录于修复之后，两张图里都能直接看到
**牌盖住了框**（`04` 里第 4 张灼烧压着实验体的蓝框、`05` 里手牌压着玩家的金框）。

`04` 的时间点挑法：燃烧之吼的绿色声波在 **5.3s** 刚起（此时还只是实验体周围的一团绿光），
**5.45s** 就已经是屏幕边缘的弧线了，所以取 **5.35s** 这个中间点 —— 声波看得出在扩散，
又还没铺满画面。

**用 JPEG 而不是 PNG**：1920×1200 的 PNG 每张要 2MB 以上，超过 1MB 上限；
JPEG（`-q:v 3`）每张只有 114~247KB，分辨率能保住。Steam 的预览图接受 jpg/png。

**取帧踩到的坑**：用户原本想用「华丽收场打火炬头聚合体+女王」那一幕，
但那是**同一回合里第二张同名牌**，被默认的「同名卡牌一回合一次」限流挡掉了 ——
`mod_log` 里那段时间没有任何 `[Cinematic] play '` 记录，画面上只有游戏自己在显示卡牌，
没有横幅也没有压暗。所以玩家卡牌那一张改用珊瑚那次（t=1.0），那才是真的演出了。

## `content/` 放什么、为什么这么放

**照抄装机后的 `mods/CriticalCinematic/`**（`Compare-Object` 逐项核对过，完全一致）：

```
content/CriticalCinematic/
  CriticalCinematic.dll        534 KB
  CriticalCinematic.json       带 id / name / pck_name 的清单
  CriticalCinematic.pck        512 B（内容就是那份清单）
  mod_manifest.json            装机脚本生成的精简版（无 id）
  frames/*.png                8 张虚影精灵表，合计 ~10 MB
```

**为什么两个 json 都带上**：游戏侧**没有**硬编码 `mod_manifest.json` 或 `manifest.json`
这两个文件名（在 `sts2.dll` 的字符串堆里搜过，`utf16=-1`），而是**扫模组目录里的 `.json`**
并打日志 `Found mod manifest file ...`（字符串堆 offset ≈ 8513900 能看到这条）。
所以工坊上下载来的模组布局其实是两种：`BaseLib/BaseLib.json`（带 id 的清单）和
ShieldOnly 那种只有 `mod_manifest.json` 的。**带全两份 = 无论它挑哪个都能加载**，
而且这与本地已经跑了很久的那份目录逐字节一致，最稳。

同名文件注意：`frames/` 里的 PNG 单个超过 1MB 是**没问题**的 ——
1MB 那条限制只针对 `image.png` 与 `previews/`（Steam 展示图的后端限制）。

## 上传步骤

```powershell
cd sts2-mod-uploader-main\sts2-mod-uploader-main
dotnet run -- upload -w ..\..\workshop
```

（README 里写的是 `ModUploader.exe upload -w <workspace-folder>`；用 `dotnet run` 时
把 `--` 后面的参数照样传即可。首次运行前先确认 Steam 已启动并登录。）

首次上传成功后：
1. 工作区里会生成 `mod_id.txt` —— **别删**，以后更新靠它定位工坊条目。
2. 回网页确认 `visibility` / 依赖（BaseLib `3737335127`）/ 预览图顺序，再改成 `public`。
3. 改 `changeNote` 再传一次即为更新（`workshop.json` 里省略的字段会保持原样，不会清空）。

## 现状

- [x] `workshop.json` —— 双语标题/描述、`private`、依赖 BaseLib
- [x] `image.png` —— 920×920，689 KB
- [x] `previews/` —— 5 张 JPEG，114~233 KB
- [x] `content/CriticalCinematic/` —— 与装机目录逐项一致
- [ ] 上传（需要 Steam 登录，我没有权限操作 Steam）

