# Dashboard Desktop Extension

The **Dashboard Desktop Extension** (`Dashboard.DesktopExtension.exe`) adds a toolbar
button to the Laserfiche Windows Desktop Client. Clicking the button opens the Dashboard
portal in a native **WebView2 popup window** — no external browser is launched.

---

## Architecture

| Aspect | Detail |
|--------|--------|
| Target framework | .NET Framework 4.8 (`net48`) |
| Output type | `WinExe` (no console window) |
| Process architecture | **x64** — matches the GovSearch AI extension, the only other Laserfiche WebView2 extension confirmed working on this machine |
| SDK dependency | `Laserfiche.ClientAutomation` (SDK 10.4) — setup/registration only |
| Click handler | WinForms `DashboardWindow` with `Microsoft.Web.WebView2 v1.0.2420.47` (no Laserfiche SDK at click time) |
| Popup window | 1400×850 initial, 1000×650 minimum, centered on screen |
| Config file | `%ProgramData%\Dashboard\extension.config.json` |
| Legacy fallback | `%ProgramData%\LFPortal\extension.config.json` (read-only; backward compat) |
| Diagnostic log | `%ProgramData%\Dashboard\logs\extension.log` |

The extension has two operating modes selected by command-line arguments:

```
Dashboard.DesktopExtension.exe --setup [--silent]
    Registers the Dashboard button in the Laserfiche Desktop Client toolbar.
    Requires admin rights and the Laserfiche Desktop Client to be installed.

Dashboard.DesktopExtension.exe --remove [--silent]
    Removes the Dashboard button from the Laserfiche Desktop Client toolbar.

Dashboard.DesktopExtension.exe -buttonclick -connguid "..." -hwnd "..." -pid "..." -databasename "..."
    Invoked by Laserfiche on button click. Reads the portal URL from the config
    file, appends ?repository=<databasename>, and opens Dashboard in a native
    WebView2 popup window. No external browser is launched.
```

Running with no arguments is equivalent to `--setup`.

---

## Prerequisites (build machine)

| Requirement | Notes |
|-------------|-------|
| Visual Studio 2022 or MSBuild 17 | Windows only |
| .NET Framework 4.8 Developer Pack | Download from Microsoft |
| Laserfiche Desktop Client installed | Required for the SDK DLL |
| `ClientAutomation.dll` present | Preferred: copied into the repository at `vendor\LaserficheSdk\bin\10.4\net-4.0\` |

The project intentionally uses a repository-relative SDK path. Before building,
copy the SDK DLL from the installed Laserfiche SDK into the repository:

```powershell
# From the repository root on Windows:
$source = "${env:ProgramFiles}\Laserfiche\SDK 10.4\bin\10.4\net-4.0\ClientAutomation.dll"
$destination = "vendor\LaserficheSdk\bin\10.4\net-4.0"
New-Item -ItemType Directory -Force $destination | Out-Null
Copy-Item $source "$destination\ClientAutomation.dll"
```

If the SDK is installed elsewhere, change only the `$source` value. Do not put an
absolute path in the project file.

On a clean CI/build computer without the proprietary SDK, the project uses
`build\ClientAutomation.ReferenceStub` for compilation only. That stub is never
copied into the installer. At runtime, toolbar registration still requires the
real `ClientAutomation.dll` installed with the Laserfiche Desktop Client.

---

## Build Instructions

The extension project is **not** part of `LFPortal.sln` (it cannot build on the
Linux/Replit development environment). Build it on a Windows machine with the
Laserfiche SDK installed:

```powershell
# From the repository root on Windows, after copying ClientAutomation.dll:
dotnet clean src\Dashboard.DesktopExtension\Dashboard.DesktopExtension.csproj `
    --configuration Release

dotnet build src\Dashboard.DesktopExtension\Dashboard.DesktopExtension.csproj `
    --configuration Release

