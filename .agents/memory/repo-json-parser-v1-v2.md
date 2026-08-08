---
name: Repository JSON parser — V1 vs V2 response shapes
description: Two different JSON shapes from GET /Repositories; RepositoryJsonParser handles both transparently.
---

## Rule
`GET /Repositories` returns different shapes by API version:
- **V1**: plain JSON array `[{"repoId":"...","repoName":"...","webclientUrl":"..."}]`
- **V2**: OData envelope `{"value":[{"repoId":"...",...}],"@odata.context":"..."}`

`RepositoryJsonParser.TryParse(body, out shape)` handles both — returns `null` for any other shape (never throws).
`RepositoryJsonParser.IsCompatibleShape(body)` is used by `ApiVersionDetectionService.RouteExistsAsync` to reject HTTP 200 responses that are not a recognizable repo list.

**Why:** Before the parser was introduced, `DeserializeRepositories` called `JsonSerializer.Deserialize<List<RepositoryDto>>(body)` which threw a `JsonException` on V2 OData bodies. Auto-detect (`RouteExistsAsync`) previously accepted any HTTP 200 without reading the body, so it selected V2 even though the V2 OData body would crash the parser.

**How to apply:**
- Never call `JsonSerializer.Deserialize<List<RepositoryDto>>` directly on the raw repo body — always go through `RepositoryJsonParser.TryParse`.
- Any new code that probes `GET /Repositories` for version detection must call `IsCompatibleShape(body)` before accepting the version as valid.
- `TestConnectionAsync` falls back to "connected (discovery limited)" when `LaserficheException(statusCode=200)` is thrown — this distinguishes "server reachable but format unknown" from "truly disconnected".
