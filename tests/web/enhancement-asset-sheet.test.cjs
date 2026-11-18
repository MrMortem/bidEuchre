const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { inflateSync } = require('node:zlib');
const { createHash } = require('node:crypto');

const root = path.resolve(__dirname, '../..');
const sheetRoot = path.join(root, 'assets/sheets/enhancements');
const json = file => JSON.parse(fs.readFileSync(file, 'utf8'));
const hash = file => createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const manifest = () => json(path.join(sheetRoot, 'enhancements.json'));

// Decode the delivered PNG bytes independently of the Skia exporter.
function readPng(file) {
  const png = fs.readFileSync(file);
  assert.equal(png.subarray(0, 8).toString('hex'), '89504e470d0a1a0a');
  const width = png.readUInt32BE(16), height = png.readUInt32BE(20);
  assert.equal(png[24], 8, '8-bit pixels');
  assert.ok([2, 6].includes(png[25]), 'RGB or RGBA PNG');
  assert.equal(png[28], 0, 'noninterlaced PNG');
  const bpp = png[25] === 6 ? 4 : 3;
  const chunks = [];
  for (let offset = 8; offset < png.length;) {
    const length = png.readUInt32BE(offset);
    if (png.toString('ascii', offset + 4, offset + 8) === 'IDAT')
      chunks.push(png.subarray(offset + 8, offset + 8 + length));
    offset += length + 12;
  }
  const raw = inflateSync(Buffer.concat(chunks));
  const stride = width * bpp, decoded = Buffer.alloc(stride * height);
  assert.equal(raw.length, (stride + 1) * height);
  for (let y = 0; y < height; y++) {
    const filter = raw[y * (stride + 1)];
    assert.ok(filter <= 4, 'valid PNG filter');
    for (let x = 0; x < stride; x++) {
      const i = y * stride + x;
      const a = x >= bpp ? decoded[i - bpp] : 0;
      const b = y > 0 ? decoded[i - stride] : 0;
      const c = y > 0 && x >= bpp ? decoded[i - stride - bpp] : 0;
      const p = a + b - c, pa = Math.abs(p - a), pb = Math.abs(p - b), pc = Math.abs(p - c);
      const predictor = [0, a, b, Math.floor((a + b) / 2), pa <= pb && pa <= pc ? a : pb <= pc ? b : c][filter];
      decoded[i] = (raw[y * (stride + 1) + x + 1] + predictor) & 255;
    }
  }
  const pixels = Buffer.alloc(width * height * 4, 255);
  for (let p = 0; p < width * height; p++)
    decoded.copy(pixels, p * 4, p * bpp, (p + 1) * bpp);
  return { width, height, pixels, bpp };
}

