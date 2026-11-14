const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');

function boot(fetch) {
  const elements = new Map();
  const get = key => {
    if (!elements.has(key)) elements.set(key, { innerHTML: '', textContent: '', classList: {
      hidden: false, add() { this.hidden = true; }, remove() { this.hidden = false; }
    } });
    return elements.get(key);
  };
  const scope = vm.createContext({ fetch, TableMotion: class {}, document: {
    addEventListener() {}, querySelector: get, querySelectorAll() { return []; }
  } });
  vm.runInContext(fs.readFileSync(path.join(__dirname, '../../src/BidEuchre.Server/wwwroot/app.js'), 'utf8'), scope);
  return { get, run: code => vm.runInContext(code, scope) };
}

test('an older Rules failure cannot overwrite a newer successful load', async () => {
  let first, second;
  const requests = [new Promise(resolve => first = resolve), new Promise(resolve => second = resolve)];
  const env = boot(() => requests.shift());
  const older = env.run('loadRules()');
  const newer = env.run('loadRules()');
  second({ ok: true, text: async () => '# Current rules' }); await newer;
  first({ ok: false }); await older;
  assert.match(env.get('#rules-content').innerHTML, /Current rules/);
  assert.equal(env.get('#rules-content').textContent, '');
  assert.equal(env.run('model.rulesLoaded'), true);
  assert.equal(env.get('#retry-rules').classList.hidden, true);
});

test('an older Rules success cannot overwrite a newer failed load', async () => {
  let first, second;
  const requests = [new Promise(resolve => first = resolve), new Promise(resolve => second = resolve)];
  const env = boot(() => requests.shift());
  const older = env.run('loadRules()');
  const newer = env.run('loadRules()');
  second({ ok: false }); await newer;
  first({ ok: true, text: async () => '# Stale rules' }); await older;
  assert.equal(env.run('model.rulesLoaded'), false);
  assert.equal(env.get('#rules-content').innerHTML, '');
  assert.equal(env.get('#retry-rules').classList.hidden, false);
});
