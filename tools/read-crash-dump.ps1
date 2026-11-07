# read-crash-dump.ps1 -- 读 Windows 崩溃转储（.dmp），打印「异常码 / 异常地址 / 归属模块 / 当时加载的 mod」
#
# 用途：游戏闪退时（比如 coreclr.dll 0xc0000005），先看是哪一块内存炸的、当时装了哪些本地 mod。
# 默认自动找最新的 %LOCALAPPDATA%\CrashDumps\SlayTheSpire2.exe.*.dmp。
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File tools\read-crash-dump.ps1
#   powershell -ExecutionPolicy Bypass -File tools\read-crash-dump.ps1 -Path "C:\...\xxx.dmp" -ModsOnly
[CmdletBinding()]
param(
    [string]$Path = '',
    [switch]$ModsOnly
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

if (-not $Path) {
    $dir = Join-Path $env:LOCALAPPDATA 'CrashDumps'
    $f = Get-ChildItem $dir -Filter 'SlayTheSpire2.exe.*.dmp' -File -ErrorAction SilentlyContinue |
         Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $f) { Write-Host "没找到崩溃转储（$dir）" -ForegroundColor Yellow; exit 1 }
    $Path = $f.FullName
    Write-Host ("最新转储：{0}（{1:N1} MB，{2}）" -f $f.Name, ($f.Length / 1MB), $f.LastWriteTime) -ForegroundColor DarkGray
}

$fs = [System.IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite')
$br = New-Object System.IO.BinaryReader($fs)
try {
    $sig = $br.ReadUInt32()
    if ($sig -ne 0x504D444D) { Write-Host '不是 minidump（header 不是 MDMP）' -ForegroundColor Red; exit 1 }
    [void]$br.ReadUInt32()                       # version
    $streamCount = $br.ReadUInt32()
    $dirRva = $br.ReadUInt32()

    $streams = @{}
    $fs.Position = $dirRva
    for ($i = 0; $i -lt $streamCount; $i++) {
        $t = $br.ReadUInt32(); $sz = $br.ReadUInt32(); $rva = $br.ReadUInt32()
        $key = [int]$t
        # ⚠ 必须转 int：Hashtable 里 uint32 键与 int 键 Equals 不相等
        if ($key -ne 0 -and -not $streams.ContainsKey($key)) { $streams[$key] = @{ Size = $sz; Rva = $rva } }
    }

    function Get-DumpString([uint32]$rva) {
        if ($rva -eq 0 -or $rva -ge $fs.Length) { return '?' }
        $back = $fs.Position
        try {
            $fs.Position = $rva
            $len = $br.ReadUInt32()
            if ($len -eq 0 -or $len -gt 4096 -or ($rva + 4 + $len) -gt $fs.Length) { return '?' }
            $bytes = $br.ReadBytes([int]$len)
            return [System.Text.Encoding]::Unicode.GetString($bytes)
        }
        finally { $fs.Position = $back }
    }

    # 模块表（stream 4），条目固定 108 字节
    $modules = New-Object System.Collections.ArrayList
    if ($streams.ContainsKey(4)) {
        $fs.Position = $streams[4].Rva
        $count = $br.ReadUInt32()
        $entryBase = $fs.Position
        for ($i = 0; $i -lt $count; $i++) {
            $fs.Position = $entryBase + $i * 108
            $imageBase = $br.ReadUInt64()
            $sizeOfImage = $br.ReadUInt32()
            $fs.Position = $entryBase + $i * 108 + 20
            $nameRva = $br.ReadUInt32()
            [void]$modules.Add([pscustomobject]@{
                Base = $imageBase
                End = $imageBase + [Math]::Max([uint64]$sizeOfImage, [uint64]1)
                Name = (Get-DumpString $nameRva)
            })
        }
    }

    function Resolve-Module([uint64]$addr) {
        foreach ($m in $modules) {
            if ($addr -ge $m.Base -and $addr -lt $m.End) {
                return ("{0} (+0x{1:X})" -f (Split-Path $m.Name -Leaf), ($addr - $m.Base))
            }
        }
        return '(不在任何模块内)'
    }

    if ($ModsOnly) {
        Write-Host '=== 当时加载的本地 mod（mods\ 目录）===' -ForegroundColor Cyan
        foreach ($m in $modules) {
            if ($m.Name -match '\\mods\\') { Write-Host ("  0x{0:X16}  {1}" -f $m.Base, (Split-Path $m.Name -Leaf)) }
        }
        exit 0
    }

    if (-not $streams.ContainsKey(6)) { Write-Host '没有异常流（ExceptionStream）' -ForegroundColor Yellow; exit 1 }
    $fs.Position = $streams[6].Rva
    $threadId = $br.ReadUInt32()
    [void]$br.ReadUInt32()
    $exCode = $br.ReadUInt32()
    [void]$br.ReadUInt32()                       # ExceptionFlags
    [void]$br.ReadUInt64()                       # ExceptionRecord
    $exAddr = $br.ReadUInt64()

    # ⚠ 按十六进制字符串比对：0xC0000005 这类值超过 Int32 范围，直接和整数常量比会永远不相等
    $codeName = switch ('{0:X8}' -f $exCode) {
        'C0000005' { '访问冲突 (ACCESS_VIOLATION)' }
        'C00000FD' { '栈溢出 (STACK_OVERFLOW)' }
        'C0000409' { '快速失败 (fail-fast)' }
        'E0434352' { '.NET 未处理异常 (CLR exception)' }
        '80000003' { '断点 (BREAKPOINT)' }
        'C000001D' { '非法指令' }
        default    { '其它' }
    }

    Write-Host ''
    Write-Host ("转储              : {0}" -f $Path)
    Write-Host ("崩溃线程          : {0}" -f $threadId)
    Write-Host ("异常码            : 0x{0:X8}  {1}" -f $exCode, $codeName) -ForegroundColor Red
    Write-Host ("异常地址          : 0x{0:X}" -f $exAddr)
    Write-Host ("→ 归属模块        : {0}" -f (Resolve-Module $exAddr)) -ForegroundColor Yellow
    Write-Host ("模块总数          : {0}" -f $modules.Count)
    Write-Host ''
    Write-Host '=== 当时加载的本地 mod（mods\ 目录）===' -ForegroundColor Cyan
    foreach ($m in $modules) {
        if ($m.Name -match '\\mods\\') { Write-Host ("  0x{0:X16}  {1}" -f $m.Base, (Split-Path $m.Name -Leaf)) }
    }
}
finally {
    $br.Close()
    $fs.Close()
}
