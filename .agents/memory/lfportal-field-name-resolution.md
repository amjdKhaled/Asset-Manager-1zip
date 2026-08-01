---
name: LFPortal field name resolution
description: How to get correct metadata field names for a document in the Archive browser.
---

# LFPortal field name resolution

## Rule
`GET /Entries/{id}/fields` returns `fieldDefinitionId` + `value` (and sometimes an inline `name`). The inline `name` may be empty on some server builds. Always join by `fieldDefinitionId` against `GET /FieldDefinitions` to get reliable human-readable names.

**Why:** On the live installation the entry fields response did not populate the inline `name` field reliably. `GET /FieldDefinitions` (confirmed available) returns the authoritative name keyed by `id`.

## How to apply
- `LFFieldValue.FieldDefinitionId` carries the numeric join key.
- `ILaserficheFieldDefinitionService.GetFieldDefinitionsAsync()` fetches the repository-wide dictionary `int → LFFieldDefinition`.
- `ArchiveController.Detail` calls both services and resolves names: prefers FieldDefinitions name, falls back to inline name, discards fields with no name at all.
- Debug logging emits `EntryId`, `Template`, `EntryFields`, `FieldDefinitions` count, and resolved names — check these first if fields appear empty.

## Confirmed endpoints
- `GET /v1/Repositories/{repoId}/Entries/{entryId}/fields` — entry field values
- `GET /v1/Repositories/{repoId}/FieldDefinitions` — repository-wide field schema
