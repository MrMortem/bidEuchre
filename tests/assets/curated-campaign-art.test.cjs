const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const {createHash} = require('node:crypto');

test('all runtime Joker textures are the exact approved uniform-border exports',()=>{
  const root=path.join(__dirname,'../..');
  const pixelRoot=path.join(root,'assets/pixel');
  const sheetRoot=path.join(root,'assets/sheets/jokers');
  const sheet=JSON.parse(fs.readFileSync(path.join(sheetRoot,'jokers.json'),'utf8'));
  const imported=JSON.parse(fs.readFileSync(path.join(pixelRoot,'curated-art.json'),'utf8'));
  const sourceRoot=path.dirname(path.resolve(pixelRoot,imported.sourceManifest));
  const jokers=imported.entries.filter(entry=>entry.category==='joker'&&entry.runtimeId);
  assert.equal(jokers.length,32);
  const hash=file=>createHash('sha256').update(fs.readFileSync(file)).digest('hex');
  for(const entry of jokers){
    const approved=sheet.frames[entry.runtimeId];
    assert.ok(approved,entry.runtimeId);
    assert.equal(hash(path.join(pixelRoot,entry.file)),approved.sha256,entry.runtimeId+' runtime artwork');
    assert.equal(hash(path.join(sheetRoot,approved.file)),approved.sha256,entry.runtimeId+' approved artwork');
    assert.equal(entry.outputSha256,approved.sha256);
    assert.equal(entry.runtimeSourceSha256,approved.sha256);
    assert.equal(hash(path.resolve(sourceRoot,entry.runtimeSource)),approved.sha256);
    assert.equal(entry.transform,'approved-uniform-joker-frame');
    assert.equal(entry.hasTransparency,true);
  }
});

test('all eight runtime enhancement textures exactly match the approved transparent exports',()=>{
  const root=path.join(__dirname,'../..');
  const pixelRoot=path.join(root,'assets/pixel');
  const sheetRoot=path.join(root,'assets/sheets/enhancements');
  const sheet=JSON.parse(fs.readFileSync(path.join(sheetRoot,'enhancements.json'),'utf8'));
  const imported=JSON.parse(fs.readFileSync(path.join(pixelRoot,'curated-art.json'),'utf8'));
  const sourceRoot=path.dirname(path.resolve(pixelRoot,imported.sourceManifest));
  const enhancements=imported.entries.filter(entry=>entry.category==='enhancement'&&entry.runtimeId);
  const hash=file=>createHash('sha256').update(fs.readFileSync(file)).digest('hex');
  assert.equal(enhancements.length,8);
  assert.deepEqual(new Set(enhancements.map(entry=>entry.runtimeId)),new Set(Object.keys(sheet.frames)));
  for(const entry of enhancements){
    const approved=sheet.frames[entry.runtimeId];
    assert.equal(hash(path.join(pixelRoot,entry.file)),approved.sha256,entry.runtimeId+' runtime artwork');
    assert.equal(hash(path.join(sheetRoot,approved.file)),approved.sha256,entry.runtimeId+' approved artwork');
    assert.equal(entry.outputSha256,approved.sha256);
    assert.equal(entry.runtimeSourceSha256,approved.sha256);
    assert.equal(hash(path.resolve(sourceRoot,entry.runtimeSource)),approved.sha256);
    assert.equal(entry.hasTransparency,true);
    assert.deepEqual([entry.width,entry.height],[124,176]);
  }
});
