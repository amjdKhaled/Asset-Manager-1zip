---
name: WiX 4 Mba.Host.config assemblyName section
description: WixToolset.Mba.Host.dll requires a <wix.bootstrapper> config section to find the BA assembly; <startup> alone is not enough and causes 0x80070490.
---

## Rule

`WixToolset.Mba.Host.config` MUST contain BOTH a `<startup>` section AND a `<wix.bootstrapper>` section group with `assemblyName`. Providing only `<startup>` causes `Error 0x80070490: Failed to create the managed bootstrapper application.`

**Why:** `WixToolset.Mba.Host.dll` (a 23 KB managed assembly auto-embedded into every bundle by `WixToolset.Bal.wixext`) is the component that actually discovers and loads the BA factory. It calls `ConfigurationManager.GetSection("wix.bootstrapper/host")` to read `assemblyName`, then loads that DLL and calls `GetCustomAttributes<BootstrapperApplicationFactoryAttribute>()`. If the section is absent the call returns null and the host returns `E_NOTFOUND` → `0x80070490`. The `<startup>` section only activates the CLR; it does NOT tell the managed host which assembly to scan.

**How to apply:** Every `WixToolset.Mba.Host.config` for a .NET 4.x managed BA must include:

```xml
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
    <host assemblyName="Dashboard.BA">          <!-- SHORT name, no .dll -->
      <supportedFrameworks>
        <add version="v4.8" sku=".NETFramework,Version=v4.8" />
      </supportedFrameworks>
    </host>
  </wix.bootstrapper>
</configuration>
```

Type names confirmed from `WixToolset.Mba.Host.dll` strings:
- `BootstrapperSectionGroup` → `WixToolset.Mba.Host.BootstrapperSectionGroup`
- `HostSection` → `WixToolset.Mba.Host.HostSection`
- `assemblyName` attribute → short assembly name without `.dll`
- `supportedFrameworks` child collection uses `<add version="..." sku="..." />`

## Investigation method that found this

Extracted the wix-ir ZIP embedded in `WixToolset.Bal.wixext.dll` → found `WixToolset.Mba.Host.dll` (23 KB) is auto-embedded alongside `mbahost.dll`. Read its strings → saw `BootstrapperSectionGroup`, `HostSection`, `assemblyNameProperty`, `GetSection`, `E_NOTFOUND`. This proved the managed host reads config for assembly discovery, not assembly scanning.

## Related invariants

- `WixToolset.Mba.Core.dll` must be an explicit `<Payload>` (not auto-embedded by WiX).
- `mbanative.dll` (win-x86, 140 KB) must be an explicit `<Payload>` (Burn is x86).
- `[assembly: BootstrapperApplicationFactory(typeof(BAFactory))]` must be present in the BA DLL (verified by Guard 5 binary scan in publish.ps1).
- The `<startup>` section is still required for CLR activation; do not remove it.
