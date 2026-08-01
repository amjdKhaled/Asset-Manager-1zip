---
name: LFPortal field name resolution
description: How to get correct metadata field names for a document in the Archive browser.
---

# LFPortal field name resolution

## Rule
`GET /Entries/{id}/fields?formatValue=false` returns an OData `value` array. Each record uses `fieldId`, `fieldName`, `fieldType`, and a nested `values[]` array whose items contain `value` and `position`. The inline `fieldName` is authoritative for the actual document field; `fieldId` can be joined to repository-wide `FieldDefinitions` when additional schema information is needed.

**Why:** The live payload uses `fieldId`/`fieldName`/`values[]`, while the original parser expected `fieldDefinitionId`/`name`/direct `value`, producing zero usable display values.

## How to apply
- `LFFieldValue.FieldDefinitionId` carries the numeric `fieldId` join key.
- `ILaserficheFieldDefinitionService.GetFieldDefinitionsAsync()` fetches the repository-wide dictionary `int → LFFieldDefinition`.
- `ArchiveController.Detail` calls both services and resolves names: prefers FieldDefinitions name, falls back to inline `fieldName`, and retains unnamed records with an ID label rather than silently dropping values.
- `LaserficheEntryService.GetEntryFieldsAsync()` logs the complete response before parsing, accepts the confirmed OData envelope, and flattens all nested `values[]` items in position order.
- Debug logging emits `EntryId`, `Template`, `EntryFields`, `FieldDefinitions` count, and resolved names — check these first if fields appear empty.

## Confirmed endpoints
- `GET /v1/Repositories/{repoId}/Entries/{entryId}/fields?formatValue=false` — entry field values
- `GET /v1/Repositories/{repoId}/FieldDefinitions` — repository-wide field schema
