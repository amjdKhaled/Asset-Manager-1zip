---
name: Phase 5 WebView2 popup
description: DashboardWindow architecture — platform target, WebView2 version, why isolated user-data folders, error diagnostics.
---

## Root cause of 0x8007000B (ERROR_BAD_FORMAT / BadImageFormatException)

AnyCPU without an explicit `<PlatformTarget>` causes the `Microsoft.Web.WebView2` NuGet
build targets to be unable to determine which architecture `WebView2Loader.dll` to
promote to the output directory. The x86 loader (or no loader) ends up on disk, but the
EXE runs as 64-bit on a 64-bit OS → architecture mismatch → `0x8007000B`.

**Fix:** `<PlatformTarget>x64</PlatformTarget>` + `<Prefer32Bit>false</Prefer32Bit>`.

## Platform target decision: x64

Evidence from the codebase:
- `CustomButtonManager/CustomButtonManager.csproj` (official Laserfiche SDK 10.4 sample) → `<PlatformTarget>x86</PlatformTarget>` — the Desktop Client itself is 32-bit.
- `laserfiche-extension/GovSearchAIAssistant.csproj` (GovSearch AI, another Laserfiche toolbar extension on the same machine using WebView2) → `<PlatformTarget>x64</PlatformTarget>` + `Microsoft.Web.WebView2 v1.0.2420.47` — confirmed working.

Our extension runs as a separate child process (not inside the 32-bit Desktop Client).
Its architecture is determined by its own PE header, not the parent's. x64 matches the
only confirmed-working WebView2 extension on this machine.

## WebView2 package version

`Microsoft.Web.WebView2 Version="1.0.2420.47"` — aligned to GovSearch (proven on machine).

## Why WebView2, not Process.Start (browser)

Browser tabs share session cookies. Switching repos in Laserfiche and clicking the button
opened a new tab with the correct `?repository=` param, but the session was already set
from the previous tab — the user still saw the old repo. WebView2 with isolated
user-data folders per click (`%TEMP%\Dashboard_<GUID>\`) eliminates this entirely.

## Isolated user-data folder per click

`CoreWebView2Environment.CreateAsync(null, userDataFolder)` where `userDataFolder` is
`%TEMP%\Dashboard_<GUID>\`. Each window has completely isolated cookies — no stale
session from a previous click can leak.

## Error classification in DashboardWindow

Three distinct error paths in `OnFormLoad` catch block:
1. `BadImageFormatException` or HRESULT `0x8007000B` → architecture mismatch message
2. HRESULT `0x80070002`/`0x80070003` or failed `GetAvailableBrowserVersionString()` → runtime missing
3. Other → generic with HRESULT + link to log

## %(DatabaseName) token confirmed correct

Verified in `CustomButtonManager/Readme.txt` (SDK 10.4): explicitly listed as "Current database name".

## Diagnostic log

`ExtensionLogger` appends to `%ProgramData%\Dashboard\logs\extension.log`.
Click handler logs: OS/process bitness, IntPtr.Size, exe path, raw args, repository,
WebView2Loader.dll existence at flat/x64/x86 paths, user data folder, runtime version, final URL.
Setup logs: toolbar name, button name, exe path, command, icon path, IconExists, button ID.
