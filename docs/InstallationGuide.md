# Dashboard — Installation Guide

**Version:** 1.0.0  
**Target environment:** Windows Server 2019/2022 or Windows 10/11, on-premises

---

## Contents

1. [Prerequisites](#1-prerequisites)
2. [Installer EXE](#2-installer-exe)
3. [IIS Configuration Verification](#3-iis-configuration-verification)
4. [Initial Laserfiche Connection Configuration](#4-initial-laserfiche-connection-configuration)
5. [Desktop Client Extension](#5-desktop-client-extension)
6. [Laserfiche Web Client Integration](#6-laserfiche-web-client-integration)
7. [First Login](#7-first-login)
8. [Blank Password Behavior](#8-blank-password-behavior)
9. [Log Locations](#9-log-locations)
10. [Verification Checklist](#10-verification-checklist)
11. [Installing on a New Machine](#11-installing-on-a-new-machine)
12. [Troubleshooting](#12-troubleshooting)

---

## 1. Prerequisites

Install each prerequisite **before** running the Dashboard installer.

| Prerequisite | Version | Notes |
|---|---|---|
| Windows Server / Windows 10+ | — | 64-bit required |
| IIS | 10.0+ | Enable: Web Server, Application Development, ASP.NET |
| ASP.NET Core 8 Hosting Bundle | 8.0.x | **Must be installed first** |
| Microsoft Edge WebView2 Runtime | Evergreen | Required for Desktop Extension only |
| Laserfiche Desktop Client | 12.x | Required for Desktop Extension only |
| Laserfiche Web Client (Web Access) | 12.x | Required for Web Client button only |

### Install IIS

```powershell
# Windows Server (PowerShell, as Administrator):
Install-WindowsFeature -Name Web-Server, Web-Asp-Net45, Web-Mgmt-Console -IncludeManagementTools

# Windows 10/11:
# Control Panel -> Programs -> Turn Windows features on/off
# Check: Internet Information Services -> World Wide Web Services -> Application Development Features -> ASP.NET 4.8
```

### Install ASP.NET Core 8 Hosting Bundle

Download from: https://dotnet.microsoft.com/download/dotnet/8.0  
Choose **Windows Hosting Bundle** (not the SDK or Runtime).

Verify after installation:
```powershell
dotnet --list-runtimes | Select-String "Microsoft.AspNetCore.App 8"
```

---

## 2. Installer EXE

Copy the complete `Release` folder to the target 64-bit Windows computer, then
run as **Administrator**:

```
LFDashboard-Setup.exe
```

The wizard detects the computer name, Laserfiche API and Web Client paths where
possible. In most deployments the administrator only confirms the detected
Laserfiche API URL; port and integration options remain under Advanced Settings.

The installer performs:
- Creates `C:\Program Files\Dashboard\WebApp\` and publishes the Dashboard web application
- Creates `C:\Program Files\Dashboard\Extension\` and copies the Desktop Extension files
- Creates IIS Application Pool `Dashboard` (No Managed Code, Integrated pipeline)
- Creates IIS Web Site `Dashboard` on port **5000**, pointing to `WebApp\`
- Creates `%ProgramData%\Dashboard\` with subdirectories:
  - `credentials\` — DPAPI-encrypted credentials (written by the Settings page)
  - `logs\` — extension diagnostic log
  - `ConfigTemplate\` — latest config schema reference files
- Creates **Start Menu** shortcuts: Open Dashboard, Configure Dashboard, Uninstall Dashboard
- Runs `Dashboard.DesktopExtension.exe --setup --silent` to register the toolbar button

### Repair or uninstall

Run `LFDashboard-Setup.exe` again after installation. The wizard automatically
switches to maintenance mode with two choices:

- **Repair** restores installer-managed files and components while preserving configuration.
- **Uninstall** removes the application, IIS site/app pool, shortcuts, and integrations.

Uninstall preserves `%ProgramData%\Dashboard` by default. Select **Also delete
saved configuration, credentials, and logs** only when a complete cleanup is
required. The same uninstall is available from Windows **Installed apps** and
the Start Menu shortcut.

### Silent / unattended installation (advanced)

```cmd
LFDashboard-Setup.exe /quiet
```

Use the internal MSI only for managed enterprise deployment and supply every
required property explicitly.

### Custom port (advanced MSI deployment)

```cmd
msiexec /i Dashboard-1.0.0-Setup.msi DASHBOARD_PORT=80
```

The interactive wizard checks that the chosen TCP port is free before starting
installation. Pick another port if it reports a conflict.

---

## 3. IIS Configuration Verification

After installation, verify in IIS Manager:

1. Open **IIS Manager** -> Application Pools -> confirm `Dashboard` exists, `.NET CLR Version = No Managed Code`, `Managed Pipeline Mode = Integrated`
2. Open **Sites** -> confirm `Dashboard` site exists, bound to port 5000
3. Open a browser on the server: `http://localhost:5000/` -> should show the Dashboard

**If you see "HTTP Error 500.30":**  
The ASP.NET Core Hosting Bundle may not be installed correctly. Re-run the Hosting Bundle installer and restart IIS (`iisreset`).

**If you see "HTTP Error 502.5":**  
IIS cannot find the `dotnet` runtime. Re-run the Hosting Bundle installer.

---

## 4. Initial Laserfiche Connection Configuration

The Dashboard ships with placeholder configuration. Before the Dashboard will display data, you must configure the Laserfiche connection.

### Option A — Settings Page (recommended)

1. Browse to `http://localhost:5000/Settings` (or `http://YOUR-SERVER:5000/Settings`)
2. Enter:
   - **API Server URL**: the URL of your Laserfiche Server's Repository API endpoint  
     Example: `https://lf-server.corp.local/LFRepositoryAPI`
   - **Repository ID**: the Laserfiche repository name (e.g., `Documents`)
   - **Username** and **Password**: a Laserfiche service account with read access
3. Click **Test Connection** to verify
4. Click **Save** — credentials are encrypted with Windows DPAPI and stored in `%ProgramData%\Dashboard\credentials\`

### Option B — Configure-Dashboard.ps1

For scripted or automated deployment:

```powershell
.\Tools\Configure-Dashboard.ps1 `
    -DashboardUrl "http://192.168.1.50:5000" `
    -LaserficheApiUrl "https://lf-server/LFRepositoryAPI" `
    -RepositoryId "Documents" `
    -DisplayName "Documents Repository"
```

Then enter credentials via the Settings page (credentials are never stored in JSON files).

> **Configuration file location:**  
> `%ProgramData%\Dashboard\laserfiche.config.json` is created by the installer with placeholder values.  
> The Settings page writes credentials separately via DPAPI — they are **never** stored in this JSON file.

---

## 5. Desktop Client Extension

### Requirement

The Desktop Extension is registered with the Laserfiche Desktop Client toolbar during MSI installation. It requires:
- Laserfiche Desktop Client 12.x to be installed **before** Dashboard is installed
- Microsoft Edge WebView2 Runtime (Evergreen or Fixed)

### Verify registration

1. Open the **Laserfiche Desktop Client**
2. Look for a **Dashboard** toolbar (top area)
3. Click the **Dashboard** button — a Dashboard popup window should appear

### Manual registration (if the toolbar button is missing)

Run as Administrator:
```cmd
"C:\Program Files\Dashboard\Extension\Dashboard.DesktopExtension.exe" --setup
```

### Configure the extension

The extension reads `%ProgramData%\Dashboard\extension.config.json` for the Dashboard URL.

Edit the file directly:

```json
{
  "portalUrl": "http://YOUR-DASHBOARD-SERVER:5000",
  "buttonLabel": "Dashboard",
  "iconPath": ""
}
```

Or use the configure script:

```powershell
.\Tools\Configure-Dashboard.ps1 -DashboardUrl "http://192.168.1.50:5000"
```

After changing the URL, re-register:
```cmd
"C:\Program Files\Dashboard\Extension\Dashboard.DesktopExtension.exe" --setup --silent
```

### WebView2 Runtime

The Desktop Extension opens Dashboard in an embedded WebView2 window. If WebView2 is not installed:

1. Download the Evergreen WebView2 Runtime: https://developer.microsoft.com/microsoft-edge/webview2/
2. Install it on the machine running the Laserfiche Desktop Client
3. Re-run `Dashboard.DesktopExtension.exe --setup`

---

## 6. Laserfiche Web Client Integration

The Web Client button is **not** deployed by the MSI because Laserfiche upgrade policy overwrites `Browse.aspx`. It is deployed by a separate PowerShell script that must be **re-run after each Laserfiche Web Client upgrade**.

### Deploy the button

Run as Administrator on the Laserfiche Web Client server.  
**Always specify your Dashboard URL** so the button points to the correct server:

```powershell
.\Deploy-WebClientButton.ps1 -DashboardUrl "http://192.168.1.50:5000"
```

The script automatically discovers the Laserfiche Web Client installation path from the registry and IIS. If auto-detection fails, specify the path explicitly:

```powershell
.\Deploy-WebClientButton.ps1 `
    -DashboardUrl "http://192.168.1.50:5000" `
    -WebClientPath "D:\Laserfiche\Web Access\Web Files"
```

The script:
- Discovers the Laserfiche Web Access installation path
- Backs up `Browse.aspx` to `Browse.aspx.bak-<timestamp>`
- Copies `lf-dashboard-button.js` to `assets\custom\`
- Patches `DASHBOARD_BASE_URL` in the deployed script to your Dashboard URL
- Adds one `<script>` tag to Browse.aspx (idempotent — won't duplicate)

### Verify

1. Open the Laserfiche Web Client: `https://your-lf-server/laserfiche/Browse.aspx`
2. Log in and open a repository
3. A **Dashboard** button (bar chart icon) should appear in the top navbar
4. Click it — a new browser tab opens for the Dashboard

### After a Laserfiche upgrade

Laserfiche upgrades overwrite `Browse.aspx`. Re-run the deployment script:

```powershell
.\Deploy-WebClientButton.ps1 -DashboardUrl "http://192.168.1.50:5000"
```

The `lf-dashboard-button.js` file in `assets\custom\` is NOT overwritten by Laserfiche upgrades.

---

## 7. First Login

Dashboard supports two login flows depending on how it is opened:

### From Laserfiche Desktop Client or Web Client

1. Dashboard opens with a **Login** page showing the detected repository
2. Enter your Laserfiche username and password
3. Click **Sign In**
4. Dashboard loads data for that repository

Subsequent navigation within the same session does not require re-login. Clicking **Change Account** logs out and returns to the Login page.

### Direct browser access

Navigating to `http://server:5000/` directly uses the credentials configured in the Settings page (no Login page is shown).

---

## 8. Blank Password Behavior

Some Laserfiche accounts have empty passwords. Dashboard supports this correctly:

- The Password field on the Login page is **not** required
- Submitting the Login form with a blank password is valid
- The empty password is sent to Laserfiche as-is — Laserfiche validates it

Do not add a required validator to the Password field.

---

## 9. Log Locations

| Component | Log Path | Notes |
|---|---|---|
| Dashboard web app | `C:\Program Files\Dashboard\WebApp\logs\dashboard-YYYYMMDD.log` | Serilog rolling log; 14-day retention |
| Desktop Extension | `%ProgramData%\Dashboard\logs\extension.log` | Append-only; one entry per extension launch |

---

## 10. Verification Checklist

After installation, confirm each item:

- [ ] `http://localhost:5000/health` returns `{"status":"Healthy"}` (or Degraded if Laserfiche not yet configured)
- [ ] `http://localhost:5000/Settings` loads the configuration form
- [ ] Settings page: Test Connection shows a check mark with server version and repo name
- [ ] `http://localhost:5000/` shows Dashboard with live data
- [ ] `http://localhost:5000/Archive` shows the folder browser
- [ ] Desktop Client: Dashboard toolbar button appears after Laserfiche restart
- [ ] Desktop Client: clicking Dashboard opens a WebView2 popup -> Login page
- [ ] Desktop Client: after login, Dashboard shows data for the active repository
- [ ] Web Client (if deployed): Dashboard button appears in navbar
- [ ] Web Client: clicking Dashboard opens a new tab -> Login page -> Dashboard

---

## 11. Installing on a New Machine

Dashboard is fully portable. The MSI, once built, can be copied to any Windows machine and configured for that environment without recompiling or editing source code.

### What changes per machine

| Setting | Where to configure | Value example |
|---|---|---|
| Dashboard public URL | `extension.config.json` / Configure-Dashboard.ps1 | `http://192.168.10.25:8080` |
| Laserfiche API URL | Settings page / Configure-Dashboard.ps1 | `https://lf-server-02/LFRepositoryAPI` |
| Repository name | Settings page / Configure-Dashboard.ps1 | `ProductionRepository` |
| Laserfiche credentials | Settings page only (DPAPI-encrypted) | — |
| Web Client button URL | Deploy-WebClientButton.ps1 `-DashboardUrl` | `http://192.168.10.25:8080` |
| Dashboard port (MSI time) | `msiexec ... DASHBOARD_PORT=8080` | `8080` |

### What stays identical

- The compiled MSI file
- All C#, Razor, and JavaScript source binaries
- The Dashboard web application DLLs
- The Desktop Extension EXE

### Step-by-step: new machine deployment

**Scenario:** The new server has:
- Computer name: `LF-SERVER-02`
- Dashboard IP/port: `http://192.168.10.25:8080`
- Laserfiche API: `https://lf-server-02/LFRepositoryAPI`
- Repository: `ProductionRepository`
- Web Client path: `D:\Laserfiche\Web Access\Web Files`

**Step 1 — Install prerequisites** (see Section 1)

**Step 2 — Install the MSI with the correct port**

```cmd
msiexec /i Dashboard-1.0.0-Setup.msi DASHBOARD_PORT=8080
```

Or double-click the MSI and accept defaults if port 5000 is acceptable.

**Step 3 — Configure the Laserfiche connection**

```powershell
.\Tools\Configure-Dashboard.ps1 `
    -DashboardUrl "http://192.168.10.25:8080" `
    -LaserficheApiUrl "https://lf-server-02/LFRepositoryAPI" `
    -RepositoryId "ProductionRepository" `
    -DisplayName "Production Repository"
```

**Step 4 — Enter credentials**

Open `http://192.168.10.25:8080/Settings`, enter a Laserfiche service account username and password, and click **Save**.

**Step 5 — Desktop Extension**

The extension was registered during MSI installation. Verify the `portalUrl` in `%ProgramData%\Dashboard\extension.config.json` was set correctly by Configure-Dashboard.ps1 (Step 3 already updated it). Open the Laserfiche Desktop Client to confirm the toolbar button appears.

If the Laserfiche Desktop Client is on a **different machine**, copy the Extension binaries from `artifacts\Extension\` to that machine, then run:
```cmd
Dashboard.DesktopExtension.exe --setup --silent
```
After first configuring `%ProgramData%\Dashboard\extension.config.json` with the correct `portalUrl`.

**Step 6 — Deploy the Web Client button**

On the Laserfiche Web Client server (may be a different machine):

```powershell
.\Deploy-WebClientButton.ps1 `
    -DashboardUrl "http://192.168.10.25:8080" `
    -WebClientPath "D:\Laserfiche\Web Access\Web Files"
```

**Step 7 — Verify**

Open `http://192.168.10.25:8080/health` — should return `{"status":"Healthy"}` or `{"status":"Degraded"}` (Degraded means the app is running but Laserfiche credentials need to be saved).

### Machine A vs Machine B vs Production — comparison

| | Machine A (development) | Machine B (test server) | Production |
|---|---|---|---|
| Dashboard URL | `http://localhost:5000` | `http://192.168.1.50:5000` | `https://dashboard.company.local` |
| Laserfiche API | `https://dev-lf/LFRepositoryAPI` | `https://test-lf/LFRepositoryAPI` | `https://prod-lf/LFRepositoryAPI` |
| MSI | Same file | Same file | Same file |
| C# source rebuild | No | No | No |
| JS edit | No | No | No |
| Configure-Dashboard.ps1 | Yes (different params) | Yes (different params) | Yes (different params) |

### Zero-source-change portability: confirmed

The following are NOT required when moving to a new machine:
- Editing any `.cs`, `.cshtml`, or `.js` file
- Rebuilding the solution
- Modifying any WiX source files
- Changing any checked-in configuration file

Only the machine-specific runtime configuration in `%ProgramData%\Dashboard\` and the Web Client button URL need to be set for the new environment.

---

## 12. Troubleshooting

| Symptom | Likely Cause | Fix |
|---|---|---|
| HTTP 500.30 on install | Hosting Bundle not installed | Install ASP.NET Core 8 Hosting Bundle; `iisreset` |
| HTTP 502.5 | dotnet not found by ANCM | Re-run Hosting Bundle installer |
| Dashboard loads but shows "Laserfiche unavailable" | Credentials not configured | Open Settings; enter and save credentials |
| Desktop Extension: button missing | Registration failed | Run `Dashboard.DesktopExtension.exe --setup` as Admin |
| Desktop Extension: WebView2 error | WebView2 not installed | Install Edge WebView2 Runtime |
| Desktop Extension: opens wrong repository | portalUrl points to wrong server | Run `Configure-Dashboard.ps1 -DashboardUrl <url>` |
| Web Client: button missing | Browse.aspx not modified | Run `Deploy-WebClientButton.ps1 -DashboardUrl <url>` as Admin |
| Web Client: button visible, click opens wrong server | DASHBOARD_BASE_URL not updated | Re-run `Deploy-WebClientButton.ps1 -DashboardUrl <url>` |
| Login fails with blank password | `[Required]` added to Password | Remove `[Required]` from `LoginInputModel.Password` |
| Credentials "not stored" after upgrade | DPAPI keys changed | Re-enter credentials in Settings page |
| Configure-Dashboard.ps1: "Invalid URL" | Missing http:// or https:// | Include the scheme: `http://192.168.1.50:5000` |
