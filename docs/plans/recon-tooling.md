# 反编译 CLI 用法（本仓库自建，替代 ILSpy MCP）

因为当前会话没有 ILSpy MCP 工具绑定，用 `tools/ilspy-cli` 代替。

```powershell
$dotnet = "D:\Sts2ModProject\SlaytheSpire2-ModScaffold\tools\dotnet\dotnet.exe"
$cli    = "D:\Sts2ModProject\SlaytheSpire2-ModScaffold\tools\ilspy-cli\bin\Release\net10.0\ilspycli.dll"
$asm    = "D:\Sts2ModProject\SlaytheSpire2-ModScaffold\references\sts2.dll"

# 列出类型（regex 过滤，忽略大小写）
& $dotnet $cli types $asm "MegaCrit\.Sts2\.Core\.Hooks\.Hook$"

# 反编译整个类型
& $dotnet $cli type $asm "MegaCrit\.Sts2\.Core\.Entities\.Cards\.CardPlay$" 1

# 反编译某类型下匹配的方法（可多个）
& $dotnet $cli method $asm "MegaCrit\.Sts2\.Core\.Hooks\.Hook$" "^AfterCardPlayed$" 1

# 快速列出成员（签名）
& $dotnet $cli members $asm "MegaCrit\.Sts2\.Core\.Entities\.Creatures\.Creature$" 1

# 按成员名全局搜索（找某个方法在哪个类）
& $dotnet $cli search $asm "PlayCard" 15
```

参数：
- `types <asm> <typeRegex>`
- `type <asm> <typeRegex> [maxMatches=8]`
- `method <asm> <typeRegex> <methodRegex> [maxMatches=8]`
- `members <asm> <typeRegex> [maxMatches=8]`
- `search <asm> <nameRegex> [maxMatches=8]`

注意：输出可能很大，请用 `Select-Object -First N` 或重定向到文件后再读。
