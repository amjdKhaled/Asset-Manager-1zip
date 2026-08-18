/**
 * lf-webclient-button.js  (deploy as: lf-dashboard-button.js)
 * ──────────────────────────────────────────────────────────────────────────
 * Adds a Dashboard button to the Laserfiche Web Access (Browse.aspx) toolbar.
 *
 * CONFIRMED for: Laserfiche Web Access on-premises (non-CloudMode, AngularJS)
 * Physical install: C:\Program Files\Laserfiche\Web Access\Web Files\
 *
 * HOW TO DEPLOY
 * ─────────────
 * 1. Set DASHBOARD_BASE_URL below to the URL users' browsers use to reach
 *    the Dashboard server.  This executes in the CLIENT browser, so
 *    "localhost" means the USER's machine, not the Laserfiche server.
 *    Example: 'http://dashboard-server:5000'  or  'http://192.168.1.100:5000'
 * 2. Copy THIS FILE to:
 *      C:\Program Files\Laserfiche\Web Access\Web Files\assets\custom\lf-dashboard-button.js
 * 3. Browse.aspx must already contain (added during initial setup):
 *      <script src="assets/custom/lf-dashboard-button.js"></script>
 *    Ctrl+F5 browser hard-refresh is sufficient after re-deploying this file.
 *    IIS restart is NOT required.
 *
 * LASERFICHE JAVASCRIPT ENVIRONMENT
 * ──────────────────────────────────
 * The Laserfiche AngularJS app shadows the global `document` identifier.
 * Confirmed: `document.querySelectorAll` is not a function in its context.
 * All DOM access uses _doc / _win, captured from the real browser window
 * at IIFE entry — before Angular can override them.
 *
 * EVENT HANDLING ARCHITECTURE — ONE NAVIGATION ONLY
 * ──────────────────────────────────────────────────
 * ONE window-level singleton guard prevents duplicate script initialization.
 * ONE capture-phase delegated listener on _doc handles all clicks.
 * ONE anchor navigation (<a>) opens the Dashboard tab.
 *
 * WHY NOT window.open?
 * When called with 'noopener,noreferrer' features, window.open() returns null
 * on some browsers even when the new tab opened successfully.  Checking that
 * null return value as a "popup blocked" signal then triggers a second anchor
 * navigation — opening two tabs from one click.  Using a single programmatic
 * anchor click (<a target="_blank" rel="noopener noreferrer">) avoids any
 * return-value ambiguity and guarantees exactly one tab per click.
 *
 * REPOSITORY DETECTION
 * ─────────────────────
 * PRIMARY: server-rendered hidden input always present in Browse.aspx:
 *   <input type="hidden" id="WebAccessRepositoryName" value="..."/>
 * FALLBACK: URL query parameters (?repo= / ?db= / ?repository=).
 *
 * SECURITY
 * ─────────
 * Only the repository name and the literal string "webclient" are sent via
 * the URL.  No credentials, tokens, or session cookies leave the Web Client.
 * ──────────────────────────────────────────────────────────────────────────
 */

