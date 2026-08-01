---
name: WiX 4 IIS port runtime configuration
description: Why iis:WebAddress Port cannot use runtime MSI properties, and how to fix it with appcmd
---

# WiX 4 IIS WebAddress/@Port — compile-time only

## Rule
`iis:WebAddress/@Port` is typed as `wxs:Integer` in the WiX 4 IIS extension schema.
It accepts only a compile-time integer (preprocessor variable or literal).
It does NOT accept runtime MSI property references like `[DASHBOARD_PORT]`.

**Why:** The IIS extension stores the port in its internal table at compile/link time.
Property expansion is not applied to integer-typed attributes.

## Fix
Use an appcmd.exe custom action to overwrite the IIS binding after ConfigureIIs runs:

```xml
<CustomAction Id="SetIisBindingPort"
              Directory="WEBAPPFOLDER"
              ExeCommand='"[SystemFolder]inetsrv\appcmd.exe" set site "SiteName" /bindings:"http/*:[DASHBOARD_PORT]:"'
              Execute="deferred"
              Impersonate="no"
              Return="ignore" />
```

Schedule `Before="InstallFinalize"` (NOT After="ConfigureIIs" — see wix4-linker-action-refs.md).

## Required: Secure="yes" on the port property
Any MSI property used in a deferred CA ExeCommand must have `Secure="yes"`:
```xml
<Property Id="DASHBOARD_PORT" Value="5000" Secure="yes" />
```
Without Secure="yes", the elevated deferred context discards the value and [DASHBOARD_PORT]
expands to empty string, breaking the appcmd call silently.

## Shortcut fix
`Shortcut/@Arguments` IS a "formatted" string — use `[DASHBOARD_PORT]` directly.
No `Secure="yes"` needed for non-deferred shortcut creation.
Change: `http://localhost:$(DashboardPort)/` → `http://localhost:[DASHBOARD_PORT]/`
