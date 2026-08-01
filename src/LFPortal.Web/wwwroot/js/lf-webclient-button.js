/**
 * lf-webclient-button.js
 * ──────────────────────────────────────────────────────────────────────────
 * Laserfiche Web Client customization script that adds a "Dashboard" button
 * to the Web Client toolbar.
 *
 * INSTALLATION
 * ─────────────
 * 1. Copy this file to your Laserfiche Web Client customization directory.
 *    Classic Web Access: place it in your custom JavaScript include location
 *    (e.g. CustomJs.js, or referenced from Browse.aspx).
 * 2. Set DASHBOARD_BASE_URL (below) to the URL of your Dashboard application,
 *    e.g. "https://dashboard.corp.local" or "https://dashboard.corp.local:5000".
 * 3. Reload the Laserfiche Web Client.
 *
 * HOW IT WORKS
 * ─────────────
 * When the button is clicked the script:
 *  1. Detects the active repository using multiple strategies (URL params,
 *     path segments, Laserfiche JS globals, DOM text).
 *  2. Opens the Dashboard application in a new browser tab with:
 *        ?repository=<repositoryName>&source=webclient
 *  3. The Dashboard RepositorySessionMiddleware reads these parameters and
 *     stores the repository + source in the ASP.NET Core session, then the
 *     auth guard redirects to /Login if the session is not yet authenticated.
 *
 * SECURITY
 * ─────────
 * Only the repository name and the literal string "webclient" are sent via
 * the URL — no credentials, tokens, or session cookies.
 * ──────────────────────────────────────────────────────────────────────────
 */

