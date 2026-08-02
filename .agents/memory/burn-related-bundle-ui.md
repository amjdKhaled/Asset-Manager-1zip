---
name: Burn related-bundle BA UI suppression
description: Why a second installer wizard appeared mid-Apply and the Display/Relation rule that prevents it
---

# Rule
A WiX managed BA must run HEADLESS (no WizardForm, no Application.Run) when
`Command.Relation != RelationType.None` or `Command.Display == Display.Embedded`.

**Why:** During a same-version upgrade, Burn executes the previously installed
RELATED bundle mid-Apply (~50%). That process's BA opened a full wizard on top
of the active installer ("second Dashboard Configuration window" bug).

**How to apply:** DashboardBA has `IsSilentExecution` gating Run(): headless
Detect->Plan->Apply with a ManualResetEvent, 60-min fail-safe timeout,
exception paths and OnShutdown all signal the event (never hang the parent
installer). Passive/None/Unknown DIRECT launches stay interactive — a headless
first-time install would produce a broken config (wizard values never
collected). Diagnostics: %ProgramData%\Dashboard\Logs\BA-runtime.log logs
PID/BA-guid/FORM-guid per event; publish.ps1 guards enforce single
`new WizardForm(` call site.

Note: an ALREADY-INSTALLED old bundle (built before this fix) will still show
its wizard once during the next upgrade; only bundles built with this fix are
silent as related executions.
