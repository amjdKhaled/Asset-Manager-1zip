# ADR-004 — Laserfiche Repository API Adapter Abstraction

**Date:** 2026-07-31
**Status:** Accepted
**Deciders:** Architecture review, Phase 0

---

## Context

The Laserfiche Repository API has two self-hosted versions (V1 and V2) with different
URL patterns and capability differences. The vendor may release V3 or break V2 in a
future Laserfiche Server release. Additionally, the exact base path of the self-hosted
API Server (`/LFRepositoryAPI/`) may differ between installations.

Without an abstraction layer, every service method in Infrastructure would contain
hard-coded URL strings. A version change would require editing dozens of methods across
multiple files.

---

## Decision

All Laserfiche API URL construction is centralized behind an **`ILaserficheApiAdapter`
interface** in the Infrastructure layer.

```csharp
internal interface ILaserficheApiAdapter
{
    string ApiVersion { get; }          // "v1" or "v2"
    string BuildRepositoriesUrl();
    string BuildTokenUrl(string repoId);
    string BuildEntryUrl(string repoId, int entryId, EntryResource resource);
    string BuildSearchUrl(string repoId, SearchType searchType);
    string BuildEdocUrl(string repoId, int entryId);
    string BuildPageImageUrl(string repoId, int entryId, int pageNumber);
}
```

The current implementation is `LaserficheV2ApiAdapter`. Every service in Infrastructure
receives `ILaserficheApiAdapter` via constructor injection and calls only its methods
to construct URLs — no raw URL strings appear outside the adapter.

---

## Rationale

### Version isolation
When Laserfiche releases a breaking API change, a new adapter (`LaserficheV3ApiAdapter`)
is implemented and registered in DI. **No service method changes.** The Application
layer and Web layer are completely unaffected.

### Configuration flexibility
The base path (`/LFRepositoryAPI/`) is read from `LaserficheOptions.ApiBasePath` and
owned by the adapter. Installations that use a non-default IIS virtual path are
supported by changing one config value, not by editing service code.

### Testability
Any service that takes `ILaserficheApiAdapter` can be unit-tested with a mock adapter
that returns predictable URLs, independent of any running Laserfiche server.

### Single source of truth for URL patterns
URL structure is defined once, in one place. Finding "where is the search endpoint
called" is a search for `ILaserficheApiAdapter.BuildSearchUrl`, not a grep through
every service file.

---

## Implementation Note

`ILaserficheApiAdapter` is an **Infrastructure-internal** interface — it is not exposed
to the Application layer. The Application layer defines service contracts
(`ILaserficheSearchService`, etc.); the adapter is an implementation detail of how
those services reach the Laserfiche API. This keeps the Application layer clean and
independent of URL concerns.

---

## V1 vs V2 Difference Handling

| Capability | V1 | V2 | Adapter responsibility |
|------------|----|----|----------------------|
| Token endpoint | `/v1/Repositories/{repoId}/Token` | `/v2/Repositories/{repoId}/Token` | `BuildTokenUrl` returns correct path |
| Search | Simple only | Simple + Advanced expression | `BuildSearchUrl(SearchType)` routes to correct endpoint |
| Auth flows | Username/password | Username/password + authorization_code | Adapter exposes `SupportedAuthFlows` |

---

## Consequences

- URL strings are tested as part of adapter unit tests, not buried in service integration tests
- Switching API versions in production is a DI configuration change with no code edits
- The adapter is responsible for knowing whether a given capability exists in the current
  API version; services that call an unavailable capability receive a clear exception
  from the adapter, not an HTTP 404 from Laserfiche

---

## Alternatives Rejected

| Alternative | Reason rejected |
|-------------|-----------------|
| Hard-coded URL strings in each service | Brittle; version change requires editing every service |
| Base URL in config only (no adapter) | Does not handle endpoint-level differences between V1 and V2 |
| Single `HttpClient` extension method per endpoint | Scatters URL construction; no single place to update on version change |
