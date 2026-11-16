// Runs on the worker of the managed thread that plays the engine (imported there with JSHost.ImportAsync).
// It gets the canvas of the page for the WebGPU surface of the managed code, and gives that code its animation frames.

const channel = new BroadcastChannel('stride-engine-worker');
const frameTimes = { tickMs: 0, frames: 0, started: 0 };

// Resolves to "<width>x<height>" once the page has transferred its canvas. The canvas is registered as the target
// named "#canvas" of Emscripten, which has no document to look it up in on this thread.
export function receiveCanvas() {
    const token = Math.random().toString(36).slice(2);
    return new Promise(resolve => {
        self.addEventListener('message', event => {
            const message = event.data;
            // The page asks every worker of the runtime: only the one that made the token answers
            if (message?.strideEngineWorker === undefined || message.token !== token) return;
            if (message.strideEngineWorker === 'who') channel.postMessage({ type: 'here', token, index: message.index });
            else if (message.strideEngineWorker === 'canvas') {
                globalThis.getDotnetRuntime(0).Module.specialHTMLTargets['#canvas'] = message.canvas;
                resolve(`${message.canvas.width}x${message.canvas.height}`);
            }
        });
        channel.postMessage({ type: 'waiting', token });
    });
}

// tick(frame) is managed code: it draws the frame. The browser presents the canvas when the animation frame returns.
export function runFrames(tick, frames) {
    frameTimes.started = performance.now();
    return new Promise((resolve, reject) => {
        let frame = 0;
        const draw = () => {
            try {
                const before = performance.now();
                tick(frame);
                frameTimes.tickMs += performance.now() - before;
                frameTimes.frames = ++frame;
                if (frame < frames) self.requestAnimationFrame(draw); else resolve();
            } catch (error) { reject(error); }
        };
        self.requestAnimationFrame(draw);
    });
}

export function report(json) {
    const result = JSON.parse(json);
    if (result.error) { channel.postMessage({ type: 'error', error: result.error }); return; }
    channel.postMessage({
        type: 'report', report: {
            ...result, tickMsPerFrame: +(frameTimes.tickMs / Math.max(1, frameTimes.frames)).toFixed(2),
            seconds: +((performance.now() - frameTimes.started) / 1000).toFixed(2), inWorker: typeof document === 'undefined',
        }
    });
}
