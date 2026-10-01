// Check PDF responses before handing them to the browser's native reader.
window.loadDocumentPdfPreviews = function (root, signal) {
    return Promise.all([...root.querySelectorAll('iframe[data-preview-source]')].map(async frame => {
        const message = root.querySelector('[data-preview-error]');
        let objectUrl;
        const release = () => { if (objectUrl) URL.revokeObjectURL(objectUrl); };
        if (signal) signal.addEventListener('abort', release, { once:true });
        else window.addEventListener('pagehide', release, { once:true });
        try {
            const response = await fetch(frame.dataset.previewSource, { signal });
            if (!response.ok) throw new Error(response.status === 404 ? 'missing' : 'unavailable');
            const blob = await response.blob();
            const prefix = await blob.slice(0, 1024).text();
            if (!blob.size || !prefix.includes('%PDF-')) throw new Error('invalid');
            if (signal?.aborted) return;
            objectUrl = URL.createObjectURL(new Blob([blob], { type:'application/pdf' }));
            frame.src = objectUrl + '#toolbar=1&view=FitH';
            frame.hidden = false;
            if (message) message.hidden = true;
        } catch (error) {
            if (error.name === 'AbortError') return;
            frame.hidden = true;
            if (message) {
                message.textContent = error.message === 'missing'
                    ? 'لا توجد صورة أو ملف للمعاينة لهذه الوثيقة.'
                    : 'تعذر تحميل ملف PDF كاملًا. أعد المحاولة أو افتح الوثيقة في Laserfiche.';
                message.hidden = false;
            }
        }
    }));
};
