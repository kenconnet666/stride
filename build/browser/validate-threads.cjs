// Usage: node validate-threads.cjs [ThreadsProbe|ThreadsGpuProbe]
// Serves the published probe with the cross-origin isolation headers that shared memory needs, opens it in Chrome and
// prints its report. ThreadsProbe: what the multithreaded runtime gives Stride; fails without real threads.
// ThreadsGpuProbe: the engine on a worker; fails unless the worker drew its frames on the canvas of the page.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const http = require('node:http');
const path = require('node:path');
const { chromium } = require('playwright');

async function main() {
    const probe = process.argv[2] ?? 'ThreadsProbe';
    const root = path.join(__dirname, probe, 'bin/Release/net11.0-browser/publish/wwwroot');
    await fs.access(path.join(root, 'index.html'));
    const mime = { '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json', '.wasm': 'application/wasm', '.html': 'text/html' };
    const server = http.createServer(async (request, response) => {
        try {
            const pathname = decodeURIComponent(new URL(request.url, 'http://localhost').pathname);
            if (pathname === '/favicon.ico') { response.writeHead(204).end(); return; }
            const file = path.resolve(root, '.' + (pathname === '/' ? '/index.html' : pathname));
            const relative = path.relative(root, file);
            if (relative.startsWith('..') || path.isAbsolute(relative)) { response.writeHead(403).end(); return; }
            response.writeHead(200, {
                'Content-Type': mime[path.extname(file)] || 'application/octet-stream', 'Cache-Control': 'no-store',
                // SharedArrayBuffer is given only to a cross-origin isolated page
                'Cross-Origin-Opener-Policy': 'same-origin', 'Cross-Origin-Embedder-Policy': 'require-corp',
            });
            response.end(await fs.readFile(file));
        } catch { response.writeHead(404).end(); }
    });
    let browser;
    const messages = [];
    try {
        await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
        browser = await chromium.launch({ headless: true, executablePath: process.env.BROWSER_EXECUTABLE });
        const page = await browser.newPage();
        page.on('pageerror', error => messages.push(`[pageerror] ${error}`));
        page.on('console', message => messages.push(`[${message.type()}] ${message.text()}`));
        await page.goto(`http://127.0.0.1:${server.address().port}/`);
        await page.waitForFunction(() => (globalThis.threadsResult ?? globalThis.threadsGpuResult)?.status !== 'running', null, { timeout: 180000 });
        const result = await page.evaluate(() => globalThis.threadsResult ?? globalThis.threadsGpuResult);
        console.log(JSON.stringify(result, null, 1));
        assert.equal(result.status, 'passed', result.error);
        assert.equal(result.isolated, true);
        if (probe === 'ThreadsGpuProbe') {
            assert.equal(result.inWorker, true);
            assert.equal(result.frames, 120);
            // The last frame cleared the canvas to (lastRed, 128, 64): the page shows what the worker drew
            const { PNG } = require('pngjs');
            const png = PNG.sync.read(await page.locator('#game').screenshot());
            const center = (png.width * Math.floor(png.height / 2) + Math.floor(png.width / 2)) * 4;
            const pixel = [...png.data.subarray(center, center + 3)];
            assert.ok(Math.abs(pixel[0] - result.lastRed) <= 2 && Math.abs(pixel[1] - 128) <= 2 && Math.abs(pixel[2] - 64) <= 2, `The canvas shows ${pixel}`);
            console.log(`canvas pixel ${pixel}`);
        } else {
            assert.equal(result.multithreadingSupported, true);
            assert.ok(result.strideWorkerThreads > 0 && result.dispatcherThreads > 1, 'The parallel loop ran on one thread');
            assert.ok(result.threadRanElsewhere && result.dispatcherResultMatches);
        }
    } finally {
        if (messages.length > 0) console.log([...new Set(messages)].slice(0, 30).join('\n').slice(0, 6000));
        await browser?.close();
        await new Promise(resolve => server.close(resolve));
    }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
