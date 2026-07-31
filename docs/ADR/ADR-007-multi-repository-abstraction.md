# ADR-007 — Multi-Repository Abstraction via IRepositoryContext

**Date:** 2026-07-31
**Status:** Accepted
**Deciders:** Architecture review, Phase 0

---

## Context

The initial deployment of LFPortal connects to a single Laserfiche repository.
However, larger organizations often manage multiple repositories (e.g., a records
repository, a general documents repository, a test repository). The architecture must
not preclude this without requiring code changes.

Additionally, all service methods must know which repository they are operating against.
Passing the repository ID as a parameter through every controller action would scatter
repository-selection logic throughout the Web layer.

---

## Decision

Repository selection is abstracted through an **`IRepositoryContext` interface** in
the Application layer. All services and the `ILaserficheAuthService` token cache accept
`RepositoryDescriptor` (not a raw repository ID string) from `IRepositoryContext`.

```csharp
// In LFPortal.Application
public interface IRepositoryContext
{
    Task<RepositoryDescriptor> GetActiveRepositoryAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RepositoryDescriptor>> GetAllRepositoriesAsync(
        CancellationToken cancellationToken = default);
}

public sealed record RepositoryDescriptor(
    string Key,           // Unique config key, used as cache key
    string ServerUrl,     // e.g. https://lf-server/LFRepositoryAPI
    string RepositoryId,  // e.g. "Documents"
    string DisplayName    // e.g. "General Documents Repository"
);
```

Two implementations ship in Infrastructure:

| Implementation | Registration | Behavior |
|----------------|-------------|----------|
| `ConfigurationRepositoryContext` | Default (single repo) | Reads one repository from `LaserficheOptions` |
| `MultiRepositoryContext` | When `Repositories[]` array is in config | Reads the active repo from session/cookie or config default |

---

## Rationale

### Adding a repository requires zero code changes
To support a second repository, the administrator:
1. Adds a second entry to the `Repositories[]` array in `laserfiche.config.json`
2. Restarts the application pool
3. (If using `MultiRepositoryContext`) selects the active repository in the LF Settings page

No service, controller, or view is modified.

### Token cache isolation
The `ILaserficheAuthService` caches Bearer tokens keyed by `RepositoryDescriptor.Key`.
Each repository maintains an independent token lifecycle. Tokens for Repository A are
never used for Repository B.

### Single responsibility for repository selection
The Web layer (controllers) never decides which repository is active. They call
`IRepositoryContext.GetActiveRepositoryAsync()` and pass the result to services. This
keeps repository selection logic in one place.

### Testability
Any test can inject a mock `IRepositoryContext` returning a fixed `RepositoryDescriptor`
without needing a configuration file or a running Laserfiche server.

---

## Current Phase Scope

Phase 1 implements `ConfigurationRepositoryContext` only (single repository).
`MultiRepositoryContext` is implemented in Phase 4 (LF Settings) when the Settings
page adds repository-switching UI. The interface contract is stable from Phase 1.

---

## Consequences

- `LaserficheOptions` does not contain a single `RepositoryId` string at the top level.
  Instead, it contains either a single `Repository` object or a `Repositories[]` array.
  The `IRepositoryContext` implementation reads whichever shape is present.
- Controllers do not receive `repoId` as a route or query parameter in the initial
  implementation. The active repository is determined server-side by `IRepositoryContext`.
- If multi-repository support requires per-user active-repository selection in the
  future, `MultiRepositoryContext` can read from `IHttpContextAccessor` session state —
  this is an infrastructure change only; no service or controller changes.

---

## Alternatives Rejected

| Alternative | Reason rejected |
|-------------|-----------------|
| Hard-code a single `repoId` in `LaserficheOptions` | Requires architectural refactor to add a second repository later |
| Pass `repoId` as parameter to every service method | Scatters repository-selection logic into every controller; no single place to enforce policy |
| Session-based repo selection from day one | Unnecessary complexity for single-repo initial deployment; `IRepositoryContext` provides the abstraction without the complexity |
