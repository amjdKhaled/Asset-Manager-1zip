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

The SDK-style project must keep `GenerateAssemblyInfo=false` because it has a manual
`Properties/AssemblyInfo.cs`, and must use `Microsoft.NET.Sdk.WindowsDesktop` with
`UseWindowsForms=true` for `System.Windows.Forms` on net48. The SDK DLL is staged at
the repository-relative `vendor\LaserficheSdk\bin\10.4\net-4.0\` path; never restore
the old absolute `C:\Program Files\...` HintPath.

**Why:** The first Windows build failed before Laserfiche API validation due to
duplicate generated assembly attributes and missing WinForms references. Keeping one
metadata authority and a portable SDK path makes the failure deterministic and
machine-independent.

## How to apply

When Phase 6 (MSI installer) is implemented, the installer build must happen on Windows and include `Dashboard.DesktopExtension.exe` built from the standalone csproj. The MSI should call `--setup --silent` on install and `--remove --silent` on uninstall.

## Runtime verification boundary

The pre-existing `laserfiche-extension` is a separate GovSearch AI WPF integration.
Its registration derives a toolbar name from `GovSearchAIAssistant.exe` and uses
AI-related labels. The Dashboard extension uses toolbar name `Dashboard`, button
label from `extension.config.json` (set to `Dashboard` for verification), and a
command pointing to `Dashboard.DesktopExtension.exe`.

**Why:** A Dashboard URL appearing inside the old GovSearch AI window proves only
that the old integration launched the site; it does not prove the new Phase 5
registration works.

**How to apply:** Keep the old integration installed during testing. Register the new
compiled EXE with `--setup`, restart Laserfiche, and verify both identities are
visible. Use `--remove` only to remove the new Dashboard toolbar.
