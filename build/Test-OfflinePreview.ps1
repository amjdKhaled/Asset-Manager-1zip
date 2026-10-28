$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
# Exercise the exact downloadable ZIP, not the pre-packaging staging folder.
$zip = Join-Path $repo 'Release\LFDashboard-Standalone-win-x64.zip'
if (-not (Test-Path $zip)) { throw 'Standalone ZIP was not produced.' }
$checksums = Get-Content (Join-Path $repo 'Release\SHA256SUMS.txt')
$checksumLine = @($checksums | Where-Object { $_ -match '^[a-fA-F0-9]{64}\s+LFDashboard-Standalone-win-x64\.zip$' })
if ($checksumLine.Count -ne 1 -or (Get-FileHash $zip -Algorithm SHA256).Hash -ne ($checksumLine[0] -split '\s+')[0]) {
    throw 'Standalone ZIP checksum does not match the published checksum.'
}
$web = Join-Path $repo 'artifacts\offline-zip-test'
if (Test-Path $web) { Remove-Item $web -Recurse -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $web)
foreach ($required in @('Start-Dashboard.cmd', 'LFPortal.Web.exe', 'coreclr.dll', 'README-OFFLINE.md')) {
    if (-not (Test-Path (Join-Path $web $required))) { throw "Standalone ZIP is missing $required" }
}
$office = Join-Path $web 'tools\LibreOffice\program\soffice.com'
if (-not (Test-Path $office)) { throw 'Downloaded distribution has no Office converter.' }
# Disposable Windows CI runner: prove conversions do not need external services.
$firewallNames = @()
try {
    foreach ($file in @($office, (Join-Path (Split-Path $office) 'soffice.exe'), (Join-Path (Split-Path $office) 'soffice.bin'), (Join-Path $web 'LFPortal.Web.exe'))) {
        if (Test-Path $file) {
            $name = 'DashboardOfflineTest-' + [guid]::NewGuid().ToString('N')
            New-NetFirewallRule -Name $name -DisplayName $name -Direction Outbound -Program $file -Action Block -RemoteAddress Any | Out-Null
            $firewallNames += $name
        }
    }
$env:DASHBOARD_OFFICE_TEST_PATH = $office
try {
    dotnet test (Join-Path $repo 'src\LFPortal.Infrastructure.Tests\LFPortal.Infrastructure.Tests.csproj') --configuration Release --filter 'FullyQualifiedName~BundledOffice'
    if ($LASTEXITCODE -ne 0) { throw 'Bundled Office conversion integration tests failed.' }
}
finally { Remove-Item Env:DASHBOARD_OFFICE_TEST_PATH -ErrorAction SilentlyContinue }

$port = 15983
$process = Start-Process (Join-Path $web 'LFPortal.Web.exe') -ArgumentList @('--urls', "http://127.0.0.1:$port") -WorkingDirectory $web -PassThru
try {
    $ready = $false
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        try {
            $response = Invoke-WebRequest "http://127.0.0.1:$port/Login" -UseBasicParsing -TimeoutSec 3
            if ($response.StatusCode -eq 200) { $ready = $true; break }
        } catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $ready) { throw 'Standalone executable did not start without dotnet run.' }
    foreach ($asset in @('/lib/chartjs/chart.umd.min.js', '/js/document-preview.js', '/fonts/IBMPlexSansArabic-Regular.ttf')) {
        $response = Invoke-WebRequest "http://127.0.0.1:$port$asset" -UseBasicParsing
        if ($response.StatusCode -ne 200 -or $response.RawContentLength -eq 0) { throw "Missing offline asset: $asset" }
    }
}
finally { if (-not $process.HasExited) { Stop-Process $process.Id -Force } }
Write-Host 'Final ZIP checksum, extraction, bundled Office conversion, standalone startup and local assets passed.'

}
finally {
    foreach ($name in $firewallNames) { Remove-NetFirewallRule -Name $name -ErrorAction SilentlyContinue }
}
