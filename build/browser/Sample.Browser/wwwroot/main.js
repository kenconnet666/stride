import { dotnet } from './_framework/dotnet.js';

// The game runs through Stride.Engine.BrowserGame, which publishes its state as globalThis.strideGame. This page adds
// what a validation reads (globalThis.sampleResult, globalThis.sampleEffectRequests) and shows the status.
const canvas = document.getElementById('game');
const status = document.getElementById('status');
const result = globalThis.sampleResult = { status: 'loading', frames: 0 };
globalThis.sampleEffectRequests = [];
let managed;
const fail = error => {
    result.status = 'failed';
    result.error = String(error?.stack || error);
    status.textContent = result.error;
    console.error(error);
};
try {
    // The size until the game settings of the sample give theirs
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
    await managed.Start();
    status.textContent = `${result.sample} is running. Click the canvas to give it the keyboard.`;
    const watch = () => {
        const game = globalThis.strideGame;
        if (game.status === 'failed') {
            if (game.managedError) result.managedError = game.managedError;
            fail(game.error);
            return;
        }
        result.status = game.status;
        if (game.status !== 'running') return;
        result.frameMs = game.frameMs;
        result.frames = managed.Frames();
        result.entities = managed.Entities();
        result.ui = managed.UiText();
        result.gamepads = managed.GamePads();
        requestAnimationFrame(watch);
    };
    requestAnimationFrame(watch);
    globalThis.stopSample = async () => { await managed.Stop(); result.status = 'stopped'; };
} catch (error) { fail(error); }
