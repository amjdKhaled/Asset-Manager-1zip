# Dashboard Desktop Extension

The **Dashboard Desktop Extension** (`Dashboard.DesktopExtension.exe`) adds a toolbar
button to the Laserfiche Windows Desktop Client. Clicking the button opens the Dashboard
portal URL in the user's default browser.

---

## Architecture

| Aspect | Detail |
|--------|--------|
| Target framework | .NET Framework 4.8 (`net48`) |
| Output type | `WinExe` (no console window) |
| SDK dependency | `Laserfiche.ClientAutomation` (SDK 10.4) — setup/registration only |
| Click handler | Pure .NET Framework — no Laserfiche SDK required at click time |
| Config file | `%ProgramData%\Dashboard\extension.config.json` |
| Legacy fallback | `%ProgramData%\LFPortal\extension.config.json` (read-only; backward compat) |

The extension has two operating modes selected by command-line arguments:

```
Dashboard.DesktopExtension.exe --setup [--silent]
    Registers the Dashboard button in the Laserfiche Desktop Client toolbar.
    Requires admin rights and the Laserfiche Desktop Client to be installed.

Dashboard.DesktopExtension.exe --remove [--silent]
    Removes the Dashboard button from the Laserfiche Desktop Client toolbar.

Dashboard.DesktopExtension.exe -buttonclick -connguid "..." -hwnd "..." -pid "..."
    Invoked by Laserfiche on button click. Reads the portal URL from the config
    file and opens it in the default browser.
```

Running with no arguments is equivalent to `--setup`.

---

## Prerequisites (build machine)

| Requirement | Notes |
|-------------|-------|
| Visual Studio 2022 or MSBuild 17 | Windows only |
| .NET Framework 4.8 Developer Pack | Download from Microsoft |
| Laserfiche Desktop Client installed | Required for the SDK DLL |
| `ClientAutomation.dll` present | Copied into the repository at `vendor\LaserficheSdk\bin\10.4\net-4.0\` |

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
```

Laserfiche substitutes the `%(…)` tokens before invoking the process. The click handler
ignores these tokens for this thin-launcher implementation; it only needs the portal URL
from the config file.

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
