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

**CONFIRMED WORKING FOLDER CHILDREN URL**: `/Entries/{id}/Laserfiche.Repository.Folder/children?$top=N`
- This OData-typed path IS the correct v1 endpoint (confirmed in Swagger by user).
- The entry ID must be the real root — NOT assumed to be 1.

**ROOT ENTRY IS NOT ALWAYS ID=1**: This server has root at ID=250. Hardcoding 1 causes all folder scans to return 0.
- Root discovery: `GET /Entries?entryPath=%5C&fallbackToClosestAncestor=false` returns the root entry object.
- Fallback: check entry 1's `parentId` — if `parentId == 0` then 1 is root.
- Result is cached process-wide in `LaserficheEntryService.s_rootIdCache` (static `ConcurrentDictionary`).
- Dashboard calls `_entryService.GetRootEntryIdAsync(ct)` before scanning — never hardcodes 1.

**Why:** The original `laserficheGetFolderChildren` used the OData-typed path. Using search expressions (`{LF:Document type}="Document"`) is unreliable — some LF server configurations don't support that token, causing false 0-count results.

## entryType field parsing

`ParseEntryType` handles both:
- Simple values: `"Document"`, `"Folder"`, `"Shortcut"`, `"RecordSeries"`
- OData qualified: `"#Laserfiche.Repository.Document"` etc. (strip `#`, split on `.`, take last segment)

`EntryApiResource` captures both `"entryType"` AND `"@odata.type"` fields. `MapEntry` prefers `entryType`, falls back to `@odata.type`.

`ParseEntryList` handles both OData envelope `{"value":[...]}` and bare JSON array `[...]`.

## All logging must be Warning/Error (not Debug) for production visibility

When folder children calls fail silently, every error should be logged at Warning or Error. Debug logs don't appear in default IIS/Windows event log configurations.

## /Dashboard/Probe endpoint

`GET /Dashboard/Probe` fires 6 raw API calls and shows full request URL + HTTP status + raw response body for each. Use this to diagnose which endpoint fails and what the actual JSON field names are on the real LF server.
