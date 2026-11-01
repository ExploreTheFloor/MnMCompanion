<#
.SYNOPSIS
  Writes the text of one version's release: its entry in CHANGELOG.md, headed with the version.

.DESCRIPTION
  The release workflow (.github/workflows/release.yml) and scripts\build-release.ps1 both use this, so a release's notes are always what the change log says. It fails when
  CHANGELOG.md has no entry for the version, or the entry is empty, so a release can't go out with only the download blurb (0.4.6 and 0.4.7 nearly did).

.EXAMPLE
  scripts\release-notes.ps1 -Version 0.4.7
#>
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Path = (Join-Path (Split-Path $PSScriptRoot -Parent) 'CHANGELOG.md')
)

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "The version should look like 1.2.3 (got '$Version')." }
if (-not (Test-Path $Path)) { throw "There is no change log at $Path." }

$lines = Get-Content $Path -Encoding utf8
$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "^## $([regex]::Escape($Version)) \(\d{4}-\d{2}-\d{2}\)\s*$") { $start = $i + 1; break }
}
if ($start -lt 0) { throw "CHANGELOG.md has no entry headed '## $Version (yyyy-mm-dd)'. Write what changed in this version before releasing it." }

$body = New-Object System.Collections.Generic.List[string]
for ($i = $start; $i -lt $lines.Count -and $lines[$i] -notmatch '^## '; $i++) { $body.Add($lines[$i]) }
# the file is wrapped to be read as text; a release page (and the update message) wants each bullet on one line, so an indented continuation is joined to its bullet
$joined = New-Object System.Collections.Generic.List[string]
foreach ($line in $body) {
    if ($line -match '^\s{2,}\S' -and $joined.Count -gt 0 -and $joined[$joined.Count - 1].Trim()) { $joined[$joined.Count - 1] += ' ' + $line.Trim() }
    else { $joined.Add($line.TrimEnd()) }
}
$text = ($joined -join "`n").Trim()
if (-not $text) { throw "The entry for $Version in CHANGELOG.md is empty." }

# the change log links to the files of this repository; a release page can't follow those, so they become full addresses
$site = 'https://github.com/ExploreTheFloor/MnMCompanion'
$text = $text -replace '\]\(README\.md#', "]($site#"
$text = $text -replace '\]\(docs/FAQ\.md#', "]($site/blob/main/docs/FAQ.md#"

"**Companion $Version**`n`n$text"
