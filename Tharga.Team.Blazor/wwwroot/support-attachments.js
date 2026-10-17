// A blob opened from the application runs in its origin, so only a type shown without script opens; anything else is saved.
const viewable = ['application/pdf', 'image/png', 'image/jpeg', 'image/gif', 'image/webp', 'text/plain'];
const revokeAfterMilliseconds = 60000;

export async function open(streamReference, contentType, fileName) {
    const buffer = await streamReference.arrayBuffer();

    if (!viewable.includes(mediaType(contentType))) {
        download(buffer, fileName);
        return false;
    }

    const url = URL.createObjectURL(new Blob([buffer], { type: contentType }));
    const opened = window.open(url, '_blank');

    if (!opened) {
        URL.revokeObjectURL(url);
        download(buffer, fileName);
        return false;
    }

    try {
        opened.opener = null;
    } catch {
    }

    revokeLater(url);
    return true;
}

export async function save(streamReference, fileName) {
    download(await streamReference.arrayBuffer(), fileName);
}

function download(buffer, fileName) {
    const url = URL.createObjectURL(new Blob([buffer], { type: 'application/octet-stream' }));
    const anchor = document.createElement('a');

    anchor.href = url;
    anchor.download = fileName || 'attachment';
    anchor.rel = 'noopener';
    anchor.style.display = 'none';

    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();

    revokeLater(url);
}

function mediaType(contentType) {
    return (contentType || '').split(';')[0].trim().toLowerCase();
}

function revokeLater(url) {
    setTimeout(() => URL.revokeObjectURL(url), revokeAfterMilliseconds);
}
