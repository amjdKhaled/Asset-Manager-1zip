# ADR-001 — ASP.NET Core MVC with Razor Views Selected Over Blazor Server

**Date:** 2026-07-31
**Status:** Accepted
**Deciders:** Architecture review, Phase 0

---

## Context

The LFPortal frontend must be built entirely in C#/.NET with no React, TypeScript, or
npm. Two ASP.NET Core-native options were evaluated:

- **Option A: ASP.NET Core MVC + Razor Views** — Traditional server-side rendering.
  Each request produces a full or partial HTML response. Interactive elements use
  vanilla JavaScript `fetch` calls.
- **Option B: Blazor Server** — Component-based UI where C# code runs on the server
  and UI updates are pushed to the browser over a persistent WebSocket (SignalR)
  connection.

---

## Decision

**ASP.NET Core MVC with Razor Views** is selected as the frontend technology.

---

## Rationale

### Stateless deployment
MVC produces a stateless HTTP request/response cycle. Any IIS server in a load-balanced
farm can handle any request without session affinity. Blazor Server requires a persistent
WebSocket connection pinned to a specific server instance (sticky sessions), which is
incompatible with many government and enterprise network configurations (load balancers,
reverse proxies, and firewall policies that terminate long-lived connections).

### On-premises reliability
LFPortal will run inside isolated government networks. WebSocket availability is not
guaranteed in these environments. MVC has zero dependency on WebSockets.

### Simplicity of deployment and debugging
MVC applications are vanilla HTTP applications. Every request can be inspected with
standard tools (browser dev tools, Fiddler, IIS logs). Blazor Server debugging requires
understanding the SignalR circuit model, which adds operational complexity.

### No JavaScript build tooling required
MVC with Razor Views and vanilla JS `fetch` requires zero npm, zero bundlers, and zero
build steps for the frontend. This keeps the project portable (clone → build → run)
and eliminates a category of dependency that cannot be satisfied in an air-gapped
environment.

### Future migration path
If Blazor is desired in the future, individual MVC Views can be incrementally replaced
with Blazor components. The service layer and domain model are frontend-agnostic and
require no changes.

---

## Consequences

- Interactive features (folder tree expansion, health badge polling, connection test)
  are implemented with small, targeted vanilla JavaScript `fetch` calls returning
  HTML partial views — no JavaScript framework required.
- The shared `_Layout.cshtml` and `_Sidebar.cshtml` components are standard Razor
  partial views.
- Server-side rendering means every page load makes at least one LF API call. Response
  caching (`[ResponseCache]`) can be applied to specific actions where appropriate.

---

## Alternatives Rejected

| Alternative | Reason rejected |
|-------------|-----------------|
| Blazor Server | Sticky session requirement; WebSocket dependency; operational complexity |
| Blazor WebAssembly | Cannot run C# on client without downloading .NET runtime to browser; does not work in all government browser policies |
| React / Angular / Vue | Violates the project requirement for a pure Microsoft/.NET stack |
