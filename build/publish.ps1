<#
.SYNOPSIS
    Builds and packages the complete Dashboard release.

.DESCRIPTION
    Orchestrates the full release build:

      1. Clean and restore
      2. dotnet publish  - Dashboard web application (net8.0, Release, framework-dependent)
      3. dotnet build   - Desktop Extension (net48, x64, Release) [Windows only]
      4. Stage all artifacts under artifacts\staging\
      5. dotnet build   - WiX installer project -> Dashboard-{version}-Setup.msi [Windows only]
      6. Assemble final artifacts\ release folder

    OUTPUT:
      artifacts\
        Dashboard-{version}-Setup.msi
        WebApp\              (dotnet publish output -- deploy to IIS)
        Extension\           (Desktop Extension EXE + dependencies)
        WebClientButton\     (lf-dashboard-button.js -- deploy to LF Web Client)
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
    [string]$Version       = "",
    [switch]$SkipMsi,
    [switch]$SkipExtension
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------- Platform detection -----------------------------------------------
# $IsWindows is only available in PowerShell 6+.  Windows PowerShell 5.1 does
# not have this automatic variable.  Use $env:OS which equals 'Windows_NT' on
# every Windows version and every PowerShell version (5.1 and 7+).

$IsWindowsOS = ($env:OS -eq 'Windows_NT')

if (-not $IsWindowsOS) {
    Write-Host "Non-Windows platform detected. MSI and Desktop Extension builds will be skipped." `
        -ForegroundColor Yellow
    $SkipMsi       = $true
    $SkipExtension = $true
}

# ---------- Paths ------------------------------------------------------------
# $PSScriptRoot is the build\ folder; repo root is one level up.

$RepoRoot      = Split-Path $PSScriptRoot -Parent
$ArtifactsDir  = Join-Path $RepoRoot "artifacts"
$StagingDir    = Join-Path $ArtifactsDir "staging"

$WebProjPath     = Join-Path $RepoRoot "src\LFPortal.Web\LFPortal.Web.csproj"
$ExtProjPath     = Join-Path $RepoRoot "src\Dashboard.DesktopExtension\Dashboard.DesktopExtension.csproj"
$InstallerProj   = Join-Path $RepoRoot "installer\Dashboard.Installer\Dashboard.Installer.wixproj"
$BAProjPath      = Join-Path $RepoRoot "installer\Dashboard.BA\Dashboard.BA.csproj"
$SetupHelperProj = Join-Path $RepoRoot "installer\Dashboard.SetupHelper\Dashboard.SetupHelper.csproj"
$BundleProj      = Join-Path $RepoRoot "installer\Dashboard.Bundle\Dashboard.Bundle.wixproj"
$DbPropsPath     = Join-Path $RepoRoot "Directory.Build.props"

# ---------- Validate required source files exist ----------------------------

foreach ($required in @($WebProjPath, $DbPropsPath)) {
    if (-not (Test-Path $required)) {
        Write-Host "[ERROR] Required file not found: $required" -ForegroundColor Red
        Write-Host "        Run this script from the repository root." -ForegroundColor Red
        exit 1
    }
}

# ---------- Resolve version --------------------------------------------------

if ([string]::IsNullOrWhiteSpace($Version)) {
    if (Test-Path $DbPropsPath) {
        [xml]$dbProps = Get-Content $DbPropsPath -Encoding UTF8
        $versionNode  = $dbProps.Project.PropertyGroup |
                        ForEach-Object { $_.Version } |
                        Where-Object { $_ }
        $Version = ($versionNode | Select-Object -First 1) -replace '\s', ''
    }
    if ([string]::IsNullOrWhiteSpace($Version)) {
        $Version = "1.0.0"
    }
}

# ---------- Helpers ----------------------------------------------------------

function Write-Step([string]$msg) {
    Write-Host ""
    Write-Host "  -- $msg" -ForegroundColor Cyan
}

function Write-OK([string]$msg) {
    Write-Host "     [OK] $msg" -ForegroundColor Green
}

function Invoke-Cmd {
    <#
    .SYNOPSIS Runs a command, captures exit code, and stops the script on failure.
    #>
    param([string]$Label, [scriptblock]$Block)

    Write-Step $Label
    & $Block
    $code = $LASTEXITCODE
    if ($code -ne $null -and $code -ne 0) {
        Write-Host "  [FAILED] '$Label' exited with code $code" -ForegroundColor Red
        exit $code
    }
}

# ---------- Header -----------------------------------------------------------

Write-Host ""
Write-Host "  Dashboard Release Build" -ForegroundColor White
Write-Host "  Version  : $Version" -ForegroundColor Gray
Write-Host "  Platform : $(if ($IsWindowsOS) { 'Windows' } else { 'Linux/macOS (MSI/Extension skipped)' })" `
    -ForegroundColor Gray
Write-Host "  Output   : $ArtifactsDir" -ForegroundColor Gray

# ---------- Clean artifacts --------------------------------------------------

Invoke-Cmd "Cleaning previous artifacts" {
    if (Test-Path $ArtifactsDir) {
        Remove-Item $ArtifactsDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path (Join-Path $StagingDir "WebApp")         -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $StagingDir "Extension")      -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $StagingDir "ConfigTemplate") -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $StagingDir "BA")             -Force | Out-Null
    Write-OK "artifacts\staging\ created."
}

