# Document previews and local startup

From the repository root, with the .NET 8 SDK installed:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Run-Dashboard.ps1 -Port 5080
```

Open http://localhost:5080 and sign in to your configured Laserfiche repository.
The application uses the existing Settings/ProgramData configuration. Explicit
command-line options such as `--urls` take precedence over saved configuration.

The archive drawer and document viewer use the same server-side page proxy.
TIFF pages are identified by their file signature and converted to PNG locally.
If a server does not support the direct page image route, the proxy uses V2
Export for the requested page. A single image packaged in a ZIP is unpacked
before being returned to the browser. Authorization and server failures remain
errors, rather than being reported as missing images.

To verify against a live repository, open scanned documents in Archive and in
the document viewer. Check the first page, next/previous pages, a document with
nonconsecutive image page numbers, and an electronic PDF. The server-side
regression tests use simulated API responses; they do not connect to Laserfiche.
