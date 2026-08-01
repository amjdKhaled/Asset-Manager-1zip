---
name: LFPortal document viewer endpoints
description: Confirmed Repository API v1 edoc route and the deliberate boundary around unconfirmed image-page routes.
---

The Repository API v1 routes:
- Electronic document: `GET /Entries/{id}/Laserfiche.Repository.Document/edoc` — a 404 means the valid entry has no edoc.
- Page list: `GET /Entries/{id}/pages` — returns OData `{ value: [{ pageNumber, width, height, mimeType }] }`.
- Page image: `GET /Entries/{id}/pages/{pageNumber}/image` — returns the raw image with content-type header.

All three are proxied server-side in `DocumentController`. `GetPageImageAsync` returns `LaserficheEdocStream` so the content type is preserved through the proxy. Page navigation uses JavaScript prev/next buttons updating a single `<img>` tag's `src`.

**Why:** Credentials must never be exposed to the browser; all three routes require a bearer token.

**How to apply:** Keep edoc preview independent from page rendering. The `Pages` collection on `DocumentViewModel` is only populated when `HasElectronicDocument` is false and `HasLaserfichePages` is true.