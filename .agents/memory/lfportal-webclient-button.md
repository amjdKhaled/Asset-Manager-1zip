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

## Event handling architecture (after double-open fix)

**ONE capture-phase delegated listener on `_doc` is the single authoritative click path.**  
No direct `addEventListener('click', ...)` on the button element itself.

```
_doc.addEventListener('click', handler, true /* capture */)
  → walks event.target up to find data-lf-dashboard-button="true"
  → calls onDashboardClick(event)
  → 500 ms _launchInProgress cooldown guard prevents duplicates
```

- `_delegatedRegistered` flag ensures it is only registered once even when MutationObserver re-injects the button.
- MutationObserver on `_doc.body` (subtree, childList) re-injects button if Angular re-renders the navbar.

## Problems solved (in order)

1. **404 on lf-dashboard-button.js** — file was never copied to `assets\custom\`. Fix: copy it.
2. **Browse.aspx script injection** — added `<script>` tag inside the `if (!CloudMode)` block.
3. **Button visible but not clickable** — Laserfiche Angular shadows the global `document` object (`document.querySelectorAll` is not a function). Fix: capture `var _doc = window.document` at IIFE entry and use `_doc` throughout; add capture-phase delegated listener which fires before Angular's bubble-phase interceptors.
4. **Double-open on single click** — having both a capture-phase delegated listener AND a direct button listener caused two executions per click. `_lastHandledEvent` guard was unreliable across phases. Fix: remove direct button listener entirely; rely solely on the capture-phase delegated listener + `_launchInProgress` 500 ms cooldown.

## Why

- `window.document` captured at IIFE entry is the real browser DOM; the bare `document` global may be Angular's proxy after the app boots.
- Capture phase fires before Angular bubble-phase, so it cannot be stopped by Angular `stopPropagation()`.
- Single handler registration avoids double-fire regardless of Angular re-rendering.

## Deployment

```powershell
Copy-Item -Path "<repo>\src\LFPortal.Web\wwwroot\js\lf-webclient-button.js" `
          -Destination "C:\Program Files\Laserfiche\Web Access\Web Files\assets\custom\lf-dashboard-button.js" `
          -Force
```

Ctrl+F5 in browser is sufficient — no IIS restart needed (static file).