(function () {
    'use strict';

    // ── SINGLETON GUARD ──────────────────────────────────────────────────
    // Prevents the entire script from running twice if Browse.aspx (or a
    // Laserfiche SPA navigation) somehow evaluates it more than once.
    // A local variable (var _registered = false) would NOT protect here —
    // each script execution gets its own closure scope.  window-level state
    // is shared across all executions.
    if (window.__lfDashboardInitialized) {
        console.log('[LFDashboard] Duplicate script initialization blocked.');
        return;
    }
    window.__lfDashboardInitialized = true;

    console.log('[LFDashboard] Script initialized.');
    // ─────────────────────────────────────────────────────────────────────

    // ── SAFE BROWSER GLOBALS ─────────────────────────────────────────────
    // Capture the real browser objects immediately, before Laserfiche Angular
    // can shadow the global `document` / `window` identifiers.
    var _win = window;
    var _doc = window.document;
    // ─────────────────────────────────────────────────────────────────────

    // ── CONFIGURATION ────────────────────────────────────────────────────
    /**
     * Base URL of the Dashboard server — NO trailing slash.
     *
     * !! This executes in the USER's browser, not on the server. !!
     * The value below is a deliberate NON-URL sentinel: the installer
     * (SetupHelper --deploy-webclient) or Deploy-WebClientButton.ps1
     * replaces it with the real Dashboard URL at deploy time.  If the
     * sentinel is still present at runtime the deployment step failed,
     * and the button shows a clear configuration error instead of
     * silently sending users to a wrong host.
     */
    var DASHBOARD_BASE_URL = '__DASHBOARD_URL_NOT_CONFIGURED__';

    /** How long (ms) to poll for rightNavbar before giving up. */
    var POLL_TIMEOUT_MS = 12000;

    /** Stable identifiers for the injected button. */
    var BUTTON_ID        = 'lf-dashboard-btn';
    var BUTTON_DATA_ATTR = 'data-lf-dashboard-button';

    /**
     * Cooldown (ms) after a launch.  Prevents a second click registered
     * within the same browser event flush from opening a second tab.
     * Does NOT prevent the user from clicking again after the cooldown.
     */
    var LAUNCH_COOLDOWN_MS = 500;
    // ─────────────────────────────────────────────────────────────────────

    /** True while a launch is in progress / cooling down. */
    var _launchInProgress = false;

    // ─────────────────────────────────────────────────────────────────────

    /**
     * Returns the active Laserfiche repository name.
     *
     * PRIMARY: server-rendered hidden input Browse.aspx always emits:
     *   <input type="hidden" id="WebAccessRepositoryName" value="..." />
     *
     * FALLBACK: URL query parameters (?repo=, ?db=, ?repository=).
     *
     * @returns {string|null}  Trimmed repository name, or null.
     */
    function getRepository() {
        var el = _doc.getElementById('WebAccessRepositoryName');
        if (el && typeof el.value === 'string' && el.value.trim().length > 0) {
            return el.value.trim();
        }

        try {
            var params = new URLSearchParams(_win.location.search);
            var fromQuery = params.get('repo') || params.get('db') || params.get('repository');
            if (fromQuery && fromQuery.trim().length > 0) return fromQuery.trim();
        } catch (e) { /* URLSearchParams unavailable — very old browser */ }

        return null;
    }

    /**
     * Builds the full Dashboard URL for the given repository.
     *
     * @param {string} repo  Validated non-empty repository name.
     * @returns {string}
     */
    function buildDashboardUrl(repo) {
        return DASHBOARD_BASE_URL.replace(/\/+$/, '') +
               '/Launch?repository=' + encodeURIComponent(repo) +
               '&source=webclient';
    }

    /**
     * Opens the Dashboard in a new tab using a single programmatic anchor.
     *
     * WHY ANCHOR INSTEAD OF window.open?
     * window.open(url, '_blank', 'noopener,noreferrer') returns null on some
     * browsers even when the new tab opened successfully.  Using the null
     * return as a "blocked" signal then triggers a second anchor navigation,
     * opening two tabs per click.  A single anchor click has no return value
     * to misinterpret — it is one navigation, always.
     *
     * @param {string} url  Full Dashboard URL including query parameters.
     */
    function openDashboard(url) {
        var a = _doc.createElement('a');
        a.href   = url;
        a.target = '_blank';
        a.rel    = 'noopener noreferrer';
        a.style.display = 'none';
        _doc.body.appendChild(a);
        a.click();
        _doc.body.removeChild(a);
    }

    /**
     * Handles a confirmed Dashboard button click.
     *
     * Guarantees exactly ONE navigation per physical click via:
     *   1. window.__lfDashboardInitialized — blocks duplicate script runs.
     *   2. _launchInProgress cooldown — blocks duplicate events within 500 ms.
     *   3. Single openDashboard() call — no fallback navigation.
     *
     * Console log sequence for one successful click:
     *   [LFDashboard] Dashboard button clicked.
     *   [LFDashboard] Repository: <name>
     *   [LFDashboard] Opening Dashboard: <url>
     *
     * @param {Event} event
     */
    function onDashboardClick(event) {
        // ── Duplicate-click guard ─────────────────────────────────────────
        if (_launchInProgress) {
            console.log('[LFDashboard] Duplicate click ignored.');
            return;
        }
        _launchInProgress = true;
        _win.setTimeout(function () { _launchInProgress = false; }, LAUNCH_COOLDOWN_MS);

        // ── Step 1: confirm click fired ───────────────────────────────────
        console.log('[LFDashboard] Dashboard button clicked.');

        // Prevent the Laserfiche navbar from also processing this event.
        if (event) {
            if (event.stopPropagation)          event.stopPropagation();
            if (event.stopImmediatePropagation) event.stopImmediatePropagation();
        }

        // ── Step 2: validate configuration ───────────────────────────────
        // Blocks both an empty value and the unpatched deploy-time sentinel:
        // the URL must have been injected by the deployment step.
        if (!DASHBOARD_BASE_URL || DASHBOARD_BASE_URL.indexOf('://') < 0) {
            console.error('[LFDashboard] DASHBOARD_BASE_URL was not patched at deploy time: ' + DASHBOARD_BASE_URL);
            alert(
                'The Dashboard button is not configured.\n\n' +
                'The Dashboard URL was not set when the Web Client integration\n' +
                'was deployed. Ask your administrator to re-run the Dashboard\n' +
                'installer or the Deploy-WebClientButton script.'
            );
            return;
        }

        // ── Step 3: detect repository ─────────────────────────────────────
        var repo = getRepository();
        if (!repo) {
            console.warn('[LFDashboard] Unable to determine active Laserfiche repository.');
            alert(
                'Could not detect the active Laserfiche repository.\n\n' +
                'Please ensure you are logged in and viewing a repository,\n' +
                'then try again.  Check the browser console for details.'
            );
            return;
        }

        console.log('[LFDashboard] Repository: ' + repo);

        // ── Step 4: open Dashboard (ONE navigation, no fallback) ──────────
        var url = buildDashboardUrl(repo);
        console.log('[LFDashboard] Opening Dashboard: ' + url);

        // openDashboard() uses a single programmatic anchor click.
        // There is no conditional fallback — nothing else can open a second tab.
        openDashboard(url);
    }

    /**
     * Creates the Dashboard button element using the real browser DOM (_doc).
     *
     * NO click listener is attached here.
     * The single authoritative click path is the capture-phase delegated
     * listener registered by ensureDelegatedHandler().
     *
     * pointer-events:none on SVG and label ensures clicks on any child
     * register the <button> as event.target, so the delegated listener's
     * parentNode walk terminates immediately.
     *
     * @returns {HTMLButtonElement}
     */
    function createButton() {
        var btn = _doc.createElement('button');
        btn.type = 'button';
        btn.id   = BUTTON_ID;
        btn.setAttribute(BUTTON_DATA_ATTR, 'true');
        btn.title     = 'Open Analytics Dashboard';
        btn.className = 'btn browseNavButton lf-dashboard-btn';

        btn.style.cssText = [
            'display:inline-flex',
            'align-items:center',
            'gap:5px',
            'padding:6px 10px',
            'color:#ffffff',
            'background:transparent',
            'border:none',
            'font-size:14px',
            'font-weight:500',
            'cursor:pointer',
            'white-space:nowrap',
            'font-family:inherit',
            'line-height:1',
            'border-radius:4px',
            'transition:background .15s',
            'pointer-events:auto',
            'position:relative',
            'z-index:1000',
            'user-select:none',
            '-webkit-user-select:none',
        ].join(';');

        btn.addEventListener('mouseover', function () {
            btn.style.background = 'rgba(255,255,255,0.15)';
        });
        btn.addEventListener('mouseout', function () {
            btn.style.background = 'transparent';
        });

        // Bar-chart icon (inline SVG — no external asset dependency).
        var svg = _doc.createElementNS('http://www.w3.org/2000/svg', 'svg');
        svg.setAttribute('width',       '20');
        svg.setAttribute('height',      '20');
        svg.setAttribute('viewBox',     '0 0 24 24');
        svg.setAttribute('fill',        'currentColor');
        svg.setAttribute('aria-hidden', 'true');
        svg.style.cssText = 'flex-shrink:0;vertical-align:middle;pointer-events:none;';
        svg.innerHTML =
            '<rect x="2"  y="10" width="4" height="11" rx="1"/>' +
            '<rect x="10" y="4"  width="4" height="17" rx="1"/>' +
            '<rect x="18" y="7"  width="4" height="14" rx="1"/>';
        btn.appendChild(svg);

        var label = _doc.createElement('span');
        label.textContent = 'Dashboard';
        label.style.cssText = 'pointer-events:none;';
        btn.appendChild(label);

        // !! No click listener added here !!
        // The sole click handler is the capture-phase delegated listener on _doc.

        return btn;
    }

    /**
     * Registers the capture-phase delegated click listener on _doc exactly once.
     *
     * Capture phase fires BEFORE Angular's bubble-phase listeners, so it
     * cannot be blocked by Angular stopPropagation() calls on a parent.
     *
     * This function is idempotent — the window-level singleton guard and the
     * fact it is called only from tryInject() (which itself checks for an
     * existing button) prevent duplicate registrations.
     */
    function ensureDelegatedHandler() {
        _doc.addEventListener('click', function (event) {
            var target = event.target;
            while (target && target !== _doc) {
                if (target.getAttribute &&
                    target.getAttribute(BUTTON_DATA_ATTR) === 'true') {
                    onDashboardClick(event);
                    return;
                }
                target = target.parentNode;
            }
        }, true /* capture phase */);
    }

    /**
     * Attempts to inject the Dashboard button into the Web Client toolbar.
     *
     * Target: id="rightNavbar" — the right side of the top action bar,
     * confirmed present in Browse.aspx.
     *
     * Idempotent: does nothing if the button already exists.
     *
     * @returns {boolean}  true when the button is present in the DOM.
     */
    function tryInject() {
        if (_doc.getElementById(BUTTON_ID)) return true;

        var rightNavbar = _doc.getElementById('rightNavbar');
        if (!rightNavbar) return false;

        var btn = createButton();

        var li = _doc.createElement('li');
        li.style.cssText = [
            'list-style:none',
            'display:inline-flex',
            'align-items:center',
            'pointer-events:auto',
        ].join(';');
        li.appendChild(btn);

        var ul = _doc.createElement('ul');
        ul.className = 'nav navbar-nav lf-dashboard-nav';
        ul.style.cssText = [
            'margin:0',
            'padding:0',
            'list-style:none',
            'display:inline-flex',
            'align-items:center',
            'pointer-events:auto',
        ].join(';');
        ul.appendChild(li);

        rightNavbar.insertBefore(ul, rightNavbar.firstChild);

        // Register the single click handler once, the first time the button
        // is successfully injected.  The singleton guard on window ensures this
        // function body only runs once regardless of re-injection.
        ensureDelegatedHandler();

        console.info('[LFDashboard] Dashboard button injected into rightNavbar.');
        return true;
    }

    /**
     * MutationObserver — re-injects the button if Laserfiche Angular re-renders
     * the navbar (e.g. after a repository switch).
     *
     * ensureDelegatedHandler() is called on re-injection but the window-level
     * singleton guard means the click listener is never registered twice.
     */
    function startObserver() {
        if (!_win.MutationObserver) return;

        var observer = new _win.MutationObserver(function () {
            if (!_doc.getElementById(BUTTON_ID) && _doc.getElementById('rightNavbar')) {
                console.info('[LFDashboard] Re-injecting Dashboard button after DOM change.');
                tryInject();
            }
        });

        observer.observe(_doc.body, { childList: true, subtree: true });
    }

    /**
     * Entry point.
     *
     * Tries to inject immediately.  If Angular has not yet rendered
     * rightNavbar, polls every 250 ms up to POLL_TIMEOUT_MS, then starts
     * the MutationObserver for subsequent re-renders.
     */
    function init() {
        if (tryInject()) {
            startObserver();
            return;
        }

        var elapsed  = 0;
        var interval = setInterval(function () {
            elapsed += 250;
            if (tryInject()) {
                clearInterval(interval);
                startObserver();
            } else if (elapsed >= POLL_TIMEOUT_MS) {
                clearInterval(interval);
                console.warn(
                    '[LFDashboard] Could not inject the Dashboard button: ' +
                    'id="rightNavbar" was not found after ' +
                    (POLL_TIMEOUT_MS / 1000) + 's. ' +
                    'Check that Browse.aspx still contains id="rightNavbar".'
                );
            }
        }, 250);
    }

    // ── Startup ───────────────────────────────────────────────────────────
    if (_doc.readyState === 'loading') {
        _doc.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

}());