# Or open the project directly in Visual Studio 2022.
```

The output is in:
```
src\Dashboard.DesktopExtension\bin\Release\net48\Dashboard.DesktopExtension.exe
```

The project must be built on Windows with the **.NET Framework 4.8 Developer Pack**
installed. A Linux/.NET SDK environment can evaluate the project but cannot compile
it without the .NET Framework 4.8 reference assemblies.

---

## Configuration File

Create `%ProgramData%\Dashboard\extension.config.json` before running `--setup`:

```json
{
  "portalUrl": "http://your-server/dashboard",
  "buttonLabel": "Dashboard",
  "iconPath": ""
}
```

| Field | Type | Description |
|-------|------|-------------|
| `portalUrl` | string | Full URL of the Dashboard portal. Required. |
| `buttonLabel` | string | Text shown on the Laserfiche toolbar button. Default: `"Dashboard"`. |
| `iconPath` | string | Absolute path to a 16×16 or 32×32 `.ico` file. Leave empty for the default Laserfiche icon. |

If the config file is absent when the button is clicked, the extension displays a
dialog explaining where to create it.

---

## Deployment

### Independent runtime verification

The repository contains an older, separate `laserfiche-extension` integration for
GovSearch AI. Do not use its executable or its existing toolbar button for this test.
The old integration identifies itself with the `GovSearchAIAssistant.exe` executable
and an AI-related button. The Dashboard extension has its own identity:

| Item | Dashboard Phase 5 value |
|------|-------------------------|
| Button label | `Dashboard` |
| Toolbar name | `Dashboard` |
| Executable | `Dashboard.DesktopExtension.exe` |
| Configuration | `%ProgramData%\Dashboard\extension.config.json` |
| Click command | `Dashboard.DesktopExtension.exe -buttonclick ...` |

The new `ToolbarRegistrar` only removes and recreates the toolbar named `Dashboard`
and only removes custom buttons whose command references
`Dashboard.DesktopExtension.exe`. It does not remove or modify the old GovSearch AI
toolbar.

### First-time runtime test

Use the compiled Release executable directly; Visual Studio and `dotnet run` are not
required.

1. Close all Dashboard extension windows. The Laserfiche Desktop Client does not have
   to be closed for the registration API call, but close it before registration for a
   clean toolbar refresh and then restart it afterward.
2. Create the configuration directory and file:

   ```powershell
   New-Item -ItemType Directory -Force `
       "$env:ProgramData\Dashboard" | Out-Null

   @'
   {
     "portalUrl": "https://your-dashboard-host/dashboard",
     "buttonLabel": "Dashboard",
     "iconPath": ""
   }
   '@ | Set-Content `
       "$env:ProgramData\Dashboard\extension.config.json" `
       -Encoding UTF8
   ```

   Replace the example `portalUrl` with the actual Dashboard URL. Keep
   `buttonLabel` exactly `Dashboard` for this verification.
3. Open **PowerShell as Administrator**. The implementation displays an error if
   registration fails and recommends administrator privileges because the
   Laserfiche ClientAutomation toolbar store may require elevated access.
4. From the repository root, register the new button:

   ```powershell
   & ".\src\Dashboard.DesktopExtension\bin\Release\net48\Dashboard.DesktopExtension.exe" --setup
   ```

   A successful run displays:
   `Dashboard toolbar button "Dashboard" added to the Laserfiche Desktop Client.`
5. Start or restart the Laserfiche Desktop Client completely. If it remains in the
   notification area, exit it there as well, then launch it again.
6. Verify that Laserfiche shows a new button labeled **Dashboard**. During this
   test, the old integration may also remain visible as **GovSearch AI** or
   **AI Assistant**. That is expected; do not click the old button.
7. Click the **Dashboard** button. Laserfiche should invoke the command registered
   by the new executable, and the configured `portalUrl` should open in the default
   browser.

The result required to approve the Phase 5 runtime gate is:

```text
Laserfiche Desktop Client
  ├─ old GovSearch AI / AI Assistant button (unchanged)
  └─ Dashboard button
       └─ Dashboard.DesktopExtension.exe
            └─ configured Dashboard URL
```

### Remove the new button

To remove only the Dashboard registration, use the same compiled executable:

