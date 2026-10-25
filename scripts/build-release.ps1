<#
.SYNOPSIS
  Builds a Companion release: runs the tests, makes the single-file MnMCompanion.exe and its SHA-256 checksum file,
  and (with -Publish) puts both on a GitHub release named after the version.

.DESCRIPTION
  The version is the <Version> in src\MnMCompanion\MnMCompanion.csproj; change it there first (0.1.0 -> 0.1.1, 0.2.0 ...).
  The program is self-contained: players need nothing installed. Output goes to dist\ :

      MnMCompanion.exe          the program
      MnMCompanion.exe.sha256   "<64 hex digits>  MnMCompanion.exe", which the app checks every update against

  -Publish needs the GitHub command line (gh) signed in, and the commit to be pushed already (the release's tag is made from it).

.EXAMPLE
  scripts\build-release.ps1                 # build and check only
  scripts\build-release.ps1 -Publish -Notes "Adds the Harvest tab."
#>
param(
    [switch]$Publish,
    [string]$Notes = ""
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\MnMCompanion\MnMCompanion.csproj'
$version = [string](([xml](Get-Content $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "The <Version> in $project should look like 1.2.3 (found '$version')." }
$dist = Join-Path $root 'dist'
Write-Host "Companion $version"

dotnet test (Join-Path $root 'MnMCompanion.sln') --nologo -v q
if ($LASTEXITCODE) { throw 'The tests failed; nothing was built.' }

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None `
    -o $dist --nologo -v q
if ($LASTEXITCODE) { throw 'The build failed.' }

$exe = Join-Path $dist 'MnMCompanion.exe'
$sum = "$exe.sha256"
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
"$hash  MnMCompanion.exe" | Set-Content $sum -Encoding ascii
"{0}  {1:N1} MB  sha256 {2}" -f $exe, ((Get-Item $exe).Length / 1MB), $hash | Write-Host

# The program must run, report this version and know it can replace itself, before anyone is offered it
$about = Join-Path $dist 'about-check.txt'
Start-Process $exe -ArgumentList '--about', '--report', "`"$about`"" -Wait
$text = if (Test-Path $about) { Get-Content $about -Raw } else { '' }
Remove-Item $about -ErrorAction SilentlyContinue
if ($text -notmatch "Companion $([regex]::Escape($version))") { throw "The built program doesn't report version $version (got: $text)." }
Write-Host 'The built program starts and reports its version.'

if ($Publish) {
    $tag = "v$version"
    $notes = if ($Notes) { $Notes } else { "Companion $version" }
    $notes += "`n`nDownload **MnMCompanion.exe** and run it; nothing needs installing. To check the download: ``(Get-FileHash MnMCompanion.exe).Hash`` should equal the digits in **MnMCompanion.exe.sha256**."
    gh release create $tag $exe $sum --title "Companion $version" --notes $notes
    if ($LASTEXITCODE) { throw 'Creating the GitHub release failed.' }
    Write-Host "Published $tag"
}
