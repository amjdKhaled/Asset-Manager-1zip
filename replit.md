# Dashboard — Laserfiche Enterprise Administration Portal

## Project Overview

Dashboard is a professional enterprise web portal for administering and navigating a
self-hosted Laserfiche Document Management System. It is a pure presentation layer over
Laserfiche — all data is sourced directly from the live Laserfiche Repository API v1.
There is no local database, no mock data, and no AI or search pipeline.

**Technology stack:**
- **Runtime:** .NET 8 / ASP.NET Core MVC + Razor Views
- **Architecture:** Clean 4-layer (Domain → Application → Infrastructure → Web)
- **HTTP:** `IHttpClientFactory` with `BearerTokenHandler`, Polly-backed standard resilience
- **Logging:** Serilog → rolling file sink (production), console sink (development)
- **Credentials:** Windows DPAPI (production) / environment variables (development)
- **Deployment target:** Windows Server + IIS (in-process ASP.NET Core Module v2)

## Solution Structure

```
LFPortal.sln
src/
  LFPortal.Domain/            # Entities, value objects, exceptions — no dependencies
  LFPortal.Application/       # Service interfaces, DTOs — depends on Domain only
  LFPortal.Infrastructure/    # HTTP services, auth, credentials, health checks
  LFPortal.Web/               # MVC controllers, Razor views, Program.cs
  Dashboard.DesktopExtension/ # Laserfiche toolbar button (net48, build on Windows only)
docs/
  README.md
  CompatibilityReport.md
  LFDesktopExtension.md       # Desktop extension build & deployment guide
  ADR/                        # Architecture Decision Records 001–007
```

## Key Architectural Decisions (see docs/ADR/)

| Decision | Record |
|---|---|
| MVC + Razor over Blazor Server | ADR-001 |
| 4-layer Clean Architecture | ADR-002 |
| Desktop Extension — .NET Framework 4.8 | ADR-003 |
| `ILaserficheApiAdapter` for URL isolation | ADR-004 |
| DPAPI credential storage | ADR-005 |
| REST API v1 over legacy SDK | ADR-006 |
| `IRepositoryContext` multi-repo abstraction | ADR-007 |

## Development Setup

### Prerequisites
- .NET 8 SDK
- Laserfiche API Server accessible at `ServerUrl` in `appsettings.json`

### Environment variables (development)
```
LF_USERNAME=<laserfiche-username>
LF_PASSWORD=<laserfiche-password>
```

### Run locally
```bash
cd src/LFPortal.Web
dotnet run
# Opens on http://localhost:5000
```

### Build (release)
```bash
dotnet build LFPortal.sln --configuration Release
```

### Publish for IIS
```bash
dotnet publish src/LFPortal.Web/LFPortal.Web.csproj \
  -c Release -r win-x64 --self-contained false \
  -o C:\inetpub\wwwroot\Dashboard
```

## Phase Status

| Phase | Task | Status |
|---|---|---|
| Phase 0 | Compatibility verification & ADRs | ✅ Complete |
| Phase 1 | Solution scaffold & LF infrastructure | ✅ Complete |
| Phase 2 | Dashboard page (live LF data) | ✅ Complete |
| Phase 3 | Document Archive browser | 🔲 Not started |
| Phase 4 | Settings page — credential UI & runtime reconfiguration | ✅ Complete |
| Phase 5 | Desktop Client Extension | ✅ Build verified; dynamic repo context, icon, Status→Settings, Desktop Client badge complete |
| Phase 6 | MSI Installer & IIS deployment package | ⏸ Not started |

## Quality Gates (apply to every phase)

- `dotnet restore` succeeds
- `dotnet build --configuration Release` → zero errors, zero warnings (LFPortal.sln)
- `Dashboard.DesktopExtension` builds separately on Windows with Laserfiche SDK 10.4
- No TODO / FIXME / placeholder / fake data in source
- No hard-coded values (config via `appsettings.json` + `IOptions<T>`)
- XML doc comments on all public types
- Documentation updated for any new architectural decisions

## API Endpoints

| Endpoint | Description |
|---|---|
| `GET /` | Redirects to Dashboard (Home controller backward-compat) |
| `GET /health` | ASP.NET Core health check (JSON) |
| `GET /api/laserfiche/status` | Live Laserfiche connection status (JSON) |
| `GET /api/laserfiche/repository` | Active repository descriptor (JSON) |
| `POST /api/laserfiche/test-connection` | Test connection with explicit credentials |

## Credential Storage Paths

| Path | Purpose |
|------|---------|
| `%ProgramData%\Dashboard\credentials\` | Primary (DPAPI-encrypted, Windows) |
| `%ProgramData%\LFPortal\credentials\` | Legacy fallback (read-only, backward compat) |

## User Preferences

- Always use C# record types for immutable value objects and DTOs.
- Always use `sealed` on classes that are not designed for inheritance.
- Credentials must never appear in configuration files, logs, or exception messages.
- All data must come from the live Laserfiche API — never from local state, mock objects, or defaults.
- Target `net8.0`; do not reference .NET Framework assemblies from the MVC/Infrastructure projects.
- `Dashboard.DesktopExtension` targets `net48` and is built separately on Windows.
- Internal C# namespaces remain `LFPortal.*` for backward compatibility; only user-facing strings say "Dashboard".
