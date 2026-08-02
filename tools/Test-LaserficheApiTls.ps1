<#
.SYNOPSIS
    Diagnoses TLS/certificate problems between the Dashboard and the
    Laserfiche Repository API (LFRepositoryAPI) hosted in IIS.

.DESCRIPTION
    Run as Administrator on the machine hosting both the Dashboard and
    LFRepositoryAPI.  Windows PowerShell 5.1 compatible.

    Performs, in order:
      1. Locates the IIS site/application hosting /LFRepositoryAPI.
      2. Inspects the HTTPS binding and its certificate (subject, issuer,
         SAN DNS names, validity dates, private key, store).
      3. Validates the certificate chain from LocalMachine context.
      4. Tests hostname identity for BOTH 'localhost' and the machine name.
      5. Performs real TLS handshakes (certificate validation NOT disabled)
         against both host names and reports PASS/FAIL for each.
      6. Prints the recommended ServerUrl for laserfiche.config.json.

    It NEVER disables certificate validation for the reachability verdict;
    the inspection callback only records what the OS reported.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Test-LaserficheApiTls.ps1
#>

[CmdletBinding()]
param(
    [string]$ApiPath = "/LFRepositoryAPI"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Continue"

function Write-Section([string]$t) {
    Write-Host ""
    Write-Host ("=" * 60)
    Write-Host $t
    Write-Host ("=" * 60)
}

$machineName = $env:COMPUTERNAME
$hostsToTest = @("localhost", $machineName)

# ------------------------------------------------------------------
# 1. Locate the IIS application hosting /LFRepositoryAPI
# ------------------------------------------------------------------
Write-Section "1. IIS application hosting $ApiPath"

Import-Module WebAdministration -ErrorAction SilentlyContinue

$siteName = $null
$app = Get-WebApplication -ErrorAction SilentlyContinue |
       Where-Object { $_.Path -ieq $ApiPath }
if ($app) {
    # GetParentElement is unreliable across versions; parse the site from ItemXPath.
    if ($app.ItemXPath -match "@name='([^']+)'") { $siteName = $Matches[1] }
    Write-Host "IIS application : $($app.Path)"
    Write-Host "Site            : $siteName"
    Write-Host "Physical path   : $([Environment]::ExpandEnvironmentVariables($app.PhysicalPath))"
    Write-Host "App pool        : $($app.ApplicationPool)"
} else {
    Write-Host "WARN: no IIS application with path $ApiPath found via Get-WebApplication."
    $siteName = "Default Web Site"
    Write-Host "Assuming site: $siteName"
}

# ------------------------------------------------------------------
# 2. HTTPS binding and certificate
# ------------------------------------------------------------------
Write-Section "2. HTTPS binding and certificate for site '$siteName'"

$cert = $null
$bindings = Get-WebBinding -Name $siteName -Protocol https -ErrorAction SilentlyContinue
if (-not $bindings) {
    Write-Host "FAIL: site '$siteName' has NO HTTPS binding."
} else {
    foreach ($b in $bindings) {
        Write-Host "Binding         : $($b.bindingInformation) (protocol: $($b.protocol))"
        $thumb = $null
        if ($b.PSObject.Properties.Name -contains "certificateHash" -and $b.certificateHash) {
            # certificateHash may be a byte[] or a hex string depending on version.
            if ($b.certificateHash -is [byte[]]) {
                $thumb = ($b.certificateHash | ForEach-Object { $_.ToString("X2") }) -join ""
            } elseif ($b.certificateHash -is [string] -and $b.certificateHash -match '^[0-9a-fA-F]{20,}$') {
                $thumb = $b.certificateHash.ToUpper()
            }
        }
        # Capture the binding port for later live tests.
        $bindingPort = ($b.bindingInformation -split ":")[1]
        if ($bindingPort -and $bindingPort -match '^\d+$') { $script:httpsPort = [int]$bindingPort }
        if (-not $thumb) {
            # Fall back to netsh for the binding's cert hash
            $port = $bindingPort
            $netsh = netsh http show sslcert 2>$null | Out-String
            $blocks = $netsh -split "(?m)^\s*$"
            foreach ($blk in $blocks) {
                if ($blk -match ":$port" -and $blk -match "Certificate Hash\s*:\s*([0-9a-fA-F]+)") {
                    $thumb = $Matches[1].ToUpper(); break
                }
            }
        }
        if ($thumb) {
            Write-Host "Cert thumbprint : $thumb"
            $cert = Get-ChildItem "Cert:\LocalMachine\My" -ErrorAction SilentlyContinue |
                    Where-Object { $_.Thumbprint -ieq $thumb } | Select-Object -First 1
            $store = "LocalMachine\My"
            if (-not $cert) {
                $cert = Get-ChildItem "Cert:\LocalMachine\WebHosting" -ErrorAction SilentlyContinue |
                        Where-Object { $_.Thumbprint -ieq $thumb } | Select-Object -First 1
                $store = "LocalMachine\WebHosting"
            }
            if ($cert) {
                Write-Host "Store           : $store"
                Write-Host "Subject         : $($cert.Subject)"
                Write-Host "Issuer          : $($cert.Issuer)"
                Write-Host "NotBefore       : $($cert.NotBefore)"
                Write-Host "NotAfter        : $($cert.NotAfter)"
                Write-Host "HasPrivateKey   : $($cert.HasPrivateKey)"
                $sanExt = $cert.Extensions | Where-Object { $_.Oid.Value -eq "2.5.29.17" }
                if ($sanExt) {
                    $sanText = $sanExt.Format($true) -replace "`r`n", "; "
                    Write-Host "SAN             : $sanText"
                } else {
                    Write-Host "SAN             : (none - identity comes from CN only)"
                }
            } else {
                Write-Host "WARN: certificate $thumb not found in LocalMachine stores."
            }
        } else {
            Write-Host "WARN: could not determine the certificate for this binding."
        }
    }
}

# ------------------------------------------------------------------
# 3. Chain validation (LocalMachine context)
# ------------------------------------------------------------------
Write-Section "3. Certificate chain validation"

if ($cert) {
    $chain = New-Object System.Security.Cryptography.X509Certificates.X509Chain
    $chain.ChainPolicy.RevocationMode = [System.Security.Cryptography.X509Certificates.X509RevocationMode]::NoCheck
    $chainOk = $chain.Build($cert)
    Write-Host "Chain builds    : $chainOk"
    if (-not $chainOk) {
        foreach ($st in $chain.ChainStatus) {
            Write-Host "  Chain problem : $($st.Status) - $($st.StatusInformation.Trim())"
        }
    }
    $selfSigned = ($cert.Subject -eq $cert.Issuer)
    Write-Host "Self-signed     : $selfSigned"
} else {
    Write-Host "SKIP: no certificate resolved in step 2."
}

# ------------------------------------------------------------------
# 4. Hostname identity match
# ------------------------------------------------------------------
Write-Section "4. Hostname identity match"

function Get-CertDnsNames([System.Security.Cryptography.X509Certificates.X509Certificate2]$c) {
    $names = @()
    $sanExt = $c.Extensions | Where-Object { $_.Oid.Value -eq "2.5.29.17" }
    if ($sanExt) {
        foreach ($line in ($sanExt.Format($true) -split "`r?`n")) {
            if ($line -match "DNS Name=(.+)$") { $names += $Matches[1].Trim() }
        }
    }
    if ($names.Count -eq 0 -and $c.Subject -match "CN=([^,]+)") { $names += $Matches[1].Trim() }
    return $names
}

if ($cert) {
    $dnsNames = Get-CertDnsNames $cert
    Write-Host "Certificate identities: $($dnsNames -join ', ')"
    foreach ($h in $hostsToTest) {
        $match = $false
        foreach ($n in $dnsNames) {
            if ($n -ieq $h) { $match = $true }
            elseif ($n.StartsWith("*.")) {
                $suffix = $n.Substring(1)   # ".domain.tld"
                if ($h -like "*$suffix" -and $h -notlike "*.*$suffix") { $match = $true }
            }
        }
        Write-Host ("Matches '{0}' : {1}" -f $h, $(if ($match) { "YES" } else { "NO" }))
    }
} else {
    Write-Host "SKIP: no certificate resolved in step 2."
}

# ------------------------------------------------------------------
# 5. Real TLS handshakes (validation NOT disabled)
# ------------------------------------------------------------------
Write-Section "5. Live TLS handshake + HTTPS request per host"

# Force modern TLS for the probe itself (PS 5.1 defaults can be TLS 1.0).
# Tls13 is not defined on older .NET Framework builds - feature-detect it.
$protocols = [Net.SecurityProtocolType]::Tls12
if ([Enum]::GetNames([Net.SecurityProtocolType]) -contains "Tls13") {
    $protocols = $protocols -bor [Net.SecurityProtocolType]::Tls13
}
[Net.ServicePointManager]::SecurityProtocol = $protocols

# Use the actual HTTPS binding port discovered in step 2 (default 443).
if (-not (Test-Path variable:script:httpsPort) -or -not $script:httpsPort) { $script:httpsPort = 443 }
$portSuffix = if ($script:httpsPort -ne 443) { ":$($script:httpsPort)" } else { "" }

$results = @{}
foreach ($h in $hostsToTest) {
    $url = "https://$h$portSuffix$ApiPath"
    Write-Host ""
    Write-Host "--- $url ---"

    # 5a. Raw handshake with detailed policy errors (recorded, not bypassed).
    $policyErrors = "unknown"
    try {
        $tcp = New-Object System.Net.Sockets.TcpClient($h, $script:httpsPort)
        $ssl = New-Object System.Net.Security.SslStream(
            $tcp.GetStream(), $false,
            { param($s, $c, $ch, $errs) $script:capturedErrors = $errs; return $true })
        $script:capturedErrors = $null
        $ssl.AuthenticateAsClient($h)
        $policyErrors = if ($script:capturedErrors) { $script:capturedErrors.ToString() } else { "None" }
        Write-Host "Handshake       : completed (protocol: $($ssl.SslProtocol))"
        Write-Host "Policy errors   : $policyErrors"
        $remote = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($ssl.RemoteCertificate)
        Write-Host "Presented cert  : $($remote.Subject) (thumbprint $($remote.Thumbprint))"
        $ssl.Dispose(); $tcp.Close()
    } catch {
        Write-Host "Handshake       : FAILED - $($_.Exception.Message)"
    }

    # 5b. Real request with FULL validation (what the Dashboard actually does).
    try {
        $resp = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 15 -ErrorAction Stop
        Write-Host "HTTPS request   : PASS (HTTP $($resp.StatusCode))"
        $results[$h] = $true
    } catch [System.Net.WebException] {
        $we = $_.Exception
        if ($we.Response) {
            $code = [int]$we.Response.StatusCode
            Write-Host "HTTPS request   : PASS at TLS level (HTTP $code returned)"
            $results[$h] = $true
        } elseif ($we.Status -eq [System.Net.WebExceptionStatus]::TrustFailure) {
            Write-Host "HTTPS request   : FAIL - certificate trust/validation error"
            Write-Host "                  $($we.Message)"
            $results[$h] = $false
        } else {
            Write-Host "HTTPS request   : FAIL - $($we.Status): $($we.Message)"
            $results[$h] = $false
        }
    } catch {
        Write-Host "HTTPS request   : FAIL - $($_.Exception.Message)"
        $results[$h] = $false
    }
}

# ------------------------------------------------------------------
# 6. Verdict
# ------------------------------------------------------------------
Write-Section "6. Verdict"

$valid = @($hostsToTest | Where-Object { $results[$_] })
if ($valid.Count -eq 0) {
    Write-Host "FAIL: neither host name passed full TLS validation."
    Write-Host "The certificate itself (or its chain/trust) must be fixed:"
    Write-Host "  - If self-signed: create a proper cert with 'localhost' and"
    Write-Host "    '$machineName' in the SAN, install it in LocalMachine\My,"
    Write-Host "    bind it in IIS, and put the issuing cert in LocalMachine\Root."
    Write-Host "  - If chain problem: install the missing intermediate/root CA"
    Write-Host "    into the LocalMachine (NOT CurrentUser) store."
} else {
    $best = $valid[0]
    Write-Host "PASS: valid TLS host name(s): $($valid -join ', ')"
    Write-Host ""
    Write-Host "Recommended ServerUrl for laserfiche.config.json:"
    Write-Host "  https://$best$portSuffix$ApiPath"
    Write-Host ""
    Write-Host "Update it with (run as Administrator):"
    Write-Host "  1. Edit C:\ProgramData\Dashboard\laserfiche.config.json"
    Write-Host "  2. Set Laserfiche.ServerUrl to the URL above"
    Write-Host "  3. Restart the Dashboard IIS site: iisreset /restart (or recycle the app pool)"
}

Write-Host ""
Write-Host "Note: the Dashboard runs under the IIS app pool identity and uses"
Write-Host "LocalMachine certificate trust. A cert that 'works in the browser'"
Write-Host "but fails here is typically trusted only in CurrentUser stores or"
Write-Host "fails hostname matching for the configured ServerUrl."
