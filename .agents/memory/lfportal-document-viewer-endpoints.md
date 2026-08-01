---
name: LFPortal document viewer endpoints
description: Confirmed Repository API v1 edoc route and the deliberate boundary around unconfirmed image-page routes.
---

The Repository API v1 electronic-document route is `/Entries/{id}/Laserfiche.Repository.Document/edoc`. Proxy it server-side with response-header preservation and streaming; a 404 from this route means the valid entry may simply have no electronic document.

**Why:** The live Swagger evidence confirms the typed edoc route, but the exact Laserfiche page-list and page-image routes are not confirmed and must not be inferred from older code.

**How to apply:** Keep PDF and browser-supported edoc preview/download independent from page rendering. Add page navigation only after receiving Swagger evidence for the exact page endpoint and response shape.