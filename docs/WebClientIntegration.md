# Laserfiche Web Client Integration

This document describes how to add the **Dashboard button** to the Laserfiche Web Client so users can open the Analytics Dashboard directly from the Web Client toolbar.

---

## How it works

```
Laserfiche Web Client
       ↓  click Dashboard button
lf-webclient-button.js (running inside Web Client page)
       ↓  detects active repository from page context
       ↓  opens new browser tab
Dashboard URL: https://dashboard.corp.local/?repository=TestEmployee&source=webclient
       ↓
RepositorySessionMiddleware stores repository + source in session
       ↓
SessionAuthGuardMiddleware redirects to /Login (session not yet authenticated)
       ↓
User enters Laserfiche username + password (password may be empty)
       ↓
Dashboard
```

No credentials, tokens, or cookies are passed from the Web Client to the Dashboard. Only the repository name and the source identifier `webclient` travel through the URL.

---

## Files

| File | Purpose |
|---|---|
| `src/LFPortal.Web/wwwroot/js/lf-webclient-button.js` | Self-contained button script to deploy on the Web Client server |
| `src/LFPortal.Web/Middleware/RepositorySessionMiddleware.cs` | Reads `?repository=` and `?source=` from the URL |
| `src/LFPortal.Web/Middleware/SessionAuthGuardMiddleware.cs` | Enforces login before access for Web Client sessions |

---

## Installation

### Step 1 — Configure the Dashboard URL in the script

Open `lf-webclient-button.js` and set the `DASHBOARD_BASE_URL` constant at the top of the file:

```javascript
var DASHBOARD_BASE_URL = 'https://dashboard.corp.local:5000';
```

Use the URL that users' browsers can reach — it does **not** need to be on the same server as the Web Client.

### Step 2 — Copy the script to your Web Client server

The file must be served from the Laserfiche Web Client web application so it can run in the browser alongside the Web Client pages.

#### Classic Laserfiche Web Access (10.x / 11.x)

1. Locate your Laserfiche Web Access installation directory (typically `C:\Program Files\Laserfiche\Web Access\`).
2. Copy `lf-webclient-button.js` into the directory.
3. Open `Browse.aspx` (or your custom version) in a text editor.
4. Add a script reference before the closing `</body>` tag:

```html
<script src="lf-webclient-button.js"></script>
```

#### Laserfiche Web Client (12.x / Cloud)

Consult Laserfiche documentation for your version's custom JavaScript injection mechanism. The file is a plain self-contained IIFE and has no dependencies.

### Step 3 — Identify the toolbar selector for your version

The script tries a list of common Laserfiche Web Client toolbar CSS selectors. If the button does not appear:

1. Open the Web Client in your browser.
2. Open Developer Tools (F12) → Elements panel.
3. Locate the toolbar element that holds other buttons.
4. Copy its CSS selector.
5. Add it to the `selectors` array in the `findToolbar()` function in `lf-webclient-button.js`.

### Step 4 — Test

1. Open the Laserfiche Web Client and navigate to a repository (e.g. **TestEmployee**).
2. The **Dashboard** button should appear in the toolbar.
3. Click it — a new browser tab opens at:
   ```
   https://dashboard.corp.local:5000/?repository=TestEmployee&source=webclient
   ```
4. The Dashboard Login page appears with the repository name shown read-only.
5. Enter your Laserfiche username and password (password may be empty) and click **Sign In**.
6. The Dashboard loads showing data for **TestEmployee**.

---

## Repository detection — how the script finds the active repository

The script tries the following strategies in order, stopping at the first success:

| Priority | Strategy | Description |
|---|---|---|
| 1 | URL query parameter | `?repo=`, `?repository=`, `?db=`, `?RepoID=` in the page URL |
| 2 | URL hash parameter | Same parameters in the hash fragment (SPA routing) |
| 3 | URL path segment | `/repository/<Name>/` or `/repo/<Name>/` in the path |
| 4 | Laserfiche JS globals | `LaserficheWebClient.repository`, `Laserfiche.app.repositoryId`, `LFRepositoryName` |
| 5 | DOM elements | Known CSS selectors / `data-repository` attributes in the rendered page |

If no strategy succeeds the user sees a prompt asking them to navigate into a repository first.

---

## Session isolation

A login to **TestEmployee** does **not** authenticate **LFNewRepoWF**.

The guard compares `AuthenticatedRepositoryId` (set on successful login) with `ActiveRepositoryId` (set from `?repository=`). When a user opens the Dashboard for a different repository, the guard redirects to `/Login` for that repository.

---

## Launch source badge

When the Dashboard is opened from the Web Client, the header shows a green **WEB CLIENT** badge:

```
LFDashboard  [● TestEmployee  WEB CLIENT]   Dashboard  Archive  Settings  Change Account
```

Compare with a Desktop Client launch (blue **DESKTOP** badge) and direct browser access (no badge).

---

## Change Account

The **Change Account** link in the Dashboard header clears the session authentication and redirects to `/Login`, keeping the active repository and source. The user must re-enter their Laserfiche credentials without leaving the Dashboard.

---

## Security notes

- Only `?repository=` and `?source=webclient` travel through the URL — non-sensitive metadata only.
- Credentials are entered on the Dashboard Login page via HTTPS POST.
- No Laserfiche Web Client session cookies are accessed, copied, or forwarded.
- The SSO path (reusing the Web Client's existing Laserfiche token) is deliberately **not** implemented. SSO can be evaluated later against an officially supported mechanism.
