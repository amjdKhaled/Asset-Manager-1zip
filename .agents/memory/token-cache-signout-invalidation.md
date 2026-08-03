---
name: Token cache sign-out invalidation
description: Why session token eviction uses generation-numbered cache keys instead of explicit key removal
---

# Token cache sign-out invalidation

Rule: to invalidate all IMemoryCache tokens for a session on sign-out, embed a per-session-scope generation counter in the cache key and increment it — do NOT track and Remove() keys.

**Why:** Explicit eviction has a race: an in-flight token acquisition (started under the old account) can `Set` a fresh token *after* the eviction pass, resurrecting the old account's token in the live session scope. With generation keys, the in-flight write lands under the old generation and is never read again (expires by TTL). Architect review flagged the tracking-based approach as a severe sign-out boundary race; the generation approach is race-free by construction and needs no cleanup of tracked-key dictionaries.

**How to apply:** Any "invalidate a whole scope of cache entries" need (per session, per user, per tenant) on IMemoryCache — bump a scope generation embedded in the key. Also note: invalidate BEFORE clearing session keys, because scope resolution requires an established session (`session.Keys.Any()`).

Related: search audit log is repository-scoped (`InMemorySearchAuditLog` entries carry RepositoryId; case-insensitive, trimmed match) and is fed by `LaserficheSearchService.ExecuteSearchAsync` — it previously had NO producer at all.
