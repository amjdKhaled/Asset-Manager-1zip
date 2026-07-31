---
name: LFPortal v1 repository response
description: Confirmed JSON shape returned by the Laserfiche v1 repository-list endpoint.
---

Laserfiche v1 `GET /LFRepositoryAPI/v1/Repositories` returns a root JSON array.
Each item uses `repoId`, `repoName`, and `webclientUrl`. Repository validation must
compare the configured repository ID to `repoId`, case-insensitively.

**Why:** Treating the array as a wrapper object caused a successful authenticated
request to be reported as a connection failure.

**How to apply:** Deserialize directly to a list of repository DTOs, find the
configured `repoId`, and only then return a successful connection status.