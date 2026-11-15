import { dotnet } from './_framework/dotnet.js';
import { attachBrowserInput } from './browser-input.js';

const canvas = document.getElementById('game');
const status = document.getElementById('status');
const result = globalThis.textInputResult = { status: 'loading' };
let detach, stopped = false;
const fail = error => {
    stopped = true;
    result.status = 'failed';
    result.error = String(error?.stack ?? error);
    status.textContent = result.error;
    console.error(error);
};
try {
    const runtime = await dotnet.create();
    runtime.setModuleImports('text-input', {
        managedError: error => { result.managedError = error; },
        log: (message, level) => { if (level >= 4) console.error(message); else console.log(message); },
        setTextInput: active => { result.textInput = active; detach?.setTextInput(active); }
    });
    const managed = (await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName)).Program;
    await runtime.runMain();
    await managed.Start(new URL('content/manifest.json', document.baseURI).href, new URL('fonts/text-input-font', document.baseURI).href, canvas.width, canvas.height);
    detach = attachBrowserInput(canvas, {
        key: managed.Key, move: managed.Move, button: managed.Button, wheel: managed.Wheel,
        pointer: managed.Pointer, surface: managed.Surface, pause: managed.Pause, cancelMouse: managed.CancelMouse,
        text: managed.Text, composition: managed.Composition
    });
    result.status = 'running';
    status.textContent = 'Running.';
    const frame = () => {
        if (stopped) return;
        try {
            managed.Frame(canvas.width, canvas.height, document.hasFocus(), !document.hidden);
            const [text, composition, active, frames] = managed.State().split('\u001f');
            Object.assign(result, { text, composition, active: active === 'True', frames: Number(frames) });
            requestAnimationFrame(frame);
        } catch (error) { fail(error); }
    };
    requestAnimationFrame(frame);
} catch (error) { fail(error); }
