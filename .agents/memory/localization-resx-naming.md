---
name: LFPortal localization RESX naming
description: How ASP.NET Core IStringLocalizer resolves RESX files when the marker class has a codebehind in the same directory — critical for getting localization to actually work.
---

## Rule
Do NOT set `ResourcesPath` in `AddLocalization()` when the RESX marker class uses a namespace that is the project root namespace.

## Why
When `SharedResource.cs` (namespace `LFPortal.Web`) lives alongside `SharedResource.resx` in `Resources/`, MSBuild links them as codebehind and embeds the resource as `LFPortal.Web.SharedResource` (uses the C# class's namespace, stripping the directory path).

- With `ResourcesPath = "Resources"`, IStringLocalizer looks for `LFPortal.Web.Resources.SharedResource` — mismatch, all keys fall back to their names.
- With no `ResourcesPath` (just `builder.Services.AddLocalization()`), it looks for `LFPortal.Web.SharedResource` — matches the embedded resource. ✓

## How to apply
- `SharedResource.cs` namespace: `LFPortal.Web` (NOT `LFPortal.Web.Resources`)
- RESX files: `Resources/SharedResource.resx` and `Resources/SharedResource.ar.resx`
- Program.cs: `builder.Services.AddLocalization();` (no ResourcesPath)
- _ViewImports.cshtml: `@inject IStringLocalizer<SharedResource> L` (works because `@using LFPortal.Web` is already there)
- Arabic satellite assembly appears at `bin/.../ar/LFPortal.Web.resources.dll` — presence confirms Arabic RESX compiled.
