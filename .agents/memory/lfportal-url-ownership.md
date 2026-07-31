---
name: LFPortal URL ownership
description: Durable rule for composing Laserfiche server URLs and API paths.
---

The repository context must provide only the Laserfiche server root. URL adapters own
appending the configured API base path and version, and must normalize an already
path-suffixed input before composing the final endpoint.

**Why:** Combining the base path in both the repository descriptor and the adapter
produced requests such as `/LFRepositoryAPI/LFRepositoryAPI/v1/...`, which Laserfiche
correctly rejected.

**How to apply:** When adding a Laserfiche request path or connection-test path, pass
the server root into the adapter and verify the resulting URL has exactly one API
base-path segment.