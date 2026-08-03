<#
.SYNOPSIS
    Configures Dashboard for a specific deployment environment.

.DESCRIPTION
    Updates the machine-specific configuration files in %ProgramData%\Dashboard\
    without requiring source-code changes or recompilation.

    This script is safe to run:
      - After a fresh MSI installation
      - When migrating to a different Laserfiche server
      - When the Dashboard server's IP or hostname changes
      - After a cross-machine migration

    Files updated:
      %ProgramData%\Dashboard\laserfiche.config.json  -- Laserfiche connection
      %ProgramData%\Dashboard\extension.config.json   -- Desktop Extension URL

    Credentials are NOT stored in configuration files.  They are entered via
    the Dashboard Settings page and encrypted with Windows DPAPI.

    PORTABILITY:
    This script uses Environment.GetFolderPath to resolve %ProgramData%
    dynamically.  It does not hard-code C:\ProgramData.

.PARAMETER DashboardUrl
    The public URL (hostname or IP) users' browsers use to reach the Dashboard.
    This becomes the PortalUrl in extension.config.json.

    Development:    http://localhost:5000
    LAN server A:   http://192.168.1.50:5000
    LAN server B:   http://10.0.0.25:8080
    Production:     https://dashboard.company.local

.PARAMETER LaserficheApiUrl
    Full URL of the Laserfiche Repository API endpoint.
    Example: https://lf-server.corp.local/LFRepositoryAPI

.PARAMETER RepositoryId
    Laserfiche repository name (not display name, not GUID).
    Example: Documents

.PARAMETER DisplayName
    Human-readable name shown in the Dashboard UI for the repository.
    Example: "Documents Repository"

.PARAMETER ApiVersion
    Laserfiche API version to use: Auto (probe v2 then v1 at runtime), v1, or v2.
    Default: Auto

.PARAMETER TimeoutSeconds
    HTTP request timeout in seconds for Laserfiche API calls.  Default: 30

.EXAMPLE
    # Configure for a LAN server:
    .\Configure-Dashboard.ps1 `
        -DashboardUrl "http://192.168.1.50:5000" `
        -LaserficheApiUrl "https://lf-server/LFRepositoryAPI" `
        -RepositoryId "Documents" `
        -DisplayName "Documents Repository"

.EXAMPLE
    # Configure only the Dashboard URL (extension + Web Client button):
    .\Configure-Dashboard.ps1 -DashboardUrl "https://dashboard.company.local"

.EXAMPLE
    # Configure only the Laserfiche connection:
    .\Configure-Dashboard.ps1 `
        -LaserficheApiUrl "https://new-lf-server/LFRepositoryAPI" `
        -RepositoryId "ProductionRepo"

.NOTES
    Compatible with Windows PowerShell 5.1 and PowerShell 7+.
    You do not need to be an Administrator to run this script -- the ProgramData
    directory is readable and writable by standard users.  If you run as
    Administrator, file permissions on the written files are broader.

    After updating configuration, either restart the Dashboard IIS Application
    Pool (iisreset or Recycle in IIS Manager) so the web app picks up the new
    settings, or wait for the app's file-change watcher to reload automatically.
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$DashboardUrl      = "",
    [string]$LaserficheApiUrl  = "",
    [string]$RepositoryId      = "",
    [string]$DisplayName       = "",
    [string]$ApiVersion        = "",
    [int]$TimeoutSeconds       = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------- Helpers ----------------------------------------------------------

function Write-Step([string]$msg) { Write-Host "  $msg" -ForegroundColor Cyan }
function Write-OK([string]$msg)   { Write-Host "  [OK] $msg" -ForegroundColor Green }
function Write-Warn([string]$msg) { Write-Host "  [WARN] $msg" -ForegroundColor Yellow }
function Write-Err([string]$msg)  { Write-Host "  [ERROR] $msg" -ForegroundColor Red }
function Write-Skip([string]$msg) { Write-Host "  [SKIP] $msg" -ForegroundColor DarkGray }

function Test-Url([string]$url) {
    # Validates a URL is well-formed http or https.
    # Returns $true if valid, $false otherwise.
    if ([string]::IsNullOrWhiteSpace($url)) { return $false }
    try {
        $uri = [System.Uri]$url
        return ($uri.Scheme -eq 'http' -or $uri.Scheme -eq 'https')
    }
    catch {
        return $false
    }
}

