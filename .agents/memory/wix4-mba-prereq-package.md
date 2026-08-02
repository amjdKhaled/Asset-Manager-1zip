---
name: WiX 4.0.5 managed BA prerequisite package — WIX6802 + WIX0103
description: ExePackagePayload with SourceFile+DownloadUrl is the confirmed correct WiX 4 syntax; Name+DownloadUrl alone still triggers WIX0103 on Windows.
---

# WiX 4.0.5 prereq package — WIX6802 + WIX0103

## WIX6802
`bal:WixManagedBootstrapperApplicationHost` requires at least one package in the
Chain with `bal:PrereqPackage="yes"`.  The native prereq BA must install the managed
BA's runtime before handing control to the managed assembly.

`WixToolset.Netfx.wixext` 4.0.5 has NO package group symbols.  Define inline.

## WIX0103 root cause
`ExePackage/@Name` without `SourceFile` → compiler looks for file at `SourceDir\<Name>` at BUILD TIME.
`ExePackagePayload/@Name + DownloadUrl` (no SourceFile) → ALSO triggers WIX0103 on real Windows.
WiX 4.0.5 requires a local SourceFile to compute hash/size/version for the bundle manifest even for
download-only packages.  DownloadUrl is a Burn RUNTIME attribute only.

## CONFIRMED CORRECT WiX 4.0.5 syntax

```xml
<!-- Direct child of <Bundle> -->
<util:RegistrySearch Id="NetFx48Release"
                     Variable="NetFx48Release"
                     Root="HKLM"
                     Key="SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"
                     Value="Release"
                     Result="value"
                     Bitness="always64" />

<!-- In <Chain>, before <MsiPackage> -->
<ExePackage Id="NetFx48Prereq"
            InstallArguments="/q /norestart /ChainingPackage &quot;[WixBundleName]&quot;"
            RepairArguments="/q /norestart /repair /ChainingPackage &quot;[WixBundleName]&quot;"
            DetectCondition="NetFx48Release >= 528040"
            Permanent="yes"
            Vital="yes"
            bal:PrereqPackage="yes">
  <ExePackagePayload Name="ndp48-web.exe"
                     SourceFile="$(var.NetFx48Installer)"
                     DownloadUrl="https://go.microsoft.com/fwlink/?LinkId=2085155" />
</ExePackage>
```

`$(var.NetFx48Installer)` is passed by publish.ps1 as `-d NetFx48Installer=<path>`.

## WiX 4 element/attribute rules

| Situation | Result |
|---|---|
| `<RemotePayload>` child of ExePackage | WIX0005 (removed in WiX 4) |
| ExePackage/@Name without SourceFile | WIX0103 (local file lookup) |
| ExePackagePayload/@Name + DownloadUrl only (no SourceFile) | WIX0103 on Windows |
| ExePackage/@Name AND child ExePackagePayload | WIX0372 (must use one, not both) |
| ExePackagePayload with SourceFile + DownloadUrl | ✅ CORRECT |
| InstallCommand (WiX 3 name) | WIX0004 → use InstallArguments |
| RepairCommand (WiX 3 name) | WIX0004 → use RepairArguments |
| No UninstallArguments without Permanent="yes" | WIX0408 |

## publish.ps1 build-time acquisition

```powershell
$prereqCacheDir   = Join-Path $RepoRoot ".build-cache\prerequisites"
$netFx48Installer = Join-Path $prereqCacheDir "ndp48-web.exe"
# 1. Create cache dir (survives Step 1 artifact wipe)
# 2. Download if missing (TLS 1.2, Invoke-WebRequest)
# 3. Get-AuthenticodeSignature: Status -eq 'Valid' AND Subject -match 'Microsoft'
# 4. Delete + fail build if invalid
# 5. Pass: "-d", "NetFx48Installer=$netFx48Installer"
```

Cache at `.build-cache\prerequisites\` (gitignored) survives `artifacts/` wipe.
Signature verified every build regardless of whether file was cached or fresh.

## Detection thresholds (Release DWORD)
528040 = Win10 1903+ / Server 2019 (minimum); 528449 = Win11 / Server 2022.
`NetFx48Release >= 528040` covers all supported targets.
