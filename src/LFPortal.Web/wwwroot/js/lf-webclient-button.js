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
 * EVENT HANDLING ARCHITECTURE
 * ────────────────────────────
 * ONE capture-phase delegated listener on _doc is the single authoritative
 * click path.  Capture phase fires before Angular's bubble-phase listeners
 * so it cannot be blocked by Angular stopPropagation() calls.
 * No direct click listener is registered on the button itself — that was
 * the cause of the double-open bug.  A 500 ms launch lock prevents any
 * residual duplicate events from opening a second tab.
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
     * 'http://localhost:5000' points to the user's own machine.
     * Change to a hostname / IP reachable by every client machine, e.g.:
     *   'http://dashboard-server:5000'
     */
    var DASHBOARD_BASE_URL = 'http://localhost:5000';

    /** How long (ms) to poll for rightNavbar before giving up. */
    var POLL_TIMEOUT_MS = 12000;

    /** Stable identifiers for the injected button. */
    var BUTTON_ID        = 'lf-dashboard-btn';
    var BUTTON_DATA_ATTR = 'data-lf-dashboard-button';

    /**
     * Cooldown duration (ms) after a successful launch.
     * Prevents residual duplicate events from opening a second tab.
     * Does NOT prevent the user from clicking again after the cooldown.
     */
    var LAUNCH_COOLDOWN_MS = 500;
    // ─────────────────────────────────────────────────────────────────────

    /**
     * True while a Dashboard window.open is in progress / just completed.
     * Reset after LAUNCH_COOLDOWN_MS.
     */
    var _launchInProgress = false;

    /**
     * True once the capture-phase delegated listener has been registered
     * on _doc.  Ensures it is registered exactly once.
     */
    var _delegatedRegistered = false;

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
               '/?repository=' + encodeURIComponent(repo) +
               '&source=webclient';
    }

    /**
     * Handles a confirmed Dashboard button click.
     *
     * ONE execution per physical click is guaranteed by:
     *   1. A single capture-phase delegated listener (no direct button listener).
     *   2. The _launchInProgress cooldown guard.
     *
     * Console log sequence for a single successful click:
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

        // Prevent Laserfiche navbar from processing this click.
        if (event) {
            if (event.stopPropagation)          event.stopPropagation();
            if (event.stopImmediatePropagation) event.stopImmediatePropagation();
        }

        // ── Step 2: validate configuration ───────────────────────────────
        if (!DASHBOARD_BASE_URL) {
            alert(
                'Dashboard URL is not configured.\n' +
                'Set DASHBOARD_BASE_URL in lf-dashboard-button.js.'
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

        // ── Step 4: open Dashboard ────────────────────────────────────────
        var url = buildDashboardUrl(repo);
        console.log('[LFDashboard] Opening Dashboard: ' + url);

        // window.open must be called synchronously within the user-gesture
        // handler — repository detection above is synchronous, so this call
        // is still within the same gesture and will not be blocked as a popup.
        var newWin = _win.open(url, '_blank', 'noopener,noreferrer');

        if (!newWin) {
            // Browser blocked the popup.  Fallback: programmatic anchor click.
            // Only reached when window.open fails — never runs alongside it.
            console.warn('[LFDashboard] Browser blocked the Dashboard popup. Trying anchor fallback.');
            try {
                var a = _doc.createElement('a');
                a.href   = url;
                a.target = '_blank';
                a.rel    = 'noopener noreferrer';
                a.style.display = 'none';
                _doc.body.appendChild(a);
                a.click();
                _doc.body.removeChild(a);
            } catch (e) {
                console.warn('[LFDashboard] Anchor fallback also failed: ' + e);
                alert(
                    'Your browser blocked the Dashboard from opening.\n\n' +
                    'Allow pop-ups for this site, or open this URL manually:\n' +
                    url
                );
            }
        }
    }

    /**
     * Creates the Dashboard button element using the real browser DOM (_doc).
     *
     * NO click listener is attached here.
     * The single authoritative click path is the capture-phase delegated
     * listener registered by ensureDelegatedHandler().  Attaching a direct
     * listener on the button as well was the root cause of the double-open bug.
     *
     * pointer-events:none on the SVG icon and text label ensures that clicks
     * anywhere inside the button register the <button> itself as event.target,
     * so the delegated listener's parentNode walk terminates quickly.
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
        // pointer-events:none — clicks on the icon fall through to the <button>.
        svg.style.cssText = 'flex-shrink:0;vertical-align:middle;pointer-events:none;';
        svg.innerHTML =
            '<rect x="2"  y="10" width="4" height="11" rx="1"/>' +
            '<rect x="10" y="4"  width="4" height="17" rx="1"/>' +
            '<rect x="18" y="7"  width="4" height="14" rx="1"/>';
        btn.appendChild(svg);

        var label = _doc.createElement('span');
        label.textContent = 'Dashboard';
        // pointer-events:none — clicks on the label fall through to the <button>.
        label.style.cssText = 'pointer-events:none;';
        btn.appendChild(label);

        // !! No click listener added here !!
        // The single authoritative handler is the capture-phase delegated
        // listener on _doc registered in ensureDelegatedHandler().

        return btn;
    }

    /**
     * Registers the capture-phase delegated click listener on _doc exactly
     * once.
     *
     * Why capture phase?
     *   Fires BEFORE Angular's bubble-phase listeners, so it cannot be
     *   silently blocked by Angular stopPropagation() calls on a parent.
     *
     * Why delegated from _doc?
     *   Survives Laserfiche Angular re-rendering the navbar.  Even if
     *   Angular replaces the button's DOM node, the document-level listener
     *   continues to match any element that carries BUTTON_DATA_ATTR.
     *
     * Why only once?
     *   _delegatedRegistered ensures that MutationObserver-triggered
     *   re-injection calls and the polling loop cannot register a second
     *   listener, which would double-fire onDashboardClick.
     */
    function ensureDelegatedHandler() {
        if (_delegatedRegistered) return;
        _delegatedRegistered = true;

        _doc.addEventListener('click', function (event) {
            var target = event.target;
            // Walk up from the clicked element.
            // With pointer-events:none on SVG/span, event.target is already
            // the <button>; the loop is a safety net for edge cases.
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

        // Insert before existing children (repo picker, user menu).
        rightNavbar.insertBefore(ul, rightNavbar.firstChild);

        // Register the single authoritative click handler (once).
        ensureDelegatedHandler();

        console.info('[LFDashboard] Dashboard button injected into rightNavbar.');
        return true;
    }

    /**
     * MutationObserver — re-injects the button if Laserfiche Angular
     * re-renders the navbar (e.g. after a repository switch).
     *
     * Scoped to _doc.body (subtree).  Acts only when our button is missing
     * and rightNavbar still exists — no-ops on all unrelated mutations.
     * The delegated click handler (ensureDelegatedHandler) is NOT
     * re-registered on re-injection because _delegatedRegistered is already
     * true.
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
    // Browse.aspx loads this script in <head>, so DOMContentLoaded fires
    // before Angular bootstraps the navbar.  Use _doc (real browser document)
    // for the readyState check and event registration.
    if (_doc.readyState === 'loading') {
        _doc.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

}());
