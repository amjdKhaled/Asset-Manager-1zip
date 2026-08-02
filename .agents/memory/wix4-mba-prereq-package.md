---
name: WiX 4.0.5 managed BA prerequisite package — WIX6802 + WIX0103
description: How to satisfy WIX6802 and avoid WIX0103 for a remote ExePackage in WiX 4.0.5; NetFx extension has no package groups; RemotePayload was removed.
---

# WiX 4.0.5 prereq package — WIX6802 + WIX0103

## WIX6802
`bal:WixManagedBootstrapperApplicationHost` requires at least one package in the
Chain with `bal:PrereqPackage="yes"`.  The native prereq BA must install the managed
BA's runtime before handing control to the managed assembly.

`WixToolset.Netfx.wixext` 4.0.5 has NO package group symbols — no `NetFxAsPrereq`,
no WXS/WXI files.  The WiX 3 package groups were removed in WiX 4.  Define inline.

## WIX0103 root cause
`ExePackage/@Name` without `SourceFile` causes the WiX COMPILER to look for the
file at `SourceDir\<Name>` at BUILD TIME.  `DownloadUrl` on ExePackage is a Burn
RUNTIME fallback attribute — it does NOT suppress the build-time source lookup.

## Correct WiX 4.0.5 remote-only ExePackage syntax

```xml
<ExePackage Id="NetFx48Prereq"
            InstallArguments="/q /norestart /ChainingPackage &quot;[WixBundleName]&quot;"
            RepairArguments="/q /norestart /repair /ChainingPackage &quot;[WixBundleName]&quot;"
            DetectCondition="NetFx48Release >= 528040"
            Permanent="yes"
            Vital="yes"
            bal:PrereqPackage="yes">
  <ExePackagePayload Name="ndp48-web.exe"
                     DownloadUrl="https://go.microsoft.com/fwlink/?LinkId=2085155" />
</ExePackage>
```

With `util:RegistrySearch` for detection (direct child of `<Bundle>`):
```xml
<util:RegistrySearch Id="NetFx48Release"
                     Variable="NetFx48Release"
                     Root="HKLM"
                     Key="SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"
                     Value="Release"
                     Result="value"
                     Bitness="always64" />
```

## Key WiX 4 element changes vs WiX 3

| WiX 3 | WiX 4.0.5 | Error if wrong |
|---|---|---|
| `<RemotePayload>` child of ExePackage | `<ExePackagePayload>` child | WIX0005 |
| ExePackage/@Name + DownloadUrl (no child) | must use child ExePackagePayload | WIX0103 |
| ExePackage has Name/DownloadUrl AND child | forbidden — one or the other | WIX0372 |
| InstallCommand | InstallArguments | WIX0004 |
| RepairCommand | RepairArguments | WIX0004 |
| UninstallCommand (omit, use Permanent="yes") | Permanent="yes" | WIX0408 |

## Rules
- No `SourceFile/Name/DownloadUrl/Compressed` on `<ExePackage>` when using child payload.
- `ExePackagePayload/@Hash` (SHA512) is optional; omit it — ndp48-web.exe is
  Authenticode-signed by Microsoft, which is stronger verification.
- `Permanent="yes"` avoids WIX0408 (UninstallArguments required otherwise).
- `WixToolset.Util.wixext` (already in bundle build args) provides `util:RegistrySearch`;
  `WixToolset.Netfx.wixext` is NOT needed.

## publish.ps1 impact
- No `$prereqDir` / `PrereqDir` define needed — remove them entirely.
- No `installer\prerequisites\` directory required — never create it.
- The `ndp48-web.exe` is NEVER downloaded at build time; Burn downloads it at
  install time ONLY when `DetectCondition` is false (rare — .NET 4.8 ships
  pre-installed on Win Server 2019/2022, Win10 1903+).

## Detection thresholds
| Release value | Platform |
|---|---|
| 528040 | Windows 10 1903+, Server 2019 (minimum threshold) |
| 528372 | Windows 10 2004 |
| 528449 | Windows 11, Server 2022 |
