// Whether somebody is using the page, for the server-side session's idle timeout (Tharga/Team#471): a Blazor circuit sends no HTTP request while they
// work, so the page says so. Only from the visible tab, and at most once per interval; the server throttles again per session, so several tabs cost no
// more than one. Nothing is kept in the browser: the session is the one in the cookie the request carries.
//
// The tabs of one browser also tell each other, over one channel, that somebody was active or that the session ended, so a "Still here?" open in another
// tab is looked at again on the server. A message carries only its kind, and decides nothing: the server reads the store. The countdown in the dialog is
// a display of what the server said was left, ticking only while the dialog is open.
const watching = new Map();
const used = ['pointerdown', 'pointermove', 'keydown', 'wheel', 'touchstart', 'focus'];
const activityRoute = '/_tharga/session/activity';
const channelName = 'tharga-session';
const kinds = ['activity', 'ended'];
let channel = null;

function openChannel() {
    if (channel || typeof BroadcastChannel === 'undefined') return channel;

    channel = new BroadcastChannel(channelName);
    channel.onmessage = event => {
        const kind = event.data && event.data.kind;
        if (!kinds.includes(kind)) return;

        watching.forEach(state => {
            // Somebody active in another tab matters only to a tab that is asking; an end matters to every tab.
            if (kind === 'activity' && !state.asking) return;
            if (state.dotnet) state.dotnet.invokeMethodAsync('OnPeerSignal', kind).catch(() => { });
        });
    };

    return channel;
}

function post(kind) {
    const open = openChannel();
    if (open && kinds.includes(kind)) open.postMessage({ kind });
}

function format(ms) {
    const seconds = Math.ceil(ms / 1000);
    return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
}

export function watchActivity(id, intervalMs, url, dotnet) {
    stopWatching(id);

    const target = url || activityRoute;
    const state = { dotnet, held: false, asking: false, countdown: null, last: Date.now(), report: null };
    state.report = () => {
        if (state.held || document.visibilityState !== 'visible') return;

        const now = Date.now();
        if (now - state.last < intervalMs) return;

        state.last = now;
        fetch(target, { method: 'POST', credentials: 'same-origin', keepalive: true })
            .then(response => { if (response.ok) post('activity'); })
            .catch(() => { });
    };

    used.forEach(name => window.addEventListener(name, state.report, { passive: true }));
    document.addEventListener('visibilitychange', state.report);
    watching.set(id, state);
    openChannel();
}

export function stopWatching(id) {
    const state = watching.get(id);
    if (!state) return;

    stopCountdown(id);
    used.forEach(name => window.removeEventListener(name, state.report));
    document.removeEventListener('visibilitychange', state.report);
    watching.delete(id);
}

// While the question is open, using the page is not an answer: only "Yes" is.
export function holdActivity(id, held) {
    const state = watching.get(id);
    if (!state) return;

    state.held = !!held;
    if (!state.held) state.last = Date.now();
}

export function announce(id, kind) {
    post(kind);
}

export function startCountdown(id, remainingMs, selector) {
    const state = watching.get(id);
    if (!state) return;

    stopCountdown(id);
    state.asking = true;

    const end = performance.now() + Math.max(0, remainingMs || 0);
    const tick = () => {
        const left = Math.max(0, end - performance.now());
        const element = document.querySelector(selector);
        if (element) element.textContent = format(left);

        state.countdown = left > 0 ? setTimeout(tick, left % 1000 || 1000) : null;
    };

    tick();

    // The question is asked of the keyboard too: focus moves to the answer that keeps the person signed in.
    const countdown = document.querySelector(selector);
    const dialog = countdown && countdown.closest('[role="alertdialog"]');
    const stay = dialog && dialog.querySelector('[data-session-watch-stay]');
    if (stay) stay.focus();
}

export function stopCountdown(id) {
    const state = watching.get(id);
    if (!state) return;

    state.asking = false;
    if (state.countdown) clearTimeout(state.countdown);
    state.countdown = null;
}
