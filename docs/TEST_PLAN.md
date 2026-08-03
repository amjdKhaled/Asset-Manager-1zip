# Dashboard — Production Verification Test Plan (Phase 1)

Purpose: verify the Phase 1 configuration architecture on real Windows machines
before Phase 2 begins. Run every scenario on a machine that is NOT the
development machine. Record PASS/FAIL and evidence (log path / screenshot) per step.

Conventions used below:
- `PD` = `%ProgramData%\Dashboard`
- `PF` = the WebApp install directory under `%ProgramFiles%`
- "Installer config" = `PD\laserfiche.config.json` (wizard-owned)
- "Runtime config" = `PD\laserfiche.runtime.json` (Settings-page-owned)

---

## 1. Clean install (machine that never had Dashboard)

| # | Step | Expected result |
|---|------|-----------------|
| 1.1 | Run the bundle as admin; complete the wizard with a detected LF API URL and a non-default port (e.g. 5100) | Install succeeds, no errors in wizard log |
| 1.2 | Inspect `PD\laserfiche.config.json` | Contains `Laserfiche.ServerUrl` = wizard value; NO `RepositoryId`, NO `DisplayName` |
| 1.3 | Inspect `PF\appsettings.json` | `Urls` = `http://0.0.0.0:5100`; `Laserfiche.ServerUrl` is empty; no repository keys |
| 1.4 | Confirm `PF\appsettings.Development.json` does not exist | Absent |
| 1.5 | Browse to the Dashboard URL | App starts, login page prompts for repository — WITHOUT visiting the Settings page first |
| 1.6 | Check startup log line `Laserfiche config: ServerUrl=` | Shows the WIZARD-entered API URL → proves the installer-written ProgramData config is read |
| 1.7 | Open Settings page | Displays the same ServerUrl the wizard wrote (same configuration source) |
| 1.8 | Save Settings (change timeout only) | `PD\laserfiche.runtime.json` is created; `PD\laserfiche.config.json` is UNCHANGED; nothing written under `PF` |
| 1.9 | `icacls PD` and subfolders | NETWORK SERVICE has write on `PD`, `PD\credentials`, `PD\logs` |
| 1.10 | Save credentials in Settings; log in | Credential file appears in `PD\credentials`; login succeeds |
| 1.11 | Stop app pool, corrupt nothing, restart | Settings values still in effect (reload from runtime config) |

## 2. Upgrade from an old (pre-Phase-1) version

| # | Step | Expected result |
|---|------|-----------------|
| 2.1 | On a machine with the OLD version installed and settings saved via old Settings page (`PF\config\laserfiche.json`) and old credentials (`%ProgramData%\LFPortal\credentials`), run the new bundle | Same-version/major upgrade completes; old bundle does not strip the Web Client button |
| 2.2 | Browse to Dashboard without touching Settings | App works using LEGACY content-root config values (backward-compat read) |
| 2.3 | Login with previously saved credentials | Works via legacy `LFPortal\credentials` read fallback |
| 2.4 | Save Settings once | Values migrate to `PD\laserfiche.runtime.json`; legacy file no longer authoritative (runtime file wins) |
| 2.5 | Re-save credentials once | New file in `PD\credentials`; subsequent logins use it |

## 3. Repair

| # | Step | Expected result |
|---|------|-----------------|
| 3.1 | Save non-default Settings values first, then run MSI Repair | Repair completes |
| 3.2 | Inspect `PD\laserfiche.runtime.json` | UNTOUCHED — administrator settings survive repair |
| 3.3 | Inspect `PD\laserfiche.config.json` | Rewritten by wizard/stored values; still contains no repository keys |
| 3.4 | Check IIS binding port | Still the ORIGINALLY chosen port, not reset to 5000 (known open issue — task ref 34; record result) |
| 3.5 | Browse Dashboard, Web Client button, Desktop Client button | All still work |

## 4. Uninstall

| # | Step | Expected result |
|---|------|-----------------|
| 4.1 | Uninstall from Apps & Features | Completes without error |
| 4.2 | `PF` WebApp directory | Removed |
| 4.3 | `PD` configs and credentials | PRESERVED (Permanent components — by design, documented) |
| 4.4 | Web Client `Browse.aspx` | Button script reference removed; page loads cleanly |
| 4.5 | IIS site + app pool | Removed |

## 5. Reinstall (after uninstall on same machine)

| # | Step | Expected result |
|---|------|-----------------|
| 5.1 | Run bundle again | Install succeeds; wizard pre-fills from preserved `PD` config where applicable |
| 5.2 | Browse Dashboard, log in with previously saved credentials | Works — DPAPI files survived uninstall and machine is unchanged |
| 5.3 | `PD\laserfiche.runtime.json` | Still intact; Settings values as before uninstall |

## 6. Second machine (different name / repository / IIS site / certificate / URL)

| # | Step | Expected result |
|---|------|-----------------|
| 6.1 | Install on machine 2 (different computer name, different LF repository names, different cert, different port and Dashboard URL) | Wizard auto-detects THAT machine's API binding, host, and cert — no value from machine 1 appears anywhere |
| 6.2 | grep the deployed `lf-dashboard-button.js` | `DASHBOARD_BASE_URL` = machine 2's wizard URL, not localhost, not machine 1 |
| 6.3 | `PD\extension.config.json` | `portalUrl` = machine 2's URL |
| 6.4 | Open Dashboard from Web Client and from Desktop Client on machine 2 | Both open the correct URL; repository comes from the client context (`?repository=`) |
| 6.5 | Direct browser access with no repository configured | Login page prompts for repository — no default repository appears |

## 7. Negative / no-silent-fallback checks

| # | Step | Expected result |
|---|------|-----------------|
| 7.1 | Temporarily remove NETWORK SERVICE write ACL from `PD`, save Settings | Save FAILS with a clear permission error — no silent write into `PF` |
| 7.2 | Delete `PD\extension.config.json`, click Desktop Client button | Clear "not configured" behavior per current design — record whether it silently opens localhost (known open issue; Phase 3 scope) |
| 7.3 | Point ServerUrl at an unreachable host | Health check reports degraded with classified message; app does not crash |
| 7.4 | Machine with no LF API installed | Wizard requires manual URL entry; no invented localhost API URL is written |
| 7.5 | TLS: API on self-signed HTTPS | Wizard offers trust step; after install, API calls succeed; no certificate validation is disabled in code |

## 8. Hardcoded-value confirmation (any machine)

| # | Step | Expected result |
|---|------|-----------------|
| 8.1 | `findstr /s /i "corp.local desktop-k1svi53" <staged WebApp>` | No hits in deployed files |
| 8.2 | Staged `appsettings.json` | No ServerUrl value, no RepositoryId/DisplayName (also enforced by publish.ps1 preflight) |
| 8.3 | Deployed files contain machine names/URLs ONLY in installer-written config files | Confirmed |

---

## Known open items (tracked, not Phase 1 regressions)

1. Direct-MSI repair may reset a non-default port to 5000 (project task ref 34).
2. Dashboard IIS site is HTTP-only; an `https://` Dashboard URL (entered or HSTS-upgraded) yields ERR_SSL_PROTOCOL_ERROR — see SSL root-cause report; fix scheduled before/with Phase 2.
3. Desktop Extension silently falls back to `http://localhost:5000` when its config file is missing (Phase 3 scope).
4. WebView2 temp profile folders accumulate in `%TEMP%` (Phase 3 scope).
