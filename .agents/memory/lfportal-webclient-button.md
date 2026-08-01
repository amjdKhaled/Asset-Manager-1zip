---
name: LFPortal Web Client Button
description: Final working architecture for the Laserfiche Web Client → Dashboard button integration and all problems solved during development.
---

## Final working architecture

- Browse.aspx loads `assets/custom/lf-dashboard-button.js` via one `<script>` tag added inside `<% if (!CloudMode) { %>`.
- Source file in repo: `src/LFPortal.Web/wwwroot/js/lf-webclient-button.js`
- Inject target: `id="rightNavbar"` — confirmed present in Browse.aspx; button inserted as `<ul><li><button>` before existing children.
- Repository source: `_doc.getElementById('WebAccessRepositoryName').value` — server-rendered hidden input Browse.aspx always emits; URL query params are fallback only.
- Opens: `http://<DASHBOARD_BASE_URL>/?repository=<repo>&source=webclient` in a new tab.

## Event handling architecture (final)

**ONE window-level singleton → ONE capture-phase listener → ONE anchor navigation.**

```
window.__lfDashboardInitialized guard (blocks duplicate script runs)
  ↓
_doc.addEventListener('click', handler, true /* capture */)
  ↓ walks event.target up to find data-lf-dashboard-button="true"
  ↓ _launchInProgress 500ms cooldown guard
  ↓ onDashboardClick()
  ↓ openDashboard(url)  ← single <a>.click(), no fallback
  ↓ ONE new tab
```

## Problems solved (in order)

1. **404 on lf-dashboard-button.js** — file was never copied to `assets\custom\`. Fix: copy it.
2. **Browse.aspx script injection** — added `<script>` tag inside the `if (!CloudMode)` block.
3. **WebAccessRepositoryName** — server-rendered hidden input, confirmed as authoritative repository source.
4. **rightNavbar** — confirmed toolbar injection location.
5. **Button visible but not clickable** — Angular shadows global `document` (`document.querySelectorAll` throws TypeError). Fix: `var _doc = window.document` captured at IIFE entry before Angular boots; capture-phase delegated listener on `_doc` fires before Angular bubble-phase interceptors.
6. **Double-open (first attempt)** — removing direct button listener alone was insufficient.
7. **Double-open (root cause confirmed)** — `window.open(url, '_blank', 'noopener,noreferrer')` returns `null` on some browsers even when the tab opened successfully. Code saw `!newWin === true` → triggered anchor fallback → two tabs from one click. Fix: remove `window.open` entirely; use a single programmatic `<a>.click()` with no conditional fallback. Added `window.__lfDashboardInitialized` window-level singleton to block duplicate script execution.

## Critical rules for future edits

- **NEVER** use `window.open(..., 'noopener,noreferrer')` with a `!result` fallback — noopener causes null return even on success in many browsers.
- **NEVER** guard duplicate script execution with a local `var` — each IIFE gets its own scope; use `window.__lfDashboardInitialized`.
- Navigation must be exactly ONE `a.click()` in `openDashboard()` — no conditional second path.

## Deployment

```powershell
Copy-Item -Path "<repo>\src\LFPortal.Web\wwwroot\js\lf-webclient-button.js" `
          -Destination "C:\Program Files\Laserfiche\Web Access\Web Files\assets\custom\lf-dashboard-button.js" `
          -Force
```

Ctrl+F5 in browser is sufficient — no IIS restart needed (static file).

## Verification PowerShell commands

```powershell
# 1. Confirm exactly one script tag in Browse.aspx
(Select-String -Path "C:\Program Files\Laserfiche\Web Access\Web Files\Browse.aspx" -Pattern "lf-dashboard-button").Count

# 2. Confirm deployed file exists
Test-Path "C:\Program Files\Laserfiche\Web Access\Web Files\assets\custom\lf-dashboard-button.js"

# 3. Compare source vs deployed (should be identical after copy)
$src  = Get-FileHash "<repo>\src\LFPortal.Web\wwwroot\js\lf-webclient-button.js"
$dest = Get-FileHash "C:\Program Files\Laserfiche\Web Access\Web Files\assets\custom\lf-dashboard-button.js"
$src.Hash -eq $dest.Hash

# 4. Confirm no other references to the old script name
Select-String -Path "C:\Program Files\Laserfiche\Web Access\Web Files\Browse.aspx" -Pattern "lf-webclient-button"
```
