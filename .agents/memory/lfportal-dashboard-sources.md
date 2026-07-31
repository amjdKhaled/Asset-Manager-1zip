---
name: LFPortal dashboard data sources
description: Which endpoints the dashboard uses, root discovery strategy, and which paths are invalid.
---

## Confirmed working endpoints (Swagger-documented, /Repositories/{repo}/ scoped)

All valid v1 URLs include `/Repositories/{repo}/` in the path:

| Purpose | URL pattern |
|---|---|
| Root discovery | `GET /Repositories/{repo}/Entries/ByPath?fullPath=%5C` |
| Single entry | `GET /Repositories/{repo}/Entries/{id}` |
| Folder children | `GET /Repositories/{repo}/Entries/{id}/Laserfiche.Repository.Folder/children` |
| Template definitions | `GET /Repositories/{repo}/TemplateDefinitions` |
| Field definitions | `GET /Repositories/{repo}/FieldDefinitions` |
| Searches | `GET /Repositories/{repo}/Searches` |

## Invalid endpoints — do NOT use

These paths were confirmed invalid on this server by Probe results:

- `GET /Entries?entryPath=\` — does not exist
- `GET /Entries/{id}/children` — does not exist
- `GET /Entries/{id}/children?$top=N` — does not exist
- Any URL missing the `/Repositories/{repo}/` scope prefix

## Root discovery

Use `GET /Repositories/{repo}/Entries/ByPath?fullPath=%5C` (backslash = LF root).  
Returns a single Entry object; read `id` field. Cache in `s_rootIdCache` per repo.  
**Never hardcode entry ID 1.** The root is ID=250 on this server.  
If ByPath fails, throw `LaserficheException` — do not silently default to 1.

## Folder children (dashboard recursive scan)

Use `BuildFolderChildrenUrl` → `/Repositories/{repo}/Entries/{id}/Laserfiche.Repository.Folder/children?$top=1000`.  
No fallback URLs. One call only.

**Why:** The original code used path-guessing fallbacks (/children, entryPath query param) that don't exist on this server, causing all folder scans to silently return 0 entries and all dashboard widgets to show zero.