# ---------- Restore ----------------------------------------------------------

Invoke-Cmd "Restoring NuGet packages" {
    $slnPath = Join-Path $RepoRoot "LFPortal.sln"
    & dotnet restore $slnPath --verbosity minimal
}

# ---------- Publish web application ------------------------------------------

Invoke-Cmd "Publishing Dashboard web application (net8.0, Release, framework-dependent)" {
    $webAppOut = Join-Path $StagingDir "WebApp"
    & dotnet publish $WebProjPath `
        --configuration Release `
        --output $webAppOut `
        --verbosity minimal `
        -p:Version=$Version
    Write-OK "Published to: $webAppOut"
}

# ---------- Build Desktop Extension (Windows only) ---------------------------

if (-not $SkipExtension) {
    if (-not (Test-Path $ExtProjPath)) {
        Write-Host "  [WARN] Desktop Extension project not found: $ExtProjPath" -ForegroundColor Yellow
        Write-Host "         Skipping extension build." -ForegroundColor Yellow
    }
    else {
        Invoke-Cmd "Building Desktop Extension (net48, x64, Release)" {
            # Requires: Laserfiche SDK 10.4 DLLs in vendor\LaserficheSdk\bin\10.4\net-4.0\
            # Target: net48, PlatformTarget=x64, Prefer32Bit=false, no RuntimeIdentifier.
            & dotnet build $ExtProjPath `
                --configuration Release `
                --verbosity minimal `
                -p:Version=$Version

            $extOut = Join-Path $RepoRoot "src\Dashboard.DesktopExtension\bin\Release\net48"
            if (Test-Path $extOut) {
                $extStaging = Join-Path $StagingDir "Extension"
                Copy-Item "$extOut\*" -Destination $extStaging -Recurse -Force
                Write-OK "Extension staged to: $extStaging"
            }
            else {
                Write-Host "  [WARN] Extension build output not found at: $extOut" -ForegroundColor Yellow
                Write-Host "         The MSI will be built without extension binaries." -ForegroundColor Yellow
            }
        }
    }

    # ---- Build Dashboard.SetupHelper (net48, x64) ---------------------------
    # SetupHelper.exe must be staged into the Extension staging folder so it is
    # harvested by HarvestDirectory into the MSI alongside the Desktop Extension.
    # This makes it available from EXTENSIONFOLDER for ExeCommand custom actions.
    if (Test-Path $SetupHelperProj) {
        Invoke-Cmd "Building Dashboard.SetupHelper (net48, x64, Release)" {
            & dotnet build $SetupHelperProj `
                --configuration Release `
                --verbosity minimal `
                -p:Version=$Version

            $helperOut = Join-Path $RepoRoot "installer\Dashboard.SetupHelper\bin\Release\net48"
            if (Test-Path $helperOut) {
                $extStaging = Join-Path $StagingDir "Extension"
                New-Item -ItemType Directory -Path $extStaging -Force | Out-Null
                Copy-Item "$helperOut\Dashboard.SetupHelper.exe" `
                    -Destination $extStaging -Force
                Write-OK "Dashboard.SetupHelper.exe staged to Extension folder."
            }
            else {
                Write-Host "  [WARN] SetupHelper build output not found at: $helperOut" -ForegroundColor Yellow
            }
        }
    }
    else {
        Write-Host "  [WARN] Dashboard.SetupHelper project not found: $SetupHelperProj" -ForegroundColor Yellow
    }
}
else {
    Write-Host ""
    Write-Host "  -- Desktop Extension build skipped." -ForegroundColor DarkGray
    # Ensure the staging directory exists so the MSI build has a target.
    New-Item -ItemType Directory -Path (Join-Path $StagingDir "Extension") -Force | Out-Null
}

# ---------- Build Dashboard.BA (managed bootstrapper, Windows only) -----------

if (-not $SkipMsi) {
    if (Test-Path $BAProjPath) {
        Invoke-Cmd "Building Dashboard.BA (managed bootstrapper DLL, net48)" {
            & dotnet build $BAProjPath `
                --configuration Release `
                --verbosity minimal `
                -p:Version=$Version

            $baOut     = Join-Path $RepoRoot "installer\Dashboard.BA\bin\Release\net48"
            $baStaging = Join-Path $StagingDir "BA"
            New-Item -ItemType Directory -Path $baStaging -Force | Out-Null

            if (Test-Path $baOut) {
                Copy-Item "$baOut\*" -Destination $baStaging -Recurse -Force
                Write-OK "Dashboard.BA staged to: $baStaging"
            }
            else {
                Write-Host "  [WARN] Dashboard.BA output not found at: $baOut" -ForegroundColor Yellow
            }
        }
    }
    else {
        Write-Host "  [WARN] Dashboard.BA project not found: $BAProjPath" -ForegroundColor Yellow
    }
}

# ---------- Stage Tools (Configure-Dashboard.ps1) ----------------------------

Invoke-Cmd "Staging configuration tools" {
    $toolsDir = Join-Path $ArtifactsDir "Tools"
    New-Item -ItemType Directory -Path $toolsDir -Force | Out-Null

    $configurePs1 = Join-Path $RepoRoot "installer\Configure-Dashboard.ps1"
    if (Test-Path $configurePs1) {
        Copy-Item $configurePs1 -Destination $toolsDir -Force
        Write-OK "Configure-Dashboard.ps1 staged in Tools\"
    }
    else {
        Write-Host "  [WARN] Configure-Dashboard.ps1 not found: $configurePs1" -ForegroundColor Yellow
    }
}

# ---------- Stage configuration templates ------------------------------------

Invoke-Cmd "Staging configuration templates" {
    $templateSrc = Join-Path $RepoRoot "config\templates"
    if (-not (Test-Path $templateSrc)) {
        Write-Host "  [WARN] config\templates\ not found at: $templateSrc" -ForegroundColor Yellow
    }
    else {
        $templateDst = Join-Path $StagingDir "ConfigTemplate"
        Copy-Item "$templateSrc\*" -Destination $templateDst -Force
        Write-OK "Config templates staged."
    }
}

# ---------- Stage Web Client button script -----------------------------------

Invoke-Cmd "Staging Web Client button script" {
    $wcbDir    = Join-Path $ArtifactsDir "WebClientButton"
    New-Item -ItemType Directory -Path $wcbDir -Force | Out-Null

    $srcJs     = Join-Path $RepoRoot "src\LFPortal.Web\wwwroot\js\lf-webclient-button.js"
    $deployPs1 = Join-Path $RepoRoot "installer\Deploy-WebClientButton.ps1"

    if (Test-Path $srcJs) {
        Copy-Item $srcJs -Destination (Join-Path $wcbDir "lf-dashboard-button.js") -Force
        Write-OK "lf-dashboard-button.js staged."
    }
    else {
        Write-Host "  [WARN] Source JS not found: $srcJs" -ForegroundColor Yellow
    }

    if (Test-Path $deployPs1) {
        Copy-Item $deployPs1 -Destination $wcbDir -Force
        Write-OK "Deploy-WebClientButton.ps1 staged."
    }

    Write-OK "Web Client artifacts in: $wcbDir"
}

# ---------- Build MSI (Windows only) -----------------------------------------

if (-not $SkipMsi) {
    if (-not (Test-Path $InstallerProj)) {
        Write-Host "  [WARN] WiX project not found: $InstallerProj" -ForegroundColor Yellow
        Write-Host "         Skipping MSI build." -ForegroundColor Yellow
    }
    else {
        Invoke-Cmd "Building MSI installer (WiX v4)" {

            # Verify WiX v4 global tool is installed.
            $null = & dotnet wix --version 2>&1
            if ($LASTEXITCODE -ne 0) {
                Write-Host "  WiX v4 global tool not found. Installing..." -ForegroundColor Yellow
                & dotnet tool install --global wix
                if ($LASTEXITCODE -ne 0) {
                    Write-Host "  [ERROR] Failed to install WiX. Install manually:" -ForegroundColor Red
                    Write-Host "          dotnet tool install --global wix" -ForegroundColor Gray
                    exit 1
                }
            }

            # Ensure required WiX extensions are present.
            $extensions = @(
                "WixToolset.UI.wixext/4.0.5",
                "WixToolset.Iis.wixext/4.0.3",
                "WixToolset.Util.wixext/4.0.5",
                "WixToolset.Bal.wixext/4.0.5"
            )
            foreach ($ext in $extensions) {
                $out = & wix extension add $ext --global 2>&1
                # Non-zero exit here means already installed or minor error; continue.
            }

            & dotnet build $InstallerProj `
                --configuration Release `
                --verbosity minimal `
                -p:ProductVersion=$Version

            $msiPath = Join-Path $ArtifactsDir "Dashboard-$Version-Setup.msi"
            if (Test-Path $msiPath) {
                Write-OK "MSI built: $msiPath"
            }
            else {
                Write-Host "  [WARN] MSI not found at expected path: $msiPath" -ForegroundColor Yellow
                Write-Host "         Check the WiX build output for the actual output path." -ForegroundColor Yellow
            }
        }
    }
}
else {
    Write-Host ""
    Write-Host "  -- MSI build skipped." -ForegroundColor DarkGray
}

# ---------- Build Bundle / Setup EXE (Windows only) --------------------------
#
# Produces artifacts\LFDashboard-Setup.exe from Dashboard.Bundle.wixproj.
# Requires:
#   - artifacts\staging\BA\Dashboard.BA.dll            (built above)
#   - artifacts\Dashboard-{version}-Setup.msi          (built above)
# Optional:
#   - installer\prerequisites\*.exe                    (user-supplied prereqs)
#
# When SkipMsi is set (e.g. on Linux) the Bundle build is also skipped because
# its inputs (the MSI and BA DLL) are unavailable.

if (-not $SkipMsi) {
    if (-not (Test-Path $BundleProj)) {
        Write-Host "  [WARN] Bundle project not found: $BundleProj" -ForegroundColor Yellow
        Write-Host "         Skipping bundle (LFDashboard-Setup.exe) build." -ForegroundColor Yellow
    }
    else {
        Invoke-Cmd "Building Burn Bundle (LFDashboard-Setup.exe)" {

            # Ensure WixToolset.Bal.wixext is available (may already be installed from MSI step).
            $null = & wix extension add WixToolset.Bal.wixext/4.0.5 --global 2>&1

            & dotnet build $BundleProj `
                --configuration Release `
                --verbosity minimal `
                -p:ProductVersion=$Version

            $bundleExe = Join-Path $ArtifactsDir "LFDashboard-Setup.exe"
            if (Test-Path $bundleExe) {
                Write-OK "Bundle built: $bundleExe"
            }
            else {
                Write-Host "  [WARN] Bundle EXE not found at expected path: $bundleExe" -ForegroundColor Yellow
                # Try alternate: wixproj may output to obj\
                $altExe = Get-ChildItem $RepoRoot -Recurse -Filter "LFDashboard-Setup.exe" `
                              -ErrorAction SilentlyContinue |
                          Select-Object -First 1
                if ($altExe) {
                    Copy-Item $altExe.FullName -Destination $ArtifactsDir -Force
                    Write-OK "Bundle EXE located and copied from: $($altExe.FullName)"
                }
                else {
                    Write-Host "  [WARN] Bundle EXE not found anywhere. Check WiX build output." `
                        -ForegroundColor Yellow
                }
            }
        }
    }
}
else {
    Write-Host ""
    Write-Host "  -- Bundle build skipped (SkipMsi is set)." -ForegroundColor DarkGray
}

