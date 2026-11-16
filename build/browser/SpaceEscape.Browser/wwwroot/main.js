import { dotnet } from './_framework/dotnet.js';

// The game runs through Stride.Engine.BrowserGame (globalThis.strideGame). This page adds the state of the game that
// validate-space-escape.cjs reads (globalThis.spaceEscapeResult) and the effects it asked for.
const status = document.getElementById('status');
const result = globalThis.spaceEscapeResult = { status: 'loading', frames: 0 };
globalThis.spaceEscapeRequests = [];
let managed;
const fail = error => {
    result.status = 'failed';
    result.error = String(error?.stack || error);
    status.textContent = result.error;
    console.error(error);
};
try {
    const runtime = await dotnet.create();
    runtime.setModuleImports('space-escape', {
        log: (message, level) => { if (level >= 4) console.error(message); else console.log(message); },
        effectRequest: (hash, payload) => { globalThis.spaceEscapeRequests.push({ hash, payload }); }
    });
    const config = runtime.getConfig();
    managed = globalThis.spaceEscapeManaged = (await runtime.getAssemblyExports(config.mainAssemblyName)).Program;
    await runtime.runMain();
    await managed.Start();
    status.textContent = 'Space Escape is running. Click the canvas to play.';
    const watch = () => {
        const game = globalThis.strideGame;
        if (game.status === 'failed') {
            if (game.managedError) result.managedError = game.managedError;
            fail(game.error);
            return;
        }
        result.status = game.status;
        if (game.status !== 'running') return;
        // CPU time of one engine frame, smoothed: what an interpreter or AOT build changes
        result.frameMs = game.frameMs;
        result.frames = managed.Frames();
        result.entities = managed.Entities();
        result.lane = managed.Lane();
        result.dead = managed.Dead();
        result.characterX = managed.CharacterX();
        result.hitboxHeight = managed.HitboxHeight();
        result.ui = managed.UiText();
        requestAnimationFrame(watch);
    };
    requestAnimationFrame(watch);
    globalThis.stopSpaceEscape = async () => { await managed.Stop(); result.status = 'stopped'; };
} catch (error) { fail(error); }
