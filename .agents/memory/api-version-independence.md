---
name: API version independence
description: How Auto/v1/v2 API version selection, runtime detection, and installer preservation work.
---

# API version independence (Auto / v1 / v2)

**Rule:** URLs are never built from the raw `ApiVersion` setting — always from
`LaserficheOptions.EffectiveApiVersion` (explicit pin wins; `Auto` resolves to the
persisted `DetectedApiVersion`, falling back to `v1` until detection completes).

**Detection:** `ApiVersionDetectionService` (BackgroundService) probes
`{root}{basePath}/v2/Repositories` then v1; HTTP 200/401/403 = route exists,
404/405/transport = not. Persists via `SaveDetectedApiVersionAsync`. Loop-safe:
it only writes when `DetectedApiVersion` is empty, and
`SaveConnectionSettingsAsync` clears `DetectedApiVersion` so a settings save
triggers a fresh probe.

**Why 401/403 count as "exists":** an authenticated server rejects the
unauthenticated probe with 401 even when the version is served — a strict
200-only rule would misdetect v2-capable servers as v1.

**Config write serialization:** `PortalConfigurationService` has two writers
(admin saves + detection). Read-merge-write is guarded by a `SemaphoreSlim` in
addition to the atomic temp+move file write.

**Installer backward compat:** MSI property `LF_API_VERSION` intentionally has
NO default. Empty (direct-MSI repair/upgrade) → `WriteConfigAction` preserves
the existing file's `ApiVersion`; fresh installs default to `Auto` inside
`BuildLaserficheConfig`. The BA wizard preselects the version read from the
existing `laserfiche.config.json` so upgrades never silently repin to Auto.
publish.ps1 smoke test includes a repair-simulation asserting a pinned v1
survives a WriteConfig run without `--api-version`.

**Caveat:** v1/v2 endpoint *shapes* differ (e.g. /Searches is v1; /Entries/Search
is v2-only) — version-independent URLs do not guarantee v2 payload compatibility.
