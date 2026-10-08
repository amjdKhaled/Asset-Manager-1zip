#Requires -Version 5.1
# CI only: never run on a Laserfiche server.
[CmdletBinding()]
param([switch] $DisposableRunner)
$ErrorActionPreference = 'Stop'
if (-not $DisposableRunner -or $env:GITHUB_ACTIONS -ne 'true') {
    throw 'This test requires a disposable GitHub Actions runner.'
}
$repo = Split-Path -Parent $PSScriptRoot
$msis = @(Get-ChildItem -LiteralPath (Join-Path $repo 'artifacts') -Filter '*-Setup.msi')
if ($msis.Count -ne 1) { throw 'Expected one freshly built MSI.' }
$msi = $msis[0].FullName
$root = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'Dashboard'
$data = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'Dashboard'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('DashboardMsiIsolation-' + [Guid]::NewGuid().ToString('N'))
$outside = Join-Path $fixture 'Laserfiche'
New-Item -ItemType Directory -Path $outside -Force | Out-Null
$sentinel = Join-Path $outside 'repository-volume.txt'
[IO.File]::WriteAllText($sentinel, 'Laserfiche repository sentinel - preserve')
$sentinelHash = (Get-FileHash -LiteralPath $sentinel -Algorithm SHA256).Hash
function Require-Sentinel {
    if ((Get-FileHash -LiteralPath $sentinel -Algorithm SHA256).Hash -ne $sentinelHash) { throw 'Laserfiche sentinel changed.' }
}
function Run-Msi([string[]] $Arguments, [bool] $ExpectSuccess = $true, [string] $ExpectedError = '') {
    $process = Start-Process -FilePath 'msiexec.exe' -ArgumentList $Arguments -PassThru
    if (-not $process.WaitForExit(180000)) { throw 'MSI operation exceeded its timeout.' }
    if ($ExpectSuccess -and $process.ExitCode -notin @(0, 3010)) {
        throw "MSI failed: $($process.ExitCode). See artifacts/msi-*.log."
    }
    if (-not $ExpectSuccess -and $process.ExitCode -in @(0, 3010)) { throw 'Unsafe MSI operation was accepted.' }
    if ($ExpectedError -and (Get-Content -LiteralPath $log -Raw) -notmatch $ExpectedError) {
        throw 'The refused MSI did not report the expected isolation reason.'
    }
    Require-Sentinel
}
# This fixture tests filesystem/IIS isolation, not whether the web application
# serves requests. ANCM presence is supplied explicitly for that reason.
Install-WindowsFeature Web-Server, Web-Mgmt-Tools | Out-Null
$appcmd = Join-Path $env:SystemRoot 'System32\inetsrv\appcmd.exe'
& $appcmd add apppool /name:LaserficheSentinel
if ($LASTEXITCODE -ne 0) { throw 'Could not create unrelated pool.' }
& $appcmd add site /name:LaserficheSentinel /bindings:http/*:54321: /physicalPath:$outside
if ($LASTEXITCODE -ne 0) { throw 'Could not create unrelated site.' }
& $appcmd set app 'LaserficheSentinel/' /applicationPool:LaserficheSentinel
if ($LASTEXITCODE -ne 0) { throw 'Could not configure unrelated site.' }

& $appcmd add site /name:Dashboard /bindings:http/*:54322: /physicalPath:$outside
if ($LASTEXITCODE -ne 0) { throw 'Could not create colliding site.' }
$log = Join-Path $repo 'artifacts\msi-collision.log'
Run-Msi @('/i', ('"' + $msi + '"'), '/qn', '/norestart', 'ANCMV2PRESENT=1',
    'INSTALL_DESKTOP_BUTTON=0', '/L*v', ('"' + $log + '"')) $false 'existing IIS site named Dashboard'
& $appcmd delete site Dashboard
if ($LASTEXITCODE -ne 0) { throw 'Could not release colliding site.' }

$log = Join-Path $repo 'artifacts\msi-install.log'
Run-Msi @('/i', ('"' + $msi + '"'), '/qn', '/norestart', 'ANCMV2PRESENT=1',
    'INSTALL_DESKTOP_BUTTON=0', 'DASHBOARD_PORT=54323', '/L*v', ('"' + $log + '"'))
$unknown = Join-Path $root 'WebApp\unrelated-document.txt'
[IO.File]::WriteAllText($unknown, 'unrelated application-folder sentinel')
$unknownData = Join-Path $data 'unrelated-document.txt'
[IO.File]::WriteAllText($unknownData, 'unrelated data-folder sentinel')
$log = Join-Path $repo 'artifacts\msi-repair.log'
Run-Msi @('/fa', ('"' + $msi + '"'), '/qn', '/norestart', 'ANCMV2PRESENT=1',
    'INSTALL_DESKTOP_BUTTON=0', '/L*v', ('"' + $log + '"'))
if ([IO.File]::ReadAllText($unknown) -ne 'unrelated application-folder sentinel') { throw 'Repair changed an unknown file.' }
$binding = & $appcmd list site Dashboard /text:bindings
if ($LASTEXITCODE -ne 0 -or "$binding" -notmatch ':54323:') { throw 'Repair did not preserve the selected Dashboard port.' }

& $appcmd set app 'LaserficheSentinel/' /applicationPool:Dashboard
if ($LASTEXITCODE -ne 0) { throw 'Could not create shared-pool fixture.' }
$log = Join-Path $repo 'artifacts\msi-shared-pool.log'
Run-Msi @('/x', ('"' + $msi + '"'), '/qn', '/norestart', '/L*v', ('"' + $log + '"')) $false 'app pool is used by another IIS site'
if (-not (Test-Path -LiteralPath (Join-Path $root 'WebApp\LFPortal.Web.dll'))) { throw 'Refused removal deleted the application.' }
& $appcmd set app 'LaserficheSentinel/' /applicationPool:LaserficheSentinel
if ($LASTEXITCODE -ne 0) { throw 'Could not release shared-pool fixture.' }

$log = Join-Path $repo 'artifacts\msi-uninstall.log'
Run-Msi @('/x', ('"' + $msi + '"'), '/qn', '/norestart', 'REMOVE_USER_DATA=1', '/L*v', ('"' + $log + '"'))
if ([IO.File]::ReadAllText($unknown) -ne 'unrelated application-folder sentinel') { throw 'Uninstall changed an unknown application file.' }
if ([IO.File]::ReadAllText($unknownData) -ne 'unrelated data-folder sentinel') { throw 'Cleanup changed an unknown data file.' }
& $appcmd list site LaserficheSentinel
if ($LASTEXITCODE -ne 0) { throw 'Uninstall removed another IIS site.' }
& $appcmd list apppool LaserficheSentinel
if ($LASTEXITCODE -ne 0) { throw 'Uninstall removed another IIS pool.' }
Require-Sentinel
Write-Host 'MSI install/repair/refusal/uninstall isolation checks passed.'

