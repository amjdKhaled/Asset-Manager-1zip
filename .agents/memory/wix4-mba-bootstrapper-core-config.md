---
name: WiX 4 MBA host config filename
description: WiX 4.0.5 mbahost.dll looks for WixToolset.Mba.Host.config (NOT BootstrapperCore.config which was the WiX 3 name). File not in any NuGet package; must be hand-authored and added as an explicit Bundle Payload.
---

## Rule

WiX 4.0.5 `mbahost.dll` looks for `WixToolset.Mba.Host.config` — NOT `BootstrapperCore.config`.
`BootstrapperCore.config` was the WiX 3 name and does nothing in WiX 4.

**Confirmed from the real Burn 4.0.5 runtime log:**
```
Error 0x8007006e: Failed to load bootstrapper config file from path:
...\.ba\WixToolset.Mba.Host.config
```

The `WixToolset.Mba.Core` 4.0.5 NuGet package does NOT ship `WixToolset.Mba.Host.config`.
The file must be hand-authored in the BA project, set `CopyToOutputDirectory=Always`,
and included as an explicit `<Payload>` in Bundle.wxs.

## Why

`mbahost.dll` (the WiX native managed-BA host) reads `WixToolset.Mba.Host.config`
at startup to know which CLR version to activate before loading the managed BA DLL.
Without it, the host cannot identify a supported runtime, falls back to `mbapreq.dll`,
and shows the prereq-BA error screen:
  "failed to load the .NET Framework runtime even though all prerequisites are installed."
.NET 4.8 may be fully present on the machine — this file is the instruction to use CLR v4.

## How to apply

1. Create `installer/Dashboard.BA/WixToolset.Mba.Host.config`:
```xml
<?xml version="1.0" encoding="utf-8" ?>
<configuration>
  <startup useLegacyV2RuntimeActivationPolicy="true">
    <supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8" />
  </startup>
</configuration>
```
   `useLegacyV2RuntimeActivationPolicy="true"` is required because
   `WixToolset.Mba.Core.dll` targets net20; this lets CLR 4 load it.

2. In `Dashboard.BA.csproj`:
```xml
<None Include="WixToolset.Mba.Host.config">
  <CopyToOutputDirectory>Always</CopyToOutputDirectory>
</None>
```

3. In `Bundle.wxs` inside `<BootstrapperApplication>`, with explicit `Name`:
```xml
<Payload SourceFile="$(var.MbaHostConfig)"
         Name="WixToolset.Mba.Host.config" />
```
The explicit `Name` guarantees the extracted filename regardless of source path.

4. In `publish.ps1` Step 9 wix build args:
```
"-d", "MbaHostConfig=$(Join-Path $baStagingDir 'WixToolset.Mba.Host.config')",
```

5. In `publish.ps1` Step 6: guard checks existence + non-zero size for all three:
   `WixToolset.Mba.Core.dll`, `Dashboard.BA.dll`, `WixToolset.Mba.Host.config`.

## Filename

Must be exactly `WixToolset.Mba.Host.config` — mbahost.dll 4.0.5 hardcodes this name.
`BootstrapperCore.config` will be silently ignored in WiX 4.
