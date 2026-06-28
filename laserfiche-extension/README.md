# Laserfiche AI Extension

A native .NET WPF extension for the **Laserfiche Desktop Client** that embeds your existing GovSearch AI web application (running on `localhost:5000`) as a floating, resizable AI assistant popup.

## Architecture

- **No website changes required** — your GovSearch AI app stays untouched
- **WebView2** hosts `http://localhost:5000` inside a modern WPF window
- **Bidirectional JSON messaging** between Laserfiche and the web app
- **SOLID + DI + Async/Await** throughout
- **Auto-reconnect** with "Waiting for Local AI..." overlay

## Project Structure

```
LaserficheAIExtension/
├── Infrastructure/
│   ├── DependencyInjection/   # Microsoft.Extensions.DI setup
│   ├── Helpers/                 # Window position persistence
│   └── Logging/                 # Serilog adapter + ILogger<T>
├── Models/
│   ├── DocumentContext.cs     # Selected document data
│   ├── WebCommand.cs          # Commands from web app
│   └── ExtensionSettings.cs   # Persisted window state
├── Services/
│   ├── LaserficheDocumentService.cs
│   ├── WebAppCommunicationService.cs
│   ├── ConnectionMonitorService.cs
│   ├── DocumentContextTracker.cs
│   └── CommandHandlerService.cs
├── SDK/
│   ├── ILaserficheSdkWrapper.cs
│   └── LaserficheSdkWrapper.cs
├── Popup/
│   ├── AIPopupWindow.xaml      # Main WPF popup UI
│   └── AIPopupWindow.xaml.cs   # Code-behind
├── Ribbon/
│   └── AIRibbonButton.cs       # Laserfiche ribbon integration
├── Communication/
│   └── WebViewBridge.cs        # Low-level WebView2 messaging
└── Properties/
    └── AssemblyInfo.cs
```

## Prerequisites

- **.NET Framework 4.8** (or .NET 6+ with appropriate package versions)
- **Microsoft Edge WebView2 Runtime** (Evergreen Standalone Installer)
- **Laserfiche Desktop Client** (with SDK assemblies accessible)
- **Visual Studio 2022** (or compatible IDE)

## Build Instructions

1. **Clone or copy** this project into your solution.
2. **Restore NuGet packages**:
   ```bash
   dotnet restore LaserficheAIExtension.csproj
   ```
3. **Add Laserfiche SDK references** (if not available via NuGet):
   - `Laserfiche.ApplicationServices.TrustClient`
   - `Laserfiche.RepositoryAccess`
   - `Laserfiche.DocumentServices`
4. **Build**:
   ```bash
   dotnet build LaserficheAIExtension.csproj -c Release
   ```
5. **Deploy** the output DLL to the Laserfiche Desktop Client extensions folder.

## How It Works

### Popup Window

When the user clicks the **AI Assistant** button in the Laserfiche ribbon:

1. `AIRibbonButton.Execute()` creates or activates `AIPopupWindow`
2. The WPF window initializes `WebView2` and navigates to `http://localhost:5000`
3. A JavaScript bridge is injected so the web app can call `window.LaserficheBridge.sendCommand()`

### Document Selection

Whenever the selected document changes in Laserfiche:

1. `DocumentContextTracker` fetches document metadata via `LaserficheSdkWrapper`
2. `WebAppCommunicationService` dispatches a `CustomEvent` to the web app:
   ```javascript
   window.dispatchEvent(new CustomEvent('laserfiche-document-changed', {
     detail: { entryId: 1234, documentName: "Invoice.pdf", ... }
   }));
   ```

### Web App → Laserfiche Commands

The web app can send commands back using:

```javascript
window.LaserficheBridge.sendCommand('OpenDocument', { entryId: 1234 });
```

Supported commands:
- `OpenDocument` — Opens the document in Laserfiche
- `UpdateMetadata` — Updates template fields
- `MoveDocument` — Moves to a different folder
- `RefreshMetadata` — Re-reads and returns current metadata
- `DownloadDocument` — Returns Base64-encoded document bytes
- `RunOcr` — Runs OCR on the document
- `RunAi` — Triggers custom AI processing
- `SearchRepository` — Executes a Laserfiche search
- `GetDocumentFields` — Returns all template fields

### Connection Monitor

If `localhost:5000` is unavailable:

1. A "Waiting for Local AI..." overlay appears
2. The monitor retries every **3 seconds**
3. When the server comes back, the page auto-reloads
4. Laserfiche Desktop Client **never freezes**

## Settings Persistence

Window state is saved to:

```
%LocalAppData%\LaserficheAIExtension\settings.json
```

Includes: position, size, maximized state, dark mode, server URL.

## Logging

Logs are written to:

```
%LocalAppData%\LaserficheAIExtension\logs\extension-.log
```

Rotated daily, 7 days retention. View in Visual Studio Output window or open the log file directly.

## License

Proprietary — GovSearch AI Platform
