<#
.SYNOPSIS
    READ-ONLY diagnostic snapshot for comparing a working vs broken Laserfiche
    API Server machine after a Dashboard installation.

.DESCRIPTION
    Collects (NO changes are made to the machine):
      - Installed .NET / ASP.NET Core runtimes
      - IIS sites, bindings, applications, app pools
      - LFRepositoryAPI app pool + application details
      - Relevant applicationHost.config sections (sites, applicationPools,
        environmentVariables) as raw XML excerpts
      - LFRepositoryAPI environment variables (pool-level + machine/user
        ASPNETCORE_/DOTNET_ variables)
      - All LFRepositoryAPI appsettings*.json paths, timestamps, and their
        EFFECTIVE relevant values (EnableGetRepositoryListApi,
        EnableLaserficheServerSSL, LaserficheServerName, ...)
      - HTTP.SYS SSL certificate bindings (netsh http show sslcert)
      - LocalMachine\My and LocalMachine\Root certificates (relevant subset)
      - LFRepositoryAPI file timestamps
      - Dashboard installation footprint + timestamps

    Run identically on both machines, then diff the two output files:
        powershell -ExecutionPolicy Bypass -File .\Get-DiagnosticSnapshot.ps1
        # produces DiagnosticSnapshot-<COMPUTERNAME>-<timestamp>.txt

.NOTES
    Every operation below is read-only: Get-*, show, list, ReadAllText.
    Requires elevation to read applicationHost.config and HKLM.
#>

[CmdletBinding()]
param(
    [string]$OutFile = ".\DiagnosticSnapshot-$env:COMPUTERNAME-$(Get-Date -Format 'yyyyMMdd-HHmmss').txt"
)

$ErrorActionPreference = 'Continue'
$sb = [System.Text.StringBuilder]::new()

function Section([string]$title) {
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine(('=' * 78))
    [void]$sb.AppendLine("== $title")
    [void]$sb.AppendLine(('=' * 78))
}
function Emit($obj) {
    if ($null -eq $obj) { [void]$sb.AppendLine("(none)"); return }
    [void]$sb.AppendLine(($obj | Out-String).TrimEnd())
}
function TryRun([string]$label, [scriptblock]$block) {
    [void]$sb.AppendLine("--- $label ---")
    try { Emit (& $block) }
    catch { [void]$sb.AppendLine("ERROR: $($_.Exception.Message)") }
    [void]$sb.AppendLine("")
}

[void]$sb.AppendLine("Diagnostic snapshot (READ-ONLY)")
[void]$sb.AppendLine("Machine : $env:COMPUTERNAME")
[void]$sb.AppendLine("User    : $env:USERNAME (admin: $(([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole('Administrators')))")
[void]$sb.AppendLine("Time    : $(Get-Date -Format 'o')")
[void]$sb.AppendLine("OS      : $([System.Environment]::OSVersion.VersionString)")

# ---------------------------------------------------------------- .NET runtimes
Section ".NET / ASP.NET Core runtimes"
TryRun "dotnet --list-runtimes" { & dotnet --list-runtimes 2>&1 }
TryRun "dotnet --list-sdks"     { & dotnet --list-sdks 2>&1 }
TryRun "ANCM modules present"   {
    Get-ChildItem "$env:ProgramFiles\IIS\Asp.Net Core Module\V2" -Recurse -Filter aspnetcorev2.dll -ErrorAction SilentlyContinue |
        Select-Object FullName, LastWriteTime,
            @{n='Version';e={ $_.VersionInfo.FileVersion }}
}

# ---------------------------------------------------------------- IIS inventory
Section "IIS sites / bindings / applications / app pools"
$appcmd = Join-Path $env:windir "System32\inetsrv\appcmd.exe"
if (Test-Path $appcmd) {
    TryRun "appcmd list sites"    { & $appcmd list sites }
    TryRun "appcmd list apps"     { & $appcmd list apps }
    TryRun "appcmd list vdirs"    { & $appcmd list vdirs }
    TryRun "appcmd list apppools" { & $appcmd list apppools }
    TryRun "LFRepositoryAPI app detail" {
        & $appcmd list app /path:"/LFRepositoryAPI" /text:*
    }
    TryRun "LFRepositoryAPI app pool detail" {
        $app = (& $appcmd list app /path:"/LFRepositoryAPI" /text:APPPOOL.NAME) | Select-Object -First 1
        if ($app) { & $appcmd list apppool "$app" /text:* } else { "LFRepositoryAPI application not found" }
    }
    TryRun "Dashboard site detail (if present)"    { & $appcmd list site "Dashboard" /text:* }
    TryRun "Dashboard apppool detail (if present)" { & $appcmd list apppool "Dashboard" /text:* }
} else {
    [void]$sb.AppendLine("appcmd.exe not found - IIS may not be installed.")
}

