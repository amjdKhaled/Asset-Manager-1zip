---
name: MSI ExeCommand trailing-backslash quoting
description: Why quoting an MSI directory property in ExeCommand corrupts the argument and rolls back the install
---

# MSI ExeCommand trailing-backslash quoting

**Rule:** Never write `"[SOMEDIRPROPERTY]"` in a WiX ExeCommand. MSI directory properties expand with a trailing backslash, so the command line ends in `\"` — CommandLineToArgvW treats that as an escaped literal quote and the receiving process gets a path containing a `"` character. On net48, `Path.Combine` then throws "Illegal characters in path"; with `Return="check"` this rolls back the whole install (Error 1722 / bundle 0x80070643).

**Fix pattern:** use `"[SOMEDIRPROPERTY]."` (trailing dot after the backslash) in the WXS, AND defensively sanitize path arguments in the helper (strip `"` and invalid path chars, normalize trailing `\.`) — see `PathUtil.SanitizeDir` in Dashboard.SetupHelper.

**Diagnosis tip:** MSI log shows only "returned actual error code 1" for ExeCommand CAs; stdout of a deferred elevated EXE is not reliably captured. The helper now writes `%ProgramData%\Dashboard\Logs\SetupHelper.log` with the full exception chain — check there first.

**Prevention:** publish.ps1 smoke-tests the freshly staged helper with the exact MSI command line (raw Start-Process argument string, reproducing the `\"` quoting and `--display-name ""`) and compares SHA256 of built vs staged EXE before building the MSI.
