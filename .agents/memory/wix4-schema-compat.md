---
name: WiX 4.0.5 schema compatibility changes vs WiX 3
description: Attribute renames, removals, and behavior changes confirmed against the real WiX 4.0.5 compiler (4.0.5+b9b2f1b4) on the Dashboard installer.
---

## Confirmed WiX 3 → WiX 4.0.5 changes

### Product / MSI

| WiX 3 | WiX 4 fix | Why |
|---|---|---|
| `RegistrySearch/@Win64="yes"` | `Bitness="always64"` | attribute renamed |
| `Feature/@Absent="disallow"` | `AllowAbsent="no"` | attribute renamed |
| `Property/@Value=""` | omit Value, add `Secure="yes"` | WIX0006 rejects empty string; Secure="yes" needed for deferred CA access |
| `<Custom>CONDITION</Custom>` (inner text) | `<Custom Condition="..." />` | WIX0400: condition moved to attribute; `"` → `&quot;` inside attr |
| `File/@NeverOverwrite="yes"` | `Component/@NeverOverwrite="yes"` | WIX0004: attribute moved from File to Component in WiX 4 |
| `WebAppPool/@ManagedRuntimeVersion=""` | omit attr; use appcmd CA | WIX0006 rejects empty string; appcmd sets managedRuntimeVersion="" post-install |
| `WebApplication/@Name=""` | `Name="Dashboard"` (non-empty) | WIX0006 rejects empty string; Name is IIS internal label |

### Bundle

| WiX 3 | WiX 4 fix | Why |
|---|---|---|
| `WixManagedBootstrapperApplicationHost BAFactoryAssembly="..."` | child `<Payload SourceFile="..." />` | attribute removed; BA DLL identified by Payload |
| `WixManagedBootstrapperApplicationHost BAConnectionTimeout="..."` | remove attribute | removed in WiX 4 |
| `MsiPackage/@DisplayInternalUI="no"` | remove attribute | removed in WiX 4; Burn always suppresses MSI UI |

### Linux compile false positives (DO NOT fix)
- WIX0389 "Directory/@Name not a relative path" — WiX on Linux rejects single-component dir names ("Dashboard", "WebApp"). Does NOT occur on Windows. Caused by WiX explicitly stating "behavior on Linux is undefined."

## Config preservation pattern (WiX 4)

```xml
<Component NeverOverwrite="yes" Permanent="yes" ...>
  <File ... />   <!-- no NeverOverwrite on File -->
</Component>
```
- `NeverOverwrite="yes"`: file never overwritten on upgrade/repair
- `Permanent="yes"`: file not removed on uninstall (Phase 6 requirement)
- ConfigTemplate\ sub-dir intentionally has NEITHER (always updated on upgrade)

## SetAppPoolNoManagedCode CA pattern

When WiX IIS extension rejects ManagedRuntimeVersion="", add a deferred CA:
```xml
<CustomAction Id="SetAppPoolNoManagedCode"
              Directory="WEBAPPFOLDER"
              ExeCommand='"[SystemFolder]inetsrv\appcmd.exe" set apppool "PoolName" /managedRuntimeVersion:""'
              Execute="deferred" Impersonate="no" Return="ignore" />
```
Schedule `After="ConfigureIIs"` with `Condition="NOT REMOVE~=&quot;ALL&quot;"`.
`[SystemFolder]` is resolved at scheduling time (safe for deferred CA ExeCommand).
