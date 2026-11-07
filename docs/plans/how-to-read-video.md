# 怎么「看视频」——从录屏里取证据的流程（本会话实测可用）

用户报视觉类 bug 时会丢一个 `屏幕录制 *.mp4` 到项目根目录（工作区里已有十几个）。
我不能直接"播放"视频，**做法是：用 ffmpeg 把视频抽成 JPEG 帧，再用 `read_image` 工具看图**（模型有视觉）。
下面是本会话验证过的命令与坑。

---

## 0. 工具位置（本机）

| 工具 | 路径 | 说明 |
|---|---|---|
| ffmpeg | `C:\Users\<user>\AppData\Local\Programs\xiezuo\resources\ffmpeg\ffmpeg.exe` | **有** |
| ffprobe | 同目录 `ffprobe.exe` | **没有**（别用；用 `ffmpeg -i` 看信息） |

⚠ **这个 ffmpeg 没编进 PNG 编码器**：`-vf ... out.png` 会报
`Automatic encoder selection failed for output stream #0:0 ... codec png disabled`。
**一律输出 `.jpg` + `-q:v 2~3`**（`read_image` 也支持 jpg）。

---

## 1. 先看元信息（时长 / 分辨率 / 帧率）

```powershell
$ff = "C:\Users\<user>\AppData\Local\Programs\xiezuo\resources\ffmpeg\ffmpeg.exe"
& $ff -hide_banner -i "屏幕录制 2026-10-26 115139.mp4" 2>&1 |
  Select-String -Pattern 'Duration|Stream #0:0' | ForEach-Object { $_.Line.Trim() }
# → Duration: 00:01:19.40 ...
# → Stream #0:0: Video: h264, yuv420p, 2542x1598, 30 fps
```

（`ffmpeg -i` 没给输出文件时返回退出码 1，这是正常的，信息在 stderr 里。）

---

## 2. 抽「缩略图墙」先扫全局（最省 token 的第一步）

每 5 秒一帧、缩到 400 宽、拼成 4×4：

```powershell
New-Item -ItemType Directory -Force .frames | Out-Null
& $ff -y -hide_banner -loglevel error -i "屏幕录制 2026-10-26 115139.mp4" `
  -vf "fps=1/5,scale=400:-2,tile=4x4" -frames:v 1 -q:v 3 ".frames\sheet.jpg"
```

然后**看图**：

```
read_image  .frames\sheet.jpg
```

16 张缩略图一次看完（1 分 19 秒的视频 ≈ 整场战斗的节奏），
就能定位"第几张格子出现了问题现象"→ 反推时间点（第 N 格 ≈ (N-1)×5 秒）。

- `fps=1/5` = 每 5 秒一帧；想更密用 `fps=1`（每 1 秒）或 `fps=2`。
- `scale=400:-2` 的 `-2` 是"高度按比例取偶数"（JPEG 要求偶数尺寸，写成 `-1` 有时会失败）。
- `tile=4x4` 的行列按需改，比如 10 秒视频用 `tile=5x4`。

---

## 3. 定位到时刻后，取一张「原尺寸大图」

```powershell
# -ss 放在 -i 前面 = 快速跳转（不解码前面所有帧）
& $ff -y -hide_banner -loglevel error -ss 20 -i "屏幕录制 2026-10-26 115139.mp4" `
  -frames:v 1 -vf "scale=1400:-2" -q:v 2 ".frames\f20.jpg"
```

```
read_image  .frames\f20.jpg
```

- 视频原始 2542×1598，`read_image` 会自己降采样；**缩到 1400 宽左右**既清楚又省 token。
- 想看清某个 UI 细节（意图数字、血量、名字），先裁再放大：

```powershell
# 只要画面右侧 1/3（Boss 与其意图），再放大 1.5 倍
& $ff -y -loglevel error -ss 20 -i "屏幕录制 2026-10-26 115139.mp4" -frames:v 1 `
  -vf "crop=iw/3:ih:iw*2/3:0,scale=1200:-2" -q:v 2 ".frames\f20_right.jpg"
```

- 不确知黑边在哪时，用 `cropdetect` 自动探测安全裁切框：
  `& $ff -hide_banner -i <video> -vf cropdetect -f null -` （看 stderr 里的 `crop=w:h:x:y`）

---

## 4. 一帧不够就抽「一小段连拍」

短动画（奥斯提瞬移、剑的朝向翻转）用 0.2 秒间隔连抽 8 张：

```powershell
& $ff -y -loglevel error -ss 18 -i "屏幕录制 2026-10-26 115139.mp4" -t 2 `
  -vf "fps=4,scale=520:-2,tile=4x2" -frames:v 1 -q:v 3 ".frames\seq.jpg"
```

`-ss 18 -t 2` = 从 18 秒开始取 2 秒；`fps=4` = 每秒 4 帧。

---

## 5. 用这套流程时的规矩（来自本项目的教训）

1. **视频只用来定位"现象与时刻"**；判定原因仍然要靠 `mod_log.txt` + `ilspycli` 反编译，
   视频里看到的"朝右/多一只手/瞬移"只是症状。
2. 用户说"还是不对"时，先抽新视频的帧对比**上一版和这一版**的差异，别凭记忆。
3. 抽出来的图放在 `.frames\`（临时目录，可随时删），**不要提交进 mod 的 pck / content**。
4. 视频文件很大（几百 MB 到 1 GB+），**别试图读原文件**，只读抽出的帧。
5. 我**听不到声音**，所以视频里的音效线索用不了；需要音频信息时请让用户文字描述。

---

## 6. 一句话版（贴给下一个会话）

> 看录屏：`ffmpeg`（本机 `...\xiezuo\resources\ffmpeg\ffmpeg.exe`，**只能用 jpg，png 编码器被裁掉了**）
> 抽帧 → `read_image` 看图。先 `fps=1/5,scale=400:-2,tile=4x4` 出缩略图墙定位时刻，
> 再 `-ss <秒> -frames:v 1 -vf scale=1400:-2` 取大图，必要时 `crop` 放大局部。
> 元信息用 `ffmpeg -hide_banner -i <file>`（没有 ffprobe）。帧放 `.frames\`。
