#Requires -Version 5.1
<#
.SYNOPSIS
Updates the installed Dashboard without deleting destination files.
.PARAMETER PublishOutputPath
Required with SkipPublish. With a fresh publish, an isolated temporary directory is used.
#>
[CmdletBinding()]
param(
    [string] $WebAppPath = '',
    [string] $AppPoolName = 'Dashboard',
    [string] $SiteName = 'Dashboard',
    [switch] $SkipPublish,
    [string] $PublishOutputPath = ''
)
Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'

if (-not ('DashboardDeploymentLinkCheck' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
public static class DashboardDeploymentLinkCheck {
    [StructLayout(LayoutKind.Sequential)]
    struct Info {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetFileInformationByHandle(SafeFileHandle handle, out Info information);
    public static void Check(string path) {
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
            Info info;
            if (!GetFileInformationByHandle(stream.SafeFileHandle, out info) || info.Links > 1)
                throw new IOException("Refused a hard-linked or unverifiable file: " + path);
        }
    }
}
'@
}

function Assert-NoReparse([string] $Path) {
    $current = [IO.Path]::GetFullPath($Path)
    while ($current) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Refused a junction or symbolic link: $current"
            }
        }
        $parent = [IO.Path]::GetDirectoryName($current)
        if ($parent -eq $current) { break }
        $current = $parent
    }
}
function Assert-SafeTree([string] $Root) {
    Assert-NoReparse $Root
    $pending = New-Object 'System.Collections.Generic.Queue[string]'
    $pending.Enqueue($Root)
    while ($pending.Count -gt 0) {
        $directory = $pending.Dequeue()
        foreach ($item in Get-ChildItem -LiteralPath $directory -Force) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Refused a junction or symbolic link: $($item.FullName)"
            }
            if ($item.PSIsContainer) { $pending.Enqueue($item.FullName) }
            else { [DashboardDeploymentLinkCheck]::Check($item.FullName) }
        }
    }
}
function Canonical([string] $Path) {
    return [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
}

$expected = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'Dashboard\WebApp'
if (-not $WebAppPath) { $WebAppPath = $expected }
if ((Canonical $WebAppPath) -ine (Canonical $expected)) {
    throw 'Deployment is restricted to the dedicated Program Files\Dashboard\WebApp folder.'
}
if ($SiteName -cne 'Dashboard' -or $AppPoolName -cne 'Dashboard') {
    throw 'Only the Dashboard IIS site and app pool can be managed.'
}
Assert-NoReparse $WebAppPath
if (-not (Test-Path -LiteralPath (Join-Path $WebAppPath 'LFPortal.Web.dll'))) {
    throw 'No installed Dashboard payload was found. Install Dashboard before running this script.'
}
Assert-SafeTree $WebAppPath

$appcmd = Join-Path $env:SystemRoot 'System32\inetsrv\appcmd.exe'
$configPath = Join-Path $env:SystemRoot 'System32\inetsrv\config\applicationHost.config'
Assert-NoReparse $configPath
[xml] $iis = Get-Content -LiteralPath $configPath -Raw
$sites = @($iis.configuration.'system.applicationHost'.sites.site)
$site = @($sites | Where-Object { $_.name -eq 'Dashboard' })
if ($site.Count -ne 1) { throw 'The Dashboard IIS site was not found.' }
$apps = @($site[0].application)
if ($apps.Count -ne 1 -or $apps[0].path -ne '/' -or $apps[0].applicationPool -ne 'Dashboard') {
    throw 'The Dashboard site has an unexpected or shared application.'
}
$vdirs = @($apps[0].virtualDirectory)
if ($vdirs.Count -ne 1 -or $vdirs[0].path -ne '/') { throw 'Unexpected IIS virtual directories.' }
$physical = [Environment]::ExpandEnvironmentVariables($vdirs[0].physicalPath)
if ((Canonical $physical) -ine (Canonical $expected)) { throw 'IIS points outside the Dashboard folder.' }
foreach ($other in $sites) {
    if ($other.name -eq 'Dashboard') { continue }
    foreach ($app in @($other.application)) {
        if ($app.applicationPool -eq 'Dashboard') { throw 'Another IIS site uses the Dashboard app pool.' }
    }
}

if ($SkipPublish) {
    if (-not $PublishOutputPath) { throw 'With -SkipPublish, supply -PublishOutputPath from a previous successful publish.' }
    $publish = Canonical $PublishOutputPath
    if (-not (Test-Path -LiteralPath (Join-Path $publish 'LFPortal.Web.dll'))) { throw 'Invalid Dashboard publish output.' }
    Assert-SafeTree $publish
} else {
    $stageRoot = Join-Path ([IO.Path]::GetTempPath()) 'DashboardFastDeploy'
    Assert-NoReparse $stageRoot
    $publish = Join-Path $stageRoot ([Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $publish -ErrorAction Stop | Out-Null
    $project = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\LFPortal.Web\LFPortal.Web.csproj'
    & dotnet publish $project --configuration Release --runtime win-x64 --self-contained true --output $publish --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $LASTEXITCODE" }
    Assert-SafeTree $publish
}
$source = (Canonical $publish) + '\'
$target = (Canonical $WebAppPath) + '\'
if ($source.StartsWith($target, [StringComparison]::OrdinalIgnoreCase) -or
    $target.StartsWith($source, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Publish source and install destination must be separate directory trees.'
}
$state = & $appcmd list apppool 'Dashboard' /text:state
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect the Dashboard app pool.' }
$wasStarted = "$state".Trim() -eq 'Started'
try {
    if ($wasStarted) {
        & $appcmd stop apppool /apppool.name:Dashboard
        if ($LASTEXITCODE -ne 0) { throw 'Could not stop the Dashboard app pool.' }
    }
    Assert-SafeTree $WebAppPath
    Assert-SafeTree $publish
    # /E adds/updates files. Never /MIR, /PURGE or deletion of unknown files.
    & robocopy $publish $WebAppPath /E /XJ /R:2 /W:1 /XF appsettings.json appsettings.Development.json /NFL /NDL /NJH /NJS /NP
    $copyExit = $LASTEXITCODE
    if ($copyExit -ge 8) { throw "Copy failed: robocopy exit $copyExit" }
} finally {
    if ($wasStarted) {
        & $appcmd start apppool /apppool.name:Dashboard
        if ($LASTEXITCODE -ne 0) { Write-Warning 'The Dashboard app pool could not be restarted; check IIS.' }
    }
}
Write-Host "Dashboard updated. Extra destination files were preserved. Publish output: $publish"

