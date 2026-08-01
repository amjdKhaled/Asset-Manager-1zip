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
 *    the Dashboard server.  This runs in the CLIENT browser, so "localhost"
 *    means the USER's machine, not the Laserfiche server.
 *    Example: 'http://dashboard-server:5000'  or  'http://192.168.1.100:5000'
 * 2. Copy THIS FILE to:
 *      C:\Program Files\Laserfiche\Web Access\Web Files\assets\custom\lf-dashboard-button.js
 * 3. Browse.aspx must already contain this line (added during initial setup):
 *      <script src="assets/custom/lf-dashboard-button.js"></script>
 *    A Ctrl+F5 browser refresh is sufficient after re-deploying this file.
 *    IIS restart is NOT required.
 *
 * LASERFICHE JAVASCRIPT ENVIRONMENT — IMPORTANT
 * ─────────────────────────────────────────────
 * The Laserfiche AngularJS app shadows the global `document` identifier.
 * Confirmed symptom: `document.querySelectorAll` is not a function.
 * All DOM access in this script uses _doc / _win, captured from the real
 * browser window object at IIFE entry — before Angular can override them.
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
    // Laserfiche's Angular application shadows the global `document` object
    // (confirmed: `document.querySelectorAll` throws TypeError in its context).
    // Capture the real browser objects at IIFE entry, before Angular can
    // replace them.  All DOM operations below use _doc / _win exclusively.
    var _win = window;
    var _doc = window.document;
    // ─────────────────────────────────────────────────────────────────────

    // ── CONFIGURATION ────────────────────────────────────────────────────
    /**
     * Base URL of the Dashboard server.
     * !! No trailing slash. !!
     *
     * IMPORTANT: This value is evaluated in the user's BROWSER, not on the
     * Laserfiche server.  'http://localhost:5000' means the user's own
     * machine.  Change this to a hostname or IP that every client machine
     * can reach, e.g. 'http://dashboard-server:5000'.
     */
    var DASHBOARD_BASE_URL = 'http://localhost:5000';

    /** How long (ms) to poll for rightNavbar before giving up. */
    var POLL_TIMEOUT_MS = 12000;

    /** Stable identifiers for the injected button. */
    var BUTTON_ID        = 'lf-dashboard-btn';
    var BUTTON_DATA_ATTR = 'data-lf-dashboard-button';
    // ─────────────────────────────────────────────────────────────────────

    /**
     * Whether the capture-phase delegated listener has been registered on
     * _doc.  Register only once regardless of how many times tryInject runs.
     */
    var _delegatedRegistered = false;

    /**
     * Used to suppress the direct-on-button listener when the capture-phase
     * delegated listener has already handled the same click event.
     */
    var _lastHandledEvent = null;

    // ─────────────────────────────────────────────────────────────────────

    /**
     * Returns the active Laserfiche repository name.
     *
     * PRIMARY: server-rendered hidden input that Browse.aspx always emits
     *   before Angular boots:
     *   <input type="hidden" id="WebAccessRepositoryName" value="..." />
     *
     * FALLBACK: URL query parameters for edge cases (?repo=, ?db=,
     *   ?repository=).
     *
     * @returns {string|null}  Trimmed repository name, or null.
     */
    function getRepository() {
        // Primary — ASP.NET code-behind sets this before Angular touches the page.
        var el = _doc.getElementById('WebAccessRepositoryName');
        if (el && typeof el.value === 'string' && el.value.trim().length > 0) {
            return el.value.trim();
        }

        // Fallback — URL query string.
        try {
            var params = new URLSearchParams(_win.location.search);
            var fromQuery = params.get('repo') || params.get('db') || params.get('repository');
            if (fromQuery && fromQuery.trim().length > 0) return fromQuery.trim();
        } catch (e) { /* URLSearchParams unavailable — very old browser */ }

        return null;
    }

    /**
     * Builds the Dashboard URL for the given repository.
     *
     * @param {string} repo  Validated non-empty repository name.
     * @returns {string}     Full URL with query parameters.
     */
    function buildDashboardUrl(repo) {
        return DASHBOARD_BASE_URL.replace(/\/+$/, '') +
               '/?repository=' + encodeURIComponent(repo) +
               '&source=webclient';
    }

    /**
     * Click handler.
     *
     * Reads the repository at click-time (not inject-time) so that
     * repository switches within the same Web Client session are picked up.
     *
     * Logging sequence (required — lets us distinguish click failure
     * from repository-detection or navigation failure):
     *   [LFDashboard] Dashboard button clicked.
     *   [LFDashboard] Repository: <name>
     *   [LFDashboard] Opening Dashboard: <url>
     *
     * @param {Event} event
     */
    function onDashboardClick(event) {
        // ── Step 1: confirm click fired ───────────────────────────────────
        console.log('[LFDashboard] Dashboard button clicked.');

        // Prevent the Laserfiche navbar from also processing this click.
        if (event) {
            if (event.stopPropagation)      event.stopPropagation();
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

        // ── Step 4: build URL and open ────────────────────────────────────
        var url = buildDashboardUrl(repo);
        console.log('[LFDashboard] Opening Dashboard: ' + url);

        // window.open must be called synchronously from a user-gesture handler
        // to avoid popup blockers.  Repository detection above is synchronous,
        // so this call is still within the same user gesture.
        var newWin = _win.open(url, '_blank', 'noopener,noreferrer');

        if (!newWin) {
            // Browser blocked the popup.  Fallback: programmatic anchor click.
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
     * Creates the Dashboard button element.
     *
     * Uses _doc.createElement (real browser DOM) rather than the bare
     * `document` global, which Laserfiche Angular may have shadowed.
     *
     * The button and its SVG / text children are set to
     * pointer-events:auto / none explicitly so that clicks on any child
     * element reliably bubble to the <button> handler.
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
            /* Ensure Laserfiche parent styles cannot make the button
               non-interactive.  z-index places it above any sibling overlay. */
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

        // Direct listener on the button (bubble phase).
        // The capture-phase delegated listener on _doc is the resilient
        // primary path; this is a belt-and-suspenders backup.
        btn.addEventListener('click', function (e) {
            // Guard: if the capture-phase delegated handler already ran for
            // this same event, do not call onDashboardClick a second time.
            if (e === _lastHandledEvent) return;
            onDashboardClick(e);
        }, false);

        // Bar-chart icon (inline SVG, no external asset dependency).
        // pointer-events:none on the icon so clicks pass through to the button.
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
        // pointer-events:none on text so clicks fall through to the button.
        label.style.cssText = 'pointer-events:none;';
        btn.appendChild(label);

        return btn;
    }

    /**
     * Registers the capture-phase delegated click listener on _doc exactly
     * once.  Capture phase fires BEFORE Angular's bubble-phase listeners,
     * so it cannot be blocked by Angular stopPropagation() calls.
     *
     * The listener walks up from event.target to find any element that has
     * data-lf-dashboard-button="true", so it works even when the click
     * lands on the SVG icon or the text span inside the button.
     */
    function ensureDelegatedHandler() {
        if (_delegatedRegistered) return;
        _delegatedRegistered = true;

        _doc.addEventListener('click', function (event) {
            var target = event.target;
            // Walk up to find our button or any ancestor with the attribute.
            while (target && target !== _doc) {
                if (target.getAttribute &&
                    target.getAttribute(BUTTON_DATA_ATTR) === 'true') {
                    // Record this event so the direct button listener can
                    // skip it and avoid a double-fire.
                    _lastHandledEvent = event;
                    onDashboardClick(event);
                    return;
                }
                target = target.parentNode;
            }
        }, true /* capture phase — fires before Angular bubble listeners */);
    }

    /**
     * Attempts to inject the Dashboard button into the Web Client toolbar.
     *
     * Target: id="rightNavbar" — the right side of the top action bar,
     * confirmed in Browse.aspx.  Holds the repository picker and user menu.
     *
     * If the button already exists this is a no-op (idempotent).
     *
     * @returns {boolean}  true when the button is present in the DOM.
     */
    function tryInject() {
        // Already present — nothing to do.
        if (_doc.getElementById(BUTTON_ID)) return true;

        var rightNavbar = _doc.getElementById('rightNavbar');
        if (!rightNavbar) return false;

        var btn = createButton();

        // Wrap in <ul><li> to match the existing .rightNavbar list structure.
        var li = _doc.createElement('li');
        li.style.cssText = [
            'list-style:none',
            'display:inline-flex',
            'align-items:center',
            /* Ensure no inherited pointer-events:none from parent <ul>. */
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

        // Insert before any existing children (repo picker, user menu).
        rightNavbar.insertBefore(ul, rightNavbar.firstChild);

        // Register the capture-phase delegated handler (once).
        ensureDelegatedHandler();

        console.info('[LFDashboard] Dashboard button injected into rightNavbar.');
        return true;
    }

    /**
     * Starts a narrow MutationObserver that watches for the Dashboard button
     * being removed from the DOM (e.g. when Laserfiche Angular re-renders the
     * navbar after a repository switch) and re-injects it automatically.
     *
     * The observer is scoped to _doc.body with subtree:true but acts only
     * when our button is missing — it does no work on unrelated mutations.
     */
    function startObserver() {
        if (!_win.MutationObserver) return; // IE10 and below — not supported

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
     * Tries to inject immediately.  If Angular has not yet rendered rightNavbar,
     * polls every 250 ms up to POLL_TIMEOUT_MS, then starts the MutationObserver
     * to handle subsequent re-renders.
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
    // Browse.aspx loads this script in the <head>, so DOMContentLoaded fires
    // before Angular bootstraps the navbar.  Use _doc (real browser document)
    // for the readyState check and event registration.
    if (_doc.readyState === 'loading') {
        _doc.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

}());
