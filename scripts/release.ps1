<#
.SYNOPSIS
  Builds Halo and publishes a release that installed copies pick up automatically.

.DESCRIPTION
  1. Bump <Version> in Directory.Build.props.
  2. Commit and push.
  3. Run:  .\scripts\release.ps1 -Publish

  Without -Publish it only builds the packages into artifacts\releases, so you can try Setup first.
  The GitHub upload uses your `gh` login.

  Installed copies of Halo check GitHub every few hours, download the update in the background
  (only the changed parts, when the previous release is on GitHub) and offer a restart.
#>
param(
    [switch]$Publish,
    [string]$NotesFile,
    [string]$OutputDir = "artifacts\releases"
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

$repo = "https://github.com/Roushan-2104/Halo-Notch-For-Windows"
$version = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> in Directory.Build.props" }
$publishDir = "artifacts\publish"
Write-Host "Halo $version" -ForegroundColor Cyan

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish src/Halo.App -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o $publishDir
if ($LASTEXITCODE) { throw "dotnet publish failed" }

dotnet tool restore | Out-Null
$token = gh auth token
if ($LASTEXITCODE -or -not $token) { throw "Sign in with 'gh auth login' first" }

# The previous release lets vpk build a delta package, so updates download only what changed.
dotnet vpk download github --repoUrl $repo --token $token -o $OutputDir
if ($LASTEXITCODE) { Write-Warning "No previous Velopack release found; this one ships as a full package only." }

$pack = @("vpk", "pack", "-u", "HaloNotch", "-v", $version, "-p", $publishDir, "-e", "Halo.exe", "-o", $OutputDir,
    "--packTitle", "Halo", "--packAuthors", "Roushan", "--icon", "assets\halo.ico")
if ($NotesFile) { $pack += @("--releaseNotes", $NotesFile) }
dotnet @pack
if ($LASTEXITCODE) { throw "vpk pack failed" }

if (-not $Publish) {
    Write-Host "Built into $OutputDir. Re-run with -Publish to release it." -ForegroundColor Yellow
    return
}
dotnet vpk upload github --repoUrl $repo --token $token -o $OutputDir --publish --releaseName "Halo v$version" --tag "v$version" --targetCommitish main
if ($LASTEXITCODE) { throw "Upload failed" }
Write-Host "Released v$version. Installed copies will update within a few hours." -ForegroundColor Green
