<#
.SYNOPSIS
    Builds and packages the complete Dashboard release.

.DESCRIPTION
    Orchestrates the full release build on Windows:

      Step 1  Clean previous artifacts
      Step 2  Restore NuGet packages
      Step 3  Publish Dashboard web application (net8.0, Release, framework-dependent)
      Step 4  Build Desktop Extension (net48, x64, Release)          [Windows only]
      Step 5  Build Dashboard.SetupHelper (net48, x64, Release)      [Windows only]
      Step 6  Build Dashboard.BA managed bootstrapper (net48)        [Windows only]
      Step 7  Stage support files (tools, templates, Web Client JS)
      Step 8  Build MSI (WiX v4)                                     [Windows only]
      Step 9  Build Burn Bundle -> LFDashboard-Setup.exe             [Windows only]
      Step 10 Assemble Release\ folder
      Step 11 Verify deliverables and print BUILD SUCCESSFUL

    OUTPUT (Windows full build):
      Release\
        LFDashboard-Setup.exe    <-- admin runs this
        README.txt
      artifacts\
        Dashboard-{version}-Setup.msi
        WebApp\
        Extension\
        WebClientButton\
        ConfigTemplate\
        docs\

.PARAMETER Version
    Product version to embed in the MSI and output filename.
    Defaults to the <Version> in Directory.Build.props.

.PARAMETER SkipMsi
    Skip the WiX MSI and Bundle builds.
    Implied automatically on non-Windows platforms.

.PARAMETER SkipExtension
    Skip the Desktop Extension, SetupHelper, and BA builds.
    Implied automatically on non-Windows platforms.

.EXAMPLE
    # Standard Windows release build (run from the repository root):
    .\build\publish.ps1

.EXAMPLE
    # Build with an explicit version:
    .\build\publish.ps1 -Version "1.2.3"

.EXAMPLE
    # Skip MSI (Linux CI -- web app only):
    .\build\publish.ps1 -SkipMsi

.NOTES
    Requires: .NET 8 SDK, WiX v4 global tool (dotnet tool install -global wix),
              .NET Framework 4.8 targeting pack (for net48 projects).
    Compatible with: Windows PowerShell 5.1 and PowerShell 7+.
    Run from the REPOSITORY ROOT directory, not from build\.
#>

