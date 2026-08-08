---
name: Web Client launch bypass — no login required
description: Web Client sessions use DPAPI credentials directly; SessionAuthGuardMiddleware must NOT redirect them to /Login.
---

## Rule
`"Laserfiche Web Client"` is **not** in `SessionAuthGuardMiddleware.GuardedSources`.

Only `"Laserfiche Desktop Client"` requires the Login form. Web Client and direct-browser sessions both use the Dashboard's own DPAPI-protected server-side credentials; no username/password form is shown.

**Why:** Web Client sets `source=webclient` in the launch URL → `RepositorySessionMiddleware` writes `ActiveRepositorySource = "Laserfiche Web Client"`. Before the fix, this source was in `GuardedSources`, which caused the guard to compare `AuthenticatedRepositoryId` vs `ActiveRepositoryId`. Since Web Client never calls the Login form, `AuthenticatedRepositoryId` was never set, and every Web Client launch was redirected to `/Login`.

**How to apply:**
- The guard checks `GuardedSources.Contains(source)`. If source is absent or is `"Laserfiche Web Client"`, the guard falls into the "direct browser" branch which only redirects when *both* session repo and configured repo are empty.
- `RepositorySessionMiddleware` runs *before* the guard, so `ActiveRepositoryId` is already set from `?repository=` by the time the guard runs.
- If a new launch source is added, decide explicitly whether it should require Login. Do not add it to `GuardedSources` unless intentional.
