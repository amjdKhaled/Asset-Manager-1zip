---
name: LFPortal Swagger evidence
description: How to distinguish documented Laserfiche routes from response-schema evidence.
---

Swagger screenshots establish the available HTTP route and method, but they do not
establish the JSON response property names. The live response body from the documented
endpoint is the source of truth for repository identification and mapping.

**Why:** Inferring a repository-info route or response schema from a different API
version can make authentication appear successful while the connection test fails.

**How to apply:** For repository validation, authenticate with the documented token
route, call only the documented `GET /Repositories` route, log the raw response safely,
and update mapping only after observing the actual server response.