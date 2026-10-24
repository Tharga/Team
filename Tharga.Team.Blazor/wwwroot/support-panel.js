// The support panel's width, its conversation's scroll position, and its one rule outside its own markup.

// Hidden on narrow screens, where the host's support page is used instead. This cannot be scoped CSS: the rule
// targets the sidebar element Radzen renders, which carries none of the component's scope attribute. Added once,
// here, so a host links nothing. The .rz-sidebar in the selector outranks Radzen's own `.rz-layout .rz-sidebar`.
export function install() {
    if (document.getElementById('support-panel-rules')) return;

    const style = document.createElement('style');
    style.id = 'support-panel-rules';
    style.textContent = '@media (max-width: 768px) { .rz-layout .rz-sidebar.support-panel-sidebar { display: none; } }';
    document.head.appendChild(style);
}

// The panel is docked right, so its left edge is dragged and its right edge stays put. The width is reported once,
// on release, so a drag is one save rather than one per pointer move.
export function enableResize(handle, reference, minimumWidth) {
    if (!handle || handle.dataset.supportPanelResize) return;

    handle.dataset.supportPanelResize = '1';
    const panel = handle.closest('.support-panel');
    if (!panel) return;

    let dragging = false;

    handle.addEventListener('pointerdown', e => {
        dragging = true;
        handle.setPointerCapture(e.pointerId);
        document.body.style.userSelect = 'none';
        e.preventDefault();
    });

    handle.addEventListener('pointermove', e => {
        if (!dragging) return;

        const maximum = Math.max(minimumWidth, window.innerWidth * 0.6);
        const width = Math.min(maximum, Math.max(minimumWidth, panel.getBoundingClientRect().right - e.clientX));
        panel.style.width = width + 'px';
    });

    const stop = e => {
        if (!dragging) return;

        dragging = false;
        try {
            handle.releasePointerCapture(e.pointerId);
        } catch {
        }

        document.body.style.userSelect = '';
        reference.invokeMethodAsync('OnResized', Math.round(panel.getBoundingClientRect().width)).catch(() => { });
    };

    handle.addEventListener('pointerup', stop);
    handle.addEventListener('pointercancel', stop);
}

// A few pixels of slack: a log scrolled by a fractional pixel is still at the bottom to a reader.
export function isAtBottom(element, slack) {
    if (!element) return true;
    return element.scrollHeight - element.scrollTop - element.clientHeight <= (slack || 4);
}

export function scrollToBottom(element) {
    if (element) element.scrollTop = element.scrollHeight;
}
