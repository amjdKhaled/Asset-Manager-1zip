---
name: Phase 6 installer architecture
description: WiX v4 MSI design decisions, build script pattern, and Web Client deployment strategy for Dashboard Phase 6.
---

## Rule
Web Client button (`Deploy-WebClientButton.ps1`) is deployed separately from the MSI. MSI must NOT touch Laserfiche's Browse.aspx because Laserfiche upgrades overwrite it and WiX rollback on that file would be unsafe.

**Why:** Laserfiche upgrades silently overwrite Browse.aspx. If the MSI wrote to it, the Web Client button would break on every Laserfiche upgrade with no clear error. A standalone PS1 is idempotent and must be re-run after each Laserfiche upgrade — this is documented and expected.

**How to apply:** The installer deploys only files under `C:\Program Files\Dashboard\`. The Web Client JS file is staged to `artifacts\WebClientButton\` by build/publish.ps1 and deployed separately.

## WiX v4 structure
- `installer/Dashboard.Installer/` — WiX project (not in LFPortal.sln; must be built on Windows)
- `Variables.wxi` — externalized properties (version, port, UpgradeCode)
- `Product.wxs` — package, features, MajorUpgrade, custom actions
- `WebApplication.wxs` — web app files (glob from staging dir) + IIS components
- `DesktopExtension.wxs` — extension files (glob from staging dir)
- `Configuration.wxs` — ProgramData files (NeverOverwrite), credentials dir ACLs
- `Shortcuts.wxs` — Start Menu shortcuts
- UpgradeCode GUID: `{A7F3C2D1-B4E5-4891-9ACE-F12345678901}` — NEVER change this

## Custom actions for Extension registration
- Install: `util:ExecCmd` → `Dashboard.DesktopExtension.exe --setup --silent`, deferred, Return="ignore"
- Uninstall: `util:ExecCmd` → `Dashboard.DesktopExtension.exe --remove --silent`, deferred, Condition='Installed AND REMOVE~="ALL"'
- Return="ignore" is correct: if LF Desktop Client not installed, setup warns but MSI still succeeds.

## Build script
- `build/publish.ps1` — orchestrates full release; auto-skips MSI and Extension on non-Windows
- Stage dir: `artifacts\staging\{WebApp,Extension,ConfigTemplate}\`
- Final output: `artifacts\{Dashboard-Setup.msi,WebApp\,Extension\,WebClientButton\,docs\}`
- Version sourced from Directory.Build.props

## Config template locations
- `config/templates/laserfiche.config.json` — web app connection template
- `config/templates/extension.config.json` — extension URL config template
- Both staged to `artifacts\staging\ConfigTemplate\` and embedded in MSI with NeverOverwrite

## Build platform note
Desktop Extension and MSI build require Windows. The Linux CI job runs `dotnet publish` only (SkipMsi=true). A separate Windows build agent or developer machine produces the final MSI.

## XML comments in WiX files
Never use `--` anywhere inside XML comments (`<!-- ... -->`). The XML spec forbids the double-hyphen sequence inside comment bodies. All `--` in WiX wxs/wixproj comments must use single-dash notation. Also, `-->` inside a comment body closes the comment early.

## PS1 encoding
All PS1 files must use pure ASCII (no Unicode box-drawing, em-dash, arrows, etc.). Windows PowerShell 5.1 reads PS1 files as ANSI when there is no BOM; UTF-8 multibyte sequences corrupt string boundaries and produce "terminator expected" parser errors.

## WiX v4 file harvesting
Use HarvestDirectory in the wixproj, not `<Files Include="...">` inside wxs ComponentGroup elements. The `<Files>` glob is MSBuild syntax that belongs in the wixproj, not WiX source language. Each HarvestDirectory item needs ComponentGroupName, DirectoryRefId, and SuppressRootDirectory metadata.

## WiX v4 custom action for installed EXE
Use `<CustomAction Directory="..." ExeCommand="..." Execute="deferred" .../>` (Type 34). Then schedule in `<InstallExecuteSequence>` with `<Custom Action="..." After/Before="...">condition</Custom>`. Do NOT use `<util:ExecCmd>` (does not exist in WiX v4).

## WiX v4 registry prerequisite check
Use `<Property Id="..."><RegistrySearch .../></Property>` (built-in) rather than `<util:RegistrySearch Variable="...">` (wrong attribute name, element may not exist in util v4).

## DefineConstants multiline
Never use multiline XML element content for DefineConstants in a wixproj. Whitespace is included verbatim in the value. Use separate PropertyGroup assignments with `$(DefineConstants);key=value` concatenation.
