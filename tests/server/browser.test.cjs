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
    path.join(root, 'src/BidEuchre.Server/bin/Debug/net10.0/BidEuchre.Server.dll'), '--urls=http://127.0.0.1:0', '--WebSessions:MaxSessions=4'
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


async function createTable(page, name) {
  const response = await page.request.post(`${base}/api/sessions`, { data: {
    name, seats: Array.from({ length: 4 }, (_, seat) => ({ name: `Human ${seat}`, kind: 'Human' }))
  } });
  assert.equal(response.status(), 201, await response.text());
  return response.json();
}
async function removeTable(page, id) {
  await page.request.delete(`${base}/api/sessions/${id}`, { headers: { 'Content-Type': 'application/json' } });
}

test('keyboard Close removes only its table, announces success and immediately frees capacity', { skip }, async () => {
  const page = await browser.newPage(); page.setDefaultTimeout(5000);
  const created = [];
  try {
    const target = await createTable(page, 'Abandoned table'); created.push(target.id);
    const listed = await (await page.request.get(`${base}/api/sessions`)).json();
    for (let i = listed.length; i < 4; i++) created.push((await createTable(page, `Capacity ${i}`)).id);
    assert.equal((await page.request.post(`${base}/api/sessions`, { data: { name: 'Full' } })).status(), 429);
    await page.goto(base);
    const open = page.getByRole('button', { name: 'Open table: Abandoned table', exact: true });
    const close = page.getByRole('button', { name: 'Close table: Abandoned table', exact: true });
    await open.focus(); await page.keyboard.press('Tab');
    assert.equal(await close.evaluate(element => element === document.activeElement), true);
    const focus = await close.evaluate(element => ({ style: getComputedStyle(element).outlineStyle, width: getComputedStyle(element).outlineWidth }));
    assert.notEqual(focus.style, 'none'); assert.notEqual(focus.width, '0px');
    await page.evaluate(() => loadSessions());
    assert.equal(await close.evaluate(element => element === document.activeElement), true, 'Refresh preserves the focused Close action');
    await page.keyboard.press('Space');
    await close.waitFor({ state: 'detached' });
    assert.equal(await open.count(), 0);
    assert.match(await page.getByRole('status').filter({ hasText: /closed/i }).innerText(), /Abandoned table/);
    assert.equal(await page.locator('#lobby-view').evaluate(element => element.classList.contains('active')), true);
    assert.equal((await page.request.get(`${base}/api/sessions/${target.id}`)).status(), 404);
    created.push((await createTable(page, 'Capacity freed')).id);
    assert.equal((await (await page.request.get(`${base}/api/sessions`)).json()).length, 4);
    assert.equal(await page.locator('#new-table-button').evaluate(element => element === document.activeElement), true);
  } finally { for (const id of created) await removeTable(page, id); await page.close(); }
});

test('failed Close leaves Open and Close available and announces an actionable error', { skip }, async () => {
  const page = await browser.newPage(); page.setDefaultTimeout(5000);
  const table = await createTable(page, 'Keep this table');
  try {
    await page.route(`**/api/sessions/${table.id}`, route => route.request().method() === 'DELETE'
      ? route.fulfill({ status: 503, contentType: 'application/json', body: '{"error":"Server busy; try again."}' })
      : route.continue());
    await page.goto(base);
    const close = page.getByRole('button', { name: 'Close table: Keep this table', exact: true });
    await close.click();
    await page.waitForFunction(() => document.querySelector('#toast').textContent.includes('Server busy'));
    assert.match(await page.getByRole('status').filter({ hasText: /Server busy/ }).innerText(), /could not close.*Keep this table/i);
    assert.equal(await close.isEnabled(), true);
    assert.equal(await page.getByRole('button', { name: 'Open table: Keep this table', exact: true }).isEnabled(), true);
    assert.equal((await page.request.get(`${base}/api/sessions/${table.id}`)).status(), 200);
    await page.getByRole('button', { name: 'Open table: Keep this table', exact: true }).click();
    await page.waitForSelector('#game-view.active');
  } finally { await removeTable(page, table.id); await page.close(); }
});

test('active table confirmation names the table and phase and cancellation preserves play', { skip }, async () => {
  const page = await browser.newPage(); page.setDefaultTimeout(5000);
  const table = await createTable(page, 'Friday active table');
  try {
    assert.equal((await page.request.post(`${base}/api/sessions/${table.id}/start`, { data: {} })).status(), 200);
    await page.goto(base);
    const close = page.getByRole('button', { name: 'Close table: Friday active table', exact: true });
    const dialogSeen = page.waitForEvent('dialog');
    const click = close.click();
    const dialog = await dialogSeen;
    assert.equal(dialog.type(), 'confirm');
    assert.match(dialog.message(), /Friday active table/); assert.match(dialog.message(), /Bidding/i);
    await dialog.dismiss(); await click;
    assert.equal(await close.isEnabled(), true);
    const state = await page.request.get(`${base}/api/sessions/${table.id}?seat=0`);
    assert.equal(state.status(), 200); assert.equal((await state.json()).game.phase, 'Bidding');
    page.once('dialog', dialog => dialog.accept());
    await close.click(); await close.waitFor({ state: 'detached' });
    assert.equal((await page.request.get(`${base}/api/sessions/${table.id}`)).status(), 404);
  } finally { await removeTable(page, table.id); await page.close(); }
});

test('closing the viewed table returns to the lobby, clears the session and stops polling', { skip }, async () => {
  const page = await browser.newPage(); page.setDefaultTimeout(5000);
  const table = await createTable(page, 'Viewed table');
  try {
    await page.goto(base);
    await page.getByRole('button', { name: 'Open table: Viewed table', exact: true }).click();
    await page.waitForFunction(() => document.querySelector('#game-name').textContent === 'Viewed table');
    const close = page.getByRole('button', { name: 'Close table: Viewed table', exact: true });
    await close.focus(); await page.keyboard.press('Enter');
    await page.waitForSelector('#lobby-view.active');
    assert.equal(await page.locator('#game-view').evaluate(element => element.classList.contains('active')), false);
    assert.deepEqual(await page.evaluate(() => ({ sessionId: model.sessionId, session: model.session, polling: model.pollTimer })),
      { sessionId: null, session: null, polling: null });
    assert.equal(await page.locator('#new-table-button').evaluate(element => element === document.activeElement), true);
    assert.equal((await page.request.get(`${base}/api/sessions/${table.id}`)).status(), 404);
  } finally { await removeTable(page, table.id); await page.close(); }
});
