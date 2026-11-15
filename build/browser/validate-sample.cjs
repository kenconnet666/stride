// Usage: node validate-sample.cjs <SampleName> [--record-effects] [--url=<address>]
// Runs the published Sample.Browser of a sample in Chrome: it must start, keep drawing frames without an error, and
// draw something. Saves a screenshot next to the publish output. With --record-effects, also writes the effects the
// sample asked for to Sample.Browser/effect-requests/<SampleName>.json, for PrepareSampleContent.ps1 to compile them.
// With --url, checks a page that is served already (dotnet run, for a build that was not published).
const assert = require('node:assert/strict');
const fs = require('node:fs/promises');
const http = require('node:http');
const path = require('node:path');
const { chromium } = require('playwright');
const { PNG } = require('pngjs');

async function main() {
    const name = process.argv[2];
    assert.ok(name, 'Pass the name of the sample, for example SpriteFonts');
    const record = process.argv.includes('--record-effects');
    const project = path.join(__dirname, 'Sample.Browser');
    const root = path.join(project, 'bin', name, 'Release/net11.0-browser/publish/wwwroot');
    const served = process.argv.find(argument => argument.startsWith('--url='))?.slice('--url='.length);
    if (!served) await fs.access(path.join(root, 'index.html'));
    const mime = { '.js': 'text/javascript', '.json': 'application/json', '.wasm': 'application/wasm', '.html': 'text/html' };
    const server = http.createServer(async (request, response) => {
        try {
            const pathname = decodeURIComponent(new URL(request.url, 'http://localhost').pathname);
            if (pathname === '/favicon.ico') { response.writeHead(204).end(); return; }
            const file = path.resolve(root, '.' + (pathname === '/' ? '/index.html' : pathname));
            const relative = path.relative(root, file);
            if (relative.startsWith('..') || path.isAbsolute(relative)) { response.writeHead(403).end(); return; }
            response.writeHead(200, { 'Content-Type': mime[path.extname(file)] || 'application/octet-stream', 'Cache-Control': 'no-store' });
            response.end(await fs.readFile(file));
        } catch { response.writeHead(404).end(); }
    });
    let browser, page;
    const errors = [];
    try {
        if (!served) await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
        browser = await chromium.launch({ headless: true, executablePath: process.env.BROWSER_EXECUTABLE });
        page = await browser.newPage({ viewport: { width: 1400, height: 1400 } });
        // A page served by dotnet run has no icon: that is not an error of the game
        await page.route('**/favicon.ico', route => route.fulfill({ status: 204 }));
        page.on('pageerror', error => errors.push(String(error)));
        const messages = [];
        page.on('console', message => { messages.push(message.text()); if (message.type() === 'error') errors.push(message.text()); });
        await page.goto(served ?? `http://127.0.0.1:${server.address().port}/`);
        await page.waitForFunction(() => globalThis.sampleResult && globalThis.sampleResult.status !== 'loading', null, { timeout: 180000 });
        let result = await page.evaluate(() => globalThis.sampleResult);
        assert.equal(result.status, 'running', `The sample did not start: ${result.managedError ?? result.error}\n${errors.join('\n')}`);
        assert.equal(result.sample, name);
        await page.locator('#game').click({ position: { x: 20, y: 20 } });
        // The splash screen shows while the scene loads: a sample with a scene is checked once its entities are there
        const hasScene = !(await page.evaluate(() => globalThis.sampleManaged.Diagnostics())).includes('initialScene=none');
        await page.waitForFunction(needsEntities => globalThis.sampleResult.status === 'failed' || (!needsEntities || globalThis.sampleResult.entities > 0), hasScene, { timeout: 120000 });
        const settled = (await page.evaluate(() => globalThis.sampleResult.frames)) + 180;
        await page.waitForFunction(target => globalThis.sampleResult.status === 'failed' || globalThis.sampleResult.frames >= target, settled, { timeout: 60000 });
        result = await page.evaluate(() => globalThis.sampleResult);
        assert.equal(result.status, 'running', `The frame loop failed: ${result.managedError ?? result.error}\n${errors.join('\n')}`);

        const screenshot = path.join(project, 'bin', name, 'screenshot.png');
        const png = PNG.sync.read(await page.locator('#game').screenshot({ path: screenshot }));
        const colors = new Set();
        for (let i = 0; i < png.data.length; i += 4) colors.add((png.data[i] << 16) | (png.data[i + 1] << 8) | png.data[i + 2]);
        if (process.argv.includes('--verbose')) console.log(messages.slice(0, 80).join(String.fromCharCode(10)).slice(0, 8000));
        assert.ok(colors.size > 8, `The canvas shows ${colors.size} color(s): nothing was drawn (${await page.evaluate(() => globalThis.sampleManaged.Diagnostics())})`);
        assert.deepEqual(errors, []);
        console.log(JSON.stringify({ sample: name, frames: result.frames, frameMs: +result.frameMs.toFixed(2), entities: result.entities, ui: result.ui.slice(0, 200),
            canvas: `${png.width}x${png.height}`, colors: colors.size, screenshot }));
    } finally {
        if (page && record) {
            const file = path.join(project, 'effect-requests', `${name}.json`);
            const captured = new Map();
            try { for (const request of JSON.parse(await fs.readFile(file, 'utf8'))) captured.set(request.hash, request); } catch { }
            for (const request of await page.evaluate(() => globalThis.sampleEffectRequests || [])) captured.set(request.hash, request);
            await fs.mkdir(path.dirname(file), { recursive: true });
            await fs.writeFile(file, JSON.stringify([...captured.values()].sort((a, b) => a.hash.localeCompare(b.hash)), null, 2));
            console.log(`Recorded ${captured.size} effect request(s) in ${file}`);
        }
        if (errors.length > 0) console.log(`errors:\n${[...new Set(errors)].slice(0, 12).join('\n').slice(0, 6000)}`);
        await browser?.close();
        if (!served) await new Promise(resolve => server.close(resolve));
    }
}
main().catch(error => { console.error(error); process.exitCode = 1; });
