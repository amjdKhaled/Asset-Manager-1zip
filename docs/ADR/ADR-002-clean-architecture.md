# ADR-002 — Clean Architecture (4-Layer) Selected

**Date:** 2026-07-31
**Status:** Accepted
**Deciders:** Architecture review, Phase 0

---

## Context

The project requires an architecture that:
- Keeps business logic isolated from infrastructure concerns (HTTP, Laserfiche API, IIS)
- Allows future modules (Workflow, Forms, Reporting, AD integration) to be added without
  changing existing layers
- Makes the Laserfiche API version swappable without touching business logic or UI
- Is understandable by any enterprise .NET developer without project-specific knowledge

---

## Decision

The project uses a **4-layer Clean Architecture**:

```
LFPortal.Domain          — Entities, value objects, enums, exceptions
LFPortal.Application     — Service interfaces, DTOs, use-case orchestration
LFPortal.Infrastructure  — HTTP clients, Laserfiche API, credential providers
LFPortal.Web             — Controllers, Razor Views, configuration
```

Dependency direction: `Web → Application ← Infrastructure`, all depending on `Domain`.
Infrastructure depends on Application (implements its interfaces) but Application does
**not** depend on Infrastructure.

---

## Rationale

### Testability
Service interfaces defined in Application can be tested with any mock implementation.
No controller or view has any dependency on the Laserfiche HTTP layer.

### Replaceability
If Laserfiche releases a breaking API version change, only `Infrastructure` changes.
`Application`, `Domain`, and `Web` are unaffected. The same principle applies to
credential storage, logging sinks, or any other infrastructure concern.

### Onboarding
Clean Architecture is a well-documented pattern. Any .NET developer joining the project
understands the structure immediately without reading internal documentation.

### SOLID compliance
- **S** — Each class has one reason to change (single responsibility enforced by layer)
- **O** — New features are added by extension (new interfaces, new implementations),
  not by modifying existing classes
- **L** — All service implementations are substitutable for their interfaces
- **I** — Interfaces are granular (one interface per capability, not one god interface)
- **D** — All dependencies point inward; no layer depends on a more outer layer

---

## Layer Responsibilities

| Layer | Responsibilities | Must NOT contain |
|-------|-----------------|------------------|
| Domain | Entities, value objects, enums, custom exceptions, `PagedResult<T>` | HTTP, EF, Laserfiche SDK, DI |
| Application | Service interfaces, DTOs, use-case methods | HTTP clients, SQL, Laserfiche HTTP calls |
| Infrastructure | `HttpClient`, LF API calls, DPAPI, token cache | Business logic, controllers, views |
| Web | Controllers (thin), Razor Views, `Program.cs`, DI wiring | Business logic, HTTP clients |

---

## Adding a Future Module

Adding a new module (e.g., Workflow integration) requires only:
1. New entities in Domain (if needed)
2. New `IWorkflowService` interface in Application
3. New `WorkflowService` implementation in Infrastructure
4. New `WorkflowController` and Views in Web
5. One line in `AddLaserficheInfrastructure()` to register the new service

No existing file is modified.

---

## Consequences

- Project references must be configured carefully to enforce the dependency rule.
  `LFPortal.Application` must **not** reference `LFPortal.Infrastructure`.
- All DI registration is centralized in `AddLaserficheInfrastructure()` in
  Infrastructure, called from `Program.cs` in Web. Controllers never instantiate
  services directly.

---

## Alternatives Rejected

| Alternative | Reason rejected |
|-------------|-----------------|
| Single-project monolith | No layer enforcement; business logic and HTTP code would mix over time |
| 2-layer (Web + Services) | Insufficient separation; Infrastructure and Application concerns would merge |
| Vertical slice architecture | Harder to enforce the "no direct LF calls in UI" constraint required by the project |
