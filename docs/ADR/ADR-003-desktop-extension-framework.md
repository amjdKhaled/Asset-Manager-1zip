# ADR-003 — Desktop Extension Target Framework

**Date:** 2026-07-31
**Status:** ⚠️ PENDING ON-SITE VERIFICATION

---

## Context

The LFPortal Desktop Extension must integrate with the Laserfiche Windows Desktop
Client as a native extension (toolbar/ribbon button). To do this, the extension DLL
must be loaded into the Desktop Client's process space. The Desktop Client is a Windows
application with a fixed .NET runtime. The extension must target a compatible framework.

**Two scenarios are possible:**

- **Scenario A:** The Laserfiche Desktop Client and its extension SDK target
  **.NET Framework 4.x** (the historical norm for Laserfiche Windows components).
  In this case, `LFPortal.DesktopExtension` must target `.NET Framework 4.x` (most
  likely `net472` or `net48`), independent of the main portal's .NET 8 target.

- **Scenario B:** A newer Desktop Client version supports **.NET 8** or later for
  extensions. In this case, both projects can target the same runtime.

---

## Decision

⚠️ **This ADR cannot be finalized until on-site verification (Check 5 in
`CompatibilityReport.md`) is completed.**

The required check is:
```powershell
Get-ChildItem "C:\Program Files\Laserfiche" -Recurse -Filter "Laserfiche.RepositoryAccess.dll"
[System.Reflection.Assembly]::ReflectionOnlyLoadFrom("{path-to-dll}").ImageRuntimeVersion
```

**Interim working assumption:** Based on the Laserfiche SDK 10.x release history,
the Windows Client architecture, and community knowledge, **Scenario A (.NET Framework
4.x) is the most probable outcome.** Phase 5 implementation will not begin until this
is confirmed.

---

## Architectural Impact of Each Scenario

### If Scenario A (.NET Framework 4.x) — Most likely

- `LFPortal.DesktopExtension` is a separate class library targeting `net472` or `net48`
- It **cannot** share code with `LFPortal.Infrastructure` (which targets `net8.0`)
- The extension's only responsibility is: read config from
  `%ProgramData%\LFPortal\extension.config.json`, launch the portal URL in the default
  browser via `System.Diagnostics.Process.Start`
- It implements whatever interface the Laserfiche SDK requires
- Both projects coexist in the same solution; the installer deploys both outputs
- This is architecturally clean: the extension is a thin launcher, not a business logic
  component

### If Scenario B (.NET 8)

- `LFPortal.DesktopExtension` targets `net8.0`
- It can reference shared Domain types if needed
- Same functional behavior: thin launcher that reads config and opens the browser

---

## ILFPortalExtension Interface

Regardless of framework, the extension declares an `ILFPortalExtension` interface
internally to provide a stable extension point for future deeper integrations:

```csharp
internal interface ILFPortalExtension
{
    void Initialize(object extensionContext);
    void OnButtonClick();
    string GetPortalUrl();
    ExtensionConfig GetConfiguration();
}
```

Future versions can implement richer behavior (embedded browser panel, context-aware
LF entry actions) by providing a new `ILFPortalExtension` implementation without
redesigning the project structure.

---

## Action Required Before Phase 5

1. Run Check 5 from `CompatibilityReport.md` on the deployment machine
2. Record the `ImageRuntimeVersion` value here
3. Update this ADR status to **Accepted** with the confirmed scenario
4. Update the `LFPortal.DesktopExtension` project target framework accordingly

---

**This ADR will be updated and finalized before Phase 5 begins.**
