#!/usr/bin/env pwsh
# Fabrium Installer Build Script
# Publishes the app and creates a Velopack installer with desktop shortcut + auto-updater
#
# Usage:  .\build-installer.ps1 [-Version "1.0.0"]

param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$PublishDir = "$Root\publish"
$InstallerDir = "$Root\installer"

Write-Host "=== Fabrium Installer Build ===" -ForegroundColor Cyan
Write-Host "Version: $Version"

# ── 1. Build the web frontend ────────────────────────────────────────────────
Write-Host "`n[1/4] Building web frontend..." -ForegroundColor Yellow
Push-Location "$Root\web"
npm run build
Pop-Location

# ── 2. Publish API (self-contained, single dir) ─────────────────────────────
Write-Host "`n[2/4] Publishing API server..." -ForegroundColor Yellow
if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
dotnet publish "$Root\src\HybridSlicer.Api\HybridSlicer.Api.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o "$PublishDir" `
    /p:PublishSingleFile=false `
    /p:IncludeNativeLibrariesForSelfExtract=false

# ── 3. Publish Launcher into the same dir ────────────────────────────────────
Write-Host "`n[3/4] Publishing Launcher..." -ForegroundColor Yellow
dotnet publish "$Root\src\HybridSlicer.Launcher\HybridSlicer.Launcher.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o "$PublishDir" `
    /p:PublishSingleFile=false `
    /p:IncludeNativeLibrariesForSelfExtract=false

# ── 4. Pack with Velopack ────────────────────────────────────────────────────
Write-Host "`n[4/4] Creating installer..." -ForegroundColor Yellow
if (Test-Path $InstallerDir) { Remove-Item $InstallerDir -Recurse -Force }

vpk pack `
    --packId "Fabrium" `
    --packTitle "Fabrium" `
    --packVersion $Version `
    --packDir "$PublishDir" `
    --mainExe "HybridSlicer.exe" `
    --outputDir "$InstallerDir" `
    --icon "$Root\src\HybridSlicer.Launcher\icon.ico"

Write-Host "`n=== Done ===" -ForegroundColor Green
Write-Host "Installer: $InstallerDir"
Get-ChildItem $InstallerDir | Format-Table Name, @{N="Size(MB)";E={[math]::Round($_.Length/1MB,1)}}
