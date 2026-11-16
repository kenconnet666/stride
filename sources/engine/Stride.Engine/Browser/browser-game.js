// The page side of Stride.Engine.BrowserGame. It follows browser-input.js in one module.
// globalThis.strideGame is the state of the game for the page: status ('loading', 'running', 'failed', 'stopped'),
// frames, sceneFrame (the first frame with the scene and its graphics compositor, which load on their own), frameMs
// (a moving average of the time of a frame) and error; startProfile() and profileReport() read the profiler of the
// engine.
const state = globalThis.strideGame = { status: 'loading', frames: 0 };
let detachInput;
let stopped = false;

const canvasOf = selector => {
    const canvas = document.querySelector(selector);
    if (!(canvas instanceof HTMLCanvasElement)) throw new Error(`Stride: no canvas matches '${selector}'.`);
    return canvas;
};

const fail = error => {
    stopped = true;
    detachInput?.();
    state.status = 'failed';
    // The managed text of the exception, when the frame reported one, says more than the JavaScript error
    state.error = state.managedError ?? String(error?.stack || error);
    console.error(error);
    globalThis.dispatchEvent(new CustomEvent('stride-game-failed', { detail: state }));
};

export function baseUrl() { return document.baseURI; }
export function canvasWidth(selector) { return canvasOf(selector).width; }
export function canvasHeight(selector) { return canvasOf(selector).height; }
export function managedError(error) { state.managedError = error; }
export function setTextInput(active) { detachInput?.setTextInput(active); }
export function setPointerLock(locked) { detachInput?.setPointerLock(locked); }
export function setGamePadVibration(index, weak, strong) { detachInput?.setGamePadVibration(index, weak, strong); }

// Gives the canvas its size (when one is asked for), connects its input to the game and starts the frames.
export async function start(selector, width, height) {
    const canvas = canvasOf(selector);
    if (width > 0 && height > 0) {
        canvas.width = width;
        canvas.height = height;
    }
    const exports = await globalThis.getDotnetRuntime(0).getAssemblyExports('Stride.Engine.dll');
    const game = exports.Stride.Engine.BrowserGame;
    detachInput = attachBrowserInput(canvas, {
        key: game.Key, text: game.Text, composition: game.Composition,
        move: game.Move, button: game.Button, wheel: game.Wheel, cancelMouse: game.CancelMouse,
        pointer: game.Pointer, surface: game.Surface, pause: game.Pause,
        gamepad: game.GamePad, gamepadRemoved: game.GamePadRemoved, motion: game.Motion,
        mouseDelta: game.MouseDelta, pointerLock: game.PointerLock
    });
    state.startProfile = game.StartProfile;
    state.profileReport = game.ProfileReport;
    stopped = false;
    state.status = 'running';
    const frame = () => {
        if (stopped) return;
        try {
            detachInput.poll();
            const started = performance.now();
            game.Frame(canvas.width, canvas.height, document.hasFocus(), !document.hidden);
            const elapsed = performance.now() - started;
            state.frameMs = state.frameMs === undefined ? elapsed : state.frameMs * 0.95 + elapsed * 0.05;
            state.frames++;
            if (state.sceneFrame === undefined && game.SceneLoaded()) state.sceneFrame = state.frames;
            requestAnimationFrame(frame);
        } catch (error) { fail(error); }
    };
    requestAnimationFrame(frame);
}

export function stop() {
    if (stopped) return;
    stopped = true;
    detachInput?.();
    state.status = 'stopped';
}