```powershell
& ".\src\Dashboard.DesktopExtension\bin\Release\net48\Dashboard.DesktopExtension.exe" --remove
```

Run that command from an elevated PowerShell window, then completely restart the
Laserfiche Desktop Client and verify that the **Dashboard** button is gone. This
does not invoke the old GovSearch AI executable or remove its button.

### First-time installation

1. Copy `Dashboard.DesktopExtension.exe` to a permanent folder on the machine
   (e.g. `C:\Program Files\Dashboard\`).
2. Create `%ProgramData%\Dashboard\extension.config.json` with your portal URL.
3. Run as administrator:
   ```
   "C:\Program Files\Dashboard\Dashboard.DesktopExtension.exe" --setup
   ```
4. Launch (or restart) the Laserfiche Desktop Client. The **Dashboard** toolbar should
   appear.

### Updating the portal URL

Edit `%ProgramData%\Dashboard\extension.config.json` and change `portalUrl`. No
re-registration is needed; the click handler reads the file fresh on every click.

### Moving the EXE

If the EXE is moved to a new path after setup, re-run `--setup` to update the
registered button command. The old button will be removed automatically.

### Removal

```
"C:\Program Files\Dashboard\Dashboard.DesktopExtension.exe" --remove
```

---

## How Registration Works

The extension uses the `Laserfiche.ClientAutomation` SDK
(`ClientManager` + `ToolbarManager`) to add a custom button to the Desktop Client's
main-window toolbar. This is the same mechanism described in the Laserfiche SDK 10.4
**CustomButtonManager** sample included in this repository (`CustomButtonManager/`).

When the user clicks the button, the Desktop Client executes:

```
"C:\...\Dashboard.DesktopExtension.exe"
    -buttonclick
    -connguid "<ConnectionGUID>"
    -hwnd "<hwnd>"
    -pid "<PID>"
    -databasename "<DatabaseName>"
```

Laserfiche substitutes the `%(…)` tokens before invoking the process. The extension
reads the `-databasename` value, appends `?repository=<DatabaseName>` to the portal URL,
and opens `DashboardWindow` — a WinForms form hosting a `WebView2` control that navigates
to that URL. No external browser is launched.

`RepositorySessionMiddleware` on the server intercepts the `?repository=` parameter and
stores it in the ASP.NET Core session. Priority order (highest first):

1. **`?repository=` from the Desktop Client** — always overwrites the session
2. Existing session value — from a previous navigation in the same window
3. Configured default — `Settings > Default Repository (Fallback)`

Each `DashboardWindow` uses an isolated WebView2 user-data folder
(`%TEMP%\Dashboard_<GUID>\`) so its session cookie is never shared with other open
Dashboard windows. This ensures that clicking the button while `TestEmployee` is active
always shows `TestEmployee`, even if a window for `LFNewRepoWF` is already open.

### Toolbar Icon

A bar-chart dashboard icon is bundled at `Resources\Dashboard.ico` alongside the EXE.
`ToolbarRegistrar` automatically passes this path to `CustomButtonInfo.IconPath` during
`--setup`. The icon contains four sizes: 16×16, 24×24, 32×32, and 48×48 pixels
(navy background `#1a2744`, accent-blue bars `#3b82f6`).

If the built-in icon is not found (e.g. non-standard install layout), `ToolbarRegistrar`
falls back to the `iconPath` field in `extension.config.json`.

---

## Backward Compatibility

Credentials and extension config written by earlier installations (when the product was
named **LFPortal**) are read transparently from the legacy paths:

| Path | Used for |
|------|----------|
| `%ProgramData%\Dashboard\extension.config.json` | Primary (current) |
| `%ProgramData%\LFPortal\extension.config.json` | Fallback read-only |
| `%ProgramData%\Dashboard\credentials\` | Primary DPAPI credentials |
| `%ProgramData%\LFPortal\credentials\` | Fallback read-only (web portal) |

New writes always go to the `Dashboard` path. Re-saving credentials from the
Settings page migrates them to the new location.
