// Usage: node validate-threads.cjs [folder of the built or published page]
// Serves ThreadsProbe with the cross-origin isolation headers that shared memory needs, opens it in Chrome and prints
// what the multithreaded runtime gave Stride. Fails when the page has no real threads.
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const http = require('node:http');
const path = require('node:path');
const { chromium } = require('playwright');

async function main() {
    const root = path.resolve(process.argv[2] ?? path.join(__dirname, 'ThreadsProbe/bin/Release/net11.0-browser/publish/wwwroot'));
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
        await page.waitForFunction(() => globalThis.threadsResult?.status !== 'running', null, { timeout: 180000 });
        const result = await page.evaluate(() => globalThis.threadsResult);
        console.log(JSON.stringify(result, null, 1));
        assert.equal(result.status, 'passed', result.error);
        assert.equal(result.isolated, true);
        assert.equal(result.multithreadingSupported, true);
        assert.ok(result.strideWorkerThreads > 0 && result.dispatcherThreads > 1, 'The parallel loop ran on one thread');
        assert.ok(result.threadRanElsewhere && result.dispatcherResultMatches);
    } finally {
        if (messages.length > 0) console.log([...new Set(messages)].slice(0, 30).join('\n').slice(0, 6000));
        await browser?.close();
        await new Promise(resolve => server.close(resolve));
    }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
