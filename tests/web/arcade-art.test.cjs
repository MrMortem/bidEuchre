const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const {createHash} = require('node:crypto');

const root = path.join(__dirname, '../..');
const pixelRoot = path.join(root, 'assets/pixel');
const imported = JSON.parse(fs.readFileSync(path.join(pixelRoot, 'curated-art.json'), 'utf8'));
const manifestPath = path.resolve(pixelRoot, imported.sourceManifest);
const manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
const sourceRoot = path.dirname(manifestPath);
const hash = bytes => createHash('sha256').update(bytes).digest('hex');

test('Plinko board and dropper preserve the attached original bytes and native geometry', () => {
  const expected = [
    ['arcade-plinko-board', 'Plinko Body.png', 1199, 1312, false],
    ['arcade-plinko-dropper', 'Plinko Dropper.png', 1312, 1199, true]
  ];
  assert.equal(imported.entries.filter(entry => entry.category === 'arcade').length, 2);
  for (const [resource, originalName, width, height, hasTransparency] of expected) {
    const output = imported.entries.find(entry => entry.resource === resource);
    const source = manifest.entries.find(entry => entry.resource === resource);
    assert.ok(output && source, resource);
    const original = fs.readFileSync(path.resolve(sourceRoot, source.source));
    const labeled = fs.readFileSync(path.resolve(sourceRoot, source.file));
    const runtime = fs.readFileSync(path.join(pixelRoot, output.file));
    assert.deepEqual(runtime, original, resource + ' must not be resampled or repainted');
    assert.deepEqual(labeled, original);
    assert.equal(hash(original), source.sha256);
    assert.equal(output.sourceSha256, source.sha256);
    assert.equal(output.outputSha256, source.sha256);
    assert.equal(runtime.readUInt32BE(16), width);
    assert.equal(runtime.readUInt32BE(20), height);
    assert.equal(output.hasTransparency, hasTransparency);
    assert.equal(output.transform, 'original-bytes');
    assert.equal(output.sourceKind, 'user-provided-local-file');
    assert.equal(output.sourceName, originalName);
    assert.equal(output.sourceDriveId, null);
    assert.equal(output.runtimeId, null);
    assert.equal(output.usage, 'scene-artwork');
  }
});

test('arcade source manifest retains every previous source and approved Joker runtime mapping', () => {
  // Verify the historical two-file Arcade import independently of later art imports.
  const arcadeRoot = path.join(root, 'assets/curated-source/2026-11-16-euchatro-arcade');
  const arcadeManifest = JSON.parse(fs.readFileSync(path.join(arcadeRoot, 'manifest.json'), 'utf8'));
  const previous = JSON.parse(fs.readFileSync(path.resolve(arcadeRoot, arcadeManifest.runtimeImport.preservedPreviousManifest), 'utf8'));
  assert.equal(arcadeManifest.entries.length, previous.entries.length + 2);
  for (const entry of previous.entries) {
    const retained = arcadeManifest.entries.find(candidate => candidate.id === entry.id);
    assert.deepEqual(retained, entry, entry.id);
    if (entry.runtimeFile) {
      const output = imported.entries.find(candidate => candidate.sourceEntryId === entry.id);
      assert.equal(hash(fs.readFileSync(path.join(pixelRoot, output.file))), entry.runtimeSha256);
    }
  }
});
