// Whether somebody is using the page, for the server-side session's idle timeout (Tharga/Team#471): a Blazor circuit sends no HTTP request while they
// work, so the page says so. Only from the visible tab, and at most once per interval; the server throttles again per session, so several tabs cost no
// more than one. Nothing is kept in the browser: the session is the one in the cookie the request carries.
const watching = new Map();
const used = ['pointerdown', 'pointermove', 'keydown', 'wheel', 'touchstart', 'focus'];
const activityRoute = '/_tharga/session/activity';

export function watchActivity(id, intervalMs, url) {
    stopWatching(id);

    const target = url || activityRoute;
    let last = Date.now();
    const report = () => {
        if (document.visibilityState !== 'visible') return;

        const now = Date.now();
        if (now - last < intervalMs) return;

        last = now;
        fetch(target, { method: 'POST', credentials: 'same-origin', keepalive: true }).catch(() => { });
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

// Stubs: the "Still here?" coordination lands with the implementation (Tharga/Team#471).
export function holdActivity(id, held) { }

export function announce(id, kind) { }

export function startCountdown(id, remainingMs, selector) { }

export function stopCountdown(id) { }