[CmdletBinding()]
param(
    [string]$Version       = "",
    [switch]$SkipMsi,
    [switch]$SkipExtension
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# =============================================================================
# HELPERS
# =============================================================================

# Write-Stage: prints a numbered step header.
# Usage: Write-Stage 1 11 "Cleaning artifacts"
function Write-Stage {
    param(
        [int]   $Num,
        [int]   $Total,
        [string]$Msg
    )
    Write-Host ""
    Write-Host ("  [Step {0}/{1}] {2}" -f $Num, $Total, $Msg) -ForegroundColor Cyan
}

function Write-OK {
    param([string]$Msg)
    Write-Host ("     [OK] {0}" -f $Msg) -ForegroundColor Green
}

function Write-Warn {
    param([string]$Msg)
    Write-Host ("  [WARN] {0}" -f $Msg) -ForegroundColor Yellow
}

# Invoke-NativeCommand: runs a native executable and captures the exit code
# IMMEDIATELY after the call -- never reads a stale or undefined $LASTEXITCODE.
#
# WHY THIS EXISTS:
#   Under Set-StrictMode -Version Latest, reading $LASTEXITCODE before any native
#   executable has ever run in the session throws "VariableIsUndefined".
#   This function guarantees that $LASTEXITCODE is read only on the very next line
#   after the native call -- making it safe and defined.
#
# Parameters:
#   -Stage          Human-readable stage name for error messages.
#   -FilePath       Executable path or name on PATH (e.g. "dotnet").
#   -Arguments      Array of arguments to pass to the executable.
#   -IgnoreFailure  When set, non-zero exit codes are allowed (e.g. "already installed").
#
# Returns: the exit code (int).
function Invoke-NativeCommand {
    param(
        [string]  $Stage,
        [string]  $FilePath,
        [string[]]$Arguments     = @(),
        [switch]  $IgnoreFailure
    )

    # Execute the native command.
    & $FilePath @Arguments

    # Read exit code IMMEDIATELY on the next line -- this is always defined
    # because a native executable just ran above.
    $ec = $LASTEXITCODE

    if ((-not $IgnoreFailure) -and ($ec -ne 0)) {
        $argDisplay = $Arguments -join ' '
        Write-Host "" 
        Write-Host "  ============================================================" -ForegroundColor Red
        Write-Host "  [FAILED] $Stage" -ForegroundColor Red
        Write-Host "           Executable : $FilePath" -ForegroundColor Red
        Write-Host "           Arguments  : $argDisplay" -ForegroundColor Red
        Write-Host "           Exit code  : $ec" -ForegroundColor Red
        Write-Host "  ============================================================" -ForegroundColor Red
        exit $ec
    }

    return $ec
}

# =============================================================================
# PLATFORM DETECTION
# =============================================================================
# $IsWindows is only available in PowerShell 6+.
# Use $env:OS which equals 'Windows_NT' on every Windows version and
# every PowerShell version (5.1 and 7+).

$IsWindowsOS = ($env:OS -eq 'Windows_NT')

if (-not $IsWindowsOS) {
    Write-Host "Non-Windows platform detected -- MSI, Bundle, and Extension builds will be skipped." `
        -ForegroundColor Yellow
    $SkipMsi       = [switch]$true
    $SkipExtension = [switch]$true
}

# =============================================================================
# TOOLCHAIN VERSION PINS  (single source of truth)
# =============================================================================
# WiX v4 uses Sdk="WixToolset.Wix/4.0.5" in .wixproj, resolved by the WiX
# global tool's MSBuild SDK resolver.  WiX 7 uses a different SDK name
# (WixToolset.Sdk) and a different extension API -- the two generations are
# NOT interchangeable.  This pin MUST match the Sdk version in the .wixproj files.
# Never install WiX without this version argument.
$WixPinnedVersion = "4.0.5"

# =============================================================================
# PATHS
# =============================================================================
# $PSScriptRoot is the build\ folder; repo root is one level up.

$RepoRoot      = Split-Path $PSScriptRoot -Parent
$ArtifactsDir  = Join-Path $RepoRoot "artifacts"
$StagingDir    = Join-Path $ArtifactsDir "staging"
$ReleaseDir    = Join-Path $RepoRoot "Release"

$WebProjPath     = Join-Path $RepoRoot "src\LFPortal.Web\LFPortal.Web.csproj"
$ExtProjPath     = Join-Path $RepoRoot "src\Dashboard.DesktopExtension\Dashboard.DesktopExtension.csproj"
$InstallerProj   = Join-Path $RepoRoot "installer\Dashboard.Installer\Dashboard.Installer.wixproj"
$BAProjPath      = Join-Path $RepoRoot "installer\Dashboard.BA\Dashboard.BA.csproj"
$SetupHelperProj = Join-Path $RepoRoot "installer\Dashboard.SetupHelper\Dashboard.SetupHelper.csproj"
$BundleProj      = Join-Path $RepoRoot "installer\Dashboard.Bundle\Dashboard.Bundle.wixproj"
$DbPropsPath     = Join-Path $RepoRoot "Directory.Build.props"

# =============================================================================
# VALIDATE REQUIRED SOURCE FILES
# =============================================================================

foreach ($required in @($WebProjPath, $DbPropsPath)) {
    if (-not (Test-Path $required)) {
        Write-Host ""
        Write-Host "  [ERROR] Required file not found: $required" -ForegroundColor Red
        Write-Host "          Make sure you are running this script from the repository root." -ForegroundColor Red
        Write-Host "          Example: .\build\publish.ps1" -ForegroundColor Gray
        exit 1
    }
}

# =============================================================================
# RESOLVE VERSION
# =============================================================================

if ([string]::IsNullOrWhiteSpace($Version)) {
    if (Test-Path $DbPropsPath) {
        [xml]$dbProps = Get-Content $DbPropsPath -Encoding UTF8
        # PropertyGroup may be a single object or an array; ForEach-Object handles both.
        $versionNode = $dbProps.Project.PropertyGroup |
                       ForEach-Object { $_.Version }   |
                       Where-Object   { $_ }
        $resolvedVer = ($versionNode | Select-Object -First 1)
        if ($resolvedVer) {
            $Version = ($resolvedVer -replace '\s', '')
        }
    }
    if ([string]::IsNullOrWhiteSpace($Version)) {
        $Version = "1.0.0"
    }
}

# Total stages -- fixed at 11.
$TotalStages = 11
$Step        = 0

# =============================================================================
# HEADER
# =============================================================================

Write-Host ""
Write-Host "  ============================================================" -ForegroundColor DarkGray
Write-Host "  Dashboard Release Build" -ForegroundColor White
Write-Host ("  Version  : {0}" -f $Version) -ForegroundColor Gray
Write-Host ("  Platform : {0}" -f $(if ($IsWindowsOS) { 'Windows' } else { 'Linux/macOS (MSI/Extension skipped)' })) `
    -ForegroundColor Gray
Write-Host ("  Output   : {0}" -f $ArtifactsDir) -ForegroundColor Gray
Write-Host "  ============================================================" -ForegroundColor DarkGray

# =============================================================================
# STEP 1 -- Clean previous artifacts
# =============================================================================

$Step++
Write-Stage $Step $TotalStages "Cleaning previous artifacts"

if (Test-Path $ArtifactsDir) {
    Remove-Item $ArtifactsDir -Recurse -Force
}
# Create all staging subdirectories (no native exe -- pure PS cmdlets, no $LASTEXITCODE).
$null = New-Item -ItemType Directory -Path (Join-Path $StagingDir "WebApp")         -Force
$null = New-Item -ItemType Directory -Path (Join-Path $StagingDir "Extension")      -Force
$null = New-Item -ItemType Directory -Path (Join-Path $StagingDir "ConfigTemplate") -Force
$null = New-Item -ItemType Directory -Path (Join-Path $StagingDir "BA")             -Force
$null = New-Item -ItemType Directory -Path $ReleaseDir                              -Force

Write-OK "artifacts\staging\ created."

# =============================================================================
# STEP 2 -- Restore NuGet packages
# =============================================================================

$Step++
Write-Stage $Step $TotalStages "Restoring NuGet packages"

$slnPath = Join-Path $RepoRoot "LFPortal.sln"
if (Test-Path $slnPath) {
    Invoke-NativeCommand -Stage "NuGet restore" -FilePath "dotnet" `
        -Arguments @("restore", $slnPath, "--verbosity", "minimal")
}
else {
    Write-Warn "LFPortal.sln not found at: $slnPath -- running restore on web project instead."
    Invoke-NativeCommand -Stage "NuGet restore (web project)" -FilePath "dotnet" `
        -Arguments @("restore", $WebProjPath, "--verbosity", "minimal")
}
Write-OK "NuGet packages restored."

# =============================================================================
# STEP 3 -- Publish web application
# =============================================================================

$Step++
Write-Stage $Step $TotalStages "Publishing Dashboard web application (net8.0, Release)"

$webAppOut = Join-Path $StagingDir "WebApp"

Invoke-NativeCommand -Stage "dotnet publish (web app)" -FilePath "dotnet" -Arguments @(
    "publish", $WebProjPath,
    "--configuration", "Release",
    "--output",        $webAppOut,
    "--verbosity",     "minimal",
    "-p:Version=$Version"
)

Write-OK "Web app published to: $webAppOut"

# =============================================================================
# STEP 4 -- Build Desktop Extension (Windows only)
# =============================================================================

$Step++

if ($SkipExtension) {
    Write-Stage $Step $TotalStages "Building Desktop Extension [SKIPPED on non-Windows]"
    Write-Host "  (Skipped)" -ForegroundColor DarkGray
}
elseif (-not (Test-Path $ExtProjPath)) {
    Write-Stage $Step $TotalStages "Building Desktop Extension [SKIPPED - project not found]"
    Write-Warn "Desktop Extension project not found: $ExtProjPath"
    Write-Warn "The MSI will be built without the extension binaries."
}
else {
    Write-Stage $Step $TotalStages "Building Desktop Extension (net48, x64, Release)"

    # Requires Laserfiche SDK DLLs in vendor\LaserficheSdk\bin\10.4\net-4.0\
    Invoke-NativeCommand -Stage "dotnet build (Desktop Extension)" -FilePath "dotnet" -Arguments @(
        "build", $ExtProjPath,
        "--configuration", "Release",
        "--verbosity",     "minimal",
        "-p:Version=$Version"
    )

    $extOut = Join-Path $RepoRoot "src\Dashboard.DesktopExtension\bin\Release\net48"
    if (Test-Path $extOut) {
        $extStaging = Join-Path $StagingDir "Extension"
        Copy-Item (Join-Path $extOut "*") -Destination $extStaging -Recurse -Force
        Write-OK "Extension binaries staged to: $extStaging"
    }
    else {
        Write-Warn "Extension build output not found at: $extOut"
        Write-Warn "The MSI will be built without the extension binaries."
    }
}

# =============================================================================
# STEP 5 -- Build Dashboard.SetupHelper (Windows only)
# =============================================================================

$Step++

if ($SkipExtension) {
    Write-Stage $Step $TotalStages "Building Dashboard.SetupHelper [SKIPPED on non-Windows]"
    Write-Host "  (Skipped)" -ForegroundColor DarkGray
}
elseif (-not (Test-Path $SetupHelperProj)) {
    Write-Stage $Step $TotalStages "Building Dashboard.SetupHelper [SKIPPED - project not found]"
    Write-Warn "SetupHelper project not found: $SetupHelperProj"
}
else {
    Write-Stage $Step $TotalStages "Building Dashboard.SetupHelper (net48, x64, Release)"

    Invoke-NativeCommand -Stage "dotnet build (SetupHelper)" -FilePath "dotnet" -Arguments @(
        "build", $SetupHelperProj,
        "--configuration", "Release",
        "--verbosity",     "minimal",
        "-p:Version=$Version"
    )

    $helperOut = Join-Path $RepoRoot "installer\Dashboard.SetupHelper\bin\Release\net48"
    if (Test-Path $helperOut) {
        $extStaging = Join-Path $StagingDir "Extension"
        $null = New-Item -ItemType Directory -Path $extStaging -Force
        # Copy only the EXE (dependencies are BCL-only; no extra DLLs needed).
        Copy-Item (Join-Path $helperOut "Dashboard.SetupHelper.exe") `
            -Destination $extStaging -Force
        Write-OK "Dashboard.SetupHelper.exe staged to Extension folder."
    }
    else {
        Write-Warn "SetupHelper build output not found at: $helperOut"
    }
}

# =============================================================================
# STEP 6 -- Build Dashboard.BA managed bootstrapper (Windows only)
# =============================================================================

$Step++

if ($SkipMsi) {
    Write-Stage $Step $TotalStages "Building Dashboard.BA [SKIPPED on non-Windows]"
    Write-Host "  (Skipped)" -ForegroundColor DarkGray
}
elseif (-not (Test-Path $BAProjPath)) {
    Write-Stage $Step $TotalStages "Building Dashboard.BA [SKIPPED - project not found]"
    Write-Warn "Dashboard.BA project not found: $BAProjPath"
}
else {
    Write-Stage $Step $TotalStages "Building Dashboard.BA managed bootstrapper (net48)"

    Invoke-NativeCommand -Stage "dotnet build (Dashboard.BA)" -FilePath "dotnet" -Arguments @(
        "build", $BAProjPath,
        "--configuration", "Release",
        "--verbosity",     "minimal",
        "-p:Version=$Version"
    )

    $baOut     = Join-Path $RepoRoot "installer\Dashboard.BA\bin\Release\net48"
    $baStaging = Join-Path $StagingDir "BA"
    $null = New-Item -ItemType Directory -Path $baStaging -Force

    if (Test-Path $baOut) {
        Copy-Item (Join-Path $baOut "*") -Destination $baStaging -Recurse -Force
        Write-OK "Dashboard.BA staged to: $baStaging"
    }
    else {
        Write-Warn "Dashboard.BA output not found at: $baOut"
    }
}

# =============================================================================
# STEP 7 -- Stage support files (tools, config templates, Web Client JS)
# =============================================================================

$Step++
Write-Stage $Step $TotalStages "Staging support files"

# Tools (Configure-Dashboard.ps1)
$toolsDir     = Join-Path $ArtifactsDir "Tools"
$null = New-Item -ItemType Directory -Path $toolsDir -Force
$configurePs1 = Join-Path $RepoRoot "installer\Configure-Dashboard.ps1"
if (Test-Path $configurePs1) {
    Copy-Item $configurePs1 -Destination $toolsDir -Force
    Write-OK "Configure-Dashboard.ps1 staged."
}
else {
    Write-Warn "Configure-Dashboard.ps1 not found: $configurePs1"
}

# Config templates
$templateSrc = Join-Path $RepoRoot "config\templates"
if (Test-Path $templateSrc) {
    $templateDst = Join-Path $StagingDir "ConfigTemplate"
    Copy-Item (Join-Path $templateSrc "*") -Destination $templateDst -Force
    Write-OK "Config templates staged."
}
else {
    Write-Warn "config\templates\ not found at: $templateSrc"
}

# Web Client button script
$wcbDir    = Join-Path $ArtifactsDir "WebClientButton"
$null = New-Item -ItemType Directory -Path $wcbDir -Force
$srcJs     = Join-Path $RepoRoot "src\LFPortal.Web\wwwroot\js\lf-webclient-button.js"
$deployPs1 = Join-Path $RepoRoot "installer\Deploy-WebClientButton.ps1"

if (Test-Path $srcJs) {
    Copy-Item $srcJs -Destination (Join-Path $wcbDir "lf-dashboard-button.js") -Force
    Write-OK "lf-dashboard-button.js staged."
}
else {
    Write-Warn "Source JS not found: $srcJs"
}

if (Test-Path $deployPs1) {
    Copy-Item $deployPs1 -Destination $wcbDir -Force
    Write-OK "Deploy-WebClientButton.ps1 staged."
}

Write-OK "Support files staged."

# =============================================================================
# STEP 8 -- Build MSI (Windows only)
# =============================================================================

$Step++

if ($SkipMsi) {
    Write-Stage $Step $TotalStages "Building MSI [SKIPPED on non-Windows]"
    Write-Host "  (Skipped)" -ForegroundColor DarkGray
}
elseif (-not (Test-Path $InstallerProj)) {
    Write-Stage $Step $TotalStages "Building MSI [SKIPPED - project not found]"
    Write-Warn "WiX installer project not found: $InstallerProj"
}
else {
    Write-Stage $Step $TotalStages "Building MSI installer (WiX v4)"

    # -----------------------------------------------------------------------
    # Ensure WiX $WixPinnedVersion is the active global tool.
    #
    # Root cause of the version-mismatch bug this block replaces:
    #   'dotnet tool install --global wix' without a version pin installs
    #   whatever is latest (e.g. 7.0.0).  WiX 7 uses Sdk="WixToolset.Sdk"
    #   while our .wixproj files declare Sdk="WixToolset.Wix/4.0.5".
    #   The WiX 7 SDK resolver does not know about WixToolset.Wix, and
    #   that package is not on NuGet as a standalone download either --
    #   result: "Could not resolve SDK 'WixToolset.Wix'."
    #
    # Fix: check the currently installed version via 'dotnet tool list --global'
    # (reliable, no PATH-dependent executable call needed) and install/update
    # to exactly $WixPinnedVersion if the version doesn't already match.
    # -----------------------------------------------------------------------
    $toolListOutput = @(& dotnet tool list --global 2>&1)
    $ec = $LASTEXITCODE   # read immediately after native call

    $wixRow = $toolListOutput | Where-Object { $_ -match '^wix\s' }
    $needsInstall = $true
    $needsUpdate  = $false

    if ($wixRow -and ($wixRow -match '\s+(\d+\.\d+[\d.]*)\s+')) {
        $installedVer = $Matches[1]
        if ($installedVer -eq $WixPinnedVersion) {
            $needsInstall = $false
            Write-OK ("WiX {0} already installed." -f $WixPinnedVersion)
        } else {
            $needsInstall = $false
            $needsUpdate  = $true
            Write-Host ("  WiX {0} found but {1} required -- updating to pinned version..." `
                -f $installedVer, $WixPinnedVersion) -ForegroundColor Yellow
        }
    } else {
        Write-Host ("  WiX global tool not found -- installing {0}..." `
            -f $WixPinnedVersion) -ForegroundColor Yellow
    }

    if ($needsUpdate) {
        # 'dotnet tool update --version X' supports both upgrades and downgrades.
        Invoke-NativeCommand `
            -Stage ("dotnet tool update --global wix to {0}" -f $WixPinnedVersion) `
            -FilePath "dotnet" `
            -Arguments @("tool", "update", "--global", "wix", "--version", $WixPinnedVersion)
        Write-OK ("WiX updated to {0}." -f $WixPinnedVersion)
    } elseif ($needsInstall) {
        Invoke-NativeCommand `
            -Stage ("dotnet tool install --global wix {0}" -f $WixPinnedVersion) `
            -FilePath "dotnet" `
            -Arguments @("tool", "install", "--global", "wix", "--version", $WixPinnedVersion)
        Write-OK ("WiX {0} installed." -f $WixPinnedVersion)
    }

    # Verify the active 'wix' command is the correct version.
    # WiX registers as a standalone 'wix' command (not 'dotnet wix').
    # On a freshly-updated session PATH may not have refreshed yet; if
    # 'wix --version' fails, prompt the user to reopen their terminal.
    $wixVerOut = @(& wix --version 2>&1)
    $wixVerEc  = $LASTEXITCODE
    if ($wixVerEc -ne 0) {
        Write-Host ""
        Write-Host "  [WARN] 'wix --version' failed (exit $wixVerEc)." -ForegroundColor Yellow
        Write-Host "         If WiX was just installed, close and reopen the terminal to" `
            -ForegroundColor Yellow
        Write-Host "         refresh PATH, then re-run .\build\publish.ps1." `
            -ForegroundColor Yellow
        Write-Host "         Continuing anyway -- dotnet build may still succeed." `
            -ForegroundColor Yellow
    } else {
        $activeVer = ($wixVerOut -join '').Trim()
        if ($activeVer -notlike "*$WixPinnedVersion*") {
            Write-Host ""
            Write-Host ("  [WARN] 'wix --version' reported '{0}' but {1} is required." `
                -f $activeVer, $WixPinnedVersion) -ForegroundColor Yellow
            Write-Host "         Close and reopen the terminal to refresh PATH, then re-run." `
                -ForegroundColor Yellow
        } else {
            Write-OK ("WiX version verified: {0}" -f $activeVer)
        }
    }

    # -----------------------------------------------------------------------
    # Ensure required WiX v4 extensions are present (global).
    # -IgnoreFailure: non-zero exit means already installed -- that is fine.
    # Extension versions must match the WiX tool generation (4.x).
    # IIS extension 4.0.3 is the last available 4.x build for that extension.
    # -----------------------------------------------------------------------
    $wixExts = @(
        "WixToolset.UI.wixext/$WixPinnedVersion",
        "WixToolset.Iis.wixext/4.0.3",
        "WixToolset.Util.wixext/$WixPinnedVersion",
        "WixToolset.Bal.wixext/$WixPinnedVersion"
    )
    foreach ($ext in $wixExts) {
        Invoke-NativeCommand -Stage ("wix extension add {0}" -f $ext) `
            -FilePath "wix" -Arguments @("extension", "add", $ext, "--global") `
            -IgnoreFailure | Out-Null
    }

    # Build the MSI.
    Invoke-NativeCommand -Stage "dotnet build (MSI)" -FilePath "dotnet" -Arguments @(
        "build", $InstallerProj,
        "--configuration", "Release",
        "--verbosity",     "minimal",
        "-p:ProductVersion=$Version"
    )

    $msiPath = Join-Path $ArtifactsDir "Dashboard-$Version-Setup.msi"
    if (Test-Path $msiPath) {
        Write-OK "MSI built: $msiPath"
    }
    else {
        Write-Warn "MSI not found at expected path: $msiPath"
        Write-Warn "Check the WiX build output for the actual output path."
    }
}

# =============================================================================
# STEP 9 -- Build Burn Bundle / LFDashboard-Setup.exe (Windows only)
# =============================================================================

$Step++

if ($SkipMsi) {
    Write-Stage $Step $TotalStages "Building Bundle [SKIPPED on non-Windows]"
    Write-Host "  (Skipped)" -ForegroundColor DarkGray
}
elseif (-not (Test-Path $BundleProj)) {
    Write-Stage $Step $TotalStages "Building Bundle [SKIPPED - project not found]"
    Write-Warn "Bundle project not found: $BundleProj"
}
else {
    Write-Stage $Step $TotalStages "Building Burn Bundle (LFDashboard-Setup.exe)"

    # WixToolset.Bal.wixext must be present (may already be from Step 8).
    Invoke-NativeCommand -Stage "wix extension add Bal" `
        -FilePath "wix" `
        -Arguments @("extension", "add", "WixToolset.Bal.wixext/$WixPinnedVersion", "--global") `
        -IgnoreFailure | Out-Null

    Invoke-NativeCommand -Stage "dotnet build (Bundle)" -FilePath "dotnet" -Arguments @(
        "build", $BundleProj,
        "--configuration", "Release",
        "--verbosity",     "minimal",
        "-p:ProductVersion=$Version"
    )

    # Expected output path (set by Dashboard.Bundle.wixproj OutputPath).
    $bundleExe = Join-Path $ArtifactsDir "LFDashboard-Setup.exe"

    if (-not (Test-Path $bundleExe)) {
        # Fallback: WiX may have placed it elsewhere; scan the repo.
        $found = Get-ChildItem $RepoRoot -Recurse -Filter "LFDashboard-Setup.exe" `
                     -ErrorAction SilentlyContinue |
                 Select-Object -First 1
        if ($found) {
            Copy-Item $found.FullName -Destination $ArtifactsDir -Force
            Write-Warn "Bundle EXE found at alternate path: $($found.FullName)"
            Write-OK   "Bundle EXE copied to: $bundleExe"
        }
        else {
            Write-Host ""
            Write-Host "  [FAILED] Bundle build completed but LFDashboard-Setup.exe was not found." `
                -ForegroundColor Red
            Write-Host "           Expected: $bundleExe" -ForegroundColor Red
            Write-Host "           Check the WiX build output above for errors." -ForegroundColor Red
            exit 1
        }
    }
    else {
        Write-OK "Bundle built: $bundleExe"
    }
}

# =============================================================================
# STEP 10 -- Assemble Release\ and artifacts\ folders
# =============================================================================

$Step++
Write-Stage $Step $TotalStages "Assembling Release\ and artifacts\ folders"

# -- Release\ (the admin-facing deliverable) --
$null = New-Item -ItemType Directory -Path $ReleaseDir -Force

$bundleExeSrc = Join-Path $ArtifactsDir "LFDashboard-Setup.exe"
if (Test-Path $bundleExeSrc) {
    Copy-Item $bundleExeSrc -Destination $ReleaseDir -Force
    Write-OK "Release\LFDashboard-Setup.exe copied."
}
elseif (-not $SkipMsi) {
    # Windows build, but EXE is missing -- Step 9 should have caught this.
    # Being defensive here; Step 11 verification will also catch it.
    Write-Warn "LFDashboard-Setup.exe not found -- Release\ will be incomplete."
}

# README.txt is source-controlled in Release\ -- nothing to copy.
$readmePath = Join-Path $ReleaseDir "README.txt"
if (Test-Path $readmePath) {
    Write-OK "Release\README.txt present."
}
else {
    Write-Warn "Release\README.txt not found at: $readmePath"
}

# -- artifacts\ (internal build outputs) --

# WebApp (staging -> artifacts\WebApp\)
$webAppDst = Join-Path $ArtifactsDir "WebApp"
if (-not (Test-Path $webAppDst)) {
    $webAppSrc = Join-Path $StagingDir "WebApp"
    if (Test-Path $webAppSrc) {
        Copy-Item $webAppSrc -Destination $webAppDst -Recurse -Force
    }
}

# Extension (staging -> artifacts\Extension\)
$extDst = Join-Path $ArtifactsDir "Extension"
if (-not (Test-Path $extDst)) {
    $extSrc = Join-Path $StagingDir "Extension"
    if (Test-Path $extSrc) {
        Copy-Item $extSrc -Destination $extDst -Recurse -Force
    }
}

# ConfigTemplate
$cfgDst = Join-Path $ArtifactsDir "ConfigTemplate"
$null = New-Item -ItemType Directory -Path $cfgDst -Force
$cfgSrc = Join-Path $StagingDir "ConfigTemplate"
if (Test-Path $cfgSrc) {
    Copy-Item (Join-Path $cfgSrc "*") -Destination $cfgDst -Force
}

# Docs
$docsDst = Join-Path $ArtifactsDir "docs"
$null = New-Item -ItemType Directory -Path $docsDst -Force
foreach ($doc in @("InstallationGuide.md", "UpgradeGuide.md", "ReleaseNotes.md")) {
    $docSrc = Join-Path $RepoRoot "docs\$doc"
    if (Test-Path $docSrc) {
        Copy-Item $docSrc -Destination $docsDst -Force
    }
}

Write-OK "Artifacts assembled."

# =============================================================================
# STEP 11 -- Verify deliverables
# =============================================================================

$Step++
Write-Stage $Step $TotalStages "Verifying deliverables"

$buildFailed = $false

if (-not $SkipMsi) {
    # On a Windows build the EXE MUST be present for the build to be considered successful.
    $exeDest = Join-Path $ReleaseDir "LFDashboard-Setup.exe"
    if (Test-Path $exeDest) {
        Write-OK "VERIFIED: Release\LFDashboard-Setup.exe"
    }
    else {
        Write-Host "  [FAILED] Release\LFDashboard-Setup.exe NOT FOUND." -ForegroundColor Red
        $buildFailed = $true
    }
}

$readmeDest = Join-Path $ReleaseDir "README.txt"
if (Test-Path $readmeDest) {
    Write-OK "VERIFIED: Release\README.txt"
}
else {
    Write-Host "  [FAILED] Release\README.txt NOT FOUND." -ForegroundColor Red
    $buildFailed = $true
}

if ($buildFailed) {
    Write-Host ""
    Write-Host "  BUILD FAILED -- one or more deliverables are missing." -ForegroundColor Red
    Write-Host "  Review the output above for errors and re-run the build." -ForegroundColor Red
    exit 1
}

# =============================================================================
# SUMMARY
# =============================================================================

Write-Host ""
Write-Host "  ============================================================" -ForegroundColor DarkGray
Write-Host ("  BUILD SUCCESSFUL -- Dashboard {0}" -f $Version) -ForegroundColor Green
Write-Host "  ============================================================" -ForegroundColor DarkGray
Write-Host ""
Write-Host "  Artifacts:" -ForegroundColor White
Get-ChildItem $ArtifactsDir -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host ("    {0}" -f $_.Name) -ForegroundColor Gray
}
Write-Host ""

if ($SkipMsi) {
    Write-Host "  MSI and Bundle were skipped (non-Windows build)." -ForegroundColor Yellow
    Write-Host "  To produce LFDashboard-Setup.exe, run on Windows:" -ForegroundColor Yellow
    Write-Host ("    .\\build\\publish.ps1 -Version {0}" -f $Version) -ForegroundColor Gray
    Write-Host ""
}
else {
    Write-Host "  Deliverable for distribution:" -ForegroundColor White
    Write-Host "    Release\LFDashboard-Setup.exe" -ForegroundColor Green
    Write-Host "    Release\README.txt" -ForegroundColor Green
    Write-Host ""
    Write-Host "  Give the admin the Release\ folder." -ForegroundColor Gray
    Write-Host "  They double-click LFDashboard-Setup.exe and follow the wizard." -ForegroundColor Gray
    Write-Host ""
}
