---
name: WiX v4 Mba.Core API quirks
description: Confirmed API surface of WixToolset.Mba.Core 4.0.5; traps when writing a managed BA.
---

# WiX v4 Mba.Core 4.0.5 — Confirmed API and Traps

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

**Why:** These traps are invisible from static code review; only caught by actual compilation.
**How to apply:** Every future managed BA or net48 project must go through a real Windows (or
at minimum Linux dotnet build) compile pass before delivery.
