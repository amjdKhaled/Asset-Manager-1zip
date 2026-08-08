---
name: V2 folder-children route
description: The Laserfiche Repository API changed the folder-children path between V1 and V2; using the V1 path on a V2 server returns HTTP 404.
---

## Rule
`BuildFolderChildrenUrl` must be version-aware. The folder-children path changed between V1 and V2:

| Version | Path |
|---------|------|
| V1 | `GET /Entries/{id}/Laserfiche.Repository.Folder/children` |
| V2 | `GET /Entries/{id}/Folder/Children?groupByEntryType=false&formatFieldValues=false` |

Sending the V1 OData-typed cast path (`Laserfiche.Repository.Folder/children`) to a V2 server returns **HTTP 404 Not Found**, which was silently swallowed by `GetAllFolderChildrenAsync` and treated as an empty folder, producing "0 folders, 0 documents, 0ms scan" on the Dashboard.

**Why:** V2 dropped the OData-typed action path in favour of a simpler route. The query params `groupByEntryType=false&formatFieldValues=false` are documented in the V2 Swagger and must be included.

**How to apply:**
- `LaserficheApiAdapter.BuildFolderChildrenUrl` branches on `ApiVersion.Equals("v2", ...)`.
- `BuildEntryUrl(FolderChildren)` delegates to `BuildFolderChildrenUrl` so both code paths stay in sync.
- 10 regression tests in `LaserficheApiAdapterUrlTests` guard the exact V2 URL and prove V1 is unchanged.
- Do NOT apply this rename to any other endpoint — only `Folder/Children` was confirmed different.
