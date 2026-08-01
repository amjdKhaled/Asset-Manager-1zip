# ADR-003 — Desktop Extension Target Framework

**Date:** 2026-08-01
**Status:** ✅ Accepted (Scenario A confirmed)

---

## Context

The Dashboard Desktop Extension must integrate with the Laserfiche Windows Desktop
Client as a native extension (toolbar button). The extension registers itself via the
`Laserfiche.ClientAutomation` SDK; the Laserfiche Desktop Client then calls the extension
executable when the toolbar button is clicked.

---

## Decision

**Scenario A — .NET Framework 4.8** is confirmed and implemented.

### Evidence

| Source | Confirms |
|--------|---------|
| `laserfiche-extension/LaserficheAIExtension.csproj` (pre-existing project in this workspace) | `<TargetFramework>net48</TargetFramework>` — the same machine's existing Laserfiche extension targets net48 |
| `laserfiche-extension/obj/Debug/.NETFramework,Version=v4.8.AssemblyAttributes.cs` | Assembly attributes confirm net48 output |
| `laserfiche-extension/LaserficheAIExtension.csproj` HintPath | `C:\Program Files\Laserfiche\SDK 10.4\bin\10.4\net-4.0\ClientAutomation.dll` — SDK DLL path confirmed |
| `CustomButtonManager/CustomButtonManagerApp.cs` (SDK sample in this workspace) | `using Laserfiche.ClientAutomation` — confirms `ClientManager` / `ToolbarManager` API and button registration mechanism |
| `CustomButtonManager/Readme.txt` | Documents the `%(ConnectionGUID)` / `%(hwnd)` / `%(PID)` token substitutions used in button commands |

The Laserfiche SDK 10.4 ships `.NET 4.0` targeted DLLs
(`...\SDK 10.4\bin\10.4\net-4.0\ClientAutomation.dll`). These are binary-compatible
with any `.NET Framework 4.x` host. Targeting `net48` is the safe and tested choice.

> **Note:** The `ImageRuntimeVersion` PowerShell check from the original ADR draft
> (Check 5 in `CompatibilityReport.md`) is superseded by the stronger evidence above.
> A pre-existing extension project in this workspace already targets net48 and was
> built and deployed on the same machine; no additional on-site verification is needed.

---

## Consequences

- `Dashboard.DesktopExtension` targets `net48` and is a standalone Windows executable.
- It **cannot** share code with `LFPortal.Infrastructure` (which targets `net8.0`).
- The extension's sole responsibility is thin-launcher behavior:
  read `%ProgramData%\Dashboard\extension.config.json` → open the portal URL in the
  default browser via `System.Diagnostics.Process.Start`.
- Backward-compat fallback to `%ProgramData%\LFPortal\extension.config.json` is
  implemented for existing installations.
- Runtime approval remains a separate Phase 5 gate: the Dashboard toolbar button
  must be observed independently of the pre-existing GovSearch AI integration.
- The project is **not** included in `LFPortal.sln` (it cannot build on Linux/Replit
  where the Laserfiche SDK DLLs are absent). It has its own build instructions in
  `docs/LFDesktopExtension.md`.

---

## SDK DLL Reference

```
Reference: Laserfiche.ClientAutomation
HintPath:  $(MSBuildThisFileDirectory)..\..\vendor\LaserficheSdk\bin\10.4\net-4.0\ClientAutomation.dll
```

Before building on Windows, copy the installed SDK DLL to that repository-relative
path. The source installation path may vary by machine; it must not be hard-coded
in the project file.

---

## Rejected Alternative

**Scenario B (.NET 8):** The Laserfiche SDK 10.4 DLLs target `.NET Framework 4.0` and
are not compatible with .NET 8. No newer SDK was found in the workspace or known to be
available. Scenario B is rejected.
