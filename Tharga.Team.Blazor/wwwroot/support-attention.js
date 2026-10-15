// Whether somebody is looking at the page: it is the visible tab and its window has focus.
// An answer arriving while nobody looks must stay unread until somebody does.
const waiting = new Map();

export function isAttended() {
    return document.visibilityState === 'visible' && document.hasFocus();
}

export function whenAttended(id, reference) {
    cancel(id);

    const check = () => {
        if (!isAttended()) return;
        cancel(id);
        reference.invokeMethodAsync('OnAttended').catch(() => { });
    };

    document.addEventListener('visibilitychange', check);
    window.addEventListener('focus', check);
    waiting.set(id, check);
}

export function cancel(id) {
    const check = waiting.get(id);
    if (!check) return;

    document.removeEventListener('visibilitychange', check);
    window.removeEventListener('focus', check);
    waiting.delete(id);
}
