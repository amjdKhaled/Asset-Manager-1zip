<#
.SYNOPSIS
    Deploys the Dashboard button to the Laserfiche Web Client (Browse.aspx).

.DESCRIPTION
    This script performs the Laserfiche Web Client integration for Dashboard.
    It is deliberately kept separate from the MSI installer because:

      1. Laserfiche Browse.aspx is a vendor file. Editing it from an MSI risks
         corruption or unrecoverable state if WiX rolls back mid-install.
      2. Laserfiche upgrades overwrite Browse.aspx -- this script must be re-run
         after each Laserfiche Web Client upgrade.
      3. Administrators may need to inspect or adjust the change before applying
         it to a production Laserfiche server.

    What this script does:
      1. Backs up Browse.aspx to Browse.aspx.bak-<timestamp>
      2. Copies lf-dashboard-button.js into assets\custom\
      3. Adds ONE <script> tag to Browse.aspx (idempotent -- never adds duplicates)
      4. Verifies the result

    What this script does NOT do:
      - Modify any other Laserfiche file
      - Change browse-custom.css or any other customization
      - Restart IIS or the Laserfiche application pool

    After a Laserfiche Web Client upgrade, Browse.aspx is overwritten.
    Re-run this script to restore the Dashboard button.

.PARAMETER LFWebPath
    Physical path to the Laserfiche Web Access installation.
    Default: C:\Program Files\Laserfiche\Web Access\Web Files

.PARAMETER DashboardScriptSource
    Path to the source lf-webclient-button.js file from the Dashboard repository
    or the MSI installation directory.
    Default: auto-detected relative to this script.

.PARAMETER Rollback
    Remove the Dashboard button from Browse.aspx and restore from the most
    recent backup. Does not remove lf-dashboard-button.js from assets\custom.

.EXAMPLE
    # Deploy with defaults (run as Administrator):
    .\Deploy-WebClientButton.ps1

.EXAMPLE
    # Deploy with explicit Laserfiche path:
    .\Deploy-WebClientButton.ps1 -LFWebPath "D:\Laserfiche\Web Files"

.EXAMPLE
    # Roll back the last change:
    .\Deploy-WebClientButton.ps1 -Rollback

.NOTES
    MUST be run as Administrator (requires write access to the Laserfiche directory).
    Re-run after every Laserfiche Web Client upgrade.
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$LFWebPath = "C:\Program Files\Laserfiche\Web Access\Web Files",

    [string]$DashboardScriptSource = "",

    [switch]$Rollback
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------- Constants --------------------------------------------------------

$ScriptTagLine    = '<script src="assets/custom/lf-dashboard-button.js"></script>'
$AnchorPattern    = 'browse-custom\.css'
$ButtonScriptDest = Join-Path $LFWebPath "assets\custom\lf-dashboard-button.js"
$BrowseAspx       = Join-Path $LFWebPath "Browse.aspx"

# ---------- Helpers ----------------------------------------------------------

function Write-Step([string]$msg) { Write-Host "  $msg" -ForegroundColor Cyan }
function Write-OK([string]$msg)   { Write-Host "  [OK] $msg" -ForegroundColor Green }
function Write-Warn([string]$msg) { Write-Host "  [WARN] $msg" -ForegroundColor Yellow }
function Write-Err([string]$msg)  { Write-Host "  [ERROR] $msg" -ForegroundColor Red }

# ---------- Resolve source JS file ------------------------------------------

if ([string]::IsNullOrWhiteSpace($DashboardScriptSource)) {
    # Candidate 1: sibling of this script (MSI copies the script here)
    $candidate1 = Join-Path $PSScriptRoot "lf-dashboard-button.js"
    # Candidate 2: standard MSI install location
    $candidate2 = "C:\Program Files\Dashboard\WebApp\wwwroot\js\lf-webclient-button.js"
    # Candidate 3: development repo location
    $candidate3 = Join-Path $PSScriptRoot "..\src\LFPortal.Web\wwwroot\js\lf-webclient-button.js"

    foreach ($c in @($candidate1, $candidate2, $candidate3)) {
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

Write-Host ""
Write-Host "  Dashboard - Laserfiche Web Client Button Deployment" -ForegroundColor White
Write-Host "  ===================================================" -ForegroundColor DarkGray
Write-Host ""

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
    Write-Err "Check the -LFWebPath parameter."
    exit 1
}

if (-not (Test-Path $DashboardScriptSource)) {
    Write-Err "Dashboard button script not found at: $DashboardScriptSource"
    exit 1
}

Write-OK "Browse.aspx:     $BrowseAspx"
Write-OK "Button script:   $DashboardScriptSource"

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

# ---------- Check DASHBOARD_BASE_URL in the script --------------------------

$scriptContent = Get-Content $ButtonScriptDest -Raw
if ($scriptContent -match "var DASHBOARD_BASE_URL\s*=\s*'http://localhost:5000'") {
    Write-Warn "DASHBOARD_BASE_URL is still set to 'http://localhost:5000'."
    Write-Warn "This points to the user's LOCAL machine, not the Dashboard server."
    Write-Warn "Edit $ButtonScriptDest and set DASHBOARD_BASE_URL to the"
    Write-Warn "network-accessible URL of your Dashboard server before use."
}

# ---------- Add script tag to Browse.aspx -----------------------------------

Write-Step "Checking Browse.aspx for existing Dashboard script tag..."

$existingTags = Select-String -Path $BrowseAspx -Pattern "lf-dashboard-button\.js" -AllMatches
if ($existingTags.Count -gt 0) {
    Write-OK "Dashboard script tag already present ($($existingTags.Count) occurrence(s)). Nothing to change."
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
            # Preserve the indentation of the anchor line.
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
    Write-OK "lf-dashboard-button.js present in assets\custom\: $verifyScript"
    Write-OK "Script tag count in Browse.aspx: $verifyTag (expected 1)"
}
else {
    Write-Warn "Verification warnings:"
    Write-Warn "  Script file present: $verifyScript"
    Write-Warn "  Script tag count:    $verifyTag (expected 1)"
}

# ---------- Summary ---------------------------------------------------------

Write-Host ""
Write-Host "  ===================================================" -ForegroundColor DarkGray
Write-OK "Web Client button deployment complete."
Write-Host ""
Write-Host "  NEXT STEPS:" -ForegroundColor White
Write-Host "  1. Verify DASHBOARD_BASE_URL in: $ButtonScriptDest" -ForegroundColor Gray
Write-Host "     Set it to the URL your users' browsers use to reach Dashboard." -ForegroundColor Gray
Write-Host "  2. Open a Laserfiche Web Client browser tab and press Ctrl+F5." -ForegroundColor Gray
Write-Host "  3. Log in, open a repository, verify the Dashboard button appears." -ForegroundColor Gray
Write-Host "  4. After any Laserfiche Web Client upgrade, re-run this script." -ForegroundColor Gray
Write-Host ""
Write-Host "  To roll back: .\Deploy-WebClientButton.ps1 -Rollback" -ForegroundColor Gray
Write-Host ""
