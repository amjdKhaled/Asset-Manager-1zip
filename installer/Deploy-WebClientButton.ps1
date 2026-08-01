<#
.SYNOPSIS
    Deploys the Dashboard button to the Laserfiche Web Client (Browse.aspx).

.DESCRIPTION
    This script performs the Laserfiche Web Client integration for Dashboard.
    It is deliberately kept separate from the MSI installer because:

      1. Laserfiche Browse.aspx is a vendor file. Editing it from an MSI risks
         corruption or unrecoverable state if WiX rolls back mid-install.
      2. Laserfiche upgrades overwrite Browse.aspx -- this script must be
         re-run after each Laserfiche Web Client upgrade.
      3. Administrators may need to inspect or adjust the change before applying
         it to a production Laserfiche server.

    What this script does:
      1. Discovers the Laserfiche Web Client installation path (registry / IIS /
         known locations), or uses the -WebClientPath parameter directly.
      2. Backs up Browse.aspx to Browse.aspx.bak-<timestamp>
      3. Copies lf-dashboard-button.js into assets\custom\
      4. If -DashboardUrl is provided, patches DASHBOARD_BASE_URL inside the
         deployed script.  If omitted, the URL already embedded in the file is
         used without modification.
      5. Adds ONE <script> tag to Browse.aspx (idempotent -- never adds duplicates)
      6. Verifies the result

    What this script does NOT do:
      - Modify any other Laserfiche file
      - Change browse-custom.css or any other customisation
      - Restart IIS or the Laserfiche application pool

    After a Laserfiche Web Client upgrade, Browse.aspx is overwritten.
    Re-run this script to restore the Dashboard button.

    PORTABILITY NOTE:
    Every machine-specific value (Dashboard URL, Web Client path) is a parameter.
    No value in this script is hard-coded to a specific environment.

.PARAMETER DashboardUrl
    The public URL users' browsers use to reach the Dashboard server.
    Example: http://192.168.1.50:5000  or  https://dashboard.company.local

    IMPORTANT: this URL is evaluated in the CLIENT browser, not on the server.
    "localhost" means the user's own machine.  Use the server's hostname or IP.

    If omitted, the URL already present in lf-dashboard-button.js is kept
    as-is.  You MUST supply this parameter on a first-time or URL-change
    deployment to avoid the button pointing at the wrong server.

.PARAMETER WebClientPath
    Physical path to the Laserfiche Web Access installation root.
    If omitted, the script auto-discovers the path from the Windows registry,
    IIS, and known default locations (in that order).
    Specify this parameter to override auto-detection.
    Example: -WebClientPath "D:\Laserfiche\Web Access\Web Files"

.PARAMETER DashboardScriptSource
    Path to the source lf-webclient-button.js (renamed lf-dashboard-button.js).
    Default: auto-detected from the MSI install location, this script's directory,
    or the development repository layout -- in that order.

.PARAMETER Rollback
    Remove the Dashboard button from Browse.aspx by restoring the most recent
    backup.  Does not remove lf-dashboard-button.js from assets\custom.

.EXAMPLE
    # Deploy with Dashboard URL (recommended):
    .\Deploy-WebClientButton.ps1 -DashboardUrl "http://10.0.0.50:5000"

.EXAMPLE
    # Deploy with explicit Laserfiche path and Dashboard URL:
    .\Deploy-WebClientButton.ps1 `
        -DashboardUrl "https://dashboard.company.local" `
        -WebClientPath "D:\Laserfiche\Web Access\Web Files"

.EXAMPLE
    # Roll back the last change:
    .\Deploy-WebClientButton.ps1 -Rollback

.NOTES
    MUST be run as Administrator (requires write access to the Laserfiche directory).
    Compatible with Windows PowerShell 5.1 and PowerShell 7+.
    Re-run after every Laserfiche Web Client upgrade.
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$DashboardUrl         = "",
    [string]$WebClientPath        = "",
    [string]$DashboardScriptSource = "",
    [switch]$Rollback
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------- Constants --------------------------------------------------------

