---
name: LFPortal search endpoint versions
description: Which search paths belong to v1 vs v2, and how the response flow differs
---

## Rule

| SearchType | v1 endpoint | v2 endpoint (DO NOT USE) |
|---|---|---|
| Simple | `POST /Repositories/{repoId}/SimpleSearches` | same |
| Advanced | `POST /Repositories/{repoId}/Searches` | `POST /Repositories/{repoId}/Entries/Search` |

`Entries/Search` does not exist in v1. It returns HTTP 405 on a v1 server.

## Response flow

**SimpleSearches (v1, synchronous)**
- Submit → server returns OData collection directly in the 200 response body.
- Body contains `"value": [...]` at the root.
- No operationToken, no polling required.
- Client-side pagination must be applied.

**Searches (v1, async long-operation)**
- Submit → server returns `{ operationToken, status }`.
- Poll `GET /Repositories/{repoId}/Tasks/{token}` until status == "Completed".
- Fetch results from `GET /Repositories/{repoId}/SearchResults/{token}?$top=N&$skip=N&$count=true`.

## Detection in code

Detect which path to take by checking for `"value"` property in the submit response JSON:
- If `value` present → inline OData → parse directly.
- If `operationToken` present → async → poll.

**Why:** v1 server returns HTTP 405 for `Entries/Search`. The inline-vs-async detection prevents empty results when SimpleSearches returns everything synchronously.
