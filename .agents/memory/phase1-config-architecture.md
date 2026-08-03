---
name: Phase 1 config architecture
description: Single writable config home in ProgramData; layering order, runtime file, fail-loud rules
---

Configuration layering (last-wins), defined in `DashboardConfigPaths` and Program.cs:
1. appsettings.json — structural defaults ONLY (no ServerUrl value, no RepositoryId/DisplayName; ServerUrl is intentionally NOT [Required] so unconfigured startup works with ValidateOnStart)
2. `<ContentRoot>\config\laserfiche.json` — legacy pre-Phase-1 file, read-only compat + non-Windows dev write fallback
3. `%ProgramData%\Dashboard\laserfiche.config.json` — installer wizard values (WriteConfigAction); never contains repository ids
4. `%ProgramData%\Dashboard\laserfiche.runtime.json` — Settings-page overrides; installer NEVER touches it, so admin settings survive repair/upgrade/reinstall

**Why:** installer file and Settings file must be separate — WriteConfigAction strips RepositoryId on repair, so a shared file would delete admin-chosen defaults.

Rules:
- On Windows, Settings ALWAYS writes to ProgramData (fail loudly on ACL problems — never fall back to Program Files). Non-Windows falls back to content-root config.
- Writes are atomic: temp file + File.Move(overwrite) with IOException retry (reload watcher/AV can hold the target).
- Settings save merges via JsonNode into existing file (preserves CredentialProvider etc.).
- Installer grants NETWORK SERVICE (via `[WIX_ACCOUNT_NETWORKSERVICE]` from util:QueryWindowsWellKnownSIDs — SID-based, works on non-English Windows) write on ProgramData\Dashboard root, credentials\, logs\.
- appsettings.Development.json excluded from publish (csproj CopyToPublishDirectory=Never) and guarded by publish.ps1 preflight, which also fails on lf-server.corp.local / "RepositoryId" / "DisplayName" tokens in staged appsettings.json.
- publish.ps1 smoke test --url now uses $env:COMPUTERNAME dynamically.
