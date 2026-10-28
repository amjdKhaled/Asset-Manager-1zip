# Offline Windows distribution (1.0.5)

Download the Windows build artifact on the pull request, or the packages on the
GitHub Releases page after merge. The old EXE committed under Release/ is 1.0.4.
Each new package includes build-info.json and a SHA-256 checksum file.

## Installed application

Run LFDashboard-Setup.exe as administrator to upgrade the existing IIS installation.
The MSI contains the web application, Chart.js, Arabic fonts, JavaScript libraries,
Magick.NET and the LibreOffice conversion runtime. .NET Framework 4.8 for the setup
wizard is embedded as a full offline installer, not a web downloader.
IIS and the ASP.NET Core Hosting Module must already be installed on the server;
the setup checks these requirements. The optional Desktop extension also requires
the WebView2 runtime. These Windows prerequisites are separate from app libraries.

## Standalone application

Extract LFDashboard-Standalone-win-x64.zip to a writable folder, then double-click
Start-Dashboard.cmd. Open http://localhost:5050. Keep the console open while using
Dashboard. To choose another port, run `Start-Dashboard.cmd 5080`. There is no SDK,
IIS, Microsoft Office, LibreOffice installation or project build requirement.
The .NET and Office runtimes are already in the ZIP. Use Settings to configure
the Laserfiche server if it has not been configured on this computer.

## Document formats

| Format | Offline preview |
| --- | --- |
| PDF | Browser's built-in PDF reader |
| PNG, JPEG, GIF, BMP, WebP | Browser image |
| Laserfiche scanned image pages | Per-page images, including TIFF-to-PNG conversion |
| Electronic TIFF, including multiple frames | Local PDF conversion retaining all pages |
| Word DOC/DOCX/DOT/DOTX, RTF, ODT/OTT | Laserfiche pages if available; otherwise local LibreOffice PDF conversion |
| Excel XLS/XLSX/XLT/XLTX, ODS/OTS, CSV/TSV | Laserfiche pages or local PDF conversion |
| PowerPoint PPT/PPTX/PPS/PPSX, ODP/OTP | Laserfiche pages or local PDF conversion |
| TXT, ODG, VSD/VSDX | Local PDF conversion where supported by the bundled LibreOffice filters |
| Other formats | Laserfiche image pages when present; otherwise download the original |

Preview conversion never changes the original repository file. Word/Excel/slide
layout may differ from Microsoft Office, especially if the document uses fonts
that are absent from Windows. Password-protected, malformed, unsupported files,
Office files larger than 100 MB, and conversions exceeding two minutes produce
an error with an option to download the original rather than an empty preview.
Do not assume every proprietary format accepted by a Laserfiche desktop plug-in
has a corresponding built-in web preview converter.

Office conversions use separate temporary profiles and disable untrusted macros.
At most two conversions run concurrently. Temporary document copies and profiles
are deleted after conversion or cancellation; no external document-viewing service
is used. PDF/image/Office file downloads still respect repository access checks.

Internet access is needed only on the build machine to acquire dependencies.
At runtime the browser assets and converters are local. The application must
still reach your Laserfiche Repository API and configured authentication server
over the local network. A cloud repository or cloud identity provider requires
access to that provider; offline assets cannot replace those services.

## Verification

CI builds Razor, runs infrastructure/web regression tests, and converts real
DOCX/XLSX/PPTX/RTF fixtures through the exact staged Office runtime. It starts
the standalone EXE and checks local JS/font endpoints. A source scan rejects
external script/style/font references. Finally the installer isolation checks
exercise upgrade/repair/uninstall behavior in disposable Windows fixtures.
Confirm archive and viewer previews against live repository entries after deployment.
