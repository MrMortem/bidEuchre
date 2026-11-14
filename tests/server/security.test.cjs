const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const { spawn } = require('node:child_process');
const path = require('node:path');
const os = require('node:os');
const http = require('node:http');
const fs = require('node:fs');

const root = path.resolve(__dirname, '../..');
let server, base, logs = '';
before(async () => {
  server = spawn(process.env.BIDEUCHRE_DOTNET || 'dotnet', [
    path.join(root, 'src/BidEuchre.Server/bin/Debug/net10.0/BidEuchre.Server.dll'),
    '--urls=http://0.0.0.0:0', '--WebSessions:MaxSessions=4',
    '--WebSessions:IdleTimeoutSeconds=2'
  ], { cwd: path.join(root, 'src/BidEuchre.Server'), env: {
    ...process.env, AllowedHosts: '*', DOTNET_CLI_HOME: path.join(root, '.dotnet-home')
  } });
  server.stdout.on('data', data => { logs += data; });
  server.stderr.on('data', data => { logs += data; });
  const deadline = Date.now() + 20000;
  while (Date.now() < deadline) {
    const address = logs.match(/Now listening on: http:\/\/0\.0\.0\.0:(\d+)/);
    if (address) { base = `http://127.0.0.1:${address[1]}`; return; }
    if (server.exitCode !== null) break;
    await new Promise(resolve => setTimeout(resolve, 50));
  }
  throw new Error(`Server did not start: ${logs}`);
});
after(async () => {
  if (server && server.exitCode === null) {
    server.kill('SIGTERM');
    await new Promise(resolve => server.once('exit', resolve));
  }
});

const table = { seats: Array.from({ length: 4 }, (_, i) => ({ name: `Human ${i}`, kind: 'Human' })) };
const create = (headers = {}) => fetch(`${base}/api/sessions`, {
  method: 'POST', headers: { 'Content-Type': 'application/json', ...headers }, body: JSON.stringify(table)
});
const statusWithHeaders = (url, headers) => new Promise((resolve, reject) => {
  const request = http.get(url, { headers }, response => {
    response.resume(); resolve(response.statusCode);
  });
  request.on('error', reject);
});

test('HTTP never launches a client-supplied executable', async () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'issue21-'));
  const marker = path.join(directory, 'executed');
  try {
    const response = await fetch(`${base}/api/engines/load`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ executable: process.execPath,
        arguments: `-e "require('fs').writeFileSync('${marker.replaceAll('\\', '\\\\')}', 'ran')"` })
    });
    assert.equal(fs.existsSync(marker), false, 'The supplied executable must never run');
    assert.equal(response.status, 403);
    assert.match((await response.json()).error, /desktop/i);
    assert.equal((await (await fetch(`${base}/api/engines`)).json()).length, 1);
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
});

test('foreign and malformed origins cannot mutate or read tables', async () => {
  for (const origin of ['https://attacker.example', 'null', `${base}1`, `${base}/path`]) {
    assert.equal((await create({ Origin: origin })).status, 403, origin);
    assert.equal((await fetch(`${base}/api/sessions`, { headers: { Origin: origin } })).status, 403, origin);
  }
  assert.equal((await fetch(`${base}/api/sessions`, { headers: { 'Sec-Fetch-Site': 'cross-site' } })).status, 403);
  assert.equal((await fetch(`${base}/api/sessions`, { headers: { 'Sec-Fetch-Site': 'same-site' } })).status, 403);
});

test('loopback hosts are enforced even when AllowedHosts is overridden', async () => {
  assert.equal(await statusWithHeaders(`${base}/api/health`, { Host: 'attacker.example' }), 403);
  assert.equal(await statusWithHeaders(`${base}/api/health`, { Host: 'localhost.evil.example' }), 403);
  assert.equal(await statusWithHeaders(`${base}/api/health`, { Host: 'localhost' }), 200);
});

test('LAN clients remain forbidden with a loopback Host and forged forwarding headers', async t => {
  const address = Object.values(os.networkInterfaces()).flat()
    .find(address => address.family === 'IPv4' && !address.internal)?.address;
  if (!address) return t.skip('No LAN interface');
  const url = new URL(base); url.hostname = address;
  assert.equal(await statusWithHeaders(`${url.origin}/api/health`, {
    Host: new URL(base).host, 'X-Forwarded-For': '127.0.0.1', 'X-Forwarded-Host': 'localhost'
  }), 403);
});

