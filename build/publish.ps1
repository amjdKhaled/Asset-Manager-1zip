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
    Can be run from any directory; all paths are derived from $PSScriptRoot.
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
    # No return value: callers must not depend on pipeline output from this function.
    # Use -IgnoreFailure when you need to proceed after a non-zero exit.
}

# New-HarvestWxs: generates a WiX 4 fragment file containing a ComponentGroup
# for every file found (recursively) in SourceDir.
#
# This replaces the HarvestDirectory MSBuild item, which only works when the
# WiX SDK resolver is registered by a globally installed WiX tool.  Because
# this project uses a repository-local tool (dotnet tool restore) to avoid
# depending on whatever WiX version the developer has globally, we use
# 'dotnet tool run wix -- build' instead of 'dotnet build <wixproj>'.  That
# CLI path does not support HarvestDirectory, so we pre-generate the harvest
# file here.
function New-HarvestWxs {
    param(
        [string]$SourceDir,
        [string]$ComponentGroupName,
        [string]$DirectoryRefId,
        [string]$OutputWxs
    )

    $emptyXml = @"
<?xml version="1.0" encoding="utf-8"?>
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Fragment>
    <ComponentGroup Id="$ComponentGroupName" />
  </Fragment>
</Wix>
"@

    if (-not (Test-Path $SourceDir)) {
        $emptyXml | Set-Content -Path $OutputWxs -Encoding UTF8
        return
    }

    $SourceDir = (Resolve-Path $SourceDir).Path.TrimEnd([char[]]@('\', '/'))
    $allFiles  = @(Get-ChildItem -Path $SourceDir -Recurse -File | Sort-Object FullName)

    if ($allFiles.Count -eq 0) {
        $emptyXml | Set-Content -Path $OutputWxs -Encoding UTF8
        return
    }

    # Map EVERY subdirectory (not just file-parent directories) to a WiX-safe ID.
    # Root directory uses the caller-supplied DirectoryRefId.
    #
    # IMPORTANT: we must enumerate ALL directories with Get-ChildItem -Directory,
    # not just those derived from $allFiles.DirectoryName.  A directory that only
    # contains sub-directories (e.g. wwwroot/) would be missing from $dirId, which
    # causes a HashTable KeyError under Set-StrictMode when building $childMap.
    $dirId = @{}
    $dirId[$SourceDir] = $DirectoryRefId
    $idx = 0
    @(Get-ChildItem -Path $SourceDir -Recurse -Directory | Sort-Object FullName) |
        ForEach-Object {
            $dPath = $_.FullName.TrimEnd([char[]]@('\', '/'))
            if (-not $dirId.ContainsKey($dPath)) {
                $idx++
                $rel = $dPath.Substring($SourceDir.Length + 1) -replace '[^A-Za-z0-9]', '_'
                if ($rel -match '^[0-9]') { $rel = "d_$rel" }
                if ($rel.Length -gt 50)   { $rel = $rel.Substring(0, 50) }
                $dirId[$dPath] = ("dir_{0}_{1}_{2}" -f $ComponentGroupName, $idx, $rel)
            }
        }

    # Build parent-to-immediate-children map for all directories.
    $childMap = @{}
    foreach ($d in ($dirId.Keys | Sort-Object Length)) {
        if ($d -ne $SourceDir) {
            $parent = (Split-Path $d -Parent).TrimEnd([char[]]@('\', '/'))
            if (-not $childMap.ContainsKey($parent)) {
                $childMap[$parent] = [System.Collections.Generic.List[string]]::new()
            }
            $childMap[$parent].Add($d)
        }
    }

    # XML attribute-value escaper: replaces the five characters that are illegal
    # inside XML attribute values.  File paths on Windows rarely contain these,
    # but ampersands in share paths (\\server\it&ops\) or angle-brackets would
    # otherwise produce malformed XML that WiX rejects at compile time.
    function EscapeXmlAttr([string]$val) {
        $val.Replace('&','&amp;').Replace('<','&lt;').Replace('>','&gt;').Replace('"','&quot;').Replace("'","&apos;")
    }

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('<?xml version="1.0" encoding="utf-8"?>')
    $lines.Add('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
    $lines.Add('  <Fragment>')

    # One <DirectoryRef> block per parent directory that has sub-directories.
    # NOTE: $pid is a read-only PowerShell automatic variable (process ID).
    #       Use $parentDirId / $childDirId instead.
    foreach ($parent in ($childMap.Keys | Sort-Object Length)) {
        $parentDirId = $dirId[$parent]
        $lines.Add("    <DirectoryRef Id=`"$parentDirId`">")
        foreach ($child in ($childMap[$parent] | Sort-Object)) {
            $childDirId  = $dirId[$child]
            $childDirName = EscapeXmlAttr (Split-Path $child -Leaf)
            $lines.Add("      <Directory Id=`"$childDirId`" Name=`"$childDirName`" />")
        }
        $lines.Add("    </DirectoryRef>")
    }

    # One <Component> per file inside <ComponentGroup>.
    # Guid="*" is safe in WiX 4: the toolset generates a deterministic GUID from
    # the component's install directory + key-path, so upgrade/repair identity is
    # stable across builds as long as the install path does not change.
    $lines.Add("    <ComponentGroup Id=`"$ComponentGroupName`">")
    $compSeq = 0
    foreach ($file in $allFiles) {
        $compSeq++
        $fileDirId  = $dirId[$file.DirectoryName]
        $fileSrcEsc = EscapeXmlAttr $file.FullName
        $lines.Add("      <Component Id=`"Comp_${ComponentGroupName}_${compSeq}`" Directory=`"$fileDirId`" Guid=`"*`">")
        $lines.Add("        <File Source=`"$fileSrcEsc`" />")
        $lines.Add("      </Component>")
    }
    $lines.Add("    </ComponentGroup>")
    $lines.Add('  </Fragment>')
    $lines.Add('</Wix>')

    ($lines -join "`r`n") | Set-Content -Path $OutputWxs -Encoding UTF8
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
# Single source of truth for the local WiX tool manifest.  Used in Step 8 to
# pass --tool-manifest explicitly so the restore works regardless of CWD.
$ToolManifest    = Join-Path $RepoRoot ".config\dotnet-tools.json"

# =============================================================================
# PREFLIGHT: VERIFY ALL REQUIRED SOURCE/BUILD FILES
# =============================================================================
# Fail early with a complete list of missing files rather than discovering
# individual absences deep into the build.  All paths derive from $RepoRoot
# so this check is independent of the caller's working directory.

$preflightAlways = @(
    $WebProjPath,
    $DbPropsPath,
    (Join-Path $RepoRoot "LFPortal.sln")
)

$preflightWindows = @(
    # WiX tool manifest (must be committed -- see .config/dotnet-tools.json)
    $ToolManifest,
    # Installer source files
    (Join-Path $RepoRoot "installer\Dashboard.Installer\Product.wxs"),
    (Join-Path $RepoRoot "installer\Dashboard.Installer\WebApplication.wxs"),
    (Join-Path $RepoRoot "installer\Dashboard.Installer\DesktopExtension.wxs"),
    (Join-Path $RepoRoot "installer\Dashboard.Installer\Configuration.wxs"),
    (Join-Path $RepoRoot "installer\Dashboard.Installer\Shortcuts.wxs"),
    (Join-Path $RepoRoot "installer\Dashboard.Bundle\Bundle.wxs"),
    # Desktop Extension project
    $ExtProjPath,
    # Managed Bootstrapper Application project
    $BAProjPath,
    # SetupHelper project
    $SetupHelperProj,
    # Config templates embedded in the MSI
    (Join-Path $RepoRoot "config\templates"),
    # Web Client button JavaScript
    (Join-Path $RepoRoot "src\LFPortal.Web\wwwroot\js\lf-webclient-button.js"),
    # README shipped in Release\
    (Join-Path $RepoRoot "Release\README.txt")
)

$missingFiles = [System.Collections.Generic.List[string]]::new()

foreach ($f in $preflightAlways) {
    if (-not (Test-Path $f)) { $missingFiles.Add($f) }
}

if ($IsWindowsOS -and (-not $SkipMsi) -and (-not $SkipExtension)) {
    foreach ($f in $preflightWindows) {
        if (-not (Test-Path $f)) { $missingFiles.Add($f) }
    }
}

if ($missingFiles.Count -gt 0) {
    Write-Host ""
    Write-Host "  ============================================================" -ForegroundColor Red
    Write-Host "  [PREFLIGHT FAILED] Required files are missing from the repository:" `
        -ForegroundColor Red
    foreach ($mf in $missingFiles) {
        Write-Host ("    MISSING: {0}" -f $mf) -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "  Sync/clone the complete repository before building." -ForegroundColor Red
    Write-Host "  ============================================================" -ForegroundColor Red
    exit 1
}

Write-OK "Preflight: all required source files present."

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

# Post-publish guard: appsettings.json must be present in the staged WebApp
# folder.  WriteConfigAction patches the "Urls" key in this file at install
# time so the ASP.NET Core app binds the wizard-selected port.  If the file
# is missing the installer will abort (WriteConfigAction now returns exit 1),
# but catching it here at build time gives a clearer message and fails fast
# before the MSI is assembled.
$stagedAppSettings = Join-Path $webAppOut "appsettings.json"
if (-not (Test-Path $stagedAppSettings)) {
    Write-Host ""
    Write-Host "  ============================================================" -ForegroundColor Red
    Write-Host "  [PREFLIGHT FAILED] appsettings.json is missing from the staged WebApp folder:" `
        -ForegroundColor Red
    Write-Host ("    MISSING: {0}" -f $stagedAppSettings) -ForegroundColor Red
    Write-Host "" -ForegroundColor Red
    Write-Host "  The MSI cannot be built without this file.  WriteConfigAction" -ForegroundColor Red
    Write-Host "  patches the Urls key in appsettings.json at install time so the" -ForegroundColor Red
    Write-Host "  web app binds the wizard-selected port.  Without it the app would" -ForegroundColor Red
    Write-Host "  silently start on Kestrel's built-in default port." -ForegroundColor Red
    Write-Host "" -ForegroundColor Red
    Write-Host "  Possible causes:" -ForegroundColor Red
    Write-Host "    - appsettings.json was excluded from the publish output" -ForegroundColor Red
    Write-Host "      (check CopyToPublishDirectory in LFPortal.Web.csproj)" -ForegroundColor Red
    Write-Host "    - The dotnet publish step failed silently (check output above)" -ForegroundColor Red
    Write-Host "  ============================================================" -ForegroundColor Red
    exit 1
}
Write-OK "appsettings.json confirmed present in staged WebApp folder."

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
        Write-Host ""
        Write-Host "  ============================================================" -ForegroundColor Red
        Write-Host "  [PREFLIGHT FAILED] Desktop Extension build output folder is missing:" `
            -ForegroundColor Red
        Write-Host ("    MISSING: {0}" -f $extOut) -ForegroundColor Red
        Write-Host "" -ForegroundColor Red
        Write-Host "  The MSI cannot be built without the extension binaries." -ForegroundColor Red
        Write-Host "  Possible causes:" -ForegroundColor Red
        Write-Host "    - The Desktop Extension build failed silently (check output above)" -ForegroundColor Red
        Write-Host "    - The project target framework is no longer net48" -ForegroundColor Red
        Write-Host "    - The output path changed from: $extOut" -ForegroundColor Red
        Write-Host "  ============================================================" -ForegroundColor Red
        exit 1
    }

    # Post-staging guard: Dashboard.DesktopExtension.exe must be present in the
    # Extension staging folder before the MSI build (Step 8) harvests it.
    # OutputType=WinExe → the primary artifact is always an EXE, never a DLL.
    # If it is absent the WiX harvester silently ships an empty Extension
    # component; catch it here with a clear message instead.
    $extExeStaged = Join-Path $StagingDir "Extension\Dashboard.DesktopExtension.exe"
    if (-not (Test-Path $extExeStaged)) {
        Write-Host ""
        Write-Host "  ============================================================" -ForegroundColor Red
        Write-Host "  [PREFLIGHT FAILED] Required Extension EXE is missing after staging:" `
            -ForegroundColor Red
        Write-Host ("    MISSING: {0}" -f $extExeStaged) -ForegroundColor Red
        Write-Host "" -ForegroundColor Red
        Write-Host "  Dashboard.DesktopExtension.exe must be produced by the Extension" -ForegroundColor Red
        Write-Host "  build and copied to artifacts\staging\Extension\ before the" -ForegroundColor Red
        Write-Host "  MSI (Step 8) can link.  Possible causes:" -ForegroundColor Red
        Write-Host "    - The Desktop Extension build failed silently (check output above)" -ForegroundColor Red
        Write-Host "    - The AssemblyName changed (check Dashboard.DesktopExtension.csproj)" -ForegroundColor Red
        Write-Host "    - The Laserfiche SDK DLLs were missing, causing a partial build" -ForegroundColor Red
        Write-Host "    - The project target framework is no longer net48" -ForegroundColor Red
        Write-Host "  ============================================================" -ForegroundColor Red
        exit 1
    }
    $extExeBytes = (Get-Item $extExeStaged).Length
    if ($extExeBytes -eq 0) {
        Write-Host ""
        Write-Host "  ============================================================" -ForegroundColor Red
        Write-Host "  [PREFLIGHT FAILED] Dashboard.DesktopExtension.exe is zero bytes:" `
            -ForegroundColor Red
        Write-Host ("    PATH: {0}" -f $extExeStaged) -ForegroundColor Red
        Write-Host "  The Extension build produced an empty file.  Check the build output above." -ForegroundColor Red
        Write-Host "  ============================================================" -ForegroundColor Red
        exit 1
    }
    Write-OK ("Dashboard.DesktopExtension.exe confirmed in Extension staging ({0:N0} bytes)." -f $extExeBytes)
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

    # Post-staging guard: Dashboard.SetupHelper.exe must be present in the
    # Extension staging folder before the MSI build (Step 8) references it as
    # a source file.  If it is absent the WiX linker fails with an opaque
    # "file not found" error; catch it here with a clear message instead.
    $setupHelperStaged = Join-Path $StagingDir "Extension\Dashboard.SetupHelper.exe"
    if (-not (Test-Path $setupHelperStaged)) {
        Write-Host ""
        Write-Host "  ============================================================" -ForegroundColor Red
        Write-Host "  [PREFLIGHT FAILED] Required SetupHelper file is missing:" -ForegroundColor Red
        Write-Host ("    MISSING: {0}" -f $setupHelperStaged) -ForegroundColor Red
        Write-Host "" -ForegroundColor Red
        Write-Host "  Dashboard.SetupHelper.exe must be produced by the SetupHelper" -ForegroundColor Red
        Write-Host "  build and copied to artifacts\staging\Extension\ before the" -ForegroundColor Red
        Write-Host "  MSI (Step 8) can link.  Possible causes:" -ForegroundColor Red
        Write-Host "    - The SetupHelper build failed silently (check output above)" -ForegroundColor Red
        Write-Host "    - The SetupHelper output path has changed from: $helperOut" -ForegroundColor Red
        Write-Host "    - The project target framework is no longer net48" -ForegroundColor Red
        Write-Host "  ============================================================" -ForegroundColor Red
        exit 1
    }
    Write-OK "Dashboard.SetupHelper.exe confirmed present in Extension staging folder."
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

    # Post-staging guard: WixToolset.Mba.Core.dll must be present in the BA
    # staging folder before the Bundle build (Step 9) references it via
    # $(var.MbaCoreAssembly).  If it is absent the Bundle linker fails with an
    # opaque "file not found" error; catch it here with a clear message instead.
    # ---- Guard 1: WixToolset.Mba.Core.dll ----
    $mbaCoreStaged = Join-Path $baStaging "WixToolset.Mba.Core.dll"
    if (-not (Test-Path $mbaCoreStaged)) {
        Write-Host ""
        Write-Host "  ============================================================" -ForegroundColor Red
        Write-Host "  [PREFLIGHT FAILED] Required BA staging file is missing:" -ForegroundColor Red
        Write-Host ("    MISSING: {0}" -f $mbaCoreStaged) -ForegroundColor Red
        Write-Host "" -ForegroundColor Red
        Write-Host "  WixToolset.Mba.Core.dll must be produced by the Dashboard.BA" -ForegroundColor Red
        Write-Host "  build and copied to artifacts\staging\BA\ before the Bundle" -ForegroundColor Red
        Write-Host "  (Step 9) can link.  Possible causes:" -ForegroundColor Red
        Write-Host "    - NuGet restore did not produce the package" -ForegroundColor Red
        Write-Host "    - The BA build failed silently (check output above)" -ForegroundColor Red
        Write-Host "    - The BA output path has changed from: $baOut" -ForegroundColor Red
        Write-Host "  ============================================================" -ForegroundColor Red
        exit 1
    }
    if ((Get-Item $mbaCoreStaged).Length -eq 0) {
        Write-Host "  [PREFLIGHT FAILED] WixToolset.Mba.Core.dll is zero bytes: $mbaCoreStaged" -ForegroundColor Red
        exit 1
    }
    Write-OK "WixToolset.Mba.Core.dll confirmed present in BA staging folder."

    # ---- Guard 2: Dashboard.BA.dll ----
    # Must be present before the Bundle build (Step 9) embeds it as the managed
    # bootstrapper payload.  Without it the Bundle ships with no UI and silently
    # fails at runtime.
    $baDllStaged = Join-Path $baStaging "Dashboard.BA.dll"
    if (-not (Test-Path $baDllStaged)) {
        Write-Host ""
        Write-Host "  ============================================================" -ForegroundColor Red
        Write-Host "  [PREFLIGHT FAILED] Required BA bootstrapper DLL is missing after staging:" `
            -ForegroundColor Red
        Write-Host ("    MISSING: {0}" -f $baDllStaged) -ForegroundColor Red
        Write-Host "" -ForegroundColor Red
        Write-Host "  Dashboard.BA.dll must be produced by the Dashboard.BA build and" -ForegroundColor Red
        Write-Host "  copied to artifacts\staging\BA\ before the Bundle (Step 9) can" -ForegroundColor Red
        Write-Host "  embed it as the managed bootstrapper payload.  Without it the" -ForegroundColor Red
        Write-Host "  Bundle ships with no UI and silently fails at runtime." -ForegroundColor Red
        Write-Host "  Possible causes:" -ForegroundColor Red
        Write-Host "    - The Dashboard.BA build failed silently (check output above)" -ForegroundColor Red
        Write-Host "    - The output DLL name changed (check Dashboard.BA.csproj <AssemblyName>)" -ForegroundColor Red
        Write-Host "    - The BA output path has changed from: $baOut" -ForegroundColor Red
        Write-Host "    - The project target framework is no longer net48" -ForegroundColor Red
        Write-Host "  ============================================================" -ForegroundColor Red
        exit 1
    }
    if ((Get-Item $baDllStaged).Length -eq 0) {
        Write-Host "  [PREFLIGHT FAILED] Dashboard.BA.dll is zero bytes: $baDllStaged" -ForegroundColor Red
        exit 1
    }
    Write-OK "Dashboard.BA.dll confirmed present in BA staging folder."

    # ---- Guard 3: WixToolset.Mba.Host.config ----
    # mbahost.dll (the WiX 4 native managed-BA host) reads this file at bundle
    # startup to know which CLR version to activate before loading Dashboard.BA.dll.
    #
    # WiX 4.0.5 RENAMED this file from BootstrapperCore.config (WiX 3).
    # Burn 4.0.5 looks for it at exactly:  .ba\WixToolset.Mba.Host.config
    # Without it, Burn emits:
    #   Error 0x8007006e: Failed to load bootstrapper config file from path:
    #   ...\.ba\WixToolset.Mba.Host.config
    # and falls back to the native prereq UI -- even when .NET 4.8 is present.
    $mbaHostConfigStaged = Join-Path $baStaging "WixToolset.Mba.Host.config"
    if (-not (Test-Path $mbaHostConfigStaged)) {
        Write-Host ""
        Write-Host "  ============================================================" -ForegroundColor Red
        Write-Host "  [PREFLIGHT FAILED] WixToolset.Mba.Host.config is missing from BA staging:" `
            -ForegroundColor Red
        Write-Host ("    MISSING: {0}" -f $mbaHostConfigStaged) -ForegroundColor Red
        Write-Host "" -ForegroundColor Red
        Write-Host "  WixToolset.Mba.Host.config must be in Dashboard.BA\bin\Release\net48\" -ForegroundColor Red
        Write-Host "  and staged to artifacts\staging\BA\ before the Bundle build." -ForegroundColor Red
        Write-Host "  Possible causes:" -ForegroundColor Red
        Write-Host "    - installer\Dashboard.BA\WixToolset.Mba.Host.config was deleted" -ForegroundColor Red
        Write-Host "    - The <None CopyToOutputDirectory=Always> entry was removed from" -ForegroundColor Red
        Write-Host "      Dashboard.BA.csproj" -ForegroundColor Red
        Write-Host "    - The file was renamed (must stay WixToolset.Mba.Host.config)" -ForegroundColor Red
        Write-Host "  ============================================================" -ForegroundColor Red
        exit 1
    }
    if ((Get-Item $mbaHostConfigStaged).Length -eq 0) {
        Write-Host "  [PREFLIGHT FAILED] WixToolset.Mba.Host.config is zero bytes: $mbaHostConfigStaged" -ForegroundColor Red
        exit 1
    }
    Write-OK "WixToolset.Mba.Host.config confirmed present in BA staging folder."

    # ---- Guard 4: mbanative.dll (win-x86) ----
    # WixToolset.Mba.Core.dll P/Invokes into mbanative.dll for every Burn engine
    # API call.  Without it the first managed API call after BA creation throws a
    # DllNotFoundException and Burn reports:
    #   Error 0x80070490: Failed to create the managed bootstrapper application.
    # Burn 4.0.5 is x86; only the win-x86 variant (140 KB) can be loaded.
    # Source: WixToolset.Mba.Core 4.0.5 NuGet, runtimes\win-x86\native\mbanative.dll,
    #         copied by the CopyMbaNativeDll MSBuild Target in Dashboard.BA.csproj.
    $mbaNativeStaged = Join-Path $baStaging "mbanative.dll"
    if (-not (Test-Path $mbaNativeStaged)) {
        Write-Host ""
        Write-Host "  ============================================================" -ForegroundColor Red
        Write-Host "  [PREFLIGHT FAILED] mbanative.dll is missing from BA staging:" `
            -ForegroundColor Red
        Write-Host ("    MISSING: {0}" -f $mbaNativeStaged) -ForegroundColor Red
        Write-Host "" -ForegroundColor Red
        Write-Host "  mbanative.dll must be copied to Dashboard.BA\bin\Release\net48\" -ForegroundColor Red
        Write-Host "  by the CopyMbaNativeDll MSBuild Target in Dashboard.BA.csproj." -ForegroundColor Red
        Write-Host "  Possible causes:" -ForegroundColor Red
        Write-Host "    - The CopyMbaNativeDll Target was removed from Dashboard.BA.csproj" -ForegroundColor Red
        Write-Host "    - The WixToolset.Mba.Core NuGet package is not restored" -ForegroundColor Red
        Write-Host "    - The package version in the Target path does not match the" -ForegroundColor Red
        Write-Host "      <PackageReference> (both must be 4.0.5)" -ForegroundColor Red
        Write-Host "    - NuGet global packages folder is not at: `$(NuGetPackageRoot)" -ForegroundColor Red
        Write-Host "      Check: dotnet nuget locals global-packages --list" -ForegroundColor Red
        Write-Host "  ============================================================" -ForegroundColor Red
        exit 1
    }
    if ((Get-Item $mbaNativeStaged).Length -eq 0) {
        Write-Host "  [PREFLIGHT FAILED] mbanative.dll is zero bytes: $mbaNativeStaged" -ForegroundColor Red
        exit 1
    }
    Write-OK ("mbanative.dll (win-x86) confirmed present in BA staging folder ({0:N0} bytes)." -f (Get-Item $mbaNativeStaged).Length)

    # ---- Guard 5: BootstrapperApplicationFactory attribute in Dashboard.BA.dll ----
    # WixToolset.Mba.Host.dll reads assemblyName from WixToolset.Mba.Host.config,
    # loads that DLL, and calls GetCustomAttributes() to find the factory type.
    # If [assembly: BootstrapperApplicationFactory(typeof(BAFactory))] is absent
    # from the compiled DLL, the host cannot locate the factory and returns
    # E_NOTFOUND, which surfaces as:
    #   Error 0x80070490: Failed to create the managed bootstrapper application.
    #
    # Binary scan (Latin-1) avoids CLR version issues in the PowerShell host while
    # still confirming the metadata string was emitted by the compiler.  The literal
    # "BootstrapperApplicationFactoryAttribute" MUST appear in the .NET metadata
    # section of any DLL that carries the attribute.
    $baDllBytes = [System.IO.File]::ReadAllBytes($baDllStaged)
    $baDllText  = [System.Text.Encoding]::GetEncoding(28591).GetString($baDllBytes)
    if (-not $baDllText.Contains("BootstrapperApplicationFactoryAttribute")) {
        Write-Host ""
        Write-Host "  ============================================================" -ForegroundColor Red
        Write-Host "  [PREFLIGHT FAILED] Dashboard.BA.dll is missing the" -ForegroundColor Red
        Write-Host "  [assembly: BootstrapperApplicationFactory(...)] attribute." -ForegroundColor Red
        Write-Host ("    CHECKED: {0}" -f $baDllStaged) -ForegroundColor Red
        Write-Host "" -ForegroundColor Red
        Write-Host "  WixToolset.Mba.Host.dll uses this attribute to locate and" -ForegroundColor Red
        Write-Host "  create the BA factory.  Without it Burn reports:" -ForegroundColor Red
        Write-Host "    Error 0x80070490: Failed to create the managed bootstrapper" -ForegroundColor Red
        Write-Host "    application." -ForegroundColor Red
        Write-Host "  Possible causes:" -ForegroundColor Red
        Write-Host "    - The [assembly: BootstrapperApplicationFactory(typeof(BAFactory))]" -ForegroundColor Red
        Write-Host "      line was removed from installer\Dashboard.BA\BAFactory.cs" -ForegroundColor Red
        Write-Host "    - BAFactory.cs was excluded from the project compile" -ForegroundColor Red
        Write-Host "  ============================================================" -ForegroundColor Red
        exit 1
    }
    Write-OK "BootstrapperApplicationFactory attribute confirmed in Dashboard.BA.dll."

    # ---- Guard 6: Dashboard.BA.dll PE architecture must be x86 (0x014C) ----
    # Burn 4.0.5 is an x86 process.  Loading an AnyCPU or x64 managed BA into
    # an x86 Burn host produces:
    #   Error 0x80131902: Failed to create the managed bootstrapper application.
    # The PE Machine field at bytes [peOffset+4 .. peOffset+5] must equal 0x014C
    # (IMAGE_FILE_MACHINE_I386).  Dashboard.BA.csproj sets PlatformTarget=x86 and
    # Prefer32Bit=true to guarantee this; this guard catches any accidental revert.
    $baPeBytes  = [System.IO.File]::ReadAllBytes($baDllStaged)
    $baPeOffset = [System.BitConverter]::ToInt32($baPeBytes, 0x3C)
    $baMachine  = [System.BitConverter]::ToUInt16($baPeBytes, $baPeOffset + 4)
    if ($baMachine -ne 0x014C) {
        Write-Host ""
        Write-Host "  ============================================================" -ForegroundColor Red
        Write-Host "  [PREFLIGHT FAILED] Dashboard.BA.dll is not x86." -ForegroundColor Red
        Write-Host ("    PE Machine: 0x{0:X4} (expected 0x014C = x86)" -f $baMachine) -ForegroundColor Red
        Write-Host ("    FILE: {0}" -f $baDllStaged) -ForegroundColor Red
        Write-Host "" -ForegroundColor Red
        Write-Host "  WiX Burn 4.0.5 is an x86 process and requires the BA" -ForegroundColor Red
        Write-Host "  architecture to match.  AnyCPU or x64 DLLs cannot be" -ForegroundColor Red
        Write-Host "  loaded; Burn reports:" -ForegroundColor Red
        Write-Host "    Error 0x80131902: Failed to create the managed" -ForegroundColor Red
        Write-Host "    bootstrapper application." -ForegroundColor Red
        Write-Host "  Fix: set <PlatformTarget>x86</PlatformTarget> and" -ForegroundColor Red
        Write-Host "  <Prefer32Bit>true</Prefer32Bit> in Dashboard.BA.csproj." -ForegroundColor Red
        Write-Host "  ============================================================" -ForegroundColor Red
        exit 1
    }
    Write-OK ("Dashboard.BA.dll architecture confirmed: x86 (0x014C).")
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
    Write-Stage $Step $TotalStages ("Building MSI installer (WiX {0} via local tool)" -f $WixPinnedVersion)

    # -----------------------------------------------------------------------
    # ARCHITECTURE: repository-local WiX toolchain
    #
    # WHY NOT GLOBAL TOOL:
    #   'dotnet tool update --global wix --version 4.0.5' cannot downgrade an
    #   already-installed newer version (e.g. 7.0.0 -> 4.0.5).  A developer may
    #   need WiX 7 for another project; we must not modify their global install.
    #
    # WHY NOT 'dotnet build <wixproj>':
    #   Sdk="WixToolset.Wix/4.0.5" requires the WiX 4 MSBuild SDK resolver,
    #   which is only registered when WiX 4 is installed as a GLOBAL tool.
    #   A local tool restore does not register that resolver, so dotnet build
    #   fails with "Could not resolve SDK 'WixToolset.Wix'."
    #
    # HOW WE BUILD:
    #   1. Verify $ToolManifest exists (absolute path from $RepoRoot)
    #   2. dotnet tool restore --tool-manifest $ToolManifest
    #   3. Push-Location $RepoRoot so 'dotnet tool run wix' discovers the manifest
    #   4. New-HarvestWxs replaces HarvestDirectory (a WiX MSBuild-only feature)
    #   5. dotnet tool run wix -- build  (bypasses MSBuild SDK resolver entirely)
    #
    # GLOBAL WiX (any version): completely ignored; no modification made to it.
    # WORKING DIRECTORY: all WiX invocations run inside Push-Location $RepoRoot;
    #   the script is CWD-independent regardless of how it was launched.
    # -----------------------------------------------------------------------

    # Guard: manifest must exist at the absolute path derived from $PSScriptRoot.
    if (-not (Test-Path $ToolManifest)) {
        Write-Host "" 
        Write-Host "  [FAILED] Tool manifest not found." -ForegroundColor Red
        Write-Host "           Expected : $ToolManifest" -ForegroundColor Red
        Write-Host "           The file .config\dotnet-tools.json must be committed" `
            -ForegroundColor Red
        Write-Host "           to the repository root." -ForegroundColor Red
        exit 1
    }
    Write-OK "Tool manifest: $ToolManifest"

    # 8a -- Restore the repository-local WiX tool using the explicit manifest path.
    #       'dotnet tool restore' exits 0 even when no manifest is found (it only
    #       warns).  Using --tool-manifest with an absolute path guarantees the
    #       correct file is used and errors are fatal.
    Write-Host "  Restoring local tools ..." -ForegroundColor Gray
    Invoke-NativeCommand -Stage "dotnet tool restore" -FilePath "dotnet" `
        -Arguments @("tool", "restore", "--tool-manifest", $ToolManifest)
    Write-OK "Local tools restored."

    # All subsequent WiX invocations run from $RepoRoot so that
    # 'dotnet tool run wix' discovers .config\dotnet-tools.json by its normal
    # manifest-search algorithm (walks up from CWD).  Push-Location / Pop-Location
    # in a try/finally block guarantees the original directory is restored even
    # if a step fails.
    Push-Location $RepoRoot
    try {
        # 8b -- Verify the local WiX version matches the manifest pin.
        #       Read $LASTEXITCODE immediately after the native call.
        $localWixOut = @(& dotnet tool run wix -- --version 2>&1)
        $localWixEc  = $LASTEXITCODE
        if ($localWixEc -ne 0) {
            Write-Host "  [FAILED] 'dotnet tool run wix -- --version' failed (exit $localWixEc)." `
                -ForegroundColor Red
            Write-Host "           Check that $ToolManifest pins wix to $WixPinnedVersion" `
                -ForegroundColor Red
            Write-Host "           and that 'dotnet tool restore' completed without errors." `
                -ForegroundColor Red
            exit 1
        }
        $localVer = ($localWixOut |
                     Where-Object { $_ -match '^\d+\.\d+' -or $_ -match 'version\s+\d' } |
                     Select-Object -First 1)
        if ($localVer) { $localVer = $localVer.Trim() } else { $localVer = ($localWixOut -join '').Trim() }
        if ($localVer -notlike "*$WixPinnedVersion*") {
            Write-Host ("  [FAILED] Local WiX reported '{0}'; expected '{1}'." `
                -f $localVer, $WixPinnedVersion) -ForegroundColor Red
            Write-Host "           Update the 'version' field in .config\dotnet-tools.json." `
                -ForegroundColor Red
            exit 1
        }
        Write-OK ("Local WiX: {0}  (global WiX, if any, is ignored)" -f $localVer)

        # 8c -- Ensure WiX extensions are in the user-scoped local cache.
        # 'extension add' exits non-zero if the extension is already cached; that
        # is expected and safe -- -IgnoreFailure suppresses the exit.
        # No --global: extensions are stored per-version in %LOCALAPPDATA%\WixToolset.
        $wixExts = @(
            "WixToolset.UI.wixext/$WixPinnedVersion",
            "WixToolset.Iis.wixext/4.0.3",
            "WixToolset.Util.wixext/$WixPinnedVersion",
            "WixToolset.Bal.wixext/$WixPinnedVersion"
        )
        foreach ($ext in $wixExts) {
            Invoke-NativeCommand -Stage ("wix extension add {0}" -f $ext) `
                -FilePath "dotnet" `
                -Arguments @("tool", "run", "wix", "--", "extension", "add", $ext) `
                -IgnoreFailure | Out-Null
        }
        Write-OK "WiX extensions ready."

        # 8d -- Pre-harvest staging directories into .wxs ComponentGroup files.
        #       HarvestDirectory is a WiX MSBuild-only feature; 'wix build' CLI
        #       does not support it, so we generate equivalent fragments here.
        $harvestDir       = Join-Path $StagingDir "Harvest"
        $null = New-Item -ItemType Directory -Path $harvestDir -Force
        $webAppHarvestWxs = Join-Path $harvestDir "WebAppComponents.wxs"
        $extHarvestWxs    = Join-Path $harvestDir "ExtensionComponents.wxs"

        $webAppStagingDir = Join-Path $StagingDir "WebApp"
        New-HarvestWxs `
            -SourceDir          $webAppStagingDir `
            -ComponentGroupName "WebAppComponents" `
            -DirectoryRefId     "WEBAPPFOLDER" `
            -OutputWxs          $webAppHarvestWxs
        $webAppFileCount = @(Get-ChildItem -Path $webAppStagingDir -Recurse -File -ErrorAction SilentlyContinue).Count
        if ($webAppFileCount -eq 0) {
            Write-Host "  [FAILED] WebApp staging directory is empty: $webAppStagingDir" -ForegroundColor Red
            Write-Host "           Step 3 (dotnet publish) must complete before Step 8." -ForegroundColor Red
            exit 1
        }
        Write-OK ("Harvested WebApp: {0} files -> {1}" -f $webAppFileCount, $webAppHarvestWxs)

        $extStagingDir = Join-Path $StagingDir "Extension"
        New-HarvestWxs `
            -SourceDir          $extStagingDir `
            -ComponentGroupName "ExtensionComponents" `
            -DirectoryRefId     "EXTENSIONFOLDER" `
            -OutputWxs          $extHarvestWxs
        $extFileCount = @(Get-ChildItem -Path $extStagingDir -Recurse -File -ErrorAction SilentlyContinue).Count
        if ($extFileCount -eq 0) {
            Write-Host "  [FAILED] Extension staging directory is empty: $extStagingDir" -ForegroundColor Red
            Write-Host "           Step 4 (Desktop Extension build) must complete before Step 8." -ForegroundColor Red
            exit 1
        }
        Write-OK ("Harvested Extension: {0} files -> {1}" -f $extFileCount, $extHarvestWxs)

        # 8e -- Build the MSI.
        # All source and output paths are absolute (derived from $RepoRoot /
        # $ArtifactsDir) so the build is independent of the current directory.
        $installerSrcDir = Join-Path $RepoRoot "installer\Dashboard.Installer"
        $cfgTemplateDir  = Join-Path $StagingDir "ConfigTemplate"
        $msiIntermDir    = Join-Path $ArtifactsDir "obj\MSI"
        $msiPath         = Join-Path $ArtifactsDir "Dashboard-$Version-Setup.msi"
        $null = New-Item -ItemType Directory -Path $msiIntermDir -Force

        $wxsSources = @(
            (Join-Path $installerSrcDir "Product.wxs"),
            (Join-Path $installerSrcDir "WebApplication.wxs"),
            (Join-Path $installerSrcDir "DesktopExtension.wxs"),
            (Join-Path $installerSrcDir "Configuration.wxs"),
            (Join-Path $installerSrcDir "Shortcuts.wxs"),
            $webAppHarvestWxs,
            $extHarvestWxs
        )

        $wixMsiArgs = @("tool", "run", "wix", "--", "build") +
                      $wxsSources +
                      @(
            "-arch",   "x64",
            "-ext",    "WixToolset.UI.wixext/$WixPinnedVersion",
            "-ext",    "WixToolset.Iis.wixext/4.0.3",
            "-ext",    "WixToolset.Util.wixext/$WixPinnedVersion",
            "-d",      "ProductVersion=$Version",
            "-d",      "DashboardPort=5000",
            "-d",      "ConfigTemplateDir=$cfgTemplateDir",
            "-b",      $installerSrcDir,
            "-intermediatefolder", $msiIntermDir,
            "-pdbtype", "none",
            "-out",    $msiPath
        )

        Invoke-NativeCommand -Stage "wix build (MSI)" -FilePath "dotnet" -Arguments $wixMsiArgs

        if (-not (Test-Path $msiPath)) {
            Write-Host "  [FAILED] MSI not found at expected path: $msiPath" -ForegroundColor Red
            exit 1
        }
        $msiBytes = (Get-Item $msiPath).Length
        Write-OK ("MSI built: {0}  ({1:N0} bytes)" -f $msiPath, $msiBytes)
    }
    finally {
        Pop-Location
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

    # Local tool was restored and extensions cached in Step 8.
    # Build the Burn bundle using 'dotnet tool run wix -- build' (same local-tool
    # pattern as Step 8 -- bypasses MSBuild and the WiX SDK resolver entirely).
    # Push-Location $RepoRoot ensures 'dotnet tool run wix' finds the manifest.

    $msiPath        = Join-Path $ArtifactsDir "Dashboard-$Version-Setup.msi"
    $baStagingDir   = Join-Path $StagingDir    "BA"
    $baAssemblyPath = Join-Path $baStagingDir  "Dashboard.BA.dll"
    $bundleSrcDir   = Join-Path $RepoRoot      "installer\Dashboard.Bundle"
    $bundleIntermDir = Join-Path $ArtifactsDir "obj\Bundle"
    $bundleExe      = Join-Path $ArtifactsDir  "LFDashboard-Setup.exe"
    $null = New-Item -ItemType Directory -Path $bundleIntermDir -Force

    # The MSI must exist before the bundle can reference it.
    if (-not (Test-Path $msiPath)) {
        Write-Host "  [FAILED] MSI not found at: $msiPath" -ForegroundColor Red
        Write-Host "           Step 8 must succeed before Step 9 can run." -ForegroundColor Red
        exit 1
    }

    # -------------------------------------------------------------------------
    # .NET Framework 4.8 prerequisite — build-time acquisition
    # -------------------------------------------------------------------------
    # WiX 4.0.5 requires the actual ndp48-web.exe to exist locally at build
    # time so the compiler can compute the payload hash/size/version it embeds
    # in the bundle manifest.  Without a local SourceFile the linker emits
    # WIX0103 even when DownloadUrl is supplied.
    #
    # The file is cached in .build-cache\prerequisites\ (gitignored) so it
    # survives the Step 1 artifact wipe and is not re-downloaded on every run.
    # The Authenticode signature is verified on every build — cached or fresh.
    # -------------------------------------------------------------------------
    $prereqCacheDir    = Join-Path $RepoRoot ".build-cache\prerequisites"
    $netFx48Installer  = Join-Path $prereqCacheDir "ndp48-web.exe"
    $netFx48Url        = "https://go.microsoft.com/fwlink/?LinkId=2085155"

    $null = New-Item -ItemType Directory -Path $prereqCacheDir -Force

    if (-not (Test-Path $netFx48Installer)) {
        Write-Host "  Downloading .NET Framework 4.8 web installer..." -ForegroundColor Cyan
        try {
            # TLS 1.2 is required by download.microsoft.com; PowerShell 5.1
            # defaults to TLS 1.0 on older Windows builds.
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
            Invoke-WebRequest -Uri $netFx48Url -OutFile $netFx48Installer -UseBasicParsing
        }
        catch {
            Write-Host "  [FAILED] Could not download ndp48-web.exe: $_" -ForegroundColor Red
            Remove-Item $netFx48Installer -Force -ErrorAction SilentlyContinue
            exit 1
        }
        Write-Host "  Downloaded: $netFx48Installer" -ForegroundColor DarkGray
    }
    else {
        Write-Host "  Using cached .NET Framework 4.8 installer: $netFx48Installer" -ForegroundColor DarkGray
    }

    # Authenticode verification — runs on every build (cached or fresh).
    # Requirement: Status = Valid, signer Subject contains "Microsoft".
    Write-Host "  Verifying Authenticode signature..." -ForegroundColor DarkGray
    $sig = Get-AuthenticodeSignature -FilePath $netFx48Installer
    if ($sig.Status -ne 'Valid') {
        Write-Host ""
        Write-Host "  [SECURITY] Authenticode verification FAILED." -ForegroundColor Red
        Write-Host "             File   : $netFx48Installer" -ForegroundColor Red
        Write-Host "             Status : $($sig.Status)" -ForegroundColor Red
        Remove-Item $netFx48Installer -Force -ErrorAction SilentlyContinue
        Write-Host "             The file has been deleted.  Re-run to download again." -ForegroundColor Red
        exit 1
    }
    $signerSubject = $sig.SignerCertificate.Subject
    if ($signerSubject -notmatch 'Microsoft') {
        Write-Host ""
        Write-Host "  [SECURITY] Signer is not Microsoft." -ForegroundColor Red
        Write-Host "             File    : $netFx48Installer" -ForegroundColor Red
        Write-Host "             Subject : $signerSubject" -ForegroundColor Red
        Remove-Item $netFx48Installer -Force -ErrorAction SilentlyContinue
        Write-Host "             The file has been deleted.  Re-run to download again." -ForegroundColor Red
        exit 1
    }
    Write-Host "  Signature valid.  Signer: $signerSubject" -ForegroundColor DarkGray

    $wixBundleArgs = @(
        "tool", "run", "wix", "--",
        "build",
        (Join-Path $bundleSrcDir "Bundle.wxs"),
        "-ext",    "WixToolset.Bal.wixext/$WixPinnedVersion",
        "-ext",    "WixToolset.Util.wixext/$WixPinnedVersion",
        "-d",      "BAAssembly=$baAssemblyPath",
        "-d",      "MbaCoreAssembly=$(Join-Path $baStagingDir 'WixToolset.Mba.Core.dll')",
        "-d",      "MbaHostConfig=$(Join-Path $baStagingDir 'WixToolset.Mba.Host.config')",
        "-d",      "MbaNative=$(Join-Path $baStagingDir 'mbanative.dll')",
        "-d",      "MsiPath=$msiPath",
        "-d",      "BundleVersion=$Version",
        "-d",      "NetFx48Installer=$netFx48Installer",
        "-intermediatefolder", $bundleIntermDir,
        "-pdbtype", "none",
        "-out",    $bundleExe
    )

    Push-Location $RepoRoot
    try {
        Invoke-NativeCommand -Stage "wix build (Bundle)" -FilePath "dotnet" -Arguments $wixBundleArgs
    }
    finally {
        Pop-Location
    }

    if (-not (Test-Path $bundleExe)) {
        Write-Host ""
        Write-Host "  [FAILED] LFDashboard-Setup.exe was not produced." -ForegroundColor Red
        Write-Host "           Expected: $bundleExe" -ForegroundColor Red
        exit 1
    }
    $exeBytes = (Get-Item $bundleExe).Length
    Write-OK ("Bundle built: {0}  ({1:N0} bytes)" -f $bundleExe, $exeBytes)
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
