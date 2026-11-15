import { dotnet } from './_framework/dotnet.js';
import { attachBrowserInput } from './browser-input.js';

const canvas = document.getElementById('game');
const status = document.getElementById('status');
const result = globalThis.sampleResult = { status: 'loading', frames: 0 };
globalThis.sampleEffectRequests = [];
let managed, detach, stopped = false;
const fail = error => {
    stopped = true;
    detach?.();
    result.status = 'failed';
    result.error = String(error?.stack || error);
    status.textContent = result.error;
    console.error(error);
};
try {
    canvas.width = 1280;
    canvas.height = 720;
    const runtime = await dotnet.create();
    runtime.setModuleImports('sample', {
        managedError: error => { result.managedError = error; },
        log: (message, level) => { if (level >= 4) console.error(message); else console.log(message); },
        effectRequest: (hash, payload) => { globalThis.sampleEffectRequests.push({ hash, payload }); }
    });
    const config = runtime.getConfig();
    managed = globalThis.sampleManaged = (await runtime.getAssemblyExports(config.mainAssemblyName)).Program;
    await runtime.runMain();
    result.sample = managed.SampleName();
    document.title = `${result.sample} — Stride WebGPU`;
    document.getElementById('title').textContent = result.sample;
    await managed.Start(new URL('content/manifest.json', document.baseURI).href, canvas.width, canvas.height);
    // The game settings of the sample say how large its back buffer is: the frame loop resizes the game to the canvas
    canvas.width = managed.PreferredWidth();
    canvas.height = managed.PreferredHeight();
    detach = attachBrowserInput(canvas, {
        key: managed.Key, move: managed.Move, button: managed.Button, wheel: managed.Wheel,
        pointer: managed.Pointer, surface: managed.Surface, pause: managed.Pause, cancelMouse: managed.CancelMouse
    });
    result.status = 'running';
    status.textContent = `${result.sample} is running. Click the canvas to give it the keyboard.`;
    const frame = () => {
        if (stopped) return;
        try {
            const started = performance.now();
            managed.Frame(canvas.width, canvas.height, document.hasFocus(), !document.hidden);
            const elapsed = performance.now() - started;
            result.frameMs = result.frameMs === undefined ? elapsed : result.frameMs * 0.95 + elapsed * 0.05;
            result.frames = managed.Frames();
            result.entities = managed.Entities();
            result.ui = managed.UiText();
            requestAnimationFrame(frame);
        } catch (error) { fail(error); }
    };
    requestAnimationFrame(frame);
    globalThis.stopSample = async () => { stopped = true; detach?.(); await managed.Stop(); result.status = 'stopped'; };
} catch (error) { fail(error); }
