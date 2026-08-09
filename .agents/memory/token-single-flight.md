---
name: Token single-flight acquisition
description: Per-key SemaphoreSlim pattern in LaserficheAuthService preventing the 429 token storm from parallel dashboard API calls
---

## The rule
`GetTokenAsync` must use a per-cache-key `SemaphoreSlim(1,1)` with double-checked locking so that N concurrent callers for the same repository key produce exactly ONE token POST.

**Why:** Dashboard uses `Task.WhenAll` extensively (root children + templates + recursive folder scan all parallel). On cache miss, all N concurrent callers independently POST to the token endpoint → HTTP 429 storm. This was the primary cause of "Laserfiche API returned HTTP 429" in the installed Dashboard.

**How to apply:**
1. Fast path: `_cache.TryGetValue` (no lock) — return immediately if hit.
2. `_keyLocks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1,1))` — GetOrAdd is atomic.
3. `await sem.WaitAsync(ct)` — serialises concurrent misses.
4. Double-check cache again inside the lock — the winning concurrent caller has already set it.
5. If still miss: call `RequestTokenAsync`, cache result, release semaphore in `finally`.

The `ConcurrentDictionary<string, SemaphoreSlim>` is instance-level (service is singleton). Semaphores accumulate over the lifetime of the process but are bounded by sessions × repos and each weighs ~100 B.

## 429 retry
`RequestTokenAsync` retries up to `MaxTokenRetries=2` times on HTTP 429, honouring `Retry-After` (capped at 30 s) with exponential back-off fallback (1 s, 2 s). 400/401/403/404 are never retried.

## Authentication mode
`AuthenticationMode = "FallbackCredentials"` is always correct unless LFDS OAuth is configured. Do not change this field — it is accurate and intentional.
