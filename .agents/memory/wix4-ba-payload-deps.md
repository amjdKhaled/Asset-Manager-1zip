---
name: WiX 4 managed BA required Payload dependencies
description: Which DLLs must be listed as Payload elements alongside the BA DLL in Bundle.wxs
---

# WiX 4 Managed BA — required Payload elements

## Rule
`bal:WixManagedBootstrapperApplicationHost` auto-embeds the native host binaries
(`mbahost.dll`, `mbapreq.dll`, `WixToolset.Mba.Host.dll`).

It does NOT auto-embed the managed API DLL `WixToolset.Mba.Core.dll`.

That DLL is a NuGet `PackageReference` on the BA project with CopyLocal=true.
It lands in the BA build output directory (e.g. `bin/Release/net48/`).
It must be an explicit `<Payload>` element or the bundle crashes at startup with
a FileNotFoundException/FileLoadException when the native host tries to load the BA assembly.

## Fix — Bundle.wxs
```xml
<BootstrapperApplication>
  <bal:WixManagedBootstrapperApplicationHost />
  <Payload SourceFile="$(var.BAAssembly)" />
  <Payload SourceFile="$(var.MbaCoreAssembly)" />
</BootstrapperApplication>
```

## Fix — publish.ps1 Step 9
Pass both as separate -d defines using PowerShell Join-Path (never embed a backslash
literal in the WXS path — it fails on the Linux WiX validator):

```powershell
$baStagingDir   = Join-Path $StagingDir "BA"
$baAssemblyPath = Join-Path $baStagingDir "Dashboard.BA.dll"
# ... in wixBundleArgs:
"-d", "BAAssembly=$baAssemblyPath",
"-d", "MbaCoreAssembly=$(Join-Path $baStagingDir 'WixToolset.Mba.Core.dll')",
```

## Complete BA bin output for this project (net48)
- `Dashboard.BA.dll` — main BA assembly (Payload ✓)
- `Dashboard.BA.pdb` — debug symbols, NOT needed as Payload
- `WixToolset.Mba.Core.dll` — managed API (Payload ✓)

No other NuGet DLL dependencies exist (only `WixToolset.Mba.Core` in csproj).
