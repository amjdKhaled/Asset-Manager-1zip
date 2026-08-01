---
name: LFPortal session auth (Desktop Client login flow)
description: How Desktop Client sessions are authenticated, how session credentials are stored, and the guard that protects Dashboard/Archive/Document.
---

# LFPortal Session Auth — Desktop Client Login Flow

## Rule
Desktop Client sessions (ActiveRepositorySource == "Laserfiche Desktop Client") require a per-session login via `/Login` before Dashboard/Archive/Document are accessible. Settings-stored credentials remain as fallback for direct browser access only.

**Why:** The ?repository= URL parameter identifies which repo to open, but has no credentials. Before this change, Dashboard opened immediately with 0 data if Settings credentials weren't pre-configured for that repository.

## Session keys
| Key | Set by | Value |
|---|---|---|
| `ActiveRepositoryId` | `RepositorySessionMiddleware` | Repository name from `?repository=` |
| `ActiveRepositorySource` | `RepositorySessionMiddleware` | `"Laserfiche Desktop Client"` |
| `AuthenticatedRepositoryId` | `LoginController` (on success) | Repository that was authenticated |
| `SessionCredUsername` | `SessionCredentialStore` | Plain-text username |
| `SessionCredPasswordProtected` | `SessionCredentialStore` | Data Protection–encrypted password |

## Credential stack (priority order for `ICredentialProvider`)
1. **Session credentials** — `SessionCredentialStore` reads from ASP.NET session. Set by `LoginController` after `TryAuthenticateAsync` succeeds.
2. **Disk store** — DPAPI (Windows) / Data Protection (non-Windows) via `CredentialChainProvider`.
3. **Environment variables** — `EnvironmentVariableCredentialProvider` fallback.

## New types added
- `ISessionCredentialStore` / `SessionCredentialStore` — in `LFPortal.Application/Interfaces` and `LFPortal.Infrastructure/Credentials`
- `SessionAwareCredentialProvider` — composite `ICredentialProvider` singleton in `LFPortal.Infrastructure/Credentials`
- `SessionAuthGuardMiddleware` — in `LFPortal.Web/Middleware`; excluded paths: /Login, /Settings, /health, /Home
- `LoginController` + `LoginViewModel` + `LoginInputModel` — in `LFPortal.Web/Controllers`
- `Views/Login/Index.cshtml` — standalone page (no _Layout.cshtml)

## `ILaserficheAuthService.TryAuthenticateAsync`
Accepts explicit username/password (bypasses ICredentialProvider), warms token cache on success, returns false on HTTP 4xx, propagates on 5xx/network. Never logs the password.

## DI registration (ServiceCollectionExtensions)
`CredentialChainProvider` registered as concrete singleton, then `ISessionCredentialStore` singleton, then `ICredentialProvider` wired as `SessionAwareCredentialProvider` (which takes both). All singletons; safe because `IHttpContextAccessor` reads per-request context at call time.

## Change Account
`GET /Login/SignOut` — clears `AuthenticatedRepositoryId` + session credentials, keeps `ActiveRepositoryId`. Redirects to `/Login`. Does NOT clear disk credentials.

## Web Client source

`?repository=<repo>&source=webclient` → `RepositorySessionMiddleware` stamps session with `"Laserfiche Web Client"`.
`?repository=<repo>` (no source, backward-compat Desktop) → stamps `"Laserfiche Desktop Client"`.

`SessionAuthGuardMiddleware.GuardedSources` contains both strings. Direct browser access (no `?repository=`) is unguarded.

## How to apply
Any time a new controller is added that should be protected for Desktop/Web Client sessions, the `SessionAuthGuardMiddleware` covers it automatically (only `/Login`, `/Settings`, `/health`, `/Home` are excluded). No per-controller attribute needed.

## Web Client JS button
`wwwroot/js/lf-webclient-button.js` — self-contained IIFE with no dependencies. Deploy to the Laserfiche Web Client server and set `DASHBOARD_BASE_URL`. Uses 5-strategy repo detection (URL params, hash, path, JS globals, DOM). See `docs/WebClientIntegration.md` for installation steps.
