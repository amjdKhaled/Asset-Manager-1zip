---
name: WiX 4 MBA required payload files
description: WiX 4.0.5 managed BA needs four explicit Bundle Payloads. Config file renamed WixToolset.Mba.Host.config (not BootstrapperCore.config). mbanative.dll (win-x86) must be copied via MSBuild Target using $(NuGetPackageRoot).
---

## Rule

WiX 4.0.5 managed BA requires FOUR explicit Bundle Payloads. None are added automatically.

| File | Source | Why |
|---|---|---|
| `Dashboard.BA.dll` | BA build output | The managed wizard |
| `WixToolset.Mba.Core.dll` | NuGet `lib/net20/` (CopyLocal) | Burn managed API |
| `WixToolset.Mba.Host.config` | Hand-authored source file | CLR activation spec for mbahost.dll |
| `mbanative.dll` (win-x86) | NuGet `runtimes/win-x86/native/` via MSBuild Target | Native P/Invoke bridge used by Mba.Core |

## Why each file matters

**`WixToolset.Mba.Host.config`** — WiX 4.0.5 renamed from `BootstrapperCore.config`.
mbahost.dll reads `.ba\WixToolset.Mba.Host.config` to activate the CLR.
Without it: `Error 0x8007006e: Failed to load bootstrapper config file`.

**`mbanative.dll` (win-x86)** — `WixToolset.Mba.Core.dll` P/Invokes into this native bridge.
Burn 4.0.5 is x86 → ONLY `win-x86` (140 KB) works; `win-x64` (174 KB) cannot be loaded by an x86 process.
Without it: `Error 0x80070490: Failed to create the managed bootstrapper application`.

## How to get mbanative.dll into the build output

`RuntimeIdentifier=win-x86` risks moving the output path for net48 projects.
Use an explicit MSBuild Target instead:

```xml
<Target Name="CopyMbaNativeDll" AfterTargets="Build">
  <PropertyGroup>
    <_MbaNativeSrc>$(NuGetPackageRoot)wixtoolset.mba.core/4.0.5/runtimes/win-x86/native/mbanative.dll</_MbaNativeSrc>
  </PropertyGroup>
  <Copy SourceFiles="$(_MbaNativeSrc)"
        DestinationFolder="$(OutputPath)"
        SkipUnchangedFiles="true"
        Condition="Exists('$(_MbaNativeSrc)')" />
  <Warning Text="mbanative.dll not found — bundle will fail at runtime with 0x80070490."
           Condition="!Exists('$(_MbaNativeSrc)')" />
</Target>
```

`$(NuGetPackageRoot)` is the NuGet global packages folder — NOT developer-specific.
Update the version (4.0.5) in the path if WixToolset.Mba.Core is upgraded.

## Bundle.wxs Payload block (complete)

```xml
<BootstrapperApplication>
  <bal:WixManagedBootstrapperApplicationHost />
  <Payload SourceFile="$(var.BAAssembly)" />
  <Payload SourceFile="$(var.MbaCoreAssembly)" />
  <Payload SourceFile="$(var.MbaHostConfig)" Name="WixToolset.Mba.Host.config" />
  <Payload SourceFile="$(var.MbaNative)"     Name="mbanative.dll" />
</BootstrapperApplication>
```

`Name=` is set explicitly on both config and native DLL to guarantee extracted filenames.

## publish.ps1 Step 6 guards

Four guards: `WixToolset.Mba.Core.dll`, `Dashboard.BA.dll`, `WixToolset.Mba.Host.config`, `mbanative.dll`.
Each checks existence AND non-zero file size.

## WixToolset.Mba.Host.config content

```xml
<?xml version="1.0" encoding="utf-8" ?>
<configuration>
  <startup useLegacyV2RuntimeActivationPolicy="true">
    <supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8" />
  </startup>
</configuration>
```

`useLegacyV2RuntimeActivationPolicy="true"` is required because Mba.Core targets net20.
