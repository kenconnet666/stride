import { dotnet } from './_framework/dotnet.js';

const output = document.getElementById('result');
globalThis.threadsResult = { status: 'running', isolated: globalThis.crossOriginIsolated };
try {
    const stage = name => { globalThis.threadsResult.stage = name; };
    stage('create');
    // Each thread is a worker, and the runtime starts them from a pool it fills beforehand. A thread that finds the
    // pool empty never starts (the engine's pool alone starts one per processor, at once): the pool is sized for all
    // the threads the application starts close together.
    const runtime = await dotnet.withConfig({ pthreadPoolInitialSize: navigator.hardwareConcurrency + 16 }).create();
    let reported;
    runtime.setModuleImports('probe', { report: json => { reported = json; } });
    stage('main');
    await runtime.runMain();
    stage('exports');
    const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
    stage('run');
    // The managed code runs on workers: the page thread only waits, and gets the answer through its report function
    await exports.Program.Run();
    const report = JSON.parse(reported);
    globalThis.threadsResult = { status: 'passed', isolated: globalThis.crossOriginIsolated, ...report };
} catch (error) {
    globalThis.threadsResult = { status: 'failed', isolated: globalThis.crossOriginIsolated, error: String(error?.message ?? error) };
}
output.textContent = JSON.stringify(globalThis.threadsResult, null, 2);