function Read-JsonFile([string]$path) {
    # Reads a JSON file and returns a hashtable.
    # Returns an empty hashtable if the file does not exist.
    if (-not (Test-Path $path)) {
        return @{}
    }
    $text = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
    # PowerShell 5.1: ConvertFrom-Json returns a PSCustomObject; convert to hashtable.
    $obj = ConvertFrom-Json -InputObject $text
    $ht  = @{}
    $obj.PSObject.Properties | ForEach-Object { $ht[$_.Name] = $_.Value }
    return $ht
}

function Write-JsonFile([string]$path, [hashtable]$data) {
    # Writes a hashtable as indented JSON to the specified path.
    $dir = Split-Path $path -Parent
    if ($dir -and -not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    $json = ConvertTo-Json -InputObject $data -Depth 10
    [System.IO.File]::WriteAllText($path, $json, [System.Text.UTF8Encoding]::new($false))
}

# ---------- Resolve ProgramData paths ----------------------------------------
# Use Environment.GetFolderPath -- never hard-code C:\ProgramData.

$ProgramData   = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::CommonApplicationData)
$DashboardData = Join-Path $ProgramData "Dashboard"
$LFConfigPath  = Join-Path $DashboardData "laserfiche.config.json"
$ExtConfigPath = Join-Path $DashboardData "extension.config.json"

# ---------- Header -----------------------------------------------------------

Write-Host ""
Write-Host "  Dashboard Configuration" -ForegroundColor White
Write-Host "  =======================" -ForegroundColor DarkGray
Write-Host "  Config directory: $DashboardData" -ForegroundColor Gray
Write-Host ""

# ---------- Validate inputs --------------------------------------------------

$anyChange = $false

if (-not [string]::IsNullOrWhiteSpace($DashboardUrl)) {
    if (-not (Test-Url $DashboardUrl)) {
        Write-Err "Invalid -DashboardUrl: '$DashboardUrl'"
        Write-Err "Must be a valid http:// or https:// URL."
        exit 1
    }
    $DashboardUrl = $DashboardUrl.TrimEnd('/')
    $anyChange = $true
}

if (-not [string]::IsNullOrWhiteSpace($LaserficheApiUrl)) {
    if (-not (Test-Url $LaserficheApiUrl)) {
        Write-Err "Invalid -LaserficheApiUrl: '$LaserficheApiUrl'"
        Write-Err "Must be a valid http:// or https:// URL."
        exit 1
    }
    $anyChange = $true
}

if (-not [string]::IsNullOrWhiteSpace($RepositoryId))   { $anyChange = $true }
if (-not [string]::IsNullOrWhiteSpace($DisplayName))    { $anyChange = $true }
if (-not [string]::IsNullOrWhiteSpace($ApiVersion))     { $anyChange = $true }
if ($TimeoutSeconds -gt 0)                              { $anyChange = $true }

