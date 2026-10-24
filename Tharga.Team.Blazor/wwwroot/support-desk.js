// Whether somebody is at work on the support desk: the page is the visible tab and they are using it.
// Reported at most once per interval, so a busy page costs a call a minute rather than one per keystroke.
const watching = new Map();
const used = ['pointerdown', 'pointermove', 'keydown', 'wheel', 'touchstart', 'focus'];

export function watchActivity(id, reference, intervalMs) {
    stopWatching(id);

    let last = Date.now();
    const report = () => {
        if (document.visibilityState !== 'visible') return;

        const now = Date.now();
        if (now - last < intervalMs) return;

        last = now;
        reference.invokeMethodAsync('OnActivity').catch(() => { });
    };

    used.forEach(name => window.addEventListener(name, report, { passive: true }));
    document.addEventListener('visibilitychange', report);
    watching.set(id, report);
}

export function stopWatching(id) {
    const report = watching.get(id);
    if (!report) return;

    used.forEach(name => window.removeEventListener(name, report));
    document.removeEventListener('visibilitychange', report);
    watching.delete(id);
}
