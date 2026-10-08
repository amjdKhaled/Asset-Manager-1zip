#Requires -Version 5.1
<#
.SYNOPSIS
Deploys the Dashboard Web Client button through the transaction-safe SetupHelper.
.PARAMETER TransactionId
Required for rollback/commit. Never guesses or restores unrelated Browse.aspx backups.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $DashboardUrl = '',
    [string] $WebClientPath = '',
    [string] $DashboardScriptSource = '',
    [switch] $Rollback,
    [switch] $Commit,
    [string] $TransactionId = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
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


if ($Rollback -and $Commit) { throw 'Choose rollback or commit.' }
if (-not $WebClientPath -and -not $Commit) { $WebClientPath = Find-LaserficheWebClientPath }
if (-not $WebClientPath -and -not $Commit) { throw 'Specify the Laserfiche Web Client path.' }
$installedHelper = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'Dashboard\Extension\Dashboard.SetupHelper.exe'
$stagedHelper = Join-Path $PSScriptRoot '..\artifacts\staging\Extension\Dashboard.SetupHelper.exe'
$helper = @($installedHelper, $stagedHelper) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $helper) { throw 'Install or build the current Dashboard SetupHelper first.' }
# Refuse a legacy helper which lacks transaction-safe deployment/commit support.
$helpText = & $helper --help
if ($LASTEXITCODE -ne 0 -or ($helpText -join "`n") -notmatch '--commit-webclient') {
    throw 'The SetupHelper is outdated. Update Dashboard before using this script.'
}
if ($Rollback -or $Commit) {
    $parsed = [Guid]::Empty
    if (-not [Guid]::TryParseExact($TransactionId, 'N', [ref]$parsed)) { throw 'Supply the exact deployment TransactionId.' }
} else {
    $TransactionId = [Guid]::NewGuid().ToString('N')
}
$verb = if ($Rollback) { '--rollback-webclient' } elseif ($Commit) { '--commit-webclient' } else { '--deploy-webclient' }
$argsForHelper = @($verb, '--transaction', $TransactionId)
if (-not $Commit) { $argsForHelper += @('--path', $WebClientPath) }
if (-not $Rollback -and -not $Commit) {
    $argsForHelper += @('--url', $DashboardUrl)
    if ($DashboardScriptSource) { $argsForHelper += @('--source-js', $DashboardScriptSource) }
}
if ($PSCmdlet.ShouldProcess($WebClientPath, $verb)) {
    & $helper @argsForHelper
    if ($LASTEXITCODE -ne 0) { throw "SetupHelper failed with $LASTEXITCODE. TransactionId: $TransactionId" }
    Write-Host "TransactionId: $TransactionId"
    if (-not $Rollback -and -not $Commit) {
        Write-Host 'Retain this ID for rollback, or use -Commit -TransactionId to discard this transaction journal.'
    }
}