# ------------------------------------------------- applicationHost.config excerpts
Section "applicationHost.config relevant sections (raw excerpts)"
$ahc = Join-Path $env:windir "System32\inetsrv\config\applicationHost.config"
TryRun "file timestamp" { Get-Item $ahc | Select-Object FullName, LastWriteTime, Length }
TryRun "sites + applicationPools + environmentVariables sections" {
    if (Test-Path $ahc) {
        [xml]$x = Get-Content $ahc -Raw
        $out = @()
        $out += "<< system.applicationHost/sites >>"
        $out += $x.configuration.'system.applicationHost'.sites.OuterXml
        $out += "<< system.applicationHost/applicationPools >>"
        $out += $x.configuration.'system.applicationHost'.applicationPools.OuterXml
        $envNodes = $x.SelectNodes("//environmentVariables")
        $out += "<< all environmentVariables nodes ($($envNodes.Count)) >>"
        foreach ($n in $envNodes) { $out += $n.OuterXml }
        $out -join "`r`n"
    } else { "not found" }
}

# ------------------------------------------------- environment variables
Section "Environment variables (machine + user, ASPNETCORE_/DOTNET_/LF-relevant)"
TryRun "Machine scope" {
    [System.Environment]::GetEnvironmentVariables('Machine').GetEnumerator() |
        Where-Object { $_.Key -match '^(ASPNETCORE_|DOTNET_|LF|Laserfiche)' } | Sort-Object Key
}
TryRun "User scope" {
    [System.Environment]::GetEnvironmentVariables('User').GetEnumerator() |
        Where-Object { $_.Key -match '^(ASPNETCORE_|DOTNET_|LF|Laserfiche)' } | Sort-Object Key
}

# ------------------------------------------------- LFRepositoryAPI configuration
Section "LFRepositoryAPI configuration files + effective values"
$lfConfigRoots = @(
    "$env:ProgramData\Laserfiche\API Server\LFRepositoryAPI",
    "$env:ProgramFiles\Laserfiche\API Server\LFRepositoryAPI"
)
$relevantKeys = @(
    'EnableGetRepositoryListApi', 'EnableLaserficheServerSSL', 'LaserficheServerName',
    'LFDSSTSBaseUrl', 'WhitelistedRedirectUris', 'KeyedConcurrentLicense',
    'LaserficheWebClientHostUrl', 'AccessTokenExpirationLimit', 'SessionIdleTimeout'
)
foreach ($root in $lfConfigRoots) {
    TryRun "appsettings*.json under $root" {
        if (-not (Test-Path $root)) { return "path not found" }
        $files = Get-ChildItem $root -Filter "appsettings*.json" -Recurse -ErrorAction SilentlyContinue
        if (-not $files) { return "no appsettings*.json found" }
        $out = @()
        foreach ($f in $files) {
            $out += "FILE: $($f.FullName)  (LastWriteTime: $($f.LastWriteTime.ToString('o')), Size: $($f.Length))"
            try {
                $json = Get-Content $f.FullName -Raw | ConvertFrom-Json
                foreach ($k in $relevantKeys) {
                    $v = $json.PSObject.Properties[$k]
                    if ($v) { $out += "    $k = $($v.Value)" }
                }
                if (-not ($relevantKeys | Where-Object { $json.PSObject.Properties[$_] })) {
                    $out += "    (no relevant top-level keys present)"
                }
            } catch { $out += "    PARSE ERROR: $($_.Exception.Message)" }
        }
        $out -join "`r`n"
    }
}
TryRun "LFRepositoryAPI install-dir file timestamps (top 2 levels)" {
    $dir = "$env:ProgramFiles\Laserfiche\API Server\LFRepositoryAPI"
    if (Test-Path $dir) {
        Get-ChildItem $dir -Recurse -Depth 1 -ErrorAction SilentlyContinue |
            Select-Object FullName, LastWriteTime, Length | Sort-Object FullName
    } else { "not found" }
}
TryRun "LFRepositoryAPI web.config timestamps/content-hash" {
    foreach ($root in $lfConfigRoots) {
        Get-ChildItem $root -Filter web.config -Recurse -ErrorAction SilentlyContinue |
            Select-Object FullName, LastWriteTime,
                @{n='SHA256';e={ (Get-FileHash $_.FullName -Algorithm SHA256).Hash }}
    }
}

