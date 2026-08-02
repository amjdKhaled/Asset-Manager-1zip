---
name: Repository is runtime session context
description: Architecture rule — Laserfiche repository must never be install-time config; token cache must be session-scoped.
---

**Rule:** The Laserfiche repository is per-session runtime context: Desktop/Web Client pass `?repository=` at launch; direct browser users pick a repository on the login page. It is never an installer setting; `Laserfiche:RepositoryId` in config is only an OPTIONAL direct-browser fallback (no `[Required]`, Settings page allows blank, template files ship it empty — the old `"YourRepositoryId"` sentinel is scrubbed by SetupHelper).

**Why:** Fixing a repo at install time broke multi-repository use and forced reinstalls to switch repositories; a stale placeholder repo also suppressed the login-page repository picker (guard middleware only redirects when session AND configured repo are both empty).

**How to apply:**
- Never reintroduce repo/display-name into the wizard, Bundle variables, MSI properties, or WriteConfig args. SetupHelper still accepts `--repo-id/--display-name` as legacy no-ops for old repair command lines.
- Token cache keys in the auth service must include repository id AND the session id of ESTABLISHED sessions (`session.Keys.Any()`); otherwise different repos/users share Bearer tokens. Empty sessions use `app` scope (disk-fallback creds), which also avoids per-request re-auth.
- Auth service `TryAuthenticateAsync` returns false only for HTTP 400/401/403; 404 (unknown repo), 5xx, TLS, socket, timeout propagate so the login page can show a classified error instead of "check username and password".
- Session Id in ASP.NET Core is only stable after data is written to the session — never key caches on the id of an empty session.
