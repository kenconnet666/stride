// Canvas DOM adapter. Input state and device processing remain in Stride C#.
//
// Returns a function that detaches the adapter. Its poll() property reads the gamepads, which have no events for
// their state: the host calls it before each frame (sink.gamepad, sink.gamepadRemoved). Its setTextInput(active) property moves the keyboard focus to a
// hidden text field while a Stride control edits text: a canvas only gets key codes, so typed text, an input
// method's composition, paste and the on-screen keyboard come from that field (sink.text, sink.composition). The
// motion of a phone or a tablet goes to sink.motion. Its setPointerLock(locked) property asks for the pointer lock:
// sink.pointerLock tells whether the canvas has it, and sink.mouseDelta gives the movements of the locked mouse.
export function attachBrowserInput(canvas, sink) {
    const lifetime = new AbortController();
    const signal = lifetime.signal;
    const oldTabIndex = canvas.getAttribute('tabindex');
    const oldTouchAction = canvas.style.touchAction;
    canvas.tabIndex = 0;
    canvas.style.touchAction = 'none';
    const active = new Map();
    const position = event => {
        const rect = canvas.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0
            ? [(event.clientX - rect.left) / rect.width, (event.clientY - rect.top) / rect.height] : null;
    };
    const pause = () => { active.clear(); sink.pause(); };

    const field = document.createElement('textarea');
    field.setAttribute('aria-label', 'Text input');
    field.autocapitalize = 'off';
    field.autocomplete = 'off';
    field.spellcheck = false;
    // 16px keeps iOS from zooming the page when the field gets the focus.
    field.style.cssText = 'position:fixed;width:1px;height:1em;padding:0;border:0;margin:0;opacity:0;pointer-events:none;resize:none;overflow:hidden;font-size:16px;';
    let textActive = false;
    let composing = false;
    // The input method shows its candidates next to the field: keep it where the user last pressed.
    const placeField = (x, y) => { field.style.left = `${x}px`; field.style.top = `${y}px`; };
    const setTextInput = value => {
        if (textActive === value) return;
        textActive = value;
        if (value) {
            if (!field.isConnected) {
                const rect = canvas.getBoundingClientRect();
                if (!field.style.left) placeField(rect.left, rect.bottom);
                document.body.appendChild(field);
            }
            field.value = '';
            field.focus({ preventScroll: true });
        } else {
            if (composing) { composing = false; sink.composition?.('', 0, 0); }
            const hadFocus = document.activeElement === field;
            field.remove();
            if (hadFocus) canvas.focus({ preventScroll: true });
        }
    };
    field.addEventListener('compositionstart', () => { composing = true; }, { signal });
    field.addEventListener('compositionupdate', event => sink.composition?.(event.data, event.data.length, 0), { signal });
    field.addEventListener('compositionend', event => {
        composing = false;
        sink.composition?.('', 0, 0);
        if (event.data) sink.text?.(event.data);
        field.value = '';
    }, { signal });
    // Committed composition text comes from compositionend: browsers disagree on the input events around it.
    field.addEventListener('input', event => {
        if (composing || event.isComposing || event.inputType?.includes('Composition')) return;
        if (event.inputType !== 'insertLineBreak' && field.value) sink.text?.(field.value);
        field.value = '';
    }, { signal });
    // Not prevented: the field must still turn the key into text. 229 is a key the input method consumed.
    field.addEventListener('keydown', event => { if (!event.isComposing && event.keyCode !== 229) sink.key(event.code, true); }, { signal });
    field.addEventListener('keyup', event => { sink.key(event.code, false); }, { signal });
    field.addEventListener('blur', event => { if (textActive && event.relatedTarget !== canvas) pause(); }, { signal });

    const pointer = (event, type) => {
        const point = position(event);
        if (!point) return;
        sink.surface(canvas.width, canvas.height);
        if (type === 0) {
            placeField(event.clientX, event.clientY);
            (textActive ? field : canvas).focus({ preventScroll: true });
            active.set(event.pointerId, event.pointerType);
            try { canvas.setPointerCapture(event.pointerId); }
            catch (error) { if (error.name !== 'NotFoundError') throw error; }
        }
        if (event.pointerType === 'mouse' && type === 1 && document.pointerLockElement === canvas) {
            // A locked pointer stays where it was: only its movement means something
            const rect = canvas.getBoundingClientRect();
            if (rect.width > 0 && rect.height > 0) sink.mouseDelta?.(event.movementX / rect.width, event.movementY / rect.height);
        } else if (event.pointerType === 'mouse') {
            sink.move(...point);
            if (type === 0 || type === 2) sink.button(event.button, type === 0);
            else if (type === 3) sink.cancelMouse();
        } else sink.pointer(event.pointerId, type, ...point);
        if (type === 2 || type === 3) active.delete(event.pointerId);
        event.preventDefault();
    };
    canvas.addEventListener('keydown', event => { if (sink.key(event.code, true)) event.preventDefault(); }, { signal });
    canvas.addEventListener('keyup', event => { if (sink.key(event.code, false)) event.preventDefault(); }, { signal });
    for (const [event, type] of [['pointerdown', 0], ['pointermove', 1], ['pointerup', 2], ['pointercancel', 3]])
        canvas.addEventListener(event, value => pointer(value, type), { signal });
    canvas.addEventListener('lostpointercapture', event => {
        if (!active.has(event.pointerId)) return;
        if (active.get(event.pointerId) === 'mouse') sink.cancelMouse();
        else sink.pointer(event.pointerId, 3, 0, 0);
        active.delete(event.pointerId);
    }, { signal });
    canvas.addEventListener('wheel', event => {
        sink.wheel(-event.deltaY / (event.deltaMode === 0 ? 100 : event.deltaMode === 1 ? 3 : 1));
        event.preventDefault();
    }, { passive: false, signal });
    canvas.addEventListener('contextmenu', event => event.preventDefault(), { signal });
    // The focus moving to the text field is still input to this canvas.
    canvas.addEventListener('blur', event => { if (event.relatedTarget !== field) pause(); }, { signal });
    window.addEventListener('blur', pause, { signal });
    document.addEventListener('visibilitychange', () => { if (document.hidden) pause(); }, { signal });
    sink.surface(canvas.width, canvas.height);
    window.addEventListener('devicemotion', event => {
        const withGravity = event.accelerationIncludingGravity, user = event.acceleration;
        if (!sink.motion || withGravity?.x == null) return;
        sink.motion(withGravity.x, withGravity.y ?? 0, withGravity.z ?? 0, user?.x ?? NaN, user?.y ?? NaN, user?.z ?? NaN);
    }, { signal });
    // Only the "standard" layout: the order of the buttons and axes of another one is not known.
    let gamepads = new Set();
    const poll = () => {
        if (!sink.gamepad) return;
        const connected = new Set();
        for (const pad of navigator.getGamepads?.() ?? []) {
            if (!pad?.connected || pad.mapping !== 'standard') continue;
            let buttons = 0;
            for (let i = 0; i < Math.min(pad.buttons.length, 16); i++) if (pad.buttons[i].pressed) buttons |= 1 << i;
            sink.gamepad(pad.index, pad.id, buttons, pad.axes[0] ?? 0, pad.axes[1] ?? 0, pad.axes[2] ?? 0, pad.axes[3] ?? 0,
                pad.buttons[6]?.value ?? 0, pad.buttons[7]?.value ?? 0);
            connected.add(pad.index);
        }
        for (const index of gamepads) if (!connected.has(index)) sink.gamepadRemoved(index);
        gamepads = connected;
    };
    const detach = () => {
        if (document.pointerLockElement === canvas) document.exitPointerLock();
        lifetime.abort();
        textActive = false;
        field.remove();
        for (const id of active.keys()) if (canvas.hasPointerCapture(id)) canvas.releasePointerCapture(id);
        pause();
        canvas.style.touchAction = oldTouchAction;
        if (oldTabIndex === null) canvas.removeAttribute('tabindex'); else canvas.setAttribute('tabindex', oldTabIndex);
    };
    // The game asks for the pointer lock; the browser gives it only shortly after a click, and takes it back on Escape
    const setPointerLock = locked => {
        if (locked && document.pointerLockElement !== canvas) Promise.resolve(canvas.requestPointerLock?.()).catch(() => { });
        else if (!locked && document.pointerLockElement === canvas) document.exitPointerLock();
    };
    document.addEventListener('pointerlockchange', () => sink.pointerLock?.(document.pointerLockElement === canvas), { signal });
    detach.setTextInput = setTextInput;
    detach.setPointerLock = setPointerLock;
    detach.poll = poll;
    return detach;
}
