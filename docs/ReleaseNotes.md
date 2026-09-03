# Dashboard — Release Notes

## Installer maintenance update

- Added a clear maintenance screen when the setup EXE is run on an installed computer.
- Added one-click Repair and confirmed Uninstall operations.
- Added an optional, unchecked full-cleanup choice for ProgramData configuration, credentials, and logs.
- Added TCP port conflict validation before IIS changes begin.
- Added 64-bit Windows validation and standard Add/Remove Programs metadata.
- Added SHA-256 checksum generation and a Windows GitHub Actions installer build.
- Fixed the Desktop Client registration option so an unchecked option is respected.

---

## Version 1.0.0 — 2026-08-01

### Initial Release

#### Dashboard Web Application

- ASP.NET Core 8 MVC application deployable to IIS via the ASP.NET Core Hosting Bundle
- Dark-theme Dashboard page with live Laserfiche repository statistics:
  - Total entries, folder count, document type breakdown (Chart.js)
  - Connection health badge (60-second auto-refresh)
  - Recently modified entries
- Document Archive browser with folder tree, grid/list view, entry detail, search, and breadcrumb
- Settings page: connection configuration, repository discovery, test connection
- Health endpoint at `/health` (JSON; used by IIS Application Initialization and monitoring tools)
- DPAPI credential encryption — credentials never stored in plain text
- Serilog rolling file log (`logs/dashboard-YYYYMMDD.log`), 14-day retention
- Multi-repository support — switch repositories via Settings or URL parameter

#### Laserfiche Desktop Client Extension

- Native toolbar button in the Laserfiche Desktop Client (SDK 10.4, net48, x64)
- Opens Dashboard in a standalone WebView2 popup window
- Passes the active Laserfiche repository via `?repository=<DatabaseName>` so Dashboard pre-selects it
- Registration via `Dashboard.DesktopExtension.exe --setup` / `--remove`
- Config file: `%ProgramData%\Dashboard\extension.config.json` (created on first install, never overwritten on upgrade)
- Extension log: `%ProgramData%\Dashboard\logs\extension.log`

#### Laserfiche Web Client Integration

- Dashboard button injected into the Laserfiche Web Client (Browse.aspx) top navbar
- Repository detected from the server-rendered `WebAccessRepositoryName` hidden field
- Opens Dashboard in a new browser tab with `?repository=<repo>&source=webclient`
- Robust JavaScript architecture:
  - `window.__lfDashboardInitialized` singleton guard — blocks duplicate script execution
  - Capture-phase delegated click listener — fires before Angular intercepts events
  - Single anchor navigation — no `window.open` / fallback race that caused duplicate tabs
  - MutationObserver — re-injects button if Angular re-renders the navbar
- Deployed via `installer/Deploy-WebClientButton.ps1` (not via MSI — Laserfiche upgrades overwrite Browse.aspx)

#### Authentication & Session

- Per-session Login flow when opened from Desktop Client or Web Client
- Blank password support (some Laserfiche accounts have no password)
- Session-scoped credentials encrypted with ASP.NET Data Protection
- `SessionAuthGuardMiddleware` guards Desktop Client and Web Client sessions
- "Change Account" clears session credentials and returns to Login

#### MSI Installer (Phase 6)

- WiX v4 single-MSI installer
- Installs Dashboard web application and configures IIS (Application Pool, Web Site, port 5000)
- Installs and registers Desktop Extension
- Prerequisite check: requires ASP.NET Core 8 Hosting Bundle
- `NeverOverwrite` config files — configuration survives all upgrades
- Safe uninstall: removes binaries, IIS site, and Desktop toolbar registration; preserves `%ProgramData%\Dashboard\`
- PowerShell build script (`build/publish.ps1`) produces deterministic release artifacts

#### Build

- Main web application: `dotnet build --configuration Release` → 0 errors, 0 warnings
- JavaScript: `node --check` syntax validation passes
- No CDN references — all static assets served locally (Chart.js, Bootstrap Icons)
- No telemetry, no external service calls other than the configured Laserfiche server

---

## Known Limitations (v1.0.0)

| Item | Notes |
|---|---|
| Web Client button after Laserfiche upgrade | Re-run `Deploy-WebClientButton.ps1` after each Laserfiche Web Client upgrade |
| DPAPI credentials are machine-scoped | Credentials must be re-entered after migrating to a new server |
| Token refresh after key-ring rotation | If ASP.NET Data Protection keys rotate mid-session, session credentials may become unreadable; the user must log in again |
| MSI build requires Windows + WiX v4 | The `build/publish.ps1` script skips the MSI step on Linux/macOS |
| Document viewer page images | Dependent on Laserfiche API endpoint availability; not all page image routes confirmed |

---

## Document Archive

| Version | Date | Summary |
|---|---|---|
| 1.0.0 | 2026-08-01 | Initial release |