test('enhancement sheet contains all eight active identities in catalogue order with verified sources', () => {
  const sheet = manifest();
  const catalogue = fs.readFileSync(path.join(root, 'src/BidEuchre.Core/FeatureCatalog.cs'), 'utf8');
  const enhancements = [...catalogue.matchAll(/new\("(enhancement:[^"]+)", FeatureCategory\.Enhancement, "([^"]+)", "([^"]+)"/g)];
  assert.equal(enhancements.length, 8);
  assert.deepEqual(Object.keys(sheet.frames), enhancements.map(match => match[1]));
  assert.equal(new Set(Object.values(sheet.frames).map(frame => frame.file)).size, 8);
  for (const [index, [, id, key, name]] of enhancements.entries()) {
    const frame = sheet.frames[id];
    assert.equal(frame.name, name);
    assert.equal(frame.key, key);
    assert.equal(frame.index, index);
    assert.equal(frame.row, Math.floor(index / 4));
    assert.equal(frame.column, index % 4);
    assert.equal(hash(path.resolve(sheetRoot, frame.sourceFile)), frame.sourceSha256, `${id}: immutable input hash`);
    assert.equal(hash(path.resolve(sheetRoot, frame.file)), frame.sha256, `${id}: exported PNG hash`);
  }
  assert.equal(hash(path.resolve(sheetRoot, sheet.meta.sourceManifest)), sheet.meta.sourceManifestSha256);
  assert.equal(hash(path.join(sheetRoot, sheet.meta.image)), sheet.meta.sha256);
  const inputs = json(path.resolve(sheetRoot, sheet.meta.sourceManifest));
  const entries = inputs.entries.filter(entry => entry.category === 'enhancement' && entry.runtimeId);
  assert.deepEqual(entries.map(entry => entry.runtimeId).sort(), Object.keys(sheet.frames).sort());
  for (const entry of entries) {
    assert.equal(sheet.frames[entry.runtimeId].sourceSha256, entry.outputSha256,
      `${entry.runtimeId}: export uses the recorded runtime input`);
  }
});

test('atlas geometry is usable as a regular 4 by 2 grid, every crop matches and gutters are transparent', () => {
  const sheet = manifest();
  assert.equal(sheet.meta.image, 'enhancements.png');
  assert.deepEqual(sheet.meta.size, { w: 624, h: 416 });
  assert.deepEqual([sheet.meta.count, sheet.meta.columns, sheet.meta.rows], [8, 4, 2]);
  assert.deepEqual([sheet.meta.cellWidth, sheet.meta.cellHeight, sheet.meta.padding], [156, 208, 16]);
  assert.equal(sheet.meta.origin, 'top-left');
  assert.equal(sheet.meta.filter, 'nearest');
  assert.equal(sheet.meta.mipmaps, false);
  assert.equal(sheet.meta.alpha, 'straight (unpremultiplied)');
  const atlas = readPng(path.join(sheetRoot, sheet.meta.image));
  assert.equal(atlas.bpp, 4);
  assert.deepEqual([atlas.width, atlas.height], [624, 416]);
  const covered = new Uint8Array(atlas.width * atlas.height);
  for (const [id, item] of Object.entries(sheet.frames)) {
    const sprite = readPng(path.join(sheetRoot, item.file));
    assert.deepEqual([sprite.width, sprite.height], [124, 176]);
    assert.equal(sprite.bpp, 4);
    assert.equal(item.rotated, false);
    assert.equal(item.trimmed, false);
    assert.deepEqual(item.sourceSize, { w: 124, h: 176 });
    assert.deepEqual(item.spriteSourceSize, { x: 0, y: 0, w: 124, h: 176 });
    const { x, y, w, h } = item.frame;
    assert.equal(x, item.column * 156 + 16);
    assert.equal(y, item.row * 208 + 16);
    assert.deepEqual([w, h], [124, 176]);
    for (let py = 0; py < h; py++) {
      const offset = (y + py) * atlas.width + x;
      assert.deepEqual(atlas.pixels.subarray(offset * 4, (offset + w) * 4),
        sprite.pixels.subarray(py * w * 4, (py + 1) * w * 4), `${id}: atlas row ${py}`);
      for (let px = 0; px < w; px++) {
        assert.equal(covered[offset + px], 0, 'Frames must not overlap');
        covered[offset + px] = 1;
      }
    }
  }
  for (let p = 0; p < covered.length; p++)
    if (!covered[p]) assert.equal(atlas.pixels[p * 4 + 3], 0, 'Gutters must have alpha 0');
});

test('exterior cleanup preserves retained RGBA pixels and the complete protected interior', () => {
  const sheet = manifest();
  const audit = json(path.join(sheetRoot, 'border-audit.json'));
  assert.equal(audit.entries.length, 8);
  assert.deepEqual(audit.entries.map(entry => entry.id).sort(), Object.keys(sheet.frames).sort());
  for (const [id, item] of Object.entries(sheet.frames)) {
    const source = readPng(path.resolve(sheetRoot, item.sourceFile));
    const sprite = readPng(path.join(sheetRoot, item.file));
    assert.deepEqual([source.width, source.height], [124, 176]);
    const entry = audit.entries.find(entry => entry.id === id);
    const guard = entry.protectedInterior;
    const expectedGuard = id === 'enhancement:glass' ? { x: 0, y: 0, w: 124, h: 176 }
      : id === 'enhancement:stone' ? { x: 7, y: 7, w: 110, h: 162 }
        : { x: 8, y: 8, w: 108, h: 160 };
    assert.deepEqual(guard, expectedGuard, `${id}: protected interior bounds`);
    let removed = 0, transparent = 0, partial = 0;
    for (let y = 0; y < source.height; y++) for (let x = 0; x < source.width; x++) {
      const p = (y * source.width + x) * 4;
      const original = source.pixels.subarray(p, p + 4);
      const actual = sprite.pixels.subarray(p, p + 4);
      const protectedPixel = x >= guard.x && x < guard.x + guard.w && y >= guard.y && y < guard.y + guard.h;
      if (protectedPixel || actual[3] > 0)
        assert.deepEqual(actual, original, `${id}: retained RGBA at ${x},${y}`);
      if (original[3] === 0) assert.equal(actual[3], 0, `${id}: existing transparency at ${x},${y}`);
      if (original[3] > 0 && actual[3] === 0) removed++;
      if (actual[3] === 0) transparent++;
      else if (actual[3] < 255) partial++;
    }
    assert.equal(entry.removedExteriorPixels, removed, `${id}: audited exterior changes`);
    assert.equal(entry.transparentPixels, transparent, `${id}: audited transparency`);
    assert.equal(entry.partialAlphaPixels, partial, `${id}: audited partial alpha`);
    assert.ok(transparent > 0, `${id}: actual transparent pixels`);
    for (const [x, y] of [[0, 0], [123, 0], [0, 175], [123, 175]])
      assert.equal(sprite.pixels[(y * 124 + x) * 4 + 3], 0, `${id}: transparent corner`);
    if (id === 'enhancement:glass') {
      assert.equal(item.sha256, item.sourceSha256, 'Glass PNG is copied byte for byte');
      assert.ok(partial > 0, 'Glass keeps its translucent artwork');
      assert.ok(transparent > 10000, 'Glass keeps its open transparent interior');
    } else {
      // All new transparent pixels must connect to the exterior, never form a hole.
      const visited = new Uint8Array(124 * 176), queue = [];
      const visit = (x, y) => {
        if (x < 0 || x >= 124 || y < 0 || y >= 176) return;
        const p = y * 124 + x;
        if (!visited[p] && sprite.pixels[p * 4 + 3] === 0) {
          visited[p] = 1;
          queue.push(p);
        }
      };
      for (let x = 0; x < 124; x++) { visit(x, 0); visit(x, 175); }
      for (let y = 0; y < 176; y++) { visit(0, y); visit(123, y); }
      for (let head = 0; head < queue.length; head++) {
        const p = queue[head], x = p % 124, y = Math.floor(p / 124);
        visit(x - 1, y); visit(x + 1, y); visit(x, y - 1); visit(x, y + 1);
      }
      assert.equal(queue.length, transparent, `${id}: no new interior transparency holes`);
    }
  }
});
