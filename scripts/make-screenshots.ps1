<#
.SYNOPSIS
  Remakes the pictures in docs\images from a made-up game folder, so they never show a real player.

.DESCRIPTION
  1. scripts\make-demo-ledger.py writes a demo ledger (invented players) dated today into a temporary folder.
  2. The built program draws each picture off screen with its --snapshot option (nothing appears on the desktop, no settings are written).
  3. The empty margin around each picture is trimmed.

  Build first (dotnet build MnMCompanion.sln). Needs python for step 1.

.EXAMPLE
  scripts\make-screenshots.ps1
  scripts\make-screenshots.ps1 -Exe src\MnMCompanion\bin\Release\net10.0-windows\win-x64\MnMCompanion.exe
#>
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\src\MnMCompanion\bin\Debug\net10.0-windows\MnMCompanion.exe'),
    [string]$Out = (Join-Path $PSScriptRoot '..\docs\images')
)

$ErrorActionPreference = 'Stop'
$Exe = (Resolve-Path $Exe).Path
New-Item -ItemType Directory -Force $Out | Out-Null
$Out = (Resolve-Path $Out).Path
$demo = Join-Path ([IO.Path]::GetTempPath()) ("mnmcompanion-demo-" + [guid]::NewGuid().ToString('N'))
python (Join-Path $PSScriptRoot 'make-demo-ledger.py') $demo | Out-Null
if ($LASTEXITCODE) { throw 'The demo ledger could not be made.' }

function Shot([string]$name, [string[]]$more) {
    $png = Join-Path $Out "$name.png"
    Remove-Item $png -ErrorAction SilentlyContinue
    $arguments = @('--ledger', "`"$demo`"", '--character', 'Brindlewick', '--snapshot', "`"$png`"") + $more
    Start-Process $Exe -ArgumentList $arguments -Wait
    Remove-Item "$png.txt" -ErrorAction SilentlyContinue
    if (-not (Test-Path $png)) { throw "No picture was made for $name." }
}

try {
    # The small window
    Shot 'window-value'  '--window', '--view', 'Value', '--size', '480x640'
    Shot 'window-items'  '--window', '--view', 'Items', '--size', '480x640'
    Shot 'window-mobs'   '--window', '--view', 'Mobs', '--size', '480x640'
    Shot 'window-feed'   '--window', '--view', 'Feed', '--size', '480x640'
    # The other pages of the bottom bar (the Map page is drawn as its first-time question: the atlas itself is a live web page)
    Shot 'window-vendor'  '--window', '--mini', 'Vendor', '--size', '480x640'
    Shot 'window-trades'  '--window', '--mini', 'Trades', '--size', '480x640'
    Shot 'window-changes' '--window', '--mini', 'Items', '--size', '480x640'
    Shot 'window-harvest' '--window', '--mini', 'Harvest', '--size', '480x640'
    Shot 'window-map'     '--window', '--mini', 'Map', '--size', '480x640'
    Shot 'window-update' '--window', '--view', 'Value', '--size', '480x640', '--preview-update'
    # The History window
    $big = '--breakdown', '--size', '1560x900'
    Shot 'history-loot'        ($big)
    Shot 'history-player'      ($big + '--member', 'Tamsin', '--tab', 'Items')
    Shot 'history-drop-rates'  ($big + '--tab', '"Drop rates"')
    Shot 'history-drop-detail' ($big + '--tab', '"Drop rates"', '--drop', '"a Dustrend legionary"')
    Shot 'history-vendors'     ($big + '--page', 'Vendors')
    Shot 'history-price-book'  ($big + '--page', 'Vendors', '--tab', '"Price book"')
    Shot 'history-harvest'     ($big + '--page', 'Harvest')
    Shot 'history-changes'     ($big + '--page', 'Changes')
    Shot 'history-trades'      ($big + '--page', 'Trades')
}
finally {
    Remove-Item $demo -Recurse -Force -ErrorAction SilentlyContinue
}

# Trim the transparent margin the off-screen drawing leaves around a window
Add-Type -AssemblyName System.Drawing
function Blank($c) { $c.A -lt 10 }
foreach ($file in Get-ChildItem $Out -Filter *.png) {
    $bmp = [System.Drawing.Bitmap]::FromFile($file.FullName)
    $w = $bmp.Width
    while ($w -gt 1) { $blank = $true; for ($y = 0; $y -lt $bmp.Height; $y += 3) { if (-not (Blank $bmp.GetPixel($w - 1, $y))) { $blank = $false; break } }; if ($blank) { $w-- } else { break } }
    $h = $bmp.Height
    while ($h -gt 1) { $blank = $true; for ($x = 0; $x -lt $bmp.Width; $x += 3) { if (-not (Blank $bmp.GetPixel($x, $h - 1))) { $blank = $false; break } }; if ($blank) { $h-- } else { break } }
    if ($w -lt $bmp.Width -or $h -lt $bmp.Height) {
        $crop = $bmp.Clone([System.Drawing.Rectangle]::new(0, 0, $w, $h), $bmp.PixelFormat)
        $bmp.Dispose()
        $tmp = "$($file.FullName).tmp"
        $crop.Save($tmp, [System.Drawing.Imaging.ImageFormat]::Png)
        $crop.Dispose()
        Move-Item $tmp $file.FullName -Force
    }
    else { $bmp.Dispose() }
}
Get-ChildItem $Out -Filter *.png | ForEach-Object { '{0,-26} {1,8:N0} bytes' -f $_.Name, $_.Length }
