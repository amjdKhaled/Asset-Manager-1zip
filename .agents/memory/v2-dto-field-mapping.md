---
name: V2 RepositoryDto field mapping
description: V2 GET /Repositories returns id/name/webClientUrl (camelCase) not repoId/repoName/webclientUrl. Parser must manually map both sets.
---

## Rule
V2 `GET /Repositories` items use **different property names** from V1:

| Field       | V1 JSON key    | V2 JSON key    |
|-------------|----------------|----------------|
| repo ID     | `repoId`       | `id`           |
| repo name   | `repoName`     | `name`         |
| web URL     | `webclientUrl` | `webClientUrl` |

`RepositoryJsonParser.TryParse` V2 branch manually iterates `JsonElement`s and probes both property name sets, so `RepositoryDto` properties are always populated.

**Why:** Deserializing the V2 `value` array directly into `List<RepositoryDto>` using `JsonSerializer.Deserialize` produced empty DTOs because the JSON property names did not match `[JsonPropertyName]` attributes. This caused `()` in the Discover dropdown and "repository not found" in Connection Status.

**How to apply:**
- Never deserialize V2 array elements into `RepositoryDto` via `JsonSerializer` — always use the manual element-mapping loop in `RepositoryJsonParser`.
- When adding new DTO fields, add element probing for both V1 and V2 key names in the loop.
- The V1 branch (plain array) still uses `JsonSerializer.Deserialize<List<RepositoryDto>>` safely, because V1 property names match the `[JsonPropertyName]` attributes.
