# Dashboard — Installation Guide

**Version:** 1.0.0  
**Target environment:** Windows Server 2019/2022 or Windows 10/11, on-premises

---

## Contents

1. [Prerequisites](#1-prerequisites)
2. [MSI Installation](#2-msi-installation)
3. [IIS Configuration Verification](#3-iis-configuration-verification)
4. [Initial Laserfiche Connection Configuration](#4-initial-laserfiche-connection-configuration)
5. [Desktop Client Extension](#5-desktop-client-extension)
6. [Laserfiche Web Client Integration](#6-laserfiche-web-client-integration)
7. [First Login](#7-first-login)
8. [Blank Password Behavior](#8-blank-password-behavior)
9. [Log Locations](#9-log-locations)
10. [Verification Checklist](#10-verification-checklist)
11. [Troubleshooting](#11-troubleshooting)

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
# Control Panel → Programs → Turn Windows features on/off
# Check: Internet Information Services → World Wide Web Services → Application Development Features → ASP.NET 4.8
```

### Install ASP.NET Core 8 Hosting Bundle

Download from: https://dotnet.microsoft.com/download/dotnet/8.0  
Choose **Windows Hosting Bundle** (not the SDK or Runtime).

Verify after installation:
```powershell
dotnet --list-runtimes | Select-String "Microsoft.AspNetCore.App 8"
```

---

## 2. MSI Installation

Run as **Administrator**:

```
Dashboard-1.0.0-Setup.msi
```

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

### Silent / unattended installation

```cmd
msiexec /i Dashboard-1.0.0-Setup.msi /quiet /norestart INSTALLFOLDER="C:\Dashboard\"
```

### Custom port

```cmd
msiexec /i Dashboard-1.0.0-Setup.msi DASHBOARD_PORT=80
```

---

## 3. IIS Configuration Verification

After installation, verify in IIS Manager:

1. Open **IIS Manager** → Application Pools → confirm `Dashboard` exists, `.NET CLR Version = No Managed Code`, `Managed Pipeline Mode = Integrated`
2. Open **Sites** → confirm `Dashboard` site exists, bound to port 5000
3. Open a browser on the server: `http://localhost:5000/` → should show the Dashboard

**If you see "HTTP Error 500.30":**  
The ASP.NET Core Hosting Bundle may not be installed correctly. Re-run the Hosting Bundle installer and restart IIS (`iisreset`).

**If you see "HTTP Error 502.5":**  
IIS cannot find the `dotnet` runtime. Re-run the Hosting Bundle installer.

---

## 4. Initial Laserfiche Connection Configuration

The Dashboard ships with placeholder configuration. Before the Dashboard will display data, you must configure the Laserfiche connection.

1. Browse to `http://localhost:5000/Settings` (or `http://YOUR-SERVER:5000/Settings`)
2. Enter:
   - **API Server URL**: the URL of your Laserfiche Server's Repository API endpoint  
     Example: `https://lf-server.corp.local/LFRepositoryAPI`
   - **Repository ID**: the Laserfiche repository name (e.g., `Documents`)
   - **Username** and **Password**: a Laserfiche service account with read access
3. Click **Test Connection** to verify
4. Click **Save** — credentials are encrypted with Windows DPAPI and stored in `%ProgramData%\Dashboard\credentials\`

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

Edit `%ProgramData%\Dashboard\extension.config.json`:

```json
{
  "portalUrl": "http://YOUR-SERVER:5000",
  "buttonLabel": "Dashboard",
  "iconPath": ""
}
```

After editing, re-register:
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

1. Copy `artifacts\WebClientButton\lf-dashboard-button.js` to the Laserfiche Web Access server
2. Run as Administrator on the Laserfiche server:

```powershell
.\Deploy-WebClientButton.ps1
```

or with an explicit Laserfiche path:

```powershell
.\Deploy-WebClientButton.ps1 -LFWebPath "C:\Program Files\Laserfiche\Web Access\Web Files"
```

The script:
- Backs up `Browse.aspx` to `Browse.aspx.bak-<timestamp>`
- Copies `lf-dashboard-button.js` to `assets\custom\`
- Adds one `<script>` tag to Browse.aspx (idempotent — won't duplicate)

### Set the Dashboard URL

Edit `C:\Program Files\Laserfiche\Web Access\Web Files\assets\custom\lf-dashboard-button.js` and set:

```javascript
var DASHBOARD_BASE_URL = 'http://YOUR-DASHBOARD-SERVER:5000';
```

> **Important:** This URL is evaluated in the user's **browser**, not on the Laserfiche server. It must be a hostname/IP that client machines can reach.

### Verify

1. Open the Laserfiche Web Client: `https://your-lf-server/laserfiche/Browse.aspx`
2. Log in and open a repository
3. A **Dashboard** button (bar chart icon) should appear in the top navbar
4. Click it — a new browser tab opens for the Dashboard

### After a Laserfiche upgrade

Laserfiche upgrades overwrite `Browse.aspx`. Re-run the deployment script:

```powershell
.\Deploy-WebClientButton.ps1
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
- [ ] Settings page: Test Connection shows ✓ with server version and repo name
- [ ] `http://localhost:5000/` shows Dashboard with live data
- [ ] `http://localhost:5000/Archive` shows the folder browser
- [ ] Desktop Client: Dashboard toolbar button appears after Laserfiche restart
- [ ] Desktop Client: clicking Dashboard opens a WebView2 popup → Login page
- [ ] Desktop Client: after login, Dashboard shows data for the active repository
- [ ] Web Client (if deployed): Dashboard button appears in navbar
- [ ] Web Client: clicking Dashboard opens a new tab → Login page → Dashboard

---

## 11. Troubleshooting

| Symptom | Likely Cause | Fix |
|---|---|---|
| HTTP 500.30 on install | Hosting Bundle not installed | Install ASP.NET Core 8 Hosting Bundle; `iisreset` |
| HTTP 502.5 | dotnet not found by ANCM | Re-run Hosting Bundle installer |
| Dashboard loads but shows "Laserfiche unavailable" | Credentials not configured | Open Settings; enter and save credentials |
| Desktop Extension: button missing | Registration failed | Run `Dashboard.DesktopExtension.exe --setup` as Admin |
| Desktop Extension: WebView2 error | WebView2 not installed | Install Edge WebView2 Runtime |
| Web Client: button missing | Browse.aspx not modified | Run `Deploy-WebClientButton.ps1` as Admin |
| Web Client: button visible, click opens nothing | DASHBOARD_BASE_URL wrong | Edit `lf-dashboard-button.js`; set correct server URL |
| Login fails with blank password | `[Required]` added to Password | Remove `[Required]` from `LoginInputModel.Password` |
| Credentials "not stored" after upgrade | DPAPI keys changed | Re-enter credentials in Settings page |
