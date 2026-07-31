---
name: LFPortal dashboard data sources
description: How the Analytics Dashboard gets its data — matches original GovSearch AI implementation
---

## Rule

The Analytics Dashboard must get its data from three sources — exactly as the original Node.js GovSearch AI backend did:

### 1. Recursive folder tree scan (Laserfiche API)

- Call `GET /Entries/1/Laserfiche.Repository.Folder/children` (v1 OData typed path) to get root children.
- For each root-level folder, call `ScanFolderAsync` recursively (with a `ConcurrentDictionary<int,byte>` visited set).
- Collect per-folder: `documents` count, `folders` count, `templateCounts` dict, `allDocs` list.
- From `allDocs`: derive `recentDocs` (sorted by creationTime) and `modifiedDocs` (sorted by lastModifiedTime).
- Cap total docs at `DOC_CAP = 120` (matches original `const DOC_CAP = 120`).
- Parallelize root folder scans with `Task.WhenAll`.

This gives: `TotalDocuments`, `TotalFolders`, `DocsWithTemplate`, `DocsWithoutTemplate`, `TemplateStats`, `RootFolders`, `RecentDocs`, `ModifiedDocs`, `AllDocs`.

### 2. Template definitions (Laserfiche API)

- `GET /TemplateDefinitions` → `IReadOnlyList<LFTemplateDefinition>`
- `TotalTemplates = templateDefs.Count`
- Fetched in parallel with the root children call.

### 3. In-memory search audit log (portal-side)

- `InMemorySearchAuditLog` (singleton, `ConcurrentQueue`, capped at 10 000 entries).
- Records every search submitted through the portal via `ISearchAuditLog.RecordSearchAsync(query)`.
- Provides: `SearchActivityByDay` (7-day rolling), `TopSearchedQueries` (top 5), `TotalSearches`.
- Does NOT persist across restarts — matches the original `MemStorage` behavior.

## @media in Razor views

Always escape `@media` CSS rules inside Razor `@section Styles` blocks as `@@media` to prevent Razor from treating `@` as a code transition.

## `@{` inside @if blocks

Move `@{ var x = ... }` computations to the top-level `@{ }` block at the top of the view (before `@section Styles`) rather than placing them inside `@if` blocks. Razor's parser can reject `@{` inside an `@if` block's HTML context in some configurations.

## Key URL for folder children

- Primary:  `/Entries/{id}/Laserfiche.Repository.Folder/children?$top=1000`  
- Fallback: `/Entries/{id}/children?$top=1000`

Both are tried by `GetAllFolderChildrenAsync` in the entry service.

**Why:** The original `laserficheGetFolderChildren` used the OData-typed path. Using search expressions (`{LF:Document type}="Document"`) is unreliable — some LF server configurations don't support that token, causing false 0-count results.
