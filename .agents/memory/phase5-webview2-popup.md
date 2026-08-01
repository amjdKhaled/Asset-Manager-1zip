---
name: Phase 5 WebView2 popup
description: DashboardWindow architecture — why WebView2 with isolated user-data folders was chosen over opening the system browser.
---

## Why WebView2, not Process.Start (browser)

The system browser reuses session cookies across tabs. When the user switches
repositories in Laserfiche and clicks the button again, the new tab shares the
old session cookie — the server stores the new repo in the session but the old
tab still displays the previous repo. The user saw `LFNewRepoWF` even when
`TestEmployee` was the active repo.

The fix: open a WinForms `DashboardWindow` (WebView2) per button click, each with its
own isolated `CoreWebView2Environment` pointing to a unique temp folder
(`%TEMP%\Dashboard_<GUID>\`). No shared cookies, no stale-session problem.

## Key design decisions

- `DashboardWindow` is a WinForms `Form` with `WebView2 { Dock = DockStyle.Fill }`.
- `EnsureCoreWebView2Async(env)` is called in `OnFormLoad` (async void handler);
  initialization errors show a MessageBox and `Close()` the form gracefully.
- `NewWindowRequested` is handled by navigating within the same WebView2 (no new
  browser windows, no leaking to the OS browser).
- `AreDevToolsEnabled = false`, `IsStatusBarEnabled = false` — clean kiosk feel.
- No IPC / single-instance logic. The requirement explicitly said
  "reliability > single-instance". Each click spawns its own window.

## %(DatabaseName) token confirmed correct

Verified in `CustomButtonManager/Readme.txt` (SDK 10.4 sample):
`%(DatabaseName)` is explicitly listed as "Current database name".
The token IS correct — the previous runtime failure was the browser-session
sharing problem, not a wrong token.

## Icon copy

`Resources\Dashboard.ico` must be present in the output directory alongside the EXE.
Added `<Content Include="Resources\Dashboard.ico"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></Content>` to the csproj.
`ToolbarRegistrar` resolves `<ExeDir>\Resources\Dashboard.ico` at setup time and only
passes it to `CustomButtonInfo.IconPath` when `File.Exists` confirms it is there —
prevents Laserfiche from silently registering a blank/broken icon.

## Diagnostics

`ExtensionLogger` appends timestamped lines to `%ProgramData%\Dashboard\logs\extension.log`.
Key log events:
- `DASHBOARD EXTENSION CLICK` section: raw args[], repository detected, portal URL, final URL
- `DASHBOARD EXTENSION SETUP` section: toolbar name, button name, exe path, command, icon path, IconExists, button ID, registration success/fail
- WebView2 user data folder path + navigation URL
