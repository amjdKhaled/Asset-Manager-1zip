# ADR-006 — Laserfiche REST API V2 Selected Over Legacy RepositoryAccess SDK

**Date:** 2026-07-31
**Status:** Accepted
**Deciders:** Architecture review, Phase 0

---

## Context

Laserfiche provides two integration approaches for server communication:

- **Legacy RepositoryAccess SDK** — A .NET class library (`Laserfiche.RepositoryAccess`)
  that communicates with the Laserfiche Server using a proprietary binary protocol.
  Targets .NET Framework 4.x. Has been the primary integration method for many years.

- **Laserfiche Repository REST API (V1/V2)** — A modern RESTful HTTP API exposed by
  the separately installed Laserfiche API Server. Targets any HTTP client on any
  platform. V2 is the current standard; V1 is maintained for compatibility.

---

## Decision

LFPortal uses the **Laserfiche Repository REST API V2** exclusively via
`System.Net.Http.HttpClient`. The legacy `Laserfiche.RepositoryAccess` SDK is **not
referenced** in any project.

---

## Rationale

### .NET 8 compatibility
The legacy SDK targets .NET Framework 4.x and cannot be loaded in a .NET 8 process
without compatibility shims that introduce instability and support risk. The REST API
has no runtime dependency — it is pure HTTP.

### No proprietary binary protocol
The REST API uses standard HTTP/JSON. Every request can be inspected with standard tools
(browser dev tools, Fiddler, curl, Postman). Debugging a binary SDK protocol requires
Laserfiche-specific tools and expertise.

### Platform independence
The REST API works from any HTTP client on any OS. If LFPortal is ever hosted on Linux
(e.g., Azure App Service Linux), the REST API continues to work unchanged. The legacy
SDK is Windows-only.

### Vendor alignment
Laserfiche's official developer documentation and all new SDK client libraries target
the REST API. The legacy SDK is not actively developed. The REST API is the vendor's
stated direction.

### Future-proof for Cloud
LFPortal targets self-hosted Laserfiche today. The REST API V2 is nearly identical
between self-hosted and Cloud — the base URL and authentication flow differ, but
endpoint paths and request/response shapes are the same. A future Cloud migration
would require changing only the base URL and auth configuration.

---

## Consequences

- The Laserfiche API Server **must be installed** as a prerequisite on the LF Server
  machine. LFPortal cannot function without it. This is documented in
  `CompatibilityReport.md` and in `docs/InstallationGuide.md`.
- All LF API communication uses `IHttpClientFactory` with the Polly retry/circuit
  breaker policy defined in Phase 1.
- The `ILaserficheApiAdapter` (ADR-004) encapsulates all URL construction so the
  REST API version is swappable without touching service logic.
- The Desktop Extension (Phase 5) is a separate concern and may use the legacy SDK
  assembly only for the extension interface contract — it does **not** use the legacy
  SDK for data retrieval. All data retrieval goes through the REST API via the portal.

---

## Alternatives Rejected

| Alternative | Reason rejected |
|-------------|-----------------|
| Laserfiche.RepositoryAccess legacy SDK | .NET Framework only; binary protocol; no future Cloud path |
| Laserfiche REST API V1 only | V1 lacks authorization_code flow for future LFDS/AD integration |
| Mixed (SDK for some operations, REST for others) | Inconsistent; duplicated auth logic; two integration surfaces to maintain |
