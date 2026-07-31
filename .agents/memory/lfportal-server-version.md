---
name: LFPortal server version retrieval
description: How to get (or not get) the Laserfiche server version from the v1 API
---

## Rule

The Laserfiche Repository API v1 `GET /Repositories` response **does not include a server version field**. The `RepositoryDto` only returns `repoId`, `repoName`, and `webclientUrl`.

To obtain a version string, probe the HTTP response headers of any authenticated API call before falling back to a descriptive label.

## Header probe order

1. `x-server-version`
2. `x-laserfiche-api-version`
3. `x-api-version`
4. `api-version`
5. `x-powered-by`
6. `server`

If none of these headers is present, fall back to `"Laserfiche API v1"` (never `"Unknown"` — that's meaningless to the user).

## Connected User

The Laserfiche API v1 does **not** expose who is currently logged in. The only reliable source of the authenticated username is `ICredentialProvider.GetCredentialsAsync(repoKey)`. Inject both `ICredentialProvider` and `IRepositoryContext` into any service that needs to display the connected user.

**Why:** The LF REST API is stateless — authentication happens per-request via Bearer token. There is no "session info" endpoint.

## Search type expressions

`{LF:Document type}="Document"` and `{LF:Document type}="Folder"` are valid LF search expressions and return correct `TotalCount` on properly configured Laserfiche 10+ servers. However, some server configurations may not support this token.

**Fallback:** If both type-specific searches return 0 but a broad `{LF:Modify date}>="1900-01-01"` search returns non-zero results, fall back to sample-based type counting from the `entryType` field in search result items.
