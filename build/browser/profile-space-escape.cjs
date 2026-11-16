// Usage: node profile-space-escape.cjs <playwright module dir> <url>
// Prints the engine profiler's time per frame of each profiling key while Space Escape is in play.
const [, , modules, url] = process.argv;
const { chromium } = require(require('path').join(modules, 'playwright'));
(async () => {
    const browser = await chromium.launch({ headless: true, executablePath: process.env.BROWSER_EXECUTABLE });
    try {
        const page = await browser.newPage({ viewport: { width: 900, height: 1300 } });
        await page.goto(url);
        await page.waitForFunction(() => globalThis.spaceEscapeResult?.status !== 'loading', null, { timeout: 120000 });
        const canvas = page.locator('#game');
        await canvas.click({ position: { x: 20, y: 20 } });
        await page.waitForFunction(() => globalThis.spaceEscapeResult.ui.includes('Touch to Start'), null, { timeout: 60000 });
        const box = await canvas.boundingBox();
        await page.mouse.click(box.x + box.width * 0.5, box.y + box.height * 0.8);
        await page.waitForFunction(() => globalThis.spaceEscapeResult.ui.includes('Distance'), null, { timeout: 20000 });
        await page.waitForTimeout(1000);
        const before = await page.evaluate(() => globalThis.spaceEscapeResult.frameMs);
        await page.evaluate(() => globalThis.strideGame.startProfile());
        await page.waitForTimeout(4000);
        const report = await page.evaluate(() => ({ text: globalThis.strideGame.profileReport(), frameMs: globalThis.spaceEscapeResult.frameMs }));
        console.log(`frame ms without profiler ${before.toFixed(2)}, with ${report.frameMs.toFixed(2)}`);
        console.log(report.text.split('\n').slice(0, 45).join('\n'));
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exit(1); });
