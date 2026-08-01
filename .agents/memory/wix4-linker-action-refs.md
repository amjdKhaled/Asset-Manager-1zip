---
name: WiX 4.0.5 linker action reference rules
description: Which IIS and sequence action names can and cannot be referenced in Custom/@After in WiX 4.0.5
---

# WiX 4.0.5 — Custom action sequencing rules

## Rule
`After="ConfigureIIs"` (WiX IIS extension internal action) causes **WIX0094** at link time:
> "The identifier 'WixAction:InstallExecuteSequence/ConfigureIIs' could not be found."

ConfigureIIs is NOT exported as a public `WixAction` symbol in WiX 4.0.5.

**Why:** The WiX IIS extension schedules ConfigureIIs internally at sequence ~3700.
It is not registered as a public symbol that user WXS can reference via After= or Before=.

## Fix
Schedule appcmd CAs `Before="InstallFinalize"`.

**Why this is safe:** InstallFinalize is always at ~6600. The IIS extension's ConfigureIIs
runs at ~3700 — all IIS objects (app pool, web site) are fully created before InstallFinalize.

## How to apply
Any `<Custom>` that previously said `After="ConfigureIIs"` must be changed to
`Before="InstallFinalize"` with the appropriate Condition.

## Other confirmed-safe sequence references
- `After="InstallFiles"` — always resolvable (standard MSI action ~4000)
- `Before="RemoveFiles"` — always resolvable (standard MSI action ~3500)
- `Before="InstallFinalize"` — always resolvable (standard MSI action ~6600)