(function () {
    'use strict';

    // ── CONFIGURATION ────────────────────────────────────────────────────
    // Set this to the base URL of your Dashboard application.
    // No trailing slash. Protocol + host (+ optional port) only.
    // Example: "https://dashboard.corp.local:5000"
    var DASHBOARD_BASE_URL = '';

    // Display label on the button
    var BUTTON_LABEL = 'Dashboard';

    // Tooltip shown on hover
    var BUTTON_TITLE = 'Open Laserfiche Analytics Dashboard';

    // CSS class added to the injected button element (for custom styling)
    var BUTTON_CSS_CLASS = 'lf-dashboard-btn';

    // How often (ms) to retry finding the toolbar while the SPA loads
    var POLL_INTERVAL_MS = 500;

    // How long (ms) to keep retrying before giving up
    var POLL_TIMEOUT_MS = 15000;
    // ─────────────────────────────────────────────────────────────────────

    /**
     * Detects the active Laserfiche repository name from the current page.
     * Tries multiple strategies in order; returns null if none succeed.
     *
     * @returns {string|null} Repository name, or null if it cannot be determined.
     */
    function detectRepository() {
        // ── Strategy 1: URL query parameters ─────────────────────────────
        // Classic Web Access appends ?repo=Name or ?repository=Name.
        try {
            var params = new URLSearchParams(window.location.search);
            var fromQuery = params.get('repo') ||
                            params.get('repository') ||
                            params.get('db') ||
                            params.get('RepoID');
            if (fromQuery && fromQuery.length > 0) return fromQuery;
        } catch (e) { /* continue */ }

        // ── Strategy 2: Hash-based routing ───────────────────────────────
        // Newer Laserfiche Web App uses SPA-style hash routing, e.g.
        //   #!/Documents?repo=TestEmployee
        try {
            var hash = window.location.hash || '';
            var hashQuery = hash.replace(/^#\/?[^?]*/, '');
            if (hashQuery.length > 1) {
                var hashParams = new URLSearchParams(hashQuery);
                var fromHash = hashParams.get('repo') ||
                               hashParams.get('repository') ||
                               hashParams.get('db');
                if (fromHash && fromHash.length > 0) return fromHash;
            }
        } catch (e) { /* continue */ }

        // ── Strategy 3: URL path segment /repository/Name/ ───────────────
        // Some Laserfiche configurations embed the repo in the path.
        try {
            var pathMatch = window.location.pathname.match(
                /\/(?:repo(?:sitory)?|db)\/([^\/\?#]+)/i
            );
            if (pathMatch && pathMatch[1]) return decodeURIComponent(pathMatch[1]);
        } catch (e) { /* continue */ }

        // ── Strategy 4: Laserfiche JavaScript globals ─────────────────────
        // The Laserfiche Web App exposes repository information through
        // various global objects depending on the version installed.
        try {
            // Laserfiche 10.x / 11.x Web Access
            if (window.LaserficheWebClient && window.LaserficheWebClient.repository) {
                return window.LaserficheWebClient.repository;
            }
            // Laserfiche Cloud / newer Web App
            if (window.Laserfiche && window.Laserfiche.app && window.Laserfiche.app.repositoryId) {
                return window.Laserfiche.app.repositoryId;
            }
            // Some versions expose it on the document
            if (typeof LFRepositoryName !== 'undefined' && LFRepositoryName) {
                return LFRepositoryName;
            }
            if (typeof repositoryName !== 'undefined' && repositoryName) {
                return repositoryName;
            }
        } catch (e) { /* continue */ }

        // ── Strategy 5: Angular / Ember / React app state ─────────────────
        // Last-resort: look for a data attribute or text element that holds
        // the repository name in the rendered DOM.  Selectors here are best
        // guesses for Laserfiche 10.3+ Web App; adjust for your environment.
        try {
            var candidates = [
                '[data-repository]',
                '[data-repo]',
                '#repositoryName',
                '.lf-repository-name',
                '.repo-name',
                'span[ng-bind="vm.repoDisplayName"]',
            ];
            for (var i = 0; i < candidates.length; i++) {
                var el = document.querySelector(candidates[i]);
                if (el) {
                    var val = (el.getAttribute('data-repository') ||
                               el.getAttribute('data-repo') ||
                               el.textContent || '').trim();
                    if (val.length > 0) return val;
                }
            }
        } catch (e) { /* continue */ }

        return null;
    }

    /**
     * Builds the Dashboard URL for the given repository.
     *
     * @param {string} repository  Repository name (already validated non-empty).
     * @returns {string}           Full URL including query parameters.
     */
    function buildDashboardUrl(repository) {
        var base = DASHBOARD_BASE_URL.replace(/\/+$/, '');
        return base +
               '/?repository=' + encodeURIComponent(repository) +
               '&source=webclient';
    }

    /**
     * Handles the Dashboard button click.
     * Detects the repository, builds the URL, and opens Dashboard in a new tab.
     */
    function onButtonClick() {
        if (!DASHBOARD_BASE_URL) {
            alert(
                'Dashboard URL is not configured.\n' +
                'Set DASHBOARD_BASE_URL in lf-webclient-button.js.'
            );
            return;
        }

        var repo = detectRepository();
        if (!repo) {
            alert(
                'Could not detect the active Laserfiche repository.\n\n' +
                'Please navigate into a repository and try again.'
            );
            return;
        }

        var url = buildDashboardUrl(repo);
        window.open(url, '_blank', 'noopener,noreferrer');
    }

    /**
     * Creates and returns the Dashboard button DOM element.
     *
     * @returns {HTMLButtonElement}
     */
    function createButton() {
        var btn = document.createElement('button');
        btn.type        = 'button';
        btn.textContent = BUTTON_LABEL;
        btn.title       = BUTTON_TITLE;
        btn.className   = BUTTON_CSS_CLASS;

        // Inline styles keep the button self-contained — no separate CSS file
        // required for a minimal install.  Override via .lf-dashboard-btn in
        // your own stylesheet if you prefer.
        btn.style.cssText = [
            'display:inline-flex',
            'align-items:center',
            'gap:6px',
            'padding:5px 12px',
            'background:#1e3a8a',
            'color:#fff',
            'border:none',
            'border-radius:4px',
            'font-size:13px',
            'font-weight:600',
            'cursor:pointer',
            'white-space:nowrap',
            'font-family:inherit',
            'line-height:1.4',
            'transition:background .15s',
        ].join(';');

        btn.addEventListener('mouseover', function () {
            btn.style.background = '#1d4ed8';
        });
        btn.addEventListener('mouseout', function () {
            btn.style.background = '#1e3a8a';
        });

        // Bar-chart icon (inline SVG — no external asset needed)
        var svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        svg.setAttribute('width',   '14');
        svg.setAttribute('height',  '14');
        svg.setAttribute('viewBox', '0 0 24 24');
        svg.setAttribute('fill',    'currentColor');
        svg.setAttribute('aria-hidden', 'true');
        svg.innerHTML =
            '<rect x="2"  y="10" width="4" height="11" rx="1"/>' +
            '<rect x="10" y="4"  width="4" height="17" rx="1"/>' +
            '<rect x="18" y="7"  width="4" height="14" rx="1"/>';
        btn.insertBefore(svg, btn.firstChild);

        btn.addEventListener('click', onButtonClick);
        return btn;
    }

    /**
     * Candidate toolbar selectors for various Laserfiche Web Client versions.
     * The script tries each in order and injects the button into the first match.
     * Add selectors here when you identify the correct one for your version.
     *
     * @returns {Element|null}
     */
    function findToolbar() {
        var selectors = [
            // Classic Web Access 10.x toolbar
            '#lfToolbar',
            '#toolbar',
            '.lf-toolbar',
            '.toolbar',
            // Laserfiche 11.x / Web App toolbar
            '[data-id="toolbar"]',
            '[role="toolbar"]',
            // Generic fallback areas
            '#header-toolbar',
            '.header-toolbar',
            '#app-toolbar',
            'nav[aria-label]',
        ];
        for (var i = 0; i < selectors.length; i++) {
            var el = document.querySelector(selectors[i]);
            if (el) return el;
        }
        return null;
    }

    /**
     * Tries to inject the Dashboard button into the toolbar.
     * Returns true on success.
     *
     * @returns {boolean}
     */
    function tryInjectButton() {
        // Do not inject more than once
        if (document.querySelector('.' + BUTTON_CSS_CLASS)) return true;

        var toolbar = findToolbar();
        if (!toolbar) return false;

        var btn = createButton();
        toolbar.appendChild(btn);
        return true;
    }

    /**
     * Entry point — polls for the toolbar until it appears, then injects the button.
     * Uses polling because Laserfiche Web Client is often a SPA that renders
     * the toolbar asynchronously after the initial page load.
     */
    function init() {
        // Try immediately first
        if (tryInjectButton()) return;

        var elapsed = 0;
        var interval = setInterval(function () {
            elapsed += POLL_INTERVAL_MS;
            if (tryInjectButton() || elapsed >= POLL_TIMEOUT_MS) {
                clearInterval(interval);
                if (elapsed >= POLL_TIMEOUT_MS && !document.querySelector('.' + BUTTON_CSS_CLASS)) {
                    console.warn(
                        '[LFDashboard] Could not find a Laserfiche Web Client toolbar ' +
                        'to inject the Dashboard button into after ' + (POLL_TIMEOUT_MS / 1000) + 's. ' +
                        'Check the toolbar selectors in lf-webclient-button.js.'
                    );
                }
            }
        }, POLL_INTERVAL_MS);
    }

    // Run after DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

}());
