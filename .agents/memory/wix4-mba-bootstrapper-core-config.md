---
name: WiX 4 MBA BootstrapperCore.config
description: mbahost.dll requires BootstrapperCore.config to activate the CLR; WixToolset.Mba.Core 4.0.5 does NOT include it — must be hand-authored and added as a Bundle Payload.
---

## Rule

`WixToolset.Mba.Core` 4.0.5 NuGet package does NOT ship `BootstrapperCore.config`.
The file must be hand-authored in the BA project, set `CopyToOutputDirectory=Always`,
and included as an explicit `<Payload>` in Bundle.wxs.

## Why

`mbahost.dll` (the WiX native managed-BA host) reads `BootstrapperCore.config`
at startup to know which CLR version to activate before loading the managed BA DLL.
Without it, the host cannot identify a supported runtime, fails to activate any CLR,
and Burn shows the prereq-BA error screen:
  "failed to load the .NET Framework runtime even though all prerequisites are installed."
.NET 4.8 may be fully present — this config is the instruction to use it.

## How to apply

1. Create `installer/Dashboard.BA/BootstrapperCore.config`:
```xml
<?xml version="1.0" encoding="utf-8" ?>
<configuration>
  <startup useLegacyV2RuntimeActivationPolicy="true">
    <supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8" />
  </startup>
</configuration>
```
   `useLegacyV2RuntimeActivationPolicy="true"` is required because
   WixToolset.Mba.Core.dll targets net20; this lets CLR 4 load it.

2. In `Dashboard.BA.csproj`:
```xml
<None Include="BootstrapperCore.config">
  <CopyToOutputDirectory>Always</CopyToOutputDirectory>
</None>
```

3. In `Bundle.wxs` inside `<BootstrapperApplication>`:
```xml
<Payload SourceFile="$(var.BootstrapperCoreConfig)" />
```

4. In `publish.ps1` Step 9 wix build args:
```
"-d", "BootstrapperCoreConfig=$(Join-Path $baStagingDir 'BootstrapperCore.config')",
```

5. In `publish.ps1` Step 6 add a guard that fails the build if the staged
   `BootstrapperCore.config` is missing (before the Bundle build runs).

## Filename

Must be exactly `BootstrapperCore.config` — mbahost.dll hardcodes this name.
