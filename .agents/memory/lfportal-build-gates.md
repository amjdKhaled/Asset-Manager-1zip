---
name: LFPortal build gates
description: Packages and attributes required to achieve zero-warning .NET 8 builds in this project.
---

## Required packages

| Package | Project | Why needed |
|---|---|---|
| `Microsoft.Extensions.Options.DataAnnotations` | Infrastructure | Provides `ValidateDataAnnotations()` extension on `OptionsBuilder<T>` |
| `System.Security.Cryptography.ProtectedData` | Infrastructure | DPAPI encrypt/decrypt |
| `Microsoft.Extensions.Http.Resilience` | Infrastructure | `AddStandardResilienceHandler()` |

## Required attributes

- `[SupportedOSPlatform("windows")]` on `DpapiCredentialProvider` class — suppresses CA1416 platform compatibility warnings for DPAPI calls.
- `using System.Runtime.Versioning;` needed for the attribute.

**How to apply:** After adding any new Windows-only P/Invoke or DPAPI call, add `[SupportedOSPlatform("windows")]` to the class and guard the DI registration with `OperatingSystem.IsWindows()`.
