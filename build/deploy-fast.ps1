#Requires -Version 5.1
<#
.SYNOPSIS
    Fast-deploy the Dashboard web app to an already-installed IIS site.

.DESCRIPTION
    Skips the full MSI/WiX/installer cycle.  Useful for iterative UI changes
    (views, CSS, JavaScript, layouts).  Typical run time: 25-40 seconds vs
    5-8 minutes for a full reinstall.

    What the script does:
      1. dotnet publish the web project (self-contained, win-x64, Release)
      2. Stop the Dashboard IIS app pool
      3. Robocopy the publish output to the IIS physical path
      4. Restart the app pool

    What it does NOT do:
      - Touch %ProgramData%\Dashboard\  (your Laserfiche credentials, port
        config, and runtime settings are safe)
      - Overwrite appsettings.json  (the MSI patches the "Urls" key at install
        time to bind the wizard-selected port; overwriting it would reset the
        port to the Kestrel default)
      - Deploy the Desktop Extension EXE or SetupHelper
      - Work as a substitute for a first install  (MSI required for that)

    PREREQUISITE: Dashboard must have been installed at least once via
    LFDashboard-Setup.exe before using this script.

──────────────────────────────────────────────────────────────────────────────
 EVEN FASTER — CSS / JavaScript changes only  (~5 seconds, no publish needed)
──────────────────────────────────────────────────────────────────────────────
 Razor views (.cshtml) are precompiled into the DLL in Release mode and
 CANNOT be updated by copying the source file — you need a full publish for
 those.  But static files (CSS, JS, images) CAN be copied directly:

   # 1. Find the IIS physical path once (or hardcode it):
   $appcmd = "$env:SystemRoot\system32\inetsrv\appcmd.exe"
   $dest   = & $appcmd list vdir /site.name:Dashboard /path:/ /text:physicalPath

   # 2. Copy the changed file:
   Copy-Item "src\LFPortal.Web\wwwroot\css\site.css"  "$dest\wwwroot\css\site.css"  -Force
   Copy-Item "src\LFPortal.Web\wwwroot\js\site.js"    "$dest\wwwroot\js\site.js"    -Force

   # No app-pool restart needed — static files are served immediately.
──────────────────────────────────────────────────────────────────────────────

.PARAMETER WebAppPath
    Physical path to the installed web app folder.
    Auto-detected from IIS (appcmd) if omitted.
    Example: "C:\Program Files\Dashboard\WebApp"

.PARAMETER AppPoolName
    IIS application pool name.  Default: Dashboard

.PARAMETER SiteName
    IIS site name used to locate the physical path.  Default: Dashboard

.PARAMETER SkipPublish
    Skip the 'dotnet publish' step and deploy an existing publish output.
    The output folder is $env:TEMP\DashboardFastDeploy\WebApp unless you
    also pass -PublishOutputPath.

.PARAMETER PublishOutputPath
    Override the publish output folder used as the copy source.
    Has no effect unless -SkipPublish is set.

.EXAMPLE
    .\build\deploy-fast.ps1
    Full publish then deploy.  ~30 seconds.

.EXAMPLE
    .\build\deploy-fast.ps1 -SkipPublish
    Deploy the last publish output without rebuilding.

.EXAMPLE
    .\build\deploy-fast.ps1 -WebAppPath "D:\Apps\Dashboard\WebApp"
    Deploy to a non-default install path.

.EXAMPLE
    .\build\deploy-fast.ps1 -SiteName Dashboard -AppPoolName Dashboard
    Same as default, but parameters shown explicitly for scripting.
#>
[CmdletBinding()]
param(
    [string] $WebAppPath        = '',
    [string] $AppPoolName       = 'Dashboard',
    [string] $SiteName          = 'Dashboard',
    [switch] $SkipPublish,
    [string] $PublishOutputPath = ''
)

Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'

# ── Console helpers ───────────────────────────────────────────────────────

