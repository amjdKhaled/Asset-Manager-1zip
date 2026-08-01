---
name: Phase 5 dynamic repository context
description: How the Desktop Client passes its active repository to Dashboard and how the server stores + uses it per session.
---

## The mechanism

**Extension side:**  
`ToolbarRegistrar` registers the button with `%(DatabaseName)` appended to the command:
```
"Dashboard.DesktopExtension.exe" -buttonclick -connguid "%(ConnectionGUID)" -hwnd "%(hwnd)" -pid "%(PID)" -databasename "%(DatabaseName)"
```
`Program.cs` parses `-databasename` with `ParseNamedArg()` and appends `?repository=<value>` to the portal URL via `Uri.EscapeDataString`. No SDK calls at click time — the token is resolved by the Desktop Client before invoking the process.

**Server side (middleware):**  
`RepositorySessionMiddleware` (registered after `UseSession()`) reads `?repository=` and stores it in two ASP.NET Core session keys:
- `"ActiveRepositoryId"` — the repository name
- `"ActiveRepositorySource"` — `"Laserfiche Desktop Client"`

**Repository context:**  
`SessionAwareRepositoryContext` (singleton, replaces `ConfigurationRepositoryContext`) reads `IHttpContextAccessor.HttpContext?.Session.GetString("ActiveRepositoryId")` on every `GetActiveRepositoryAsync()` call, falling back to `LaserficheOptions.RepositoryId` when the session key is absent. Safe for singleton because it reads `IHttpContextAccessor` (also singleton) at call time rather than capturing scope at construction.

**Why:**  
All infrastructure services already call `IRepositoryContext.GetActiveRepositoryAsync()`. Swapping the implementation is zero-change for callers. `BearerTokenHandler` (transient) gets the session-scoped repo transparently via the same singleton context.

## Settings display

`SettingsController.Index` reads session keys directly (`HttpContext.Session.GetString("ActiveRepositoryId")`) and populates `SettingsViewModel.ActiveRepositoryId` and `ActiveRepositorySource`. The Settings view shows a "Connection Status" card with a "Repository Source" row displaying either a blue "Desktop Client" badge or a gray "Default Configuration" badge.

## Header badge

`_Layout.cshtml` reads session via `Context.Session.GetString(...)` and injects `IOptions<LaserficheOptions>` for the fallback display name. Shows a pill badge — highlighted blue when Desktop Client is active.

## Session lifetime

Default: 8 hours idle timeout, `.Dashboard.Session` cookie name, HttpOnly + IsEssential.

## Key files changed (architectural, not a changelog)
- `LFPortal.Infrastructure.csproj` — added `<FrameworkReference Include="Microsoft.AspNetCore.App" />` for `IHttpContextAccessor`
- `Program.cs` (Web) — `AddDistributedMemoryCache`, `AddSession`, `UseSession`, `UseMiddleware<RepositorySessionMiddleware>`, default route → Dashboard
- `Views/_ViewImports.cshtml` — added `@using Microsoft.AspNetCore.Http` for session extension methods in views
