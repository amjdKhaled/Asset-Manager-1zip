---
name: SetupHelper smoke test quoting
description: Why the smoke test must use Start-Process -ArgumentList array form and trailing \. instead of a raw argument string.
---

**Rule:** The publish.ps1 SetupHelper smoke test must use `Start-Process -ArgumentList @(...)` (array form) with `$smokeWebApp + "\."` for `--webapp-path`, NOT a raw string with `'"' + $smokeWebApp + '\"'`.

**Why:** When `-ArgumentList` receives a single string, PowerShell passes it verbatim to `CreateProcess`, and `CommandLineToArgvW` parses it in the child. A trailing `\"` in `--webapp-path "C:\...\WebApp\"` is interpreted as an escaped quote (not the closing delimiter), so the parser treats everything after it — including `--config-dir "C:\...\Config"` — as part of the `--webapp-path` value. Result: `--config-dir` is silently swallowed, the default ProgramData path is used, and the smoke test reports [OK] even though parsing was wrong.

**The fix:** Array form lets PowerShell quote each element properly. Appending `\.` to the WebApp path (matching what the MSI's `"[WEBAPPFOLDER]."` trick produces) ensures PathUtil.SanitizeDir is exercised and the trailing `\.` is stripped correctly.

**How to apply:**
```powershell
$smokeWebAppArg = $smokeWebApp + "\."  # reproduces [WEBAPPFOLDER]. MSI behavior
Start-Process -FilePath $helper -ArgumentList @(
    "--write-config",
    "--url",         "http://...",
    "--webapp-path", $smokeWebAppArg,
    "--config-dir",  $smokeConfig
) -NoNewWindow -Wait -PassThru -RedirectStandardOutput $stdoutFile
```

**Validation must check** (not just exit code):
- `Config directory:` in stdout equals `$smokeConfig`, not `%ProgramData%\Dashboard`
- `laserfiche.config.json` exists at `$smokeConfig\`, not ProgramData
- `webapp-path` in stdout does NOT contain `"--config-dir"`