$ScriptTagLine = '<script src="assets/custom/lf-dashboard-button.js"></script>'
$AnchorPattern = 'browse-custom\.css'

# ---------- Helpers ----------------------------------------------------------

function Write-Step([string]$msg) { Write-Host "  $msg" -ForegroundColor Cyan }
function Write-OK([string]$msg)   { Write-Host "  [OK] $msg" -ForegroundColor Green }
function Write-Warn([string]$msg) { Write-Host "  [WARN] $msg" -ForegroundColor Yellow }
function Write-Err([string]$msg)  { Write-Host "  [ERROR] $msg" -ForegroundColor Red }

# ---------- Auto-discover Laserfiche Web Client path -------------------------

function Find-LaserficheWebClientPath {
    <#
    .SYNOPSIS
    Attempts to discover the Laserfiche Web Access installation directory.
    Checks (in order): Windows registry, IIS, known default paths.
    Returns the path if Browse.aspx is found, $null otherwise.
    #>

    $candidates = [System.Collections.Generic.List[string]]::new()

    # 1. Registry: 64-bit Laserfiche Web Access installation info
    $regPaths = @(
        'HKLM:\SOFTWARE\Laserfiche\WebAccess',
        'HKLM:\SOFTWARE\WOW6432Node\Laserfiche\WebAccess',
        'HKLM:\SOFTWARE\Laserfiche\WebAccess\10',
        'HKLM:\SOFTWARE\Laserfiche\WebAccess\11',
        'HKLM:\SOFTWARE\Laserfiche\WebAccess\12'
    )
    foreach ($rp in $regPaths) {
        if (Test-Path $rp) {
            $key = Get-Item $rp -ErrorAction SilentlyContinue
            if ($key) {
                foreach ($valueName in @('WebFilesPath','InstallPath','Path','WebPath','Directory')) {
                    $val = $key.GetValue($valueName, $null)
                    if ($val -and ($val -is [string]) -and $val.Length -gt 0) {
                        $candidates.Add($val.TrimEnd('\'))
                    }
                }
            }
        }
    }

    # 2. IIS: look for a site whose physical path contains Browse.aspx
    try {
        $webAdminModule = Get-Module -Name WebAdministration -ListAvailable -ErrorAction SilentlyContinue
        if ($webAdminModule) {
            Import-Module WebAdministration -ErrorAction SilentlyContinue
            $sites = Get-Website -ErrorAction SilentlyContinue
            if ($sites) {
                foreach ($site in $sites) {
                    $physPath = $site.physicalPath
                    if ($physPath) {
                        $physPath = [System.Environment]::ExpandEnvironmentVariables($physPath).TrimEnd('\')
                        $candidates.Add($physPath)
                    }
                }
            }
        }
    }
    catch {
        # WebAdministration not available or IIS not installed -- continue
    }

    # 3. Known default installation paths (multiple LF versions)
    $knownPaths = @(
        'C:\Program Files\Laserfiche\Web Access\Web Files',
        'C:\Program Files (x86)\Laserfiche\Web Access\Web Files',
        'C:\Program Files\Laserfiche\Web Access',
        'C:\Program Files (x86)\Laserfiche\Web Access',
        'C:\Laserfiche\Web Access\Web Files',
        'C:\Laserfiche\Web Files'
    )
    foreach ($kp in $knownPaths) {
        $candidates.Add($kp)
    }

    # Return the first candidate whose Browse.aspx exists
    foreach ($c in $candidates) {
        if ($c -and (Test-Path (Join-Path $c 'Browse.aspx'))) {
            return $c
        }
    }

    return $null
}

# ---------- Resolve Laserfiche Web Client path --------------------------------

Write-Host ""
Write-Host "  Dashboard - Laserfiche Web Client Button Deployment" -ForegroundColor White
Write-Host "  ====================================================" -ForegroundColor DarkGray
Write-Host ""

if ([string]::IsNullOrWhiteSpace($WebClientPath)) {
    Write-Step "Auto-detecting Laserfiche Web Client installation path..."
    $discovered = Find-LaserficheWebClientPath
    if ($discovered) {
        $WebClientPath = $discovered
        Write-OK "Found: $WebClientPath"
    }
    else {
        Write-Err "Could not auto-detect the Laserfiche Web Client installation."
        Write-Err "Checked registry, IIS configuration, and known default paths."
        Write-Err "Specify the path explicitly:"
        Write-Err "  .\Deploy-WebClientButton.ps1 -WebClientPath `"D:\Laserfiche\Web Access\Web Files`""
        exit 1
    }
}
else {
    Write-OK "Web Client path (explicit): $WebClientPath"
}

$ButtonScriptDest = Join-Path $WebClientPath "assets\custom\lf-dashboard-button.js"
$BrowseAspx       = Join-Path $WebClientPath "Browse.aspx"

# ---------- Resolve source JS file ------------------------------------------

if ([string]::IsNullOrWhiteSpace($DashboardScriptSource)) {
    # Candidate 1: sibling of this script (MSI artifacts\WebClientButton\)
    $c1 = Join-Path $PSScriptRoot "lf-dashboard-button.js"
    # Candidate 2: standard MSI install location
    $c2 = "C:\Program Files\Dashboard\WebApp\wwwroot\js\lf-webclient-button.js"
    # Candidate 3: development repo layout
    $c3 = Join-Path $PSScriptRoot "..\src\LFPortal.Web\wwwroot\js\lf-webclient-button.js"

    foreach ($c in @($c1, $c2, $c3)) {
        $resolved = [System.IO.Path]::GetFullPath($c)
        if (Test-Path $resolved) {
            $DashboardScriptSource = $resolved
            break
        }
    }

    if ([string]::IsNullOrWhiteSpace($DashboardScriptSource)) {
        Write-Err "Could not auto-detect the Dashboard button script."
        Write-Err "Pass -DashboardScriptSource <path> to specify it explicitly."
        exit 1
    }
}

# ---------- Check administrator rights --------------------------------------

$currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal   = New-Object Security.Principal.WindowsPrincipal($currentUser)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Err "This script must be run as Administrator."
    Write-Err "Right-click PowerShell and choose 'Run as Administrator'."
    exit 1
}

# ---------- Validate paths --------------------------------------------------

Write-Step "Validating paths..."

if (-not (Test-Path $BrowseAspx)) {
    Write-Err "Browse.aspx not found at: $BrowseAspx"
    Write-Err "Verify -WebClientPath is the Laserfiche Web Files directory."
    exit 1
}

if (-not (Test-Path $DashboardScriptSource)) {
    Write-Err "Dashboard button script not found at: $DashboardScriptSource"
    exit 1
}

Write-OK "Browse.aspx:   $BrowseAspx"
Write-OK "Source script: $DashboardScriptSource"

# ---------- Rollback mode ---------------------------------------------------

if ($Rollback) {
    Write-Step "Rolling back Browse.aspx from most recent backup..."

    $backups = Get-Item "$BrowseAspx.bak-*" -ErrorAction SilentlyContinue |
               Sort-Object Name -Descending

    if (-not $backups) {
        Write-Warn "No Browse.aspx backup files found.  Nothing to roll back."
        exit 0
    }

    $latestBackup = $backups[0].FullName
    Write-Step "Restoring from: $latestBackup"

    if ($PSCmdlet.ShouldProcess($BrowseAspx, "Restore from $latestBackup")) {
        Copy-Item -Path $latestBackup -Destination $BrowseAspx -Force
        Write-OK "Browse.aspx restored."
    }

    Write-Host ""
    Write-OK "Rollback complete.  Ctrl+F5 in the browser to verify."
    exit 0
}

# ---------- Backup Browse.aspx ----------------------------------------------

Write-Step "Backing up Browse.aspx..."

$timestamp  = Get-Date -Format "yyyyMMdd-HHmmss"
$backupPath = "$BrowseAspx.bak-$timestamp"

if ($PSCmdlet.ShouldProcess($backupPath, "Create backup")) {
    Copy-Item -Path $BrowseAspx -Destination $backupPath -Force
}

Write-OK "Backup: $backupPath"

# ---------- Copy Dashboard button script ------------------------------------

Write-Step "Deploying Dashboard button script..."

$customDir = Split-Path $ButtonScriptDest -Parent
if (-not (Test-Path $customDir)) {
    Write-Warn "assets\custom\ directory not found -- creating it."
    New-Item -ItemType Directory -Path $customDir -Force | Out-Null
}

if ($PSCmdlet.ShouldProcess($ButtonScriptDest, "Copy button script")) {
    Copy-Item -Path $DashboardScriptSource -Destination $ButtonScriptDest -Force
}

Write-OK "Script deployed to: $ButtonScriptDest"

# ---------- Patch DASHBOARD_BASE_URL in the deployed script -----------------

if (-not [string]::IsNullOrWhiteSpace($DashboardUrl)) {
    Write-Step "Setting DASHBOARD_BASE_URL to: $DashboardUrl"

    $scriptContent = [System.IO.File]::ReadAllText(
        $ButtonScriptDest,
        [System.Text.Encoding]::UTF8)

    # Replace: var DASHBOARD_BASE_URL = '<any url>';
    # Preserves the surrounding code structure and comment block intact.
    $pattern     = "(var DASHBOARD_BASE_URL\s*=\s*)'[^']*'"
    $replacement = "`$1'$DashboardUrl'"
    $patched     = [System.Text.RegularExpressions.Regex]::Replace(
                       $scriptContent, $pattern, $replacement)

    if ($patched -eq $scriptContent) {
        Write-Warn "Could not locate 'var DASHBOARD_BASE_URL = ...' in the script."
        Write-Warn "Check that the file is the correct Dashboard button script."
        Write-Warn "The URL was NOT patched.  Edit $ButtonScriptDest manually."
    }
    else {
        [System.IO.File]::WriteAllText(
            $ButtonScriptDest,
            $patched,
            [System.Text.UTF8Encoding]::new($false))
        Write-OK "DASHBOARD_BASE_URL set to: $DashboardUrl"
    }
}
else {
    # No URL was provided -- check whether the default localhost placeholder remains
    $existingContent = [System.IO.File]::ReadAllText(
        $ButtonScriptDest,
        [System.Text.Encoding]::UTF8)

    if ($existingContent -match "var DASHBOARD_BASE_URL\s*=\s*'http://localhost") {
        Write-Warn ""
        Write-Warn "DASHBOARD_BASE_URL is set to a localhost URL."
        Write-Warn "This will ONLY work for users on the same machine as the Dashboard server."
        Write-Warn "For network access, re-run with:"
        Write-Warn "  .\Deploy-WebClientButton.ps1 -DashboardUrl `"http://YOUR-SERVER:5000`""
        Write-Warn ""
    }
    elseif ($existingContent -match "var DASHBOARD_BASE_URL\s*=\s*'([^']+)'") {
        $existingUrl = $Matches[1]
        Write-OK "DASHBOARD_BASE_URL retained from source: $existingUrl"
    }
}

# ---------- Add script tag to Browse.aspx -----------------------------------

Write-Step "Checking Browse.aspx for existing Dashboard script tag..."

$existingTags = Select-String -Path $BrowseAspx -Pattern "lf-dashboard-button\.js" -AllMatches
if ($existingTags.Count -gt 0) {
    Write-OK "Dashboard script tag already present ($($existingTags.Count) occurrence(s)).  Nothing to change."
}
else {
    Write-Step "Adding <script> tag to Browse.aspx..."

    $lines    = Get-Content $BrowseAspx -Encoding UTF8
    $newLines = [System.Collections.Generic.List[string]]::new()
    $inserted = $false

    foreach ($line in $lines) {
        $newLines.Add($line)
        # Insert the Dashboard script tag immediately after the browse-custom.css line.
        if (-not $inserted -and $line -match $AnchorPattern) {
            $trimmed = $line.TrimStart()
            $indent  = $line.Substring(0, $line.Length - $trimmed.Length)
            $newLines.Add("$indent$ScriptTagLine")
            $inserted = $true
        }
    }

    if (-not $inserted) {
        Write-Warn "Could not find the browse-custom.css anchor line in Browse.aspx."
        Write-Warn "Adding the script tag before </head> instead."

        $newLines = [System.Collections.Generic.List[string]]::new()
        foreach ($line in $lines) {
            if (-not $inserted -and $line -match '</head>') {
                $newLines.Add("    $ScriptTagLine")
                $inserted = $true
            }
            $newLines.Add($line)
        }
    }

    if ($PSCmdlet.ShouldProcess($BrowseAspx, "Insert Dashboard script tag")) {
        [System.IO.File]::WriteAllLines(
            $BrowseAspx,
            $newLines,
            [System.Text.UTF8Encoding]::new($false))
    }

    Write-OK "Script tag inserted."
}

# ---------- Verify ----------------------------------------------------------

Write-Step "Verifying result..."

$verifyScript = Test-Path $ButtonScriptDest
$verifyTag    = (Select-String -Path $BrowseAspx -Pattern "lf-dashboard-button\.js").Count
$verifyDupe   = $verifyTag -le 1

if ($verifyScript -and $verifyTag -ge 1 -and $verifyDupe) {
    Write-OK "lf-dashboard-button.js present in assets\custom\"
    Write-OK "Script tag count in Browse.aspx: $verifyTag (expected 1)"
}
else {
    Write-Warn "Verification warnings:"
    Write-Warn "  Script file present: $verifyScript"
    Write-Warn "  Script tag count:    $verifyTag (expected 1)"
}

# ---------- Show the configured URL -----------------------------------------

$finalContent = [System.IO.File]::ReadAllText(
    $ButtonScriptDest,
    [System.Text.Encoding]::UTF8)

$activeUrl = ""
if ($finalContent -match "var DASHBOARD_BASE_URL\s*=\s*'([^']+)'") {
    $activeUrl = $Matches[1]
}

# ---------- Summary ---------------------------------------------------------

Write-Host ""
Write-Host "  ====================================================" -ForegroundColor DarkGray
Write-OK "Web Client button deployment complete."
Write-Host ""
Write-Host "  CONFIGURATION SUMMARY:" -ForegroundColor White
Write-Host "  Web Client path:   $WebClientPath" -ForegroundColor Gray
Write-Host "  Script deployed:   $ButtonScriptDest" -ForegroundColor Gray
if ($activeUrl.Length -gt 0) {
    Write-Host "  Dashboard URL:     $activeUrl" -ForegroundColor Gray
}
Write-Host ""
Write-Host "  NEXT STEPS:" -ForegroundColor White
Write-Host "  1. Open a Laserfiche Web Client browser tab and press Ctrl+F5." -ForegroundColor Gray
Write-Host "  2. Log in, open a repository, verify the Dashboard button appears." -ForegroundColor Gray
Write-Host "  3. After any Laserfiche Web Client upgrade, re-run this script." -ForegroundColor Gray
Write-Host ""
Write-Host "  To roll back:  .\Deploy-WebClientButton.ps1 -Rollback" -ForegroundColor Gray
Write-Host ""