# ---------- Assemble final Release\ folder -----------------------------------
#
# Produces the deliverable that an admin receives:
#   Release\LFDashboard-Setup.exe   -- the one EXE to double-click
#   Release\README.txt              -- 12-line plain text guide
#
# The Release\ folder intentionally contains ONLY these two files.
# Everything else lives in artifacts\ for internal use.

Invoke-Cmd "Assembling Release folder" {

    $releaseDir = Join-Path $RepoRoot "Release"
    New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

    # Copy the bundle EXE
    $bundleExe = Join-Path $ArtifactsDir "LFDashboard-Setup.exe"
    if (Test-Path $bundleExe) {
        Copy-Item $bundleExe -Destination $releaseDir -Force
        Write-OK "Release\LFDashboard-Setup.exe ready."
    }
    elseif ($SkipMsi) {
        Write-Host "  [INFO] Bundle EXE skipped on non-Windows build; Release\ contains README only." `
            -ForegroundColor DarkGray
    }
    else {
        Write-Host "  [WARN] LFDashboard-Setup.exe not found; Release\ will be incomplete." `
            -ForegroundColor Yellow
    }

    # Copy README.txt
    $readmeSrc = Join-Path $RepoRoot "Release\README.txt"
    if (Test-Path $readmeSrc) {
        # Already in place (source-controlled in Release\)
        Write-OK "Release\README.txt present."
    }
    else {
        Write-Host "  [WARN] Release\README.txt not found." -ForegroundColor Yellow
    }

    Write-OK "Release folder: $releaseDir"
}

