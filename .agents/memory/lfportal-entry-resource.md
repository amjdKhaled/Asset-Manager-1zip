---
name: LFPortal EntryResource naming
description: Naming collision between the adapter enum and the service's local response model.
---

## Rule

In `LaserficheEntryService`, the local JSON response model record must NOT be named `EntryResource` — that name is already taken by the `LFPortal.Infrastructure.Adapters.EntryResource` enum. The compiler resolves the inner type first, causing the adapter enum calls to fail.

**Fix applied:** Rename the local record to `EntryApiResource`. Qualify adapter enum references as `Adapters.EntryResource.Details` etc.

**How to apply:** Whenever adding a new private response model record inside a service class, check if the name collides with any adapter enums in scope. Prefer suffixing with `Resource`, `ApiResource`, or `Response` to keep names distinct.
