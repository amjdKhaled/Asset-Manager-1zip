---
name: WiX 4.0.5 managed BA prerequisite package — WIX6802
description: How to satisfy WIX6802 (bal:PrereqPackage requirement) in WiX 4.0.5; NetFx extension does NOT provide package groups
---

# WiX 4.0.5 WIX6802 — Managed BA prereq package

## The error
WIX6802: "There must be at least one package with bal:PrereqPackage='yes' when using
the ManagedBootstrapperApplicationHost."

## Root cause
`bal:WixManagedBootstrapperApplicationHost` requires a native prereq package because the
managed BA DLL cannot install its own runtime — Burn's native engine must run first.

## WixToolset.Netfx.wixext 4.0.5 does NOT provide NetFxAsPrereq package groups
Inspected the NuGet package — no WXS/WXI files, no package group symbols, no strings
matching "Prereq" or "PackageGroup". The WiX 3 `NetFxAsPrereq` package groups were
removed. The WiX error message pointing to WixNetFxExtension is misleading for v4.0.5.

## Fix — inline ExePackage with bal:PrereqPackage="yes"

```xml
<!-- In Wix element: xmlns:util="http://wixtoolset.org/schemas/v4/wxs/util" -->

<!-- Detect .NET 4.8 via registry (as direct child of Bundle) -->
<util:RegistrySearch Id="NetFx48Release"
                     Variable="NetFx48Release"
                     Root="HKLM"
                     Key="SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"
                     Value="Release"
                     Result="value"
                     Bitness="always64" />

<!-- In Chain, before MsiPackage -->
<ExePackage Id="NetFx48Prereq"
            Name="ndp48-web.exe"
            DownloadUrl="https://go.microsoft.com/fwlink/?LinkId=2085155"
            InstallArguments="/q /norestart /ChainingPackage &quot;[WixBundleName]&quot;"
            RepairArguments="/q /norestart /repair /ChainingPackage &quot;[WixBundleName]&quot;"
            DetectCondition="NetFx48Release >= 528040"
            Permanent="yes"
            Vital="yes"
            bal:PrereqPackage="yes" />
```

## Key WiX 4 attribute renames on ExePackage (vs WiX 3)
- `InstallCommand` → `InstallArguments`
- `RepairCommand` → `RepairArguments`
- `UninstallCommand` → `UninstallArguments`

## Avoid WIX0408 (UninstallArguments required)
Use `Permanent="yes"` — .NET Framework is an OS component; never uninstall it.

## No publish.ps1 changes needed for the NetFx extension
`WixToolset.Util.wixext` (already in the bundle build) provides `util:RegistrySearch`.
`WixToolset.Netfx.wixext` is NOT needed for this use case.

## Runtime behaviour on modern Windows
.NET 4.8 Release DWORD >= 528040 on Windows Server 2019/2022 and Win10 1903+.
DetectCondition is true → package is skipped; no download occurs.
On rare older machines, Burn's native engine downloads ndp48-web.exe silently
and installs it before handing control to the managed BA.
