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

$IsWindowsOS = $IsWindows -or ($PSVersionTable.PSVersion.Major -le 5)

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

$WebProjPath   = Join-Path $RepoRoot "src\LFPortal.Web\LFPortal.Web.csproj"
$ExtProjPath   = Join-Path $RepoRoot "src\Dashboard.DesktopExtension\Dashboard.DesktopExtension.csproj"
$InstallerProj = Join-Path $RepoRoot "installer\Dashboard.Installer\Dashboard.Installer.wixproj"
$DbPropsPath   = Join-Path $RepoRoot "Directory.Build.props"

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
}
else {
    Write-Host ""
    Write-Host "  -- Desktop Extension build skipped." -ForegroundColor DarkGray
    # Ensure the staging directory exists so the MSI build has a target.
    New-Item -ItemType Directory -Path (Join-Path $StagingDir "Extension") -Force | Out-Null
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
                "WixToolset.Util.wixext/4.0.5"
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
    Write-Host "  MSI was skipped. To build the MSI on Windows:" -ForegroundColor Yellow
    Write-Host "    Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force" -ForegroundColor Gray
    Write-Host "    .\build\publish.ps1 -Version $Version" -ForegroundColor Gray
    Write-Host ""
}
else {
    $msi = Get-ChildItem $ArtifactsDir -Filter "*.msi" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($msi) {
        Write-Host "  MSI installer: $($msi.FullName)" -ForegroundColor Green
        Write-Host ""
    }
}
