import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import vm from 'node:vm';
const source = await readFile(new URL('../src/LFPortal.Web/wwwroot/js/document-preview.js', import.meta.url), 'utf8');
async function run(status, body, cancel = false) {
    const frame = { dataset:{previewSource:'/Document/Content?entryId=619'}, hidden:true };
    const message = {hidden:true};
    const revoked = [];
    const controller = new AbortController();
    const window = {addEventListener(){}};
    const context = {window, Blob, URL:{createObjectURL:()=> 'blob:pdf', revokeObjectURL:url=>revoked.push(url)},
        fetch:async () => { if(cancel) controller.abort(); return {ok:status === 200, status, blob:async()=>new Blob([body])}; }};
    vm.runInNewContext(source, context);
    await window.loadDocumentPdfPreviews({querySelectorAll:()=>[frame], querySelector:()=>message}, controller.signal);
    return {frame,message,revoked,controller};
}
const pdf = await run(200, '%PDF-1.7\nfixture');
assert.equal(pdf.frame.hidden, false);
assert.equal(pdf.frame.src, 'blob:pdf#toolbar=1&view=FitH');
assert.equal(pdf.message.hidden, true);
pdf.controller.abort();
assert.deepEqual(pdf.revoked, ['blob:pdf']);
for (const [status, body] of [[502,'error'], [401,'login'], [200,'<html>login</html>'], [200,'']]) {
    const result = await run(status, body);
    assert.equal(result.frame.hidden, true);
    assert.equal(result.frame.src, undefined);
    assert.equal(result.message.hidden, false);
    assert.match(result.message.textContent, /تعذر تحميل/);
}
const absent = await run(404,'');
assert.match(absent.message.textContent, /لا توجد صورة/);
const aborted = await run(200,'%PDF-1.7',true);
assert.equal(aborted.frame.src, undefined);
console.log('PDF preview UI: 8 scenarios passed (valid, errors, missing, cancellation, URL cleanup).');
