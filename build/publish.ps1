<#
.SYNOPSIS
    Builds and packages the complete Dashboard release.

.DESCRIPTION
    Orchestrates the full release build:

      1. Clean and restore
      2. dotnet publish — Dashboard web application (net8.0, Release, framework-dependent)
      3. dotnet build   — Desktop Extension (net48, x64, Release) [Windows only]
      4. Stage all artifacts under artifacts\staging\
      5. dotnet build   — WiX installer project → Dashboard-{version}-Setup.msi [Windows only]
      6. Assemble final artifacts\ release folder

    OUTPUT:
      artifacts\
        Dashboard-{version}-Setup.msi
        WebApp\              (dotnet publish output — deploy to IIS)
        Extension\           (Desktop Extension EXE + dependencies)
        WebClientButton\     (lf-dashboard-button.js — deploy to LF Web Client)
        ConfigTemplate\
          laserfiche.config.json
          extension.config.json
        docs\
          InstallationGuide.md
          UpgradeGuide.md
          ReleaseNotes.md

.PARAMETER Version
    Product version to embed in the MSI and output filename.
    Defaults to the version in Directory.Build.props.

.PARAMETER SkipMsi
    Skip the WiX MSI build.  Useful on Linux/CI when only the publish
    artifacts are needed and the MSI will be built in a separate Windows step.

.PARAMETER SkipExtension
    Skip the Desktop Extension build.  Set automatically on non-Windows platforms.

.EXAMPLE
    # Standard Windows release build (run from the repository root):
    .\build\publish.ps1

.EXAMPLE
    # Build with explicit version:
    .\build\publish.ps1 -Version "1.2.3"

.EXAMPLE
    # Build without MSI (for Linux CI):
    .\build\publish.ps1 -SkipMsi

.NOTES
    Run from the repository root directory.
    On Windows: run in a Developer PowerShell or any terminal with .NET 8 SDK on PATH.
    On Linux/macOS: -SkipMsi and -SkipExtension are implied automatically.
#>

[CmdletBinding()]
param(
    [string]$Version    = "",
    [switch]$SkipMsi,
    [switch]$SkipExtension
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Detect platform ────────────────────────────────────────────────────────────

$IsWindowsOS = $IsWindows -or ($PSVersionTable.PSVersion.Major -le 5)

if (-not $IsWindowsOS) {
    Write-Host "Non-Windows platform detected. MSI and Desktop Extension builds will be skipped." -ForegroundColor Yellow
    $SkipMsi       = $true
    $SkipExtension = $true
}

# ── Paths ─────────────────────────────────────────────────────────────────────

$RepoRoot     = $PSScriptRoot | Split-Path -Parent
$ArtifactsDir = Join-Path $RepoRoot "artifacts"
$StagingDir   = Join-Path $ArtifactsDir "staging"

$WebProjPath  = Join-Path $RepoRoot "src\LFPortal.Web\LFPortal.Web.csproj"
$ExtProjPath  = Join-Path $RepoRoot "src\Dashboard.DesktopExtension\Dashboard.DesktopExtension.csproj"
$InstallerProj = Join-Path $RepoRoot "installer\Dashboard.Installer\Dashboard.Installer.wixproj"
$DbPropsPath  = Join-Path $RepoRoot "Directory.Build.props"

# ── Resolve version ────────────────────────────────────────────────────────────

if ([string]::IsNullOrWhiteSpace($Version)) {
    # Read from Directory.Build.props
    if (Test-Path $DbPropsPath) {
        [xml]$dbProps = Get-Content $DbPropsPath
        $versionNode  = $dbProps.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ }
        $Version      = ($versionNode | Select-Object -First 1) -replace '\s', ''
    }
    if ([string]::IsNullOrWhiteSpace($Version)) {
        $Version = "1.0.0"
    }
}

# ── Header ────────────────────────────────────────────────────────────────────

