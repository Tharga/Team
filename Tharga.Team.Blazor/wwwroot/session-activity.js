// The browser half of the server-side session (Tharga/Team#471); what it reports and tells other tabs is in the implementation guide, "Session limits".
const watching = new Map();
const used = ['pointerdown', 'pointermove', 'keydown', 'wheel', 'touchstart', 'focus'];
const activityRoute = '/_tharga/session/activity';
const channelName = 'tharga-session';
const kinds = ['activity', 'ended'];
let channel = null;

function openChannel() {
    if (channel || watching.size === 0 || typeof BroadcastChannel === 'undefined') return channel;

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

function closeChannelWhenIdle() {
    if (!channel || watching.size > 0) return;

    channel.close();
    channel = null;
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
    const state = { dotnet, held: false, asking: false, countdown: null, trap: null, dialog: null, last: Date.now(), report: null };
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
    closeChannelWhenIdle();
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

// The dialog is this instance's own element: the time and the buttons are looked up inside it, never across the page.
export function startCountdown(id, remainingMs, dialog) {
    const state = watching.get(id);
    if (!state) return;

    stopCountdown(id);
    state.asking = true;
    if (!dialog || typeof dialog.querySelector !== 'function') return;

    const time = dialog.querySelector('[data-session-watch-time]');
    const end = performance.now() + Math.max(0, remainingMs || 0);
    const tick = () => {
        const left = Math.max(0, end - performance.now());
        if (time) time.textContent = format(left);

        state.countdown = left > 0 ? setTimeout(tick, left % 1000 || 1000) : null;
    };

    tick();

    // A modal question keeps the keyboard: Tab and Shift+Tab go round its answers, and the one that keeps the person signed in has the focus first.
    const answers = () => Array.from(dialog.querySelectorAll('button')).filter(button => !button.disabled);
    state.dialog = dialog;
    state.trap = event => {
        if (event.key !== 'Tab') return;

        const buttons = answers();
        if (buttons.length === 0) return;

        const at = buttons.indexOf(document.activeElement);
        const next = event.shiftKey ? (at <= 0 ? buttons.length - 1 : at - 1) : (at === -1 || at === buttons.length - 1 ? 0 : at + 1);
        buttons[next].focus();
        event.preventDefault();
    };
    dialog.addEventListener('keydown', state.trap);

    const stay = dialog.querySelector('[data-session-watch-stay]');
    if (stay) stay.focus();
}

export function stopCountdown(id) {
    const state = watching.get(id);
    if (!state) return;

    state.asking = false;
    if (state.countdown) clearTimeout(state.countdown);
    state.countdown = null;
    if (state.dialog && state.trap) state.dialog.removeEventListener('keydown', state.trap);
    state.dialog = null;
    state.trap = null;
}
