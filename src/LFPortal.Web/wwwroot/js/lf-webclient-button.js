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
 * 1. Set DASHBOARD_BASE_URL below to the URL users use to reach the Dashboard.
 * 2. Copy THIS FILE to:
 *      C:\Program Files\Laserfiche\Web Access\Web Files\assets\custom\lf-dashboard-button.js
 * 3. Add ONE LINE to Browse.aspx right after the browse-custom.css include
 *    (see deployment instructions in docs/WebClientIntegration.md).
 *
 * REPOSITORY DETECTION
 * ─────────────────────
 * Uses the server-rendered hidden input:
 *   <input type="hidden" id="WebAccessRepositoryName" value="TestEmployee"/>
 * This is set by the ASP.NET code-behind before Angular boots — it is always
 * present and always correct. URL parameter parsing is a fallback only.
 *
 * SECURITY
 * ─────────
 * Only the repository name and the literal string "webclient" are sent via
 * the URL. No credentials, tokens, or session cookies leave the Web Client.
 * ──────────────────────────────────────────────────────────────────────────
 */

(function () {
    'use strict';

    // ── CONFIGURATION ────────────────────────────────────────────────────
    // The base URL of the Dashboard application.
    // No trailing slash. Example: "http://localhost:5000"
    var DASHBOARD_BASE_URL = 'http://localhost:5000';

    // How long (ms) to keep polling for the rightNavbar before giving up.
    var POLL_TIMEOUT_MS = 12000;
    // ─────────────────────────────────────────────────────────────────────

    /**
     * Returns the active Laserfiche repository name.
     *
     * PRIMARY: reads the server-rendered hidden input that Browse.aspx
     * always emits:
     *   <input type="hidden" id="WebAccessRepositoryName" value="..." />
     *
     * FALLBACK: URL query parameters (?repo= or ?db=) for edge cases where
     * the page context is read before the hidden field is available.
     *
     * @returns {string|null}
     */
    function getRepository() {
        // Primary — server-rendered; always present and correct once the
        // ASP.NET page has rendered.
        var el = document.getElementById('WebAccessRepositoryName');
        if (el && el.value && el.value.length > 0) {
            return el.value;
        }

        // Fallback — URL query string (Browse.aspx uses ?repo= in some
        // configurations and ?db= in others per the official docs).
        try {
            var params = new URLSearchParams(window.location.search);
            var fromQuery = params.get('repo') || params.get('db') || params.get('repository');
            if (fromQuery && fromQuery.length > 0) return fromQuery;
        } catch (e) { /* URLSearchParams not available — very old browser */ }

        return null;
    }

    /**
     * Builds the Dashboard URL for the given repository.
     *
     * @param {string} repo  Validated non-empty repository name.
     * @returns {string}     Full URL including query parameters.
     */
    function buildDashboardUrl(repo) {
        return DASHBOARD_BASE_URL.replace(/\/+$/, '') +
               '/?repository=' + encodeURIComponent(repo) +
               '&source=webclient';
    }

    /**
     * Click handler — reads the repository at click-time (not at inject-time)
     * so that repository switches within the same session are captured.
     */
    function onDashboardClick() {
        if (!DASHBOARD_BASE_URL) {
            alert(
                'Dashboard URL is not configured.\n' +
                'Set DASHBOARD_BASE_URL in lf-dashboard-button.js.'
            );
            return;
        }

        var repo = getRepository();
        if (!repo) {
            alert(
                'Could not detect the active Laserfiche repository.\n\n' +
                'Please reload the page and try again. If the problem persists,\n' +
                'check the browser console for lf-dashboard-button.js errors.'
            );
            return;
        }

        var url = buildDashboardUrl(repo);
        window.open(url, '_blank', 'noopener,noreferrer');
    }

    /**
     * Creates the Dashboard button element, styled to match the existing
     * Laserfiche Web Access navbar buttons (browseNavButton class).
     *
     * @returns {HTMLButtonElement}
     */
    function createButton() {
        var btn = document.createElement('button');
        btn.type      = 'button';
        btn.id        = 'lf-dashboard-btn';
        btn.title     = 'Open Analytics Dashboard';
        btn.className = 'btn browseNavButton lf-dashboard-btn';

        // Inline overrides — keep the button visually consistent with the
        // Laserfiche navbar without requiring a separate CSS rule.
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
        ].join(';');

        btn.addEventListener('mouseover', function () {
            btn.style.background = 'rgba(255,255,255,0.15)';
        });
        btn.addEventListener('mouseout', function () {
            btn.style.background = 'transparent';
        });

        // Bar-chart icon — inline SVG, no external asset dependency.
        // Uses the same waicon24 sizing class the Web Client uses for toolbar icons.
        var svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        svg.setAttribute('width',   '20');
        svg.setAttribute('height',  '20');
        svg.setAttribute('viewBox', '0 0 24 24');
        svg.setAttribute('fill',    'currentColor');
        svg.setAttribute('aria-hidden', 'true');
        svg.style.cssText = 'flex-shrink:0;vertical-align:middle;';
        svg.innerHTML =
            '<rect x="2"  y="10" width="4" height="11" rx="1"/>' +
            '<rect x="10" y="4"  width="4" height="17" rx="1"/>' +
            '<rect x="18" y="7"  width="4" height="14" rx="1"/>';
        btn.appendChild(svg);

        var label = document.createElement('span');
        label.textContent = 'Dashboard';
        btn.appendChild(label);

        btn.addEventListener('click', onDashboardClick);
        return btn;
    }

    /**
     * Attempts to inject the Dashboard button into the Web Client toolbar.
     *
     * Target: id="rightNavbar" — the right side of the top action bar,
     * confirmed in Browse.aspx. Holds the repository picker and user menu.
     * The button is inserted as the FIRST item so it appears to the left of
     * the repository picker.
     *
     * @returns {boolean}  true when the button was successfully injected.
     */
    function tryInject() {
        if (document.getElementById('lf-dashboard-btn')) return true; // already present

        var rightNavbar = document.getElementById('rightNavbar');
        if (!rightNavbar) return false;

        // Wrap in <ul><li> to match the existing .rightNavbar children.
        var li  = document.createElement('li');
        li.appendChild(createButton());

        var ul  = document.createElement('ul');
        ul.className  = 'nav navbar-nav lf-dashboard-nav';
        ul.style.cssText = 'margin:0;';
        ul.appendChild(li);

        // Insert before any existing children (repo picker, user menu).
        rightNavbar.insertBefore(ul, rightNavbar.firstChild);

        console.info('[LFDashboard] Dashboard button injected into rightNavbar.');
        return true;
    }

    /**
     * Entry point.
     *
     * Tries immediately; if the Angular-rendered rightNavbar is not yet in
     * the DOM, polls every 250 ms up to POLL_TIMEOUT_MS.
     *
     * Angular finishes rendering the toolbar well within 3-5 seconds on a
     * typical LAN, so the button appears before the user can click it.
     */
    function init() {
        if (tryInject()) return;

        var elapsed  = 0;
        var interval = setInterval(function () {
            elapsed += 250;
            if (tryInject() || elapsed >= POLL_TIMEOUT_MS) {
                clearInterval(interval);
                if (elapsed >= POLL_TIMEOUT_MS && !document.getElementById('lf-dashboard-btn')) {
                    console.warn(
                        '[LFDashboard] Could not inject the Dashboard button: ' +
                        'id="rightNavbar" was not found after ' + (POLL_TIMEOUT_MS / 1000) + 's. ' +
                        'Check that Browse.aspx still contains id="rightNavbar".'
                    );
                }
            }
        }, 250);
    }

    // Run after DOM is ready (Browse.aspx loads the script in the <head>,
    // so DOMContentLoaded fires before Angular bootstraps the navbar).
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

}());
