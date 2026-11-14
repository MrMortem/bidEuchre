const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const { spawn } = require('node:child_process');
const path = require('node:path');
let chromium;
try { ({ chromium } = require('../browser/node_modules/playwright')); } catch {}
const root = path.resolve(__dirname, '../..');
let browser, server, base, logs = '';
before(async () => {
  if (!chromium) return;
  browser = await chromium.launch({ headless: true });
  server = spawn(process.env.BIDEUCHRE_DOTNET || 'dotnet', [
    path.join(root, 'src/BidEuchre.Server/bin/Debug/net10.0/BidEuchre.Server.dll'), '--urls=http://127.0.0.1:0'
  ], { cwd: path.join(root, 'src/BidEuchre.Server'), env: {
    ...process.env, DOTNET_CLI_HOME: path.join(root, '.dotnet-home')
  } });
  server.stdout.on('data', data => { logs += data; });
  server.stderr.on('data', data => { logs += data; });
  const deadline = Date.now() + 20000;
  while (Date.now() < deadline) {
    const address = logs.match(/Now listening on: (http:\/\/127\.0\.0\.1:\d+)/);
    if (address) { base = address[1]; return; }
    if (server.exitCode !== null) break;
    await new Promise(resolve => setTimeout(resolve, 30));
  }
  throw new Error(`Browser test server did not start: ${logs}`);
});
after(async () => {
  if (browser) await browser.close();
  if (server && server.exitCode === null) {
    server.kill('SIGTERM'); await new Promise(resolve => server.once('exit', resolve));
  }
});
const skip = chromium ? false : 'Run npm ci --prefix tests/browser and install Chromium to enable real browser regressions';
test('Tab, Enter and Space reopen lobby tables and move focus into the table', { skip }, async () => {
  const page = await browser.newPage(); page.setDefaultTimeout(5000);
  try {
    const response = await page.request.post(`${base}/api/sessions`, { data: {
      name: 'Keyboard table', seats: Array.from({ length: 4 }, (_, seat) => ({ name: `Human ${seat}`, kind: 'Human' }))
    } });
    assert.equal(response.status(), 201);
    await page.goto(base);
    const open = page.getByRole('button', { name: 'Open table: Keyboard table', exact: true });
    await open.waitFor();
    await page.locator('#new-table-button').focus();
    await page.keyboard.press('Tab');
    assert.equal(await open.evaluate(element => element === document.activeElement), true);
    const focus = await open.evaluate(element => ({ style: getComputedStyle(element).outlineStyle, width: getComputedStyle(element).outlineWidth }));
    assert.notEqual(focus.style, 'none'); assert.notEqual(focus.width, '0px');
    await page.evaluate(() => loadSessions());
    assert.equal(await open.evaluate(element => element === document.activeElement), true, 'Refreshing lobby tables must preserve the focused Open action');
    await page.keyboard.press('Enter');
    await page.waitForFunction(() => document.querySelector('#game-name').textContent === 'Keyboard table');
    await page.waitForFunction(() => document.activeElement.id === 'back-to-tables');
    await page.keyboard.press('Enter');
    await page.waitForSelector('#lobby-view.active');
    await page.locator('#new-table-button').focus(); await page.keyboard.press('Tab');
    assert.equal(await open.evaluate(element => element === document.activeElement), true);
    await page.keyboard.press('Space');
    await page.waitForSelector('#game-view.active');
    await page.waitForFunction(() => document.activeElement.id === 'back-to-tables');
  } finally { await page.close(); }
});

test('Rules failure stays retryable and Retry loads the real Markdown', { skip }, async () => {
  const page = await browser.newPage(); page.setDefaultTimeout(5000); let calls = 0;
  try {
    await page.route('**/api/rules', async route => {
      calls++;
      if (calls === 1) await route.fulfill({ status: 503, contentType: 'application/json', body: '{"error":"temporarily unavailable"}' });
      else await route.continue();
    });
    await page.goto(base);
    await page.getByRole('button', { name: 'Rules', exact: false }).click();
    await page.getByRole('button', { name: 'Retry rules', exact: true }).waitFor();
    assert.match(await page.locator('#rules-content').innerText(), /could not load|unable to load/i);
    assert.doesNotMatch(await page.locator('#rules-content').innerText(), /temporarily unavailable/);
    await page.getByRole('button', { name: 'Retry rules', exact: true }).click();
    await page.waitForFunction(() => document.querySelectorAll('#rules-content h1, #rules-content h2').length > 0);
    assert.equal(calls, 2);
    assert.equal(await page.getByRole('button', { name: 'Retry rules', exact: true }).isVisible(), false);
  } finally { await page.close(); }
});
