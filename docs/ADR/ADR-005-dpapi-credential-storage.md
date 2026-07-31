# ADR-005 — Windows DPAPI Selected for Credential Storage

**Date:** 2026-07-31
**Status:** Accepted
**Deciders:** Architecture review, Phase 0

---

## Context

LFPortal must authenticate with the Laserfiche API Server using a username and password.
These credentials must be stored somewhere the application can retrieve them at runtime.
The project requirement explicitly prohibits storing credentials as plain text in any
configuration file.

The deployment target is Windows Server in an on-premises government environment, so
Windows-native credential storage mechanisms are available.

---

## Decision

**Windows DPAPI (`System.Security.Cryptography.ProtectedData`)** is the default
credential storage mechanism for production deployments, implemented behind an
**`ICredentialProvider` interface** in the Application layer.

`LaserficheOptions` (bound from `appsettings.json`) contains **only non-sensitive
values**: `ServerUrl`, `RepositoryId`, `TimeoutSeconds`, `CredentialProvider` (enum).
Credentials are never present in any configuration file.

---

## `ICredentialProvider` Interface

```csharp
// In LFPortal.Application
public interface ICredentialProvider
{
    Task<LaserficheCredential> GetCredentialsAsync(
        string repositoryKey,
        CancellationToken cancellationToken = default);

    Task StoreCredentialsAsync(
        string repositoryKey,
        string username,
        string password,
        CancellationToken cancellationToken = default);
}
```

Three implementations in Infrastructure, selected by the `CredentialProvider` config value:

| Implementation | Config value | Use case |
|----------------|-------------|----------|
| `DpapiCredentialProvider` | `"DPAPI"` | Default production (IIS service account, machine scope) |
| `WindowsCredentialManagerProvider` | `"CredentialManager"` | When admin prefers GUI-based management |
| `EnvironmentVariableCredentialProvider` | `"Environment"` | Development only (Replit, CI) |

---

## How DPAPI Works in IIS Context

DPAPI encrypts data using a key derived from the Windows account that performs the
encryption. For IIS service accounts:

- **Machine scope** (`DataProtectionScope.LocalMachine`) is used so any process running
  on the same machine can decrypt, regardless of which service account is active.
  This is the correct scope for an IIS Application Pool identity.
- Encrypted bytes are stored in `%ProgramData%\LFPortal\credentials.dat` (not in
  `wwwroot` or any web-accessible location).
- The LF Settings page (Phase 4) calls `ICredentialProvider.StoreCredentialsAsync`
  when the administrator saves credentials — the credentials are encrypted on write
  and never held in memory beyond the immediate authentication call.

---

## Rationale

### Windows-native — no external key management system
DPAPI is built into every Windows Server installation. There is no additional
infrastructure, no license, and no internet dependency. This is critical for the
air-gapped deployment target.

### Service account isolation
When machine-scope DPAPI is used under an IIS Application Pool identity, the encrypted
blob is tied to the machine. Moving `credentials.dat` to another machine without the
same Windows machine key renders it unreadable — an appropriate security boundary for
government deployments.

### ICredentialProvider is swappable
Because all credential access goes through `ICredentialProvider`, a future requirement
(Azure Key Vault, HashiCorp Vault, or a government-specific HSM) is satisfied by
implementing a new provider and changing one line in DI registration. No service,
controller, or view changes.

### Plain text is never acceptable
Storing `password=PlainText` in `appsettings.json` means credentials are in source
control, in IIS configuration exports, and in any backup of the web application
directory. DPAPI eliminates this risk entirely.

---

## Credential Logging Policy

- Credentials are **never logged** at any log level
- `LaserficheCredential` overrides `ToString()` to return `"[REDACTED]"`
- Structured logging templates must never include credential properties

---

## Consequences

- The LF Settings page must provide a UI for entering and saving credentials (via
  `ICredentialProvider.StoreCredentialsAsync`). The saved password field displays
  only a masked placeholder after initial save.
- The installer (Phase 6) must ensure the `%ProgramData%\LFPortal\` directory is
  created with appropriate ACLs (readable/writable by the IIS Application Pool identity,
  not readable by other users).
- Upgrading LFPortal does not affect `credentials.dat` — the installer's `NeverOverwrite`
  policy on config files applies equally to the credentials store.

---

## Alternatives Rejected

| Alternative | Reason rejected |
|-------------|-----------------|
| Plain text in `appsettings.json` | Explicitly prohibited by project requirements |
| Plain text in environment variables | Visible in IIS manager, process listings, and system audit logs |
| Windows Credential Manager only | Not accessible from IIS Application Pool identity without additional configuration; DPAPI is simpler for this use case |
| Azure Key Vault | Cloud dependency; violates the zero-internet-dependency requirement |
