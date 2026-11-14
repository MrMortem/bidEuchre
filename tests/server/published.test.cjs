const { test, before, after } = require('node:test');
const assert = require('node:assert/strict');
const { spawn, execFileSync } = require('node:child_process');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const dotnet = process.env.BIDEUCHRE_DOTNET || 'dotnet';
let directory, campaignRoot, server, base, logs = '';
before(async () => {
  campaignRoot = fs.mkdtempSync(path.join(os.tmpdir(), 'bideuchre-published-campaign-'));
  directory = fs.mkdtempSync(path.join(os.tmpdir(), 'bideuchre-published-'));
  execFileSync(dotnet, ['publish', path.join(root, 'src/BidEuchre.Server/BidEuchre.Server.csproj'),
    '-c', 'Debug', '-o', directory, '--nologo', '-p:UseSharedCompilation=false'], {
    cwd: root, env: { ...process.env, DOTNET_CLI_HOME: path.join(root, '.dotnet-home') }, stdio: 'pipe'
  });
  server = spawn(dotnet, [path.join(directory, 'BidEuchre.Server.dll'), '--urls=http://127.0.0.1:0', `--WebCampaigns:DataDirectory=${campaignRoot}`], {
    cwd: directory, env: { ...process.env, DOTNET_CLI_HOME: path.join(root, '.dotnet-home') }
  });
  server.stdout.on('data', data => { logs += data; });
  server.stderr.on('data', data => { logs += data; });
  const deadline = Date.now() + 20000;
  while (Date.now() < deadline) {
    const address = logs.match(/Now listening on: (http:\/\/127\.0\.0\.1:\d+)/);
    if (address) { base = address[1]; return; }
    if (server.exitCode !== null) break;
    await new Promise(resolve => setTimeout(resolve, 30));
  }
  throw new Error(`Published server did not start: ${logs}`);
});
after(async () => {
  if (server && server.exitCode === null) {
    server.kill('SIGTERM'); await new Promise(resolve => server.once('exit', resolve));
  }
  if (campaignRoot) fs.rmSync(campaignRoot, { recursive: true, force: true });
  if (directory) fs.rmSync(directory, { recursive: true, force: true });
});
test('published server serves authoritative rules outside the source tree', async () => {
  assert.equal((await fetch(base)).status, 200);
  const response = await fetch(`${base}/api/rules`);
  assert.equal(response.status, 200, await response.clone().text());
  assert.match(response.headers.get('content-type'), /text\/markdown/);
  assert.equal(await response.text(), fs.readFileSync(path.join(root, 'bid-euchre-final-rules.md'), 'utf8'));
});
test('published server includes every campaign catalog placeholder PNG', async () => {
  for (const category of ['joker', 'tarot', 'sigil', 'planet']) {
    const source = path.join(root, 'assets/pixel/campaign-placeholders', category);
    const files = fs.readdirSync(source).filter(name => name.endsWith('.png'));
    assert.ok(files.length > 0, `${category} fixture directory must contain art`);
    for (const name of files) {
      const response = await fetch(`${base}/pixel/campaign-placeholders/${category}/${name}`);
      assert.equal(response.status, 200, `${category}/${name}`);
      assert.match(response.headers.get('content-type'), /image\/png/, `${category}/${name}`);
      assert.deepEqual(Buffer.from(await response.arrayBuffer()), fs.readFileSync(path.join(source, name)));
    }
  }
  assert.equal(fs.existsSync(path.join(directory, 'wwwroot/pixel/campaign-placeholders/source')), false);
});
