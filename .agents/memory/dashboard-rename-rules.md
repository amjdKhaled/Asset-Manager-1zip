---
name: Dashboard rename rules
description: What to rename vs. preserve when the product name "LFPortal" appears in code or docs.
---

## Rule

User-facing strings (page titles, nav logo, footer, version Display, Directory.Build.props Company/Product/Copyright, log file paths, all .md docs) say **Dashboard**.

Internal identifiers (C# namespaces `LFPortal.*`, class names, solution/project filenames, Laserfiche-specific names) stay as `LFPortal`.

**Why:** The product was renamed mid-project. Changing namespaces would be a large refactor with no functional benefit and would break any downstream code referencing the namespaces.

## Credential path backward compat

- Primary write path: `%ProgramData%\Dashboard\credentials\`
- Fallback read path: `%ProgramData%\LFPortal\credentials\` (DPAPI only; read-only)
- Implemented in `DpapiCredentialProvider.cs` via `LegacyCredentialDirectory` static field.
- Same pattern applies to extension config: `%ProgramData%\Dashboard\extension.config.json` → `%ProgramData%\LFPortal\extension.config.json`.

**How to apply:** Any new path under ProgramData should use `Dashboard` as the folder name and add a legacy fallback for `LFPortal`.
