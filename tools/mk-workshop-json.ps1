# Rebuild workshop\workshop.json from description.txt + changenote.txt + title.txt.
# ASCII-only script on purpose: Windows PowerShell 5.1 reads a BOM-less .ps1 as ANSI.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$jsonPath = Join-Path $root 'workshop\workshop.json'
$descPath = Join-Path $root 'workshop\description.txt'
$notePath = Join-Path $root 'workshop\changenote.txt'
$titlePath = Join-Path $root 'workshop\title.txt'

function Read-Clean([string]$path) {
    $t = [System.IO.File]::ReadAllText($path)
    $t = $t -replace "`r`n", "`n"
    $t = $t -replace "`r", "`n"
    return $t.Trim()
}

function Esc([string]$s) {
    $s = $s.Replace('\', '\\')
    $s = $s.Replace('"', '\"')
    $s = $s.Replace("`r", '')
    $s = $s.Replace("`n", '\n')
    $s = $s.Replace("`t", '\t')
    return $s
}

$title = Read-Clean $titlePath
$desc = Read-Clean $descPath
$note = Read-Clean $notePath

# Only the keys that MUST be machine-controlled live here: title / description /
# changeNote / dependencies.
#
# Deliberately NOT emitted (verified in sts2-mod-uploader-main/src/UploadCommand.cs):
#   visibility         - SetItemVisibility runs unless the key is null; a stale
#                        "private" would flip a published item back to private.
#   tags               - SetItemTags runs whenever tags != null; an empty array
#                        WIPES whatever tags were chosen on the workshop page.
#   contentDescriptors - reconciled against the item (empty array removes them).
# Leaving the keys out = null = the uploader leaves the item's page settings alone,
# so manual edits on steamcommunity.com survive every future upload.
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('{')
$lines.Add('  "title": "' + (Esc $title) + '",')
$lines.Add('  "description": "' + (Esc $desc) + '",')
$lines.Add('  "changeNote": "' + (Esc $note) + '",')
$lines.Add('  "dependencies": [3737335127]')
$lines.Add('}')

$utf8 = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($jsonPath, (($lines -join "`n") + "`n"), $utf8)

function Show([string]$label, [string]$v) {
    $head = $v.Substring(0, [Math]::Min(24, $v.Length)).Replace("`n", '<LF>')
    $tail = $v.Substring([Math]::Max(0, $v.Length - 24)).Replace("`n", '<LF>')
    Write-Host ("{0}: len={1} head=[{2}] tail=[{3}]" -f $label, $v.Length, $head, $tail)
}

$check = [System.IO.File]::ReadAllText($jsonPath) | ConvertFrom-Json
Show 'title ' $check.title
Show 'desc  ' $check.description
Show 'note  ' $check.changeNote
Write-Host ("deps=" + ($check.dependencies -join ',') + " ; visibility=" + $(if ($null -eq $check.visibility) { '<omitted>' } else { $check.visibility }) + " ; tags=" + $(if ($null -eq $check.tags) { '<omitted>' } else { $check.tags.Count }))
$raw = [System.IO.File]::ReadAllText($jsonPath)
Write-Host ("raw file length = " + $raw.Length + " ; Bilibili lines = " + (($raw -split "\\n") | Select-String -Pattern 'Bilibili').Count)