if (-not $anyChange) {
    Write-Warn "No parameters provided.  Nothing to configure."
    Write-Host ""
    Write-Host "  Usage examples:" -ForegroundColor White
    Write-Host "    .\Configure-Dashboard.ps1 -DashboardUrl `"http://192.168.1.50:5000`"" -ForegroundColor Gray
    Write-Host "    .\Configure-Dashboard.ps1 ``" -ForegroundColor Gray
    Write-Host "        -LaserficheApiUrl `"https://lf-server/LFRepositoryAPI`" ``" -ForegroundColor Gray
    Write-Host "        -RepositoryId `"Documents`"" -ForegroundColor Gray
    Write-Host ""
    exit 0
}

# ---------- Ensure config directory exists -----------------------------------

if (-not (Test-Path $DashboardData)) {
    Write-Step "Creating config directory: $DashboardData"
    if ($PSCmdlet.ShouldProcess($DashboardData, "Create directory")) {
        New-Item -ItemType Directory -Path $DashboardData -Force | Out-Null
    }
    Write-OK "Directory created."
}

# ---------- Update extension.config.json -------------------------------------

if (-not [string]::IsNullOrWhiteSpace($DashboardUrl)) {
    Write-Step "Updating extension.config.json (Desktop Extension URL)..."

    $extConfig = Read-JsonFile $ExtConfigPath

    # Preserve existing values; update only DashboardUrl-related key.
    if (-not $extConfig.ContainsKey('portalUrl'))    { $extConfig['portalUrl']    = $DashboardUrl }
    if (-not $extConfig.ContainsKey('buttonLabel'))  { $extConfig['buttonLabel']  = 'Dashboard' }
    if (-not $extConfig.ContainsKey('iconPath'))     { $extConfig['iconPath']     = '' }

    # Always update portalUrl
    $extConfig['portalUrl'] = $DashboardUrl

    if ($PSCmdlet.ShouldProcess($ExtConfigPath, "Write extension config")) {
        Write-JsonFile $ExtConfigPath $extConfig
    }

    Write-OK "extension.config.json: portalUrl = $DashboardUrl"
}
else {
    Write-Skip "extension.config.json: -DashboardUrl not provided, skipped."
}

# ---------- Update laserfiche.config.json ------------------------------------

$lfChange = (-not [string]::IsNullOrWhiteSpace($LaserficheApiUrl)) -or
            (-not [string]::IsNullOrWhiteSpace($ApiVersion))       -or
            ($TimeoutSeconds -gt 0)

if ($lfChange) {
    Write-Step "Updating laserfiche.config.json (Laserfiche connection)..."

    # Read existing or start with defaults
    $raw = [ordered]@{}
    if (Test-Path $LFConfigPath) {
        $existing = Read-JsonFile $LFConfigPath
        foreach ($k in $existing.Keys) {
            $raw[$k] = $existing[$k]
        }
    }

    # Build Laserfiche sub-section
    if (-not $raw.ContainsKey('Laserfiche')) {
        $raw['Laserfiche'] = [ordered]@{
            'ServerUrl'          = 'https://YOUR-LF-SERVER/LFRepositoryAPI'
            'ApiBasePath'        = '/LFRepositoryAPI'
            'ApiVersion'         = 'Auto'
            'TimeoutSeconds'     = 30
            'CredentialProvider' = 'DPAPI'
        }
    }

    $lf = $raw['Laserfiche']
    # $lf is a PSCustomObject when read from JSON; convert to hashtable
    if ($lf -is [System.Management.Automation.PSCustomObject]) {
        $lfHt = [ordered]@{}
        $lf.PSObject.Properties | ForEach-Object { $lfHt[$_.Name] = $_.Value }
        $lf = $lfHt
    }

    if (-not [string]::IsNullOrWhiteSpace($LaserficheApiUrl)) {
        $lf['ServerUrl'] = $LaserficheApiUrl
    }
    # RepositoryId and DisplayName: repository is runtime session context.
    # Actively remove legacy values rather than preserving them.
    $null = $lf.Remove('RepositoryId')
    $null = $lf.Remove('DisplayName')
    if (-not [string]::IsNullOrWhiteSpace($ApiVersion)) {
        $lf['ApiVersion'] = $ApiVersion
    }
    if ($TimeoutSeconds -gt 0) {
        $lf['TimeoutSeconds'] = $TimeoutSeconds
    }
    # CredentialProvider and ApiBasePath: preserve existing or keep default
    if (-not $lf.ContainsKey('CredentialProvider')) { $lf['CredentialProvider'] = 'DPAPI' }
    if (-not $lf.ContainsKey('ApiBasePath'))        { $lf['ApiBasePath'] = '/LFRepositoryAPI' }

    $raw['Laserfiche'] = $lf

    if ($PSCmdlet.ShouldProcess($LFConfigPath, "Write Laserfiche config")) {
        Write-JsonFile $LFConfigPath $raw
    }

    if (-not [string]::IsNullOrWhiteSpace($LaserficheApiUrl)) {
        Write-OK "laserfiche.config.json: ServerUrl     = $LaserficheApiUrl"
    }
    Write-OK "laserfiche.config.json: RepositoryId/DisplayName removed (runtime session context)."
}
else {
    Write-Skip "laserfiche.config.json: no Laserfiche parameters provided, skipped."
}

# ---------- Summary ----------------------------------------------------------

Write-Host ""
Write-Host "  ====================================================" -ForegroundColor DarkGray
Write-OK "Configuration complete."
Write-Host ""
Write-Host "  NEXT STEPS:" -ForegroundColor White
Write-Host "  1. Open the Dashboard Settings page to enter Laserfiche credentials." -ForegroundColor Gray
Write-Host "     Credentials are encrypted with DPAPI -- never stored in JSON." -ForegroundColor Gray
Write-Host "  2. Restart the Dashboard IIS Application Pool to reload the new config:" -ForegroundColor Gray
Write-Host "     iisreset  (or recycle only the Dashboard pool in IIS Manager)" -ForegroundColor Gray
if (-not [string]::IsNullOrWhiteSpace($DashboardUrl)) {
    Write-Host "  3. To also configure the Web Client button, run:" -ForegroundColor Gray
    Write-Host "     .\Deploy-WebClientButton.ps1 -DashboardUrl `"$DashboardUrl`"" -ForegroundColor Gray
}
Write-Host ""
