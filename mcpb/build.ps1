#!/usr/bin/env pwsh
# Builds the Claude Desktop extension (.mcpb) for statuscake-mcp.
#
# Publishes the F#/.NET server as a self-contained, single-file win-x64
# executable (so the host needs neither .NET nor anything else on PATH),
# stages it next to the manifest, then packs the bundle into a .mcpb file.
#
# Usage:
#   pwsh mcpb/build.ps1                 # build dist/statuscake-mcp.mcpb
#   pwsh mcpb/build.ps1 -Runtime win-x64
#   pwsh mcpb/build.ps1 -Version 1.2.3  # stamp this version into the binary + manifest

[CmdletBinding()]
param(
    [string]$Runtime = 'win-x64',
    [string]$Configuration = 'Release',
    # Override the manifest version (CI passes the git tag); defaults to the manifest's own version.
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$mcpbDir   = $PSScriptRoot
$repoRoot  = Split-Path -Parent $mcpbDir
$project   = Join-Path $repoRoot 'src/StatusCakeMcp/StatusCakeMcp.fsproj'
$manifest  = Join-Path $mcpbDir  'manifest.json'
$stageDir  = Join-Path $mcpbDir  'stage'
$serverDir = Join-Path $stageDir 'server'
$distDir   = Join-Path $repoRoot 'dist'
$output    = Join-Path $distDir  'statuscake-mcp.mcpb'

# Keep the binary, the staged manifest, and the bundle all on one version.
$manifestJson = Get-Content $manifest -Raw
if (-not $Version) { $Version = ($manifestJson | ConvertFrom-Json).version }

Write-Host "Publishing $Runtime ($Configuration), single-file self-contained..." -ForegroundColor Cyan

# Fresh stage each run so stale files never leak into the bundle.
if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
New-Item -ItemType Directory -Path $serverDir -Force | Out-Null
New-Item -ItemType Directory -Path $distDir   -Force | Out-Null

dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -p:Version=$Version `
    -o $serverDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

# Bundle only what the runtime needs; drop symbols/dev config if any slipped through.
Get-ChildItem $serverDir -Include '*.pdb', 'appsettings.Development.json' -Recurse |
    Remove-Item -Force -ErrorAction SilentlyContinue

$exe = Join-Path $serverDir 'StatusCakeMcp.exe'
if (-not (Test-Path $exe)) { throw "Expected entry point not found: $exe" }

# Stamp the chosen version into the staged manifest (regex keeps the file's formatting).
$stagedJson = $manifestJson -replace '("version"\s*:\s*")[^"]*(")', "`${1}$Version`${2}"
[System.IO.File]::WriteAllText((Join-Path $stageDir 'manifest.json'), $stagedJson)

Write-Host "Packing $output (v$Version)..." -ForegroundColor Cyan

# Prefer the official packer (validates the manifest); fall back to a plain zip.
$packed = $false
if (Get-Command npx -ErrorAction SilentlyContinue) {
    npx --yes @anthropic-ai/mcpb pack $stageDir $output
    if ($LASTEXITCODE -eq 0) { $packed = $true }
    else { Write-Warning "mcpb pack failed; falling back to Compress-Archive." }
}

if (-not $packed) {
    Write-Host "mcpb CLI unavailable; zipping with Compress-Archive." -ForegroundColor Yellow
    if (Test-Path $output) { Remove-Item $output -Force }
    $zip = [System.IO.Path]::ChangeExtension($output, 'zip')
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $stageDir '*') -DestinationPath $zip -Force
    Move-Item $zip $output -Force
}

Write-Host "Built $output" -ForegroundColor Green
