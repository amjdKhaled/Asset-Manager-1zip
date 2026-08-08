---
name: SSO OAuth2 Architecture
description: LFDS Authorization Code + PKCE flow implementation decisions and V1/V2 token compatibility
---

## Rule
V2 token endpoint is always used for LFDS authorization code exchange (`BuildTokenUrlV2`); V1 resource endpoints remain unchanged and accept the V2-issued token because token validation is server-side regardless of URL version.

**Why:** LFDS only issues codes redeemable at `/v2/Repositories/{id}/Token`. The Bearer token is a Laserfiche Server session credential, not tied to an API path version. Both v1 and v2 resource URLs accept it.

**How to apply:** `LaserficheApiAdapter.BuildTokenUrlV2` always hardcodes `v2`; `EffectiveApiVersion` continues to control all resource endpoint URLs. After `ExchangeAuthorizationCodeAsync` succeeds, the token is stored under the same `CacheKeyFor(repository)` key that `GetTokenAsync`/`BearerTokenHandler` use — no code change needed downstream.

## Loop prevention
Guard → `/Login` (form) → if `Sso.IsConfigured && !ssoFailed` → redirect to `/Login/StartSso`. If callback fails → `RedirectToAction("Index","Login", new{ssoFailed=true})`. The `ssoFailed=true` query param prevents the index action from auto-redirecting again.

## State security
- State string: 32 bytes cryptographic random, base64url-encoded → 43 chars
- State stored in `OAuthStateStore` (IMemoryCache, 10-minute TTL) AND in ASP.NET session
- Callback validates: state matches session (CSRF), entry exists and not expired (validity), entry not used (anti-replay), repository matches (anti-tampering)
- Anti-replay: `OAuthStateStore.TryConsume` marks entry used and removes from cache atomically

## Config structure
`Laserfiche:Sso:LfdsBaseUrl` — empty = SSO disabled (password form shown)
`Laserfiche:Sso:ClientId` — default "LFDashboard"
`Laserfiche:Sso:RedirectUri` — empty = computed from request at runtime

## New files
- `src/LFPortal.Infrastructure/OAuth/` — LaserficheOAuthOptions, OAuthStateEntry, IOAuthStateStore, OAuthStateStore
- `src/LFPortal.Infrastructure/Adapters/ILaserficheApiAdapter.cs` — added BuildTokenUrlV2
- `src/LFPortal.Application/Interfaces/ILaserficheAuthService.cs` — added ExchangeAuthorizationCodeAsync
- `src/LFPortal.Infrastructure/Services/LaserficheAuthService.cs` — implemented ExchangeAuthorizationCodeAsync
- `src/LFPortal.Web/Controllers/LoginController.cs` — rewritten; added StartSso, Callback actions
