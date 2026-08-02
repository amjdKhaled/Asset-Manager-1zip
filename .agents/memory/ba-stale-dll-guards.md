---
name: BA stale DLL guards
description: How to prevent a stale Dashboard.BA.dll from silently bundling old wizard UI (e.g. removed Repository ID / Display Name fields).
---

**UTF-16 PE string scans are alignment-fragile.** .NET string literals live in the UTF-16 #US heap, which need not start on an even file offset — decoding a whole EXE/DLL from offset 0 as Unicode can MISS odd-aligned strings (caused a real false "stale binary" failure on the Windows publish for the SetupHelper prepare-tls guard). Rules: (1) prefer a behavioral probe — SetupHelper supports `--help`/`--list-actions` printing its RegisteredCommands array (kept in sync with the dispatch switch) and exiting 0 with zero machine changes; publish.ps1 runs the STAGED exe and asserts exit 0 + output contains the verb. (2) SHA256 source-vs-staged compare is the correct stale-binary detector — never label a missing raw string as "stale". (3) Where a raw string scan must remain (BA DLL — not runnable standalone), decode at BOTH offsets 0 and 1.

**Rule:** publish.ps1 must enforce three layers of protection to prevent a stale Dashboard.BA.dll from being bundled with the installer and showing a second unexpected configuration window:

1. **Step 1 Clean** — delete `installer\Dashboard.BA\bin\` and `installer\Dashboard.BA\obj\` (plus SetupHelper bin/obj) before any build. `artifacts\` is cleaned separately; these intermediate folders are NOT cleaned by removing artifacts alone.

2. **Source vs staged SHA256 guard** (after staging in Step 6) — hash `installer\Dashboard.BA\bin\Release\net48\Dashboard.BA.dll` and `artifacts\staging\BA\Dashboard.BA.dll`; fail if they differ.

3. **String scan guard** (after staging in Step 6) — read staged DLL as UTF-16LE and search for removed UI strings (`"Repository ID"`, `"Display Name"`). Presence proves the DLL was compiled from old source before those wizard fields were removed. Fail and print a clear message.

**Why:** MSBuild incremental builds will reuse a DLL in bin/ from a previous build if source timestamps don't force a rebuild. Cleaning bin/obj in Step 1 prevents this. The hash and string-scan guards catch any case where the clean failed (locked files) before the installer is produced.

**How to apply:** The WizardForm.cs also has a static `_instanceCount` Interlocked guard: if a second WizardForm is ever constructed in the same process, it throws `InvalidOperationException` immediately instead of showing a second window.

**The symptom this prevents:** During installation at ~50% progress, a second "Dashboard Configuration" window would open with old wizard fields (Repository ID, Display Name) from a stale DLL compiled before those fields were removed.