function Write-Step([string]$Msg) {
    Write-Host "-> $Msg" -ForegroundColor Cyan
}
function Write-OK([string]$Msg) {
    Write-Host "   OK  $Msg" -ForegroundColor Green
}
function Write-Info([string]$Msg) {
    Write-Host "   ..  $Msg" -ForegroundColor DarkGray
}
function Write-Fail([string]$Msg) {
    Write-Host ""
    Write-Host "   FAIL  $Msg" -ForegroundColor Red
    Write-Host ""
    exit 1
}

Write-Host ""
Write-Host "  Dashboard -- Fast Deploy" -ForegroundColor White
Write-Host "  ──────────────────────────────────────────" -ForegroundColor DarkGray
Write-Host ""

# ── Locate repo root (script lives in build\) ─────────────────────────────

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot  = Split-Path -Parent $ScriptDir
$AppcmdExe = "$env:SystemRoot\system32\inetsrv\appcmd.exe"

# ── Step 1: dotnet publish ─────────────────────────────────────────────────

$PublishDir = if ($PublishOutputPath) {
    $PublishOutputPath
} else {
    Join-Path $env:TEMP 'DashboardFastDeploy\WebApp'
}

if ($SkipPublish) {
    Write-Step "Skipping publish (-SkipPublish).  Source: $PublishDir"
    if (-not (Test-Path $PublishDir)) {
        Write-Fail ("Publish output not found at '$PublishDir'." +
                    "  Run without -SkipPublish first.")
    }
} else {
    Write-Step "Publishing web app (self-contained win-x64 Release)..."

    $WebProjPath = Join-Path $RepoRoot 'src\LFPortal.Web\LFPortal.Web.csproj'
    if (-not (Test-Path $WebProjPath)) {
        Write-Fail ("Web project not found: $WebProjPath`n" +
                    "  Run this script from the repository root, or from any subfolder.")
    }

    $dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnetCmd) {
        Write-Fail ("'dotnet' not found in PATH.  Install the .NET 8 SDK from " +
                    "https://dotnet.microsoft.com/download")
    }

    # Clean previous publish output so stale files don't survive.
    if (Test-Path $PublishDir) {
        Remove-Item $PublishDir -Recurse -Force
    }
    $null = New-Item -ItemType Directory -Path $PublishDir -Force

    $publishArgs = @(
        'publish', $WebProjPath,
        '--configuration',  'Release',
        '--runtime',        'win-x64',
        '--self-contained', 'true',
        '--output',         $PublishDir,
        '--verbosity',      'minimal'
    )

    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Fail "dotnet publish failed (exit code $LASTEXITCODE)."
    }
    Write-OK "Published to: $PublishDir"
}

# ── Step 2: resolve IIS physical path ─────────────────────────────────────