function Write-Step([string]$msg) { Write-Host "`n  ── $msg" -ForegroundColor Cyan }
function Write-OK([string]$msg)   { Write-Host "     [OK] $msg" -ForegroundColor Green }
function Invoke-Step([string]$label, [scriptblock]$sb) {
    Write-Step $label
    & $sb
    if ($LASTEXITCODE -ne $null -and $LASTEXITCODE -ne 0) {
        Write-Host "  [FAILED] Exit code: $LASTEXITCODE" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

Write-Host ""
Write-Host "  Dashboard Release Build" -ForegroundColor White
Write-Host "  Version  : $Version"     -ForegroundColor Gray
Write-Host "  Platform : $(if ($IsWindowsOS) { 'Windows' } else { 'Linux/macOS (MSI skipped)' })" -ForegroundColor Gray
Write-Host "  Output   : $ArtifactsDir" -ForegroundColor Gray
Write-Host ""

# ── Clean artifacts ────────────────────────────────────────────────────────────

Invoke-Step "Cleaning previous artifacts" {
    if (Test-Path $ArtifactsDir) {
        Remove-Item $ArtifactsDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $StagingDir\WebApp        -Force | Out-Null
    New-Item -ItemType Directory -Path $StagingDir\Extension     -Force | Out-Null
    New-Item -ItemType Directory -Path $StagingDir\ConfigTemplate -Force | Out-Null
    Write-OK "artifacts\staging\ created."
}

# ── Restore ────────────────────────────────────────────────────────────────────

Invoke-Step "Restoring NuGet packages" {
    & dotnet restore (Join-Path $RepoRoot "LFPortal.sln") --verbosity minimal
}

# ── Publish Dashboard web application ─────────────────────────────────────────

Invoke-Step "Publishing Dashboard web application (net8.0, Release, framework-dependent)" {
    & dotnet publish $WebProjPath `
        --configuration Release `
        --output "$StagingDir\WebApp" `
        --verbosity minimal `
        -p:Version=$Version

    Write-OK "Published to: $StagingDir\WebApp"
}

# ── Build Desktop Extension (Windows only) ─────────────────────────────────────

if (-not $SkipExtension) {
    Invoke-Step "Building Desktop Extension (net48, x64, Release)" {
        # The Desktop Extension project is excluded from LFPortal.sln
        # and must be built separately on Windows.
        # Requires: Laserfiche SDK 10.4 DLLs in vendor\LaserficheSdk\bin\10.4\net-4.0\
        & dotnet build $ExtProjPath `
            --configuration Release `
            --verbosity minimal `
            -p:Version=$Version

        # Copy output to staging
        $extOutputDir = Join-Path $RepoRoot "src\Dashboard.DesktopExtension\bin\Release\net48"
        if (Test-Path $extOutputDir) {
            Copy-Item "$extOutputDir\*" -Destination "$StagingDir\Extension" -Recurse -Force
            Write-OK "Extension staged to: $StagingDir\Extension"
        }
        else {
            Write-Host "  [WARN] Extension output not found at: $extOutputDir" -ForegroundColor Yellow
        }
    }
}
else {
    Write-Host "`n  ── Desktop Extension build skipped." -ForegroundColor DarkGray
    # Create empty directory so the MSI build has a target (it will be empty
    # and produce a stub; replace with real output on Windows)
    New-Item -ItemType Directory -Path "$StagingDir\Extension" -Force | Out-Null
}

# ── Stage config templates ─────────────────────────────────────────────────────

Invoke-Step "Staging configuration templates" {
    Copy-Item (Join-Path $RepoRoot "config\templates\*") `
              -Destination "$StagingDir\ConfigTemplate" -Force
    Write-OK "Config templates staged."
}

# ── Stage Web Client button script ─────────────────────────────────────────────

Invoke-Step "Staging Web Client button script" {
    $wcbDir = Join-Path $ArtifactsDir "WebClientButton"
    New-Item -ItemType Directory -Path $wcbDir -Force | Out-Null
    $srcJs = Join-Path $RepoRoot "src\LFPortal.Web\wwwroot\js\lf-webclient-button.js"
    Copy-Item $srcJs -Destination (Join-Path $wcbDir "lf-dashboard-button.js") -Force
    # Also copy the deployment script
    $deployScript = Join-Path $RepoRoot "installer\Deploy-WebClientButton.ps1"
    Copy-Item $deployScript -Destination $wcbDir -Force
    Write-OK "Web Client artifacts staged to: $wcbDir"
}

# ── Build MSI (Windows only) ───────────────────────────────────────────────────

if (-not $SkipMsi) {
    Invoke-Step "Building MSI installer (WiX v4)" {
        # Ensure WiX v4 global tool is installed
        $wixVersion = & dotnet wix --version 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  Installing WiX v4 global tool..." -ForegroundColor Yellow
            & dotnet tool install --global wix
        }

        # Ensure required extensions are installed
        foreach ($ext in @("WixToolset.UI.wixext/4.0.5",
                           "WixToolset.Iis.wixext/4.0.3",
                           "WixToolset.Util.wixext/4.0.5")) {
            & wix extension add $ext --global 2>&1 | Out-Null
        }

        & dotnet build $InstallerProj `
            --configuration Release `
            --verbosity minimal `
            -p:ProductVersion=$Version

        $msiPath = Join-Path $ArtifactsDir "Dashboard-$Version-Setup.msi"
        if (Test-Path $msiPath) {
            Write-OK "MSI: $msiPath"
        }
        else {
            Write-Host "  [WARN] MSI output not found at expected path: $msiPath" -ForegroundColor Yellow
        }
    }
}
else {
    Write-Host "`n  ── MSI build skipped." -ForegroundColor DarkGray
}

# ── Assemble final artifacts folder ───────────────────────────────────────────

Invoke-Step "Assembling release artifacts" {
    # WebApp
    $dst = Join-Path $ArtifactsDir "WebApp"
    if (-not (Test-Path $dst)) { Copy-Item "$StagingDir\WebApp" -Destination $dst -Recurse -Force }

    # Extension
    $dst = Join-Path $ArtifactsDir "Extension"
    if (-not (Test-Path $dst)) { Copy-Item "$StagingDir\Extension" -Destination $dst -Recurse -Force }

    # ConfigTemplate
    $dst = Join-Path $ArtifactsDir "ConfigTemplate"
    New-Item -ItemType Directory -Path $dst -Force | Out-Null
    Copy-Item "$StagingDir\ConfigTemplate\*" -Destination $dst -Force

    # Docs
    $dst = Join-Path $ArtifactsDir "docs"
    New-Item -ItemType Directory -Path $dst -Force | Out-Null
    foreach ($doc in @("InstallationGuide.md", "UpgradeGuide.md", "ReleaseNotes.md")) {
        $src = Join-Path $RepoRoot "docs\$doc"
        if (Test-Path $src) { Copy-Item $src -Destination $dst -Force }
    }

    Write-OK "Release artifacts assembled in: $ArtifactsDir"
}

# ── Final summary ──────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "  ════════════════════════════════════════════════════════" -ForegroundColor DarkGray
Write-Host "  Build complete — Dashboard $Version" -ForegroundColor Green
Write-Host ""
Write-Host "  Artifacts:" -ForegroundColor White
Get-ChildItem $ArtifactsDir -Depth 0 | ForEach-Object {
    Write-Host "    $($_.Name)" -ForegroundColor Gray
}
Write-Host ""
if ($SkipMsi) {
    Write-Host "  MSI was skipped.  To build the MSI:" -ForegroundColor Yellow
    Write-Host "    Run this script on a Windows machine with WiX v4 installed." -ForegroundColor Gray
    Write-Host "    .\build\publish.ps1 -Version $Version" -ForegroundColor Gray
}
Write-Host ""
