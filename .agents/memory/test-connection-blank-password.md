---
name: TestConnection and Save — blank password resolves from DPAPI store
description: Settings page never returns the stored password to the browser; blank Password field means "use the stored value".
---

## Rule
`SettingsController.TestConnection` and `Save` treat a blank `Password` field as "use the DPAPI-stored password", not "password is missing".

Effective credential resolution order:
1. Use form-posted value if non-blank.
2. Otherwise call `ICredentialProvider.GetCredentialsAsync("default")` and use stored value.
3. If no stored credentials exist and form is blank, return a clear error — do not silently proceed with empty credentials.

**Why:** The Settings page intentionally never renders the stored DPAPI password in the HTML. The Password `<input>` is always empty on page load. Before the fix, `TestConnection` rejected blank password with "All four fields must be filled in", making it impossible to test without retyping the password every time.

**How to apply:**
- `TestConnection` only requires `ServerUrl` and `RepositoryId` to be non-blank (no stored fallback for those). Username and Password fall back to the store.
- `Save`: if `Username` is provided but `Password` is blank, load the stored password and re-save with the new username + old password. If both are blank, skip credential write (preserve what's stored). If both are filled, save both as-is.
- Never log or render the credential values.
