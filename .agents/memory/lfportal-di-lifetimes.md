---
name: LFPortal DI lifetime rules
description: Correct service lifetimes for the Laserfiche Infrastructure layer — violations cause runtime captive-dependency errors.
---

## Rule

| Service | Lifetime | Reason |
|---|---|---|
| `ILaserficheApiAdapter` | Singleton | URL builder — immutable from options |
| `IRepositoryContext` | Singleton | Config-driven — stateless |
| `ICredentialProvider` | Singleton | Stateless OS reads; loggers are singleton-safe |
| `ILaserficheAuthService` | Singleton | Token cache is IMemoryCache (singleton) |
| `BearerTokenHandler` | Transient | HttpClientFactory manages its lifetime |
| All domain services | Scoped | HttpClient usage scoped per request |

**Why:** `AddHttpMessageHandler<T>()` resolves `T` from the root provider for the message handler pipeline. Any scoped service injected into the handler will cause a "Cannot resolve scoped service from root provider" error at startup. The fix is to make the handler's dependencies singleton-safe.

**How to apply:** When adding new infrastructure services that need to be injected into `BearerTokenHandler` or any other message handler, verify the new service is singleton-safe before registering it at singleton lifetime.