# ---------- Assemble final artifacts folder ----------------------------------

Invoke-Cmd "Assembling release artifacts" {

    # WebApp
    $webAppDst = Join-Path $ArtifactsDir "WebApp"
    if (-not (Test-Path $webAppDst)) {
        Copy-Item (Join-Path $StagingDir "WebApp") -Destination $webAppDst -Recurse -Force
    }

    # Extension
    $extDst = Join-Path $ArtifactsDir "Extension"
    if (-not (Test-Path $extDst)) {
        Copy-Item (Join-Path $StagingDir "Extension") -Destination $extDst -Recurse -Force
    }

    # ConfigTemplate
    $cfgDst = Join-Path $ArtifactsDir "ConfigTemplate"
    New-Item -ItemType Directory -Path $cfgDst -Force | Out-Null
    $cfgSrc = Join-Path $StagingDir "ConfigTemplate\*"
    if (Test-Path (Join-Path $StagingDir "ConfigTemplate")) {
        Copy-Item $cfgSrc -Destination $cfgDst -Force
    }

    # Tools
    $toolsSrc = Join-Path $ArtifactsDir "Tools"
    if ((Test-Path $toolsSrc) -and (Get-ChildItem $toolsSrc).Count -eq 0) {
        # Already staged above
    }

    # Docs
    $docsDst = Join-Path $ArtifactsDir "docs"
    New-Item -ItemType Directory -Path $docsDst -Force | Out-Null
    foreach ($doc in @("InstallationGuide.md", "UpgradeGuide.md", "ReleaseNotes.md")) {
        $docSrc = Join-Path $RepoRoot "docs\$doc"
        if (Test-Path $docSrc) {
            Copy-Item $docSrc -Destination $docsDst -Force
        }
    }

    Write-OK "Release artifacts assembled in: $ArtifactsDir"
}

