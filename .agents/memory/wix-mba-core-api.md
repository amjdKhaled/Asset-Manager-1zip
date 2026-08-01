---
name: WiX v4 Mba.Core API quirks
description: Confirmed API surface of WixToolset.Mba.Core 4.0.5 and toolchain pinning rules.
---

# WiX v4 Mba.Core 4.0.5 — Confirmed API and Traps

## WiX version pin (SINGLE SOURCE OF TRUTH)
`$WixPinnedVersion = "4.0.5"` is declared at the top of `build/publish.ps1`.
Never install `wix` without `--version $WixPinnedVersion`.
`dotnet tool update --global wix --version X` works for both upgrades AND downgrades.

## WiX SDK names — generational incompatibility
- WiX 4.x: `Sdk="WixToolset.Wix/4.0.5"` — resolved by the WiX 4 global tool's SDK resolver.
- WiX 7.x: uses `Sdk="WixToolset.Sdk"` — completely different; WiX 7's resolver doesn't
  know about `WixToolset.Wix`, and that package does NOT exist on NuGet as a standalone download.
- Installing `wix` without a version pin gets the latest (7.x) → "Could not resolve SDK 'WixToolset.Wix'."

## WiX version detection in publish.ps1
Use `dotnet tool list --global` (reliable, no PATH dependency) to check installed version.
Do NOT use `dotnet wix --version` — WiX registers as a standalone `wix` command, not as a
dotnet subcommand; `dotnet wix` silently fails or invokes the wrong binary.

## BootstrapperApplication constructor
`protected BootstrapperApplication(IEngine engine)` — requires IEngine.
Must be called from the derived class constructor: `: base(engine)`.

## Engine property trap
There is a **class** named `WixToolset.Mba.Core.Engine` in the assembly.
Writing `Engine.Plan(...)` resolves to that *class* (static), NOT to any
instance property — causes CS0120 "object reference required".
**Fix:** Store IEngine as a private field `_engine` and call `_engine.Plan(...)`.

## Command property trap
The `Command` (IBootstrapperCommand) base-class property is `private protected` —
inaccessible from a different assembly.
**Fix:** Accept `IBootstrapperCommand command` in the constructor and store it.

## BAFactory.Create signature
```csharp
protected override IBootstrapperApplication Create(IEngine engine, IBootstrapperCommand command)
    => new DashboardBA(engine, command);
```

## ErrorEventArgs ambiguity
`ErrorEventArgs` is ambiguous between `WixToolset.Mba.Core.ErrorEventArgs` and `System.IO.ErrorEventArgs`.
**Fix:** `using MbaErrorEventArgs = WixToolset.Mba.Core.ErrorEventArgs;`

## net48 incompatibilities
- `Dictionary<K,V>.GetValueOrDefault()` — added in .NET Core 2.0 / Standard 2.1; NOT in .NET Framework 4.8.
  **Fix:** `bool ok = d.TryGetValue(key, out var v); return ok ? v : def;`
- Properties cannot be passed as `out` parameters (CS0206). Use local variables then assign.
- `TextBox.PlaceholderText` — in .NET Framework 4.7.2+ but absent from Linux reference assemblies.
  Remove or conditionalize; it is only a UI hint.

## CS8618 WinForms field declarations
WinForms fields initialized in BuildForm() (not the constructor) trigger CS8618 in nullable-enabled projects.
**Fix:** Declare them with `= null!` (null-forgiving). Do NOT disable nullable globally.

## MSI placeholder BMP files
WiX UI requires Banner.bmp (493×58) and Dialog.bmp (493×312) at MSI link time.
Created via Python: dark-blue Banner, light-grey Dialog.
Stored in `installer/Dashboard.Installer/`.

**Why:** These traps are invisible from static code review; only caught by actual compilation
and a real Windows build attempt.
**How to apply:** Every future managed BA or net48 project must go through a Linux dotnet build
pass AND a Windows WiX build pass before delivery.
