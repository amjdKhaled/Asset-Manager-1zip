---
name: Scan root entry ID discovery
description: Why GetRootEntryIdAsync must always call ByPath, never short-circuit on the configured default value of 1.
---

## Rule
`GetRootEntryIdAsync` MUST always call the ByPath endpoint (`GET /Entries/ByPath?fullPath=\`) to obtain the authoritative repository root ID. The configured `RootEntryId` value is used ONLY as a fallback when ByPath fails.

## Why
The default `RootEntryId` in `appsettings.json` is 1. On some Laserfiche installations entry 1 is **not** the root folder — it may be a system entry or recycle bin. The old code short-circuited (`if (configuredRootId > 0) return configuredRootId`) because it could not distinguish "admin explicitly set it to 1" from "it's the factory default". This caused `GetAllFolderChildrenAsync` to call the folder-children endpoint on entry 1, which returned HTTP 400/404 (not a folder), silently caught as `[]`. The dashboard showed 0ms scan / 0 folders / 0 documents even though credentials and templates worked fine.

## How to apply
- `GetRootEntryIdAsync`: always attempt ByPath first; the result is cached per repo in `s_rootIdCache`. Fall back to `configuredRootId` only on exception or HTTP error.
- `TryParseByPathId`: handles both the wrapped shape `{"entry":{"id":N}}` (v1/some v2) and the direct shape `{"id":N,"entryType":"Folder"}` (some v2 builds).
- `GetAllFolderChildrenAsync`: follows `@odata.nextLink` pagination (cap 50 pages) using `ParseEntryListWithNextLink` and `ODataPagedList<T>`.
- Any new test that exercises `GetRootEntryIdAsync` must provide a mock ByPath response, not just rely on `configuredRootId > 0`.
