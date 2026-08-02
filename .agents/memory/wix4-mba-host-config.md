---
name: WiX 4 Mba.Host.config — complete verified schema
description: Exact config file schema for WixToolset.Mba.Host.config, verified by DLL inspection; includes root cause of 0x80131902 and what NOT to include.
---

## Rule

`WixToolset.Mba.Host.config` requires BOTH a `<startup>` section AND a `<wix.bootstrapper>` section group with `assemblyName`.

**How the host reads this file:**
1. Native `mbahost.dll` reads `<startup>` to activate the CLR (before any managed code runs).
2. Managed `WixToolset.Mba.Host.dll` (23 KB, auto-embedded by `WixToolset.Bal.wixext`) calls `ConfigurationManager.GetSection("wix.bootstrapper/host")` to read `assemblyName`.
3. It loads `Assembly.Load(assemblyName)` from the `.ba\` AppBase directory.
4. Scans for `[assembly: BootstrapperApplicationFactory(typeof(BAFactory))]`.
5. Calls `Activator.CreateInstance(factoryType)` → `factory.Create(pArgs, pResults)`.

**Confirmed correct schema (verified against WixToolset.Mba.Host 4.0.5 DLL metadata):**

```xml
<?xml version="1.0" encoding="utf-8" ?>
<configuration>
  <configSections>
    <sectionGroup name="wix.bootstrapper"
                  type="WixToolset.Mba.Host.BootstrapperSectionGroup, WixToolset.Mba.Host">
      <section name="host"
               type="WixToolset.Mba.Host.HostSection, WixToolset.Mba.Host" />
    </sectionGroup>
  </configSections>
  <startup useLegacyV2RuntimeActivationPolicy="true">
    <supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8" />
  </startup>
  <wix.bootstrapper>
    <host assemblyName="Dashboard.BA" />
  </wix.bootstrapper>
</configuration>
```

## What caused 0x80131902 (ConfigurationErrorsException)

**Root cause confirmed:** `SupportedFrameworkElement` in `WixToolset.Mba.Host.dll` has exactly ONE `ConfigurationProperty`: `version`. The `sku` attribute does NOT exist in the type. In .NET Framework, `ConfigurationElement` throws `ConfigurationErrorsException` (HRESULT `0x80131902`) on ANY unrecognised XML attribute. Adding `<add version="v4.8" sku=".NETFramework,Version=v4.8"/>` introduced the `sku` attribute → immediate config parse error → `Create()` was never reached → `StartupLogger` never fired.

**What NOT to include in `<wix.bootstrapper>`:**
- `<supportedFrameworks>` with `sku` attribute — `SupportedFrameworkElement` has no `sku` property; including it crashes config parsing
- `version="v4.8"` — CLR version strings use `v4.0` for all .NET 4.x; `v4.8` is the framework marketing version, not the CLR runtime version

`<supportedFrameworks>` itself is optional; omitting it is safest.

## Error sequence summary

| Config state | Error | Cause |
|---|---|---|
| No config file | `0x8007006e` | mbahost.dll cannot find `WixToolset.Mba.Host.config` |
| `<startup>` only | `0x80070490` | `GetSection()` returns null → factory not found |
| `<configSections>` + `<wix.bootstrapper>` + `sku` attr | `0x80131902` | `ConfigurationErrorsException` from unrecognised `sku` |
| `<configSections>` + `<wix.bootstrapper>` (no sku) | ✅ proceeds | Config parses → assembly loads → factory created |

## Investigation method (how DLL schema was determined)

1. `WixToolset.Bal.bal.wixlib` embedded resource in `WixToolset.Bal.wixext.dll` is a ZIP.
2. Extracted: `unzip wixlib.zip "wix-ir/WixToolset.Mba.Host.dll"` → 23 KB managed DLL.
3. Ran `strings` on it: found `version`, `versionProperty`, `assemblyName`, `assemblyNameProperty`, `ConfigurationManager`, `GetSection`, `GetBAFactoryTypeFromAssembly`, `BootstrapperSectionGroup`, `HostSection`.
4. Confirmed **NO** `sku`, `skuProperty` anywhere in the DLL → `sku` is an unrecognised attribute.

## mbahost.dll architecture (from wixlib)

WiX auto-selects the correct `mbahost.dll` variant for the bundle architecture:
- `wix-ir/mbahost.dll` = ARM64
- `wix-ir/mbahost.dll-1` = x64/AMD64
- `wix-ir/mbahost.dll-2` = x86/I386  ← selected for Burn x86 bundles automatically

## Related invariants

- `WixToolset.Mba.Core.dll` — NOT auto-embedded; must be an explicit `<Payload>`.
- `mbanative.dll` (win-x86) — NOT auto-embedded; must be an explicit `<Payload>`. Source: `WixToolset.Mba.Core 4.0.5` NuGet, `runtimes/win-x86/native/mbanative.dll`. Copied by `CopyMbaNativeDll` MSBuild target in `Dashboard.BA.csproj`.
- `[assembly: BootstrapperApplicationFactory(typeof(BAFactory))]` must be in the DLL (Guard 5 in publish.ps1 binary-scans for this string).
- `Dashboard.BA.dll` must be `PlatformTarget=x86` (Burn is x86 process; Guard 6 in publish.ps1 checks PE machine = 0x014C).
- `BAFactory()` explicit constructor logs to `%TEMP%\LFDashboard-BA-startup.log` and `%ProgramData%\LFDashboard\Logs\BA-startup.log`. If either log is absent after launch, the failure is before `Activator.CreateInstance(BAFactory)` — inside the managed host itself.
- `0x80131902` = `ConfigurationErrorsException` = config parse error (NOT architecture mismatch).
- Architecture mismatch would produce `BadImageFormatException` (0x8007000B), not 0x80131902.