# ---------- Final summary ----------------------------------------------------

Write-Host ""
Write-Host "  ==========================================================" -ForegroundColor DarkGray
Write-Host "  Build complete -- Dashboard $Version" -ForegroundColor Green
Write-Host ""
Write-Host "  Artifacts:" -ForegroundColor White
Get-ChildItem $ArtifactsDir -Depth 0 | ForEach-Object {
    Write-Host "    $($_.Name)" -ForegroundColor Gray
}
Write-Host ""

if ($SkipMsi) {
    Write-Host "  MSI and Bundle were skipped. To build on Windows:" -ForegroundColor Yellow
    Write-Host "    Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force" -ForegroundColor Gray
    Write-Host "    .\build\publish.ps1 -Version $Version" -ForegroundColor Gray
    Write-Host ""
}
else {
    $bundleExe = Join-Path $RepoRoot "Release\LFDashboard-Setup.exe"
    if (Test-Path $bundleExe) {
        Write-Host "  Deliverable: Release\LFDashboard-Setup.exe" -ForegroundColor Green
        Write-Host "               Release\README.txt" -ForegroundColor Green
        Write-Host ""
        Write-Host "  To distribute: give the admin the Release\ folder." -ForegroundColor White
        Write-Host "  They double-click LFDashboard-Setup.exe and follow the wizard." -ForegroundColor Gray
        Write-Host ""
    }
    else {
        $msi = Get-ChildItem $ArtifactsDir -Filter "*.msi" -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($msi) {
            Write-Host "  MSI installer: $($msi.FullName)" -ForegroundColor Green
            Write-Host "  (Bundle EXE not found; Bundle build may have failed.)" -ForegroundColor Yellow
            Write-Host ""
        }
    }
}
