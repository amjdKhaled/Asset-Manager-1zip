# Dashboard — Upgrade Guide

---

## Upgrade from any previous Dashboard version

### What is preserved

| Item | Preserved? | Notes |
|---|---|---|
| `%ProgramData%\Dashboard\laserfiche.config.json` | ✅ Yes | `NeverOverwrite` in installer |
| `%ProgramData%\Dashboard\extension.config.json` | ✅ Yes | `NeverOverwrite` in installer |
| `%ProgramData%\Dashboard\credentials\*.dpapi` | ✅ Yes | Not touched by installer |
| `%ProgramData%\Dashboard\logs\` | ✅ Yes | Not touched by installer |
| IIS Site `Dashboard` | ✅ Yes | Updated in-place; not recreated |
| IIS Application Pool `Dashboard` | ✅ Yes | Updated in-place |
| Start Menu shortcuts | ✅ Yes | Recreated at same locations |
| Web application binaries | 🔄 Updated | `C:\Program Files\Dashboard\WebApp\` |
| Desktop Extension binaries | 🔄 Updated | `C:\Program Files\Dashboard\Extension\` |
| `appsettings.json` | 🔄 Updated | Contains only schema, no credentials |
| `ConfigTemplate\*.json` | 🔄 Updated | Always shows latest config schema |

### What is NOT preserved

| Item | Notes |
|---|---|
| Laserfiche Web Client `Browse.aspx` edits | Must re-run `Deploy-WebClientButton.ps1` after each Laserfiche upgrade |

---

## Steps to upgrade

1. **Run the new MSI** as Administrator:

   ```cmd
   msiexec /i Dashboard-1.x.x-Setup.msi /quiet /norestart
   ```

   The installer automatically removes the previous version and installs the new one.  
   Configuration files in `%ProgramData%\Dashboard\` are untouched.

2. **Verify IIS** is still running after the upgrade:

   ```powershell
   Get-WebSite -Name "Dashboard" | Select-Object Name, State, PhysicalPath
   ```

3. **Verify the web application:**

   ```
   http://localhost:5000/health
   http://localhost:5000/
   ```

4. **Re-register the Desktop Extension** (recommended after every upgrade):

   ```cmd
   "C:\Program Files\Dashboard\Extension\Dashboard.DesktopExtension.exe" --setup --silent
   ```

   This ensures the toolbar command path points to the new EXE location and picks up any registration changes.

5. **Check the Web Client button** (if deployed):

   The `lf-dashboard-button.js` file in Laserfiche's `assets\custom\` is NOT changed by the Dashboard upgrade or by Laserfiche upgrades. No action is required unless you are also upgrading the Laserfiche Web Client itself.

   After a **Laserfiche** Web Client upgrade (Laserfiche overwrites Browse.aspx):

   ```powershell
   .\Deploy-WebClientButton.ps1
   ```

---

## Credential preservation

DPAPI-encrypted credential files (`%ProgramData%\Dashboard\credentials\*.dpapi`) are NOT touched by the Dashboard installer. They are bound to the Windows machine's key and continue working after an upgrade **as long as**:

- The same machine is used (DPAPI keys are machine-scoped)
- The machine has not been re-imaged or re-joined to a domain
- The IIS Application Pool identity has not changed

If credentials must be re-entered (e.g., after a machine rebuild), open `http://localhost:5000/Settings` and save the credentials again.

---

## Rollback

The MSI installer does not support automatic rollback to a previous version. To roll back:

1. Uninstall the current version: `msiexec /x Dashboard-1.x.x-Setup.msi`
2. Re-install the previous version: `msiexec /i Dashboard-1.x.x-Setup.msi`

Configuration in `%ProgramData%\Dashboard\` is preserved across both steps.

### Roll back the Web Client button

If the Web Client button stops working after a `Deploy-WebClientButton.ps1` run:

```powershell
.\Deploy-WebClientButton.ps1 -Rollback
```

This restores the most recent `Browse.aspx.bak-<timestamp>` backup.

---

## Cross-machine migration

To move Dashboard to a new server:

1. **Export config** from the old server:
   - Copy `%ProgramData%\Dashboard\laserfiche.config.json`
   - Copy `%ProgramData%\Dashboard\extension.config.json`
   - **Do NOT copy credentials** — DPAPI blobs are machine-specific and cannot be decrypted on a different machine

2. **Install** Dashboard on the new server (standard MSI installation)

3. **Restore config files** to `%ProgramData%\Dashboard\`

4. **Re-enter credentials** in `http://new-server:5000/Settings`

5. **Update DASHBOARD_BASE_URL** in `lf-dashboard-button.js` to point to the new server

---

## Checking upgrade history

The extension log records each setup invocation:

```
%ProgramData%\Dashboard\logs\extension.log
```

The web application log records startup and configuration loading:

```
C:\Program Files\Dashboard\WebApp\logs\dashboard-YYYYMMDD.log
```
