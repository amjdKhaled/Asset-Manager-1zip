param([Parameter(Mandatory = $true)][string]$WebAppPath)
$ErrorActionPreference = 'Stop'
$cache = Join-Path $PSScriptRoot '..\.build-cache\office'
$null = New-Item -ItemType Directory -Path $cache -Force
$version = '26.2.6'
$msi = Join-Path $cache "LibreOffice_$($version)_Win_x86-64.msi"
$baseUrl = "https://download.documentfoundation.org/libreoffice/stable/$version/win/x86_64/LibreOffice_$($version)_Win_x86-64.msi"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
if (-not (Test-Path $msi)) { Invoke-WebRequest -UseBasicParsing -Uri $baseUrl -OutFile $msi }
# Verify against the publisher's checksum before executing the administrative extraction.
$checksum = (Invoke-WebRequest -UseBasicParsing -Uri "$baseUrl.sha256").Content.Trim().Split(' ')[0]
if ($checksum -notmatch '^[a-fA-F0-9]{64}$' -or (Get-FileHash $msi -Algorithm SHA256).Hash -ne $checksum) {
    throw 'LibreOffice checksum verification failed.'
}
$extract = Join-Path $cache 'administrative-image'
$marker = Join-Path $extract 'complete.txt'
if (-not (Test-Path $marker)) {
    $null = New-Item -ItemType Directory -Path $extract -Force
    $process = Start-Process msiexec.exe -Wait -PassThru -ArgumentList @('/a', "`"$msi`"", '/qn', "TARGETDIR=`"$extract`"")
    if ($process.ExitCode -notin @(0, 3010)) { throw "LibreOffice extraction failed: $($process.ExitCode)" }
    Set-Content $marker $version
}
$soffice = Get-ChildItem $extract -Filter soffice.com -Recurse | Select-Object -First 1
if (-not $soffice) { throw 'LibreOffice administrative image has no soffice.com.' }
$runtimeRoot = Split-Path (Split-Path $soffice.FullName)
$destination = Join-Path $WebAppPath 'tools\LibreOffice'
$null = New-Item -ItemType Directory -Path $destination -Force
Copy-Item (Join-Path $runtimeRoot '*') $destination -Recurse -Force
# Administrative images may place VC runtime DLLs in system directories. Keep
# those redistributable DLLs beside the private converter for clean Windows hosts.
Get-ChildItem $extract -Recurse -File | Where-Object { $_.Name -match '^(vcruntime140.*|msvcp140.*|concrt140)\.dll$' } | ForEach-Object {
    Copy-Item $_.FullName (Join-Path $destination 'program') -Force
}
if (-not (Test-Path (Join-Path $destination 'program\soffice.com'))) { throw 'Office runtime was not staged.' }
Set-Content (Join-Path $destination 'DASHBOARD-RUNTIME.txt') "LibreOffice $version. Downloaded from $baseUrl. Includes publisher license and notices."
Write-Host "Staged offline Office converter: $destination"
