// Runs on the worker of the managed thread that plays the engine (imported there with JSHost.ImportAsync).
// It gets the canvas of the page, draws frames on it with WebGPU, and calls managed code each frame.

const channel = new BroadcastChannel('stride-engine-worker');

function receiveCanvas() {
    const token = Math.random().toString(36).slice(2);
    return new Promise(resolve => {
        self.addEventListener('message', event => {
            const message = event.data;
            // The page asks every worker of the runtime: only the one that made the token answers
            if (message?.strideEngineWorker === undefined || message.token !== token) return;
            if (message.strideEngineWorker === 'who') channel.postMessage({ type: 'here', token, index: message.index });
            else if (message.strideEngineWorker === 'canvas') resolve(message.canvas);
        });
        channel.postMessage({ type: 'waiting', token });
    });
}

// tick(frame) is managed code: it returns the red channel of the frame, 0 to 255, computed on Stride's thread pool.
export async function run(tick, frames) {
    try {
        const canvas = await receiveCanvas();
        if (!navigator.gpu) throw new Error('This worker has no WebGPU.');
        const adapter = await navigator.gpu.requestAdapter();
        const device = await adapter.requestDevice();
        const context = canvas.getContext('webgpu');
        const format = navigator.gpu.getPreferredCanvasFormat();
        context.configure({ device, format, alphaMode: 'opaque' });
        let frame = 0, tickMs = 0, red = 0;
        const started = performance.now();
        await new Promise((resolve, reject) => {
            const draw = () => {
                try {
                    const before = performance.now();
                    red = tick(frame);
                    tickMs += performance.now() - before;
                    const encoder = device.createCommandEncoder();
                    encoder.beginRenderPass({
                        colorAttachments: [{ view: context.getCurrentTexture().createView(), loadOp: 'clear', storeOp: 'store', clearValue: { r: red / 255, g: 0.5, b: 0.25, a: 1 } }]
                    }).end();
                    device.queue.submit([encoder.finish()]);
                    if (++frame < frames) self.requestAnimationFrame(draw); else resolve();
                } catch (error) { reject(error); }
            };
            self.requestAnimationFrame(draw);
        });
        await device.queue.onSubmittedWorkDone();
        const report = { frames: frame, lastRed: red, tickMsPerFrame: +(tickMs / frame).toFixed(2), seconds: +((performance.now() - started) / 1000).toFixed(2), inWorker: typeof document === 'undefined' };
        channel.postMessage({ type: 'report', report });
        return JSON.stringify(report);
    } catch (error) {
        channel.postMessage({ type: 'error', error: String(error?.message ?? error) });
        throw error;
    }
}