# ------------------------------------------------- HTTP.SYS / certificates
Section "HTTP.SYS SSL bindings + certificate stores"
TryRun "netsh http show sslcert" { & netsh http show sslcert }
TryRun "netsh http show urlacl (LF/Dashboard relevant)" {
    (& netsh http show urlacl) | Select-String -Context 0,3 -Pattern 'LF|Laserfiche|Dashboard|:5000'
}
TryRun "LocalMachine\My certificates" {
    Get-ChildItem Cert:\LocalMachine\My |
        Select-Object Subject, Issuer, Thumbprint, NotBefore, NotAfter, HasPrivateKey |
        Sort-Object Subject
}
TryRun "LocalMachine\Root certificates (non-standard/self-signed focus: Subject==Issuer, recent)" {
    Get-ChildItem Cert:\LocalMachine\Root |
        Select-Object Subject, Issuer, Thumbprint, NotBefore, NotAfter |
        Sort-Object NotBefore -Descending
}

# ------------------------------------------------- Dashboard footprint
Section "Dashboard installation footprint + timestamps"
TryRun "Dashboard install folder" {
    $d = "$env:ProgramFiles\Dashboard"
    if (Test-Path $d) {
        Get-ChildItem $d -Recurse -Depth 1 -ErrorAction SilentlyContinue |
            Select-Object FullName, LastWriteTime | Sort-Object FullName
    } else { "not found" }
}
TryRun "%ProgramData%\Dashboard contents" {
    $d = "$env:ProgramData\Dashboard"
    if (Test-Path $d) {
        Get-ChildItem $d -Recurse -ErrorAction SilentlyContinue |
            Select-Object FullName, LastWriteTime, Length | Sort-Object FullName
    } else { "not found" }
}
TryRun "Dashboard MSI registration (install date)" {
    Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*" -ErrorAction SilentlyContinue |
        Where-Object { $_.DisplayName -match 'Dashboard' } |
        Select-Object DisplayName, DisplayVersion, InstallDate, InstallLocation
}
TryRun "Dashboard setup logs" {
    Get-ChildItem "$env:ProgramData\Dashboard\logs" -ErrorAction SilentlyContinue |
        Select-Object FullName, LastWriteTime, Length
}
TryRun "Web Client Browse.aspx state (script-tag + backups)" {
    $candidates = @(
        "$env:ProgramFiles\Laserfiche\Web Access\Web Files",
        "$env:ProgramFiles\Laserfiche\Web Access"
    )
    $out = @()
    foreach ($c in $candidates) {
        $b = Join-Path $c "Browse.aspx"
        if (Test-Path $b) {
            $tagCount = (Select-String -Path $b -Pattern 'lf-dashboard-button' -AllMatches -ErrorAction SilentlyContinue |
                         ForEach-Object { $_.Matches.Count } | Measure-Object -Sum).Sum
            $out += "$b  LastWriteTime=$((Get-Item $b).LastWriteTime.ToString('o'))  dashboard-tag-count=$([int]$tagCount)"
            Get-ChildItem $c -Filter "Browse.aspx.bak-*" -ErrorAction SilentlyContinue |
                ForEach-Object { $out += "  backup: $($_.FullName)  $($_.LastWriteTime.ToString('o'))" }
        }
    }
    if ($out) { $out -join "`r`n" } else { "Browse.aspx not found at default paths" }
}

# ------------------------------------------------- write output
$sb.ToString() | Set-Content -Path $OutFile -Encoding UTF8
Write-Host "Snapshot written to: $OutFile"
Write-Host "This script made NO changes to the machine."