test('browser form submissions cannot create tables', async () => {
  assert.equal((await fetch(`${base}/api/sessions`, { method: 'POST', body: JSON.stringify(table) })).status, 415);
  const { id } = await (await create()).json();
  assert.equal((await fetch(`${base}/api/sessions/${id}`, { method: 'DELETE' })).status, 415);
  assert.equal((await fetch(`${base}/api/sessions/${id}`)).status, 200);
  assert.equal((await fetch(`${base}/api/sessions/${id}`, {
    method: 'DELETE', headers: { 'Content-Type': 'application/json' }
  })).status, 204);
});

test('same-origin local hot-seat play still works', async () => {
  const response = await create({ Origin: base, 'Sec-Fetch-Site': 'same-origin' });
  assert.equal(response.status, 201);
  const { id } = await response.json();
  assert.equal((await fetch(`${base}/api/sessions/${id}/start`, {
    method: 'POST', headers: { 'Content-Type': 'application/json', Origin: base }
  })).status, 200);
  const view = await (await fetch(`${base}/api/sessions/${id}?seat=0`)).json();
  assert.ok(view.game);
  assert.equal((await fetch(`${base}/api/sessions/${id}`, {
    method: 'DELETE', headers: { 'Content-Type': 'application/json' }
  })).status, 204);
});

test('parallel table creation cannot exceed the configured cap', async () => {
  const responses = await Promise.all(Array.from({ length: 10 }, () => create()));
  assert.equal(responses.filter(response => response.status === 201).length, 4);
  assert.equal(responses.filter(response => response.status === 429).length, 6);
  const sessions = await (await fetch(`${base}/api/sessions`)).json();
  assert.equal(sessions.length, 4);
  for (const { id } of sessions) await fetch(`${base}/api/sessions/${id}`, {
    method: 'DELETE', headers: { 'Content-Type': 'application/json' }
  });
});

test('idle tables expire while requests to a table keep it alive', async () => {
  const idle = await (await create()).json();
  const active = await (await create()).json();
  for (let i = 0; i < 6; i++) {
    await new Promise(resolve => setTimeout(resolve, 500));
    assert.equal((await fetch(`${base}/api/sessions/${active.id}`)).status, 200);
    await fetch(`${base}/api/sessions`); // Lobby polling must not keep idle tables alive.
  }
  assert.equal((await fetch(`${base}/api/sessions/${idle.id}`)).status, 404);
  assert.deepEqual((await (await fetch(`${base}/api/sessions`)).json()).map(s => s.id), [active.id]);
});

test('undefined contracts return 400 and leave the auction state unchanged', async () => {
  const session = await (await create()).json();
  const request = async (endpoint, body) => fetch(`${base}/api/sessions/${session.id}${endpoint}`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body)
  });
  const state = async () => (await (await fetch(`${base}/api/sessions/${session.id}?seat=0`)).json()).game;
  try {
    assert.equal((await request('/start', {})).status, 200);
    let view = await state(); const bidder = view.currentSeat;
    assert.equal((await request('/actions', { seat: bidder, type: 'bid', bid: 'Four' })).status, 200);
    for (let i = 0; i < 3; i++) {
      view = await state();
      assert.equal((await request('/actions', { seat: view.currentSeat, type: 'pass' })).status, 200);
    }
    const before = await state();
    for (const contract of [{ mode: 99 }, { mode: 'Trump', suit: 99 }, { mode: -1 }, { mode: 'High', suit: 99 }]) {
      assert.equal((await request('/actions', { seat: bidder, type: 'contract', ...contract })).status, 400);
      assert.deepEqual(await state(), before);
    }
    assert.equal((await request('/actions', { seat: bidder, type: 'contract', mode: 'Trump', suit: 'Spades' })).status, 200);
    assert.equal((await state()).phase, 'Playing');
  } finally {
    await fetch(`${base}/api/sessions/${session.id}`, { method: 'DELETE', headers: { 'Content-Type': 'application/json' } });
  }
});
