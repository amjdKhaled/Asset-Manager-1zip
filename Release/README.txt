Laserfiche Dashboard - Windows Setup
====================================

INSTALL
1. Keep all files in this Release folder together.
2. Right-click LFDashboard-Setup.exe and choose "Run as administrator".
3. Confirm the detected Laserfiche API URL and optional integrations.
4. Click Install.

The installer selects this computer's hostname automatically. Advanced Settings
contains the IIS port and optional Desktop/Web Client integrations. The selected
port must be free on the target computer.

REPAIR OR REMOVE
Run LFDashboard-Setup.exe again after installation:
  - Repair restores installed program files and components.
  - Uninstall removes the app, IIS site/app pool, shortcuts, and integrations.

Saved configuration, credentials, and logs in %ProgramData%\Dashboard are kept
by default. During uninstall, select the cleanup checkbox only when you also
want those saved files permanently deleted.

You can also uninstall from Windows Installed apps or the Start Menu shortcut.

PREREQUISITES
  - 64-bit Windows Server 2019/2022 or Windows 10/11 with IIS
  - ASP.NET Core Module V2 (installed by the Windows Hosting Bundle):
    https://dotnet.microsoft.com/download/dotnet/8.0
  - Microsoft Edge WebView2 Runtime (Desktop Extension only):
    https://developer.microsoft.com/microsoft-edge/webview2/

VERIFY DOWNLOAD
Compare LFDashboard-Setup.exe against SHA256SUMS.txt with:
  certutil -hashfile LFDashboard-Setup.exe SHA256

Support and source:
https://github.com/amjdKhaled/Asset-Manager-1zip
