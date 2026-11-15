import { dotnet } from './_framework/dotnet.js';

const status = document.getElementById('status');
const result = globalThis.gameStudioResult = { status: 'loading' };
try {
    const runtime = await dotnet.withApplicationArgumentsFromQuery().create();
    result.status = 'running';
    status?.remove();
    await runtime.runMain(runtime.getConfig().mainAssemblyName, []);
} catch (error) {
    result.status = 'failed';
    result.error = String(error?.stack ?? error);
    if (status) status.textContent = result.error;
    console.error(error);
}
