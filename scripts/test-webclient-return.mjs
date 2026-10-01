import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';

const clientSource = fs.readFileSync('src/LFPortal.Web/wwwroot/js/lf-webclient-button.js', 'utf8')
    .replace('__DASHBOARD_URL_NOT_CONFIGURED__', 'http://localhost:5000');
const receiverEvents = {};
let focused = 0;
let toolbar = true;
const client = {
    location: new URL('https://localhost/laserfiche/Browse.aspx?db=TestEmployee#/id=1;view=browse'),
    addEventListener: (type, fn) => { receiverEvents[type] = fn; },
    focus: () => focused++,
    document: {
        readyState: 'loading',
        addEventListener() {},
        getElementById: id => id === 'rightNavbar' ? (toolbar ? {} : null) :
            id === 'WebAccessRepositoryName' ? { value: 'TestEmployee' } : null
    }
};
vm.runInNewContext(clientSource, { window: client, URL, URLSearchParams, console });
const replies = [];
const portal = { postMessage: (data, origin) => replies.push({ data, origin }) };
const searchUrl = 'https://localhost/laserfiche/Browse.aspx?db=testemployee#search=%7BLF%3AID%3D445%7D;view=search';
const request = (origin = 'http://localhost:5000', url = searchUrl) => receiverEvents.message({
    origin, source: portal, data: { type: 'lf-dashboard-search', requestId: 'one', url }
});
request('https://evil.test');
request('http://localhost:5000', searchUrl.replace('db=testemployee', 'db=another'));
request('http://localhost:5000', searchUrl.replace('/laserfiche/', '/another/'));
toolbar = false; request(); toolbar = true;
assert.equal(replies.length, 0);
request();
assert.equal(replies.length, 1);
assert.equal(client.location.hash, '#/search=%7BLF%3AID%3D445%7D;view=search');
assert.equal(client.location.pathname, '/laserfiche/Browse.aspx');
assert.equal(client.location.search, '?db=TestEmployee');
assert.equal(focused, 1);
assert.equal(replies[0].origin, 'http://localhost:5000');

const senderSource = fs.readFileSync('src/LFPortal.Web/wwwroot/js/lf-webclient-return.js', 'utf8');
let click;
let listener;
let fallback;
let cancelled = false;
let prevented = 0;
let sent;
const assigned = [];
const opener = { closed: false, focus() {}, postMessage: data => { sent = data; } };
const window = {
    opener, location: { assign: url => assigned.push(url) },
    addEventListener: (_, fn) => { listener = fn; },
    removeEventListener: () => { listener = undefined; }
};
vm.runInNewContext(senderSource, {
    window, URL, Date, Math,
    document: { addEventListener: (_, fn) => { click = fn; } },
    setTimeout: fn => { fallback = fn; return 1; },
    clearTimeout: () => { cancelled = true; }
});
const link = { href: 'http://localhost:5000/Archive/OpenInLaserfiche?entryId=445', dataset: { laserficheUrl: searchUrl } };
const event = { button: 0, target: { closest: () => link }, preventDefault: () => prevented++ };
click(event);
assert.equal(prevented, 1);
assert.equal(sent.url, searchUrl);
listener({ source: opener, origin: 'https://evil.test', data: { type: 'lf-dashboard-search-accepted', requestId: sent.requestId } });
assert.equal(cancelled, false);
listener({ source: opener, origin: 'https://localhost', data: { type: 'lf-dashboard-search-accepted', requestId: sent.requestId } });
assert.equal(cancelled, true);
assert.equal(assigned.length, 0);
click(event); fallback();
assert.deepEqual(assigned, [link.href]);
window.opener = null; click(event);
assert.equal(prevented, 2);
console.log('Web Client return: existing-session search, origin/repository/path checks, acknowledgement and fallback passed.');
