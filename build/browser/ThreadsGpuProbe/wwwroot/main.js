import { dotnet } from './_framework/dotnet.js';

// The page thread: it owns the DOM, gives its canvas to the worker of the engine, and waits for the report.
const output = document.getElementById('result');
globalThis.threadsGpuResult = { status: 'running', isolated: globalThis.crossOriginIsolated };
try {
    const runtime = await dotnet.withConfig({ pthreadPoolInitialSize: navigator.hardwareConcurrency + 16 }).create();
    await runtime.runMain();

    // The worker announces itself on a channel; a canvas can only be transferred with a message to the worker itself,
    // so the page asks each worker of the runtime which one it is.
    const channel = new BroadcastChannel('stride-engine-worker');
    let workers = [];
    const report = new Promise((resolve, reject) => {
        channel.onmessage = event => {
            const message = event.data;
            if (message.type === 'waiting') {
                workers = [...(runtime.Module.PThread.runningWorkers ?? Object.values(runtime.Module.PThread.pthreads))];
                workers.forEach((worker, index) => worker.postMessage({ strideEngineWorker: 'who', token: message.token, index }));
            } else if (message.type === 'here') {
                const worker = workers[message.index];
                const canvas = document.getElementById('game').transferControlToOffscreen();
                worker.postMessage({ strideEngineWorker: 'canvas', token: message.token, canvas }, [canvas]);
            } else if (message.type === 'report') resolve(message.report);
            else if (message.type === 'error') reject(new Error(message.error));
        };
    });
    const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
    await Promise.all([exports.Program.Run(), report]);
    globalThis.threadsGpuResult = { status: 'passed', isolated: globalThis.crossOriginIsolated, ...(await report) };
} catch (error) {
    globalThis.threadsGpuResult = { status: 'failed', isolated: globalThis.crossOriginIsolated, error: String(error?.message ?? error) };
}
output.textContent = JSON.stringify(globalThis.threadsGpuResult, null, 2);
