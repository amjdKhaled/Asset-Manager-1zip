---
name: Desktop extension confirmed net48
description: ADR-003 finalized — framework, SDK path, registration mechanism, and build constraints for Dashboard.DesktopExtension.
---

## Decision (ADR-003 — Accepted)

- Target framework: **net48** (.NET Framework 4.8)
- SDK: `Laserfiche.ClientAutomation` from `C:\Program Files\Laserfiche\SDK 10.4\bin\10.4\net-4.0\ClientAutomation.dll`
- Registration API: `ClientManager` → `ToolbarManager.AddCustomToolbarButton` (same as `CustomButtonManager/` sample in workspace)
- Button command pattern: `"path\to\Dashboard.DesktopExtension.exe" -buttonclick -connguid "%(ConnectionGUID)" -hwnd "%(hwnd)" -pid "%(PID)"`

## Evidence

Pre-existing `laserfiche-extension/LaserficheAIExtension.csproj` in the workspace already targets `net48` and references `ClientAutomation.dll` at the SDK 10.4 path — the framework is confirmed by the existing working project, not just theory.

## Build constraint

`Dashboard.DesktopExtension.csproj` is **NOT in LFPortal.sln**. It cannot build on Linux/Replit (missing Windows-only DLLs). Build separately on Windows with the Laserfiche SDK installed. See `docs/LFDesktopExtension.md`.

**Why:** Adding it to the solution would break the 0-errors gate on the CI/Replit environment.

## How to apply

When Phase 6 (MSI installer) is implemented, the installer build must happen on Windows and include `Dashboard.DesktopExtension.exe` built from the standalone csproj. The MSI should call `--setup --silent` on install and `--remove --silent` on uninstall.
