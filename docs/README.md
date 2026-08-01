# Dashboard Documentation

## Phase 0 — Compatibility & Architecture

| Document | Purpose |
|----------|---------|
| [CompatibilityReport.md](CompatibilityReport.md) | Component version matrix, on-site verification checklist, known limitations |

## Architecture Decision Records (ADR)

Each major architectural decision has a corresponding ADR explaining what was decided,
why, and what alternatives were rejected.

| ADR | Title | Status |
|-----|-------|--------|
| [ADR-001](ADR/ADR-001-aspnetcore-mvc-over-blazor.md) | ASP.NET Core MVC selected over Blazor Server | ✅ Accepted |
| [ADR-002](ADR/ADR-002-clean-architecture.md) | Clean Architecture (4-layer) selected | ✅ Accepted |
| [ADR-003](ADR/ADR-003-desktop-extension-framework.md) | Desktop Extension target framework — .NET Framework 4.8 | ✅ Accepted |
| [ADR-004](ADR/ADR-004-repository-api-adapter.md) | Laserfiche Repository API adapter abstraction | ✅ Accepted |
| [ADR-005](ADR/ADR-005-dpapi-credential-storage.md) | Windows DPAPI for credential storage | ✅ Accepted |
| [ADR-006](ADR/ADR-006-rest-api-over-legacy-sdk.md) | REST API v1 over legacy RepositoryAccess SDK | ✅ Accepted |
| [ADR-007](ADR/ADR-007-multi-repository-abstraction.md) | Multi-repository abstraction via IRepositoryContext | ✅ Accepted |

## Desktop Extension (Phase 5)

| Document | Purpose |
|----------|---------|
| [LFDesktopExtension.md](LFDesktopExtension.md) | SDK version, .NET target framework, build instructions, deployment steps |

## Deployment (Phase 6)

| Document | Purpose |
|----------|---------|
| InstallationGuide.md | Prerequisites, step-by-step IIS install, first-time configuration |
| UpgradeGuide.md | In-place upgrade steps, what is preserved, rollback instructions |
| ReleaseNotes.md | Version history and changes |
