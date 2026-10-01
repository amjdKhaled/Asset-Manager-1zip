(function () {
    'use strict';
    document.addEventListener('click', function (event) {
        if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        var link = event.target.closest('a');
        if (!link || !window.opener || window.opener.closed) return;
        var target;
        try { target = new URL(link.href); } catch (_) { return; }
        // All archive and document buttons retain their ordinary href fallback.
        // The local redirect route already knows the active Web Client address.
        var direct = link.dataset.laserficheUrl || link.href;
        try { target = new URL(direct); } catch (_) { return; }
        if (!/\/browse\.aspx$/i.test(target.pathname) || !/^#[/?]?search=.+;view=search$/.test(target.hash)) return;
        event.preventDefault();
        var parent = window.opener;
        var requestId = String(Date.now()) + '-' + Math.random().toString(36).slice(2);
        var timeout;
        function accepted(message) {
            if (message.source !== parent || message.origin !== target.origin ||
                !message.data || message.data.type !== 'lf-dashboard-search-accepted' ||
                message.data.requestId !== requestId) return;
            clearTimeout(timeout);
            window.removeEventListener('message', accepted);
            try { parent.focus(); } catch (_) { }
        }
        window.addEventListener('message', accepted);
        timeout = setTimeout(function () {
            window.removeEventListener('message', accepted);
            window.location.assign(link.href);
        }, 800);
        try { parent.postMessage({ type: 'lf-dashboard-search', requestId: requestId, url: target.href }, target.origin); }
        catch (_) { clearTimeout(timeout); window.removeEventListener('message', accepted); window.location.assign(link.href); }
    });
}());
