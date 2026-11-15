import { dotnet } from './_framework/dotnet.js';

const status = document.getElementById('status');
const result = globalThis.gameStudioResult = { status: 'loading' };
try {
    const runtime = await dotnet.create();
    result.status = 'running';
    status?.remove();
    // ?project=<address of a project archive>: the project the editor opens
    const project = new URLSearchParams(location.search).get('project');
    await runtime.runMain(runtime.getConfig().mainAssemblyName, project ? [new URL(project, location.href).href] : []);
} catch (error) {
    result.status = 'failed';
    result.error = String(error?.stack ?? error);
    if (status) status.textContent = result.error;
    console.error(error);
}
