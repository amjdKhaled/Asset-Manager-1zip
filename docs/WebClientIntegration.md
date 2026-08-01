# Laserfiche Web Client Integration — Deployment Guide

**Confirmed installation details:**
- Web Client type: Laserfiche Web Access (on-premises, AngularJS, non-CloudMode)
- Physical path: `C:\Program Files\Laserfiche\Web Access\Web Files\`
- Web Client URL: `https://localhost/laserfiche/Browse.aspx`
- Dashboard URL: `http://localhost:5000`

---

## How it works

```
User is inside TestEmployee in Laserfiche Web Client
       ↓  click Dashboard button (injected into rightNavbar)
lf-dashboard-button.js reads:
   document.getElementById('WebAccessRepositoryName').value  → "TestEmployee"
       ↓  opens new browser tab
http://localhost:5000/?repository=TestEmployee&source=webclient
       ↓
RepositorySessionMiddleware stores repository + source in session
       ↓
SessionAuthGuardMiddleware → redirects to /Login (not yet authenticated)
       ↓
User enters Laserfiche username + password on Dashboard Login page
       ↓
Dashboard shows TestEmployee data
```

---

## Deployment steps

### Step 1 — Copy the button script to the Web Client

Open Command Prompt as Administrator and run:

```
copy /Y "C:\path\to\Dashboard\wwwroot\js\lf-webclient-button.js" "C:\Program Files\Laserfiche\Web Access\Web Files\assets\custom\lf-dashboard-button.js"
```

Or manually copy the file — the **destination** must be exactly:
```
C:\Program Files\Laserfiche\Web Access\Web Files\assets\custom\lf-dashboard-button.js
```

> The `assets\custom\` folder already exists — Laserfiche Web Access loads
> `browse-custom.css` from it, confirming it is the official on-premises
> customization directory.

---

### Step 2 — Add one line to Browse.aspx

Open `C:\Program Files\Laserfiche\Web Access\Web Files\Browse.aspx` in a
text editor **as Administrator** (e.g. Notepad run as Administrator).

Find this block (around line 37):

```aspx
    <% if (!CloudMode) { %>
    <link rel="stylesheet" href="assets/custom/browse-custom.css" />
    <% } %>
```

Change it to:

```aspx
    <% if (!CloudMode) { %>
    <link rel="stylesheet" href="assets/custom/browse-custom.css" />
    <script src="assets/custom/lf-dashboard-button.js"></script>
    <% } %>
```

That is the **only** change to Browse.aspx. Save the file.

---

### Step 3 — Confirm the Dashboard URL in the script

Open `lf-dashboard-button.js` in the `assets\custom\` folder and verify the
top of the file shows:

```javascript
var DASHBOARD_BASE_URL = 'http://localhost:5000';
```

If the Dashboard is on a **different server or port**, change this value to
match. The URL must be reachable from the user's browser (not from the IIS
server itself).

---

### Step 4 — No IIS restart required

Because `Browse.aspx` is an ASP.NET page (not a compiled DLL) and the JS
file is a static asset, a browser reload is sufficient. IIS does not need
to be restarted.

---

## Verification

1. Open the Laserfiche Web Client: `https://localhost/laserfiche/Browse.aspx`
2. Log in and open repository **TestEmployee**.
3. Open browser DevTools (F12) → Console. You should see:
   ```
   [LFDashboard] Dashboard button injected into rightNavbar.
   ```
4. A **Dashboard** button (bar-chart icon) should appear in the top navbar,
   to the left of the repository picker.
5. Click it. A new tab opens:
   ```
   http://localhost:5000/?repository=TestEmployee&source=webclient
   ```
6. The Dashboard Login page appears with "TestEmployee" shown read-only.
7. Enter your Laserfiche credentials and click **Sign In**.
8. Dashboard loads showing TestEmployee data.

Repeat with **LFNewRepoWF** to confirm repository switching works.

---

## Repository detection — how it works

The script reads the server-rendered hidden field that Browse.aspx always
emits:

```html
<input type="hidden" id="WebAccessRepositoryName" value="TestEmployee"/>
```

This is set by the ASP.NET code-behind (`RepositoryName` property) before
Angular boots, so it is always present and always correct — regardless of
URL format, navigation state, or Angular routing. No URL parsing is
involved as the primary mechanism.

---

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Button does not appear | `rightNavbar` not found | Check browser console for `[LFDashboard]` warning; confirm the Browse.aspx `<script>` tag was saved |
| "Could not detect repository" alert | `WebAccessRepositoryName` field missing | Verify you are logged in and viewing a repository, not the login page |
| New tab opens but redirected to wrong repo | Old session in Dashboard | Click Change Account in Dashboard to clear the session |
| `http://localhost:5000` refused to connect | Dashboard not running | Start the Dashboard with `dotnet run --project src/LFPortal.Web/LFPortal.Web.csproj --urls http://0.0.0.0:5000` |
| Mixed content warning in browser | Web Client is HTTPS, Dashboard is HTTP | Either run Dashboard behind HTTPS or use the browser exception for localhost |

---

## Required tests

**TEST A — Web Client / TestEmployee**
1. Open Web Client → log in → navigate to TestEmployee
2. Click Dashboard button
3. Expected: Login page with "Repository: TestEmployee", "Source: WEB CLIENT"
4. Sign in → Dashboard shows TestEmployee data ✓

**TEST B — Web Client / LFNewRepoWF**
1. Switch Web Client to LFNewRepoWF (use repository picker)
2. Click Dashboard button
3. Expected: Login page with "Repository: LFNewRepoWF"
4. Sign in → Dashboard shows LFNewRepoWF data ✓

**TEST C — Desktop Client regression**
1. Open Laserfiche Desktop Client → TestEmployee
2. Click Dashboard toolbar button (existing Desktop Extension)
3. Expected: WebView2 popup → Login page with "Source: DESKTOP"
4. Sign in → Dashboard works as before ✓