if (-not $WebAppPath) {
    Write-Step "Detecting IIS physical path for site '$SiteName'..."

    if (Test-Path $AppcmdExe) {
        # appcmd list vdir /site.name:<name> /path:/ /text:physicalPath
        # prints the root virtual directory's physical path, e.g.
        #   C:\Program Files\Dashboard\WebApp
        $detected = & $AppcmdExe list vdir "/site.name:$SiteName" '/path:/' '/text:physicalPath' 2>$null
        if ($detected) {
            $WebAppPath = ($detected | Select-Object -First 1).ToString().TrimEnd('\', '/')
            Write-OK "Detected: $WebAppPath"
        } else {
            Write-Info "appcmd found no virtual directory for site '$SiteName'."
        }
    } else {
        Write-Info "appcmd.exe not found at: $AppcmdExe"
        Write-Info "IIS may not be installed, or you're running on a non-IIS machine."
    }
}

if (-not $WebAppPath) {
    $WebAppPath = "$env:ProgramFiles\Dashboard\WebApp"
    Write-Info "Falling back to default install path: $WebAppPath"
}

if (-not (Test-Path $WebAppPath)) {
    Write-Fail (
        "Web app path does not exist: $WebAppPath`n" +
        "  Ensure Dashboard has been installed via LFDashboard-Setup.exe at least once.`n" +
        "  If you used a custom INSTALLFOLDER at install time, pass:`n" +
        "      -WebAppPath 'C:\YourCustomPath\WebApp'"
    )
}

Write-OK "Deploy target: $WebAppPath"

# ── Step 3: stop app pool ─────────────────────────────────────────────────

Write-Step "Stopping IIS app pool '$AppPoolName'..."

if (Test-Path $AppcmdExe) {
    # 'stop' on an already-stopped pool exits 0 with "APPPOOL already stopped"
    # — that is fine, we proceed either way.
    $stopOutput = & $AppcmdExe stop apppool "/apppool.name:$AppPoolName" 2>&1
    Write-OK "App pool stopped (or was already stopped)."
} else {
    Write-Info ("appcmd.exe not available -- skipping app pool stop." +
                "  IIS may recycle the process on its own after the files change.")
}

# Brief pause to let any in-flight requests drain before overwriting binaries.
Start-Sleep -Milliseconds 500

# ── Step 4: robocopy ──────────────────────────────────────────────────────

Write-Step "Copying files to IIS path..."

# /MIR  - mirror: copies new/changed files AND removes files in the
#         destination that are no longer in the source (keeps the site clean).
#
# /XF appsettings.json
#       - CRITICAL: the MSI installer patches "Urls" in appsettings.json to
#         bind the wizard-selected port (e.g. http://*:5000).  Overwriting
#         this file with the publish-time default would reset the port.
#         /XF also protects it from the /MIR deletion pass.
#
# /XF appsettings.Development.json
#       - Should never appear in a Release publish, but excluded as a safety
#         net.  Development settings must never run in the IIS site.
#
# /NFL /NDL /NJH /NJS /NP
#       - Suppress per-file, per-directory, header, summary, and progress
#         output so the console stays readable.
$robocopyArgs = @(
    $PublishDir,
    $WebAppPath,
    '/MIR',
    '/XF', 'appsettings.json',
    '/XF', 'appsettings.Development.json',
    '/NFL', '/NDL', '/NJH', '/NJS', '/NP'
)

robocopy @robocopyArgs

# robocopy exit codes are bit-flags.  0-7 = success (combinations of:
#   0 = no change, 1 = files copied, 2 = extra files, 4 = mismatches).
# 8+ = real errors (I/O errors, access denied, etc.).
if ($LASTEXITCODE -ge 8) {
    Write-Fail "robocopy failed with exit code $LASTEXITCODE.  Check the output above."
}

Write-OK "Files deployed."

# ── Step 5: restart app pool ──────────────────────────────────────────────

Write-Step "Starting IIS app pool '$AppPoolName'..."

if (Test-Path $AppcmdExe) {
    $startOutput = & $AppcmdExe start apppool "/apppool.name:$AppPoolName" 2>&1
    Write-OK "App pool started."
} else {
    Write-Info ("appcmd.exe not available -- you may need to restart the app pool" +
                " manually in IIS Manager.")
}

# ── Done ──────────────────────────────────────────────────────────────────

# Read the port from the installed appsettings.json so we can print the URL.
$installedSettings = Join-Path $WebAppPath 'appsettings.json'
$port = 5000
if (Test-Path $installedSettings) {
    try {
        $json  = Get-Content $installedSettings -Raw | ConvertFrom-Json
        $urls  = $json.Urls
        if ($urls -match ':(\d+)') { $port = [int]$Matches[1] }
    } catch {
        # ConvertFrom-Json failed -- use the default port.
    }
}

Write-Host ""
Write-Host "  ──────────────────────────────────────────" -ForegroundColor DarkGray
Write-Host "  Fast deploy complete!" -ForegroundColor Green
Write-Host "  Dashboard: http://localhost:$port" -ForegroundColor White
Write-Host "  Config (%ProgramData%\Dashboard\) was not touched." -ForegroundColor DarkGray
Write-Host ""
