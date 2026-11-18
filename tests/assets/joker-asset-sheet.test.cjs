const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { inflateSync } = require('node:zlib');
const { createHash } = require('node:crypto');

const root = path.resolve(__dirname, '../..');
const sheetRoot = path.join(root, 'assets/sheets/jokers');
const manifest = JSON.parse(fs.readFileSync(path.join(sheetRoot, 'jokers.json')));
const audit = JSON.parse(fs.readFileSync(path.join(sheetRoot, 'border-audit.json')));
const hash = file => createHash('sha256').update(fs.readFileSync(file)).digest('hex');

// Independent PNG decoding checks the delivered RGBA bytes, including RGB-only source PNGs.
function readPng(file) {
  const png = fs.readFileSync(file);
  assert.equal(png.subarray(0, 8).toString('hex'), '89504e470d0a1a0a');
  const width = png.readUInt32BE(16), height = png.readUInt32BE(20);
  assert.equal(png[24], 8);
  assert.ok([2, 6].includes(png[25]), 'RGB or RGBA PNG');
  assert.equal(png[28], 0);
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
  for (let y = 0; y < height; y++) {
    const filter = raw[y * (stride + 1)];
    assert.ok(filter <= 4);
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

test('Joker sheet contains every active Joker exactly once, with game names and current provenance', () => {
  const catalogue = fs.readFileSync(path.join(root, 'src/BidEuchre.Core/FeatureCatalog.cs'), 'utf8');
  const jokers = [...catalogue.matchAll(/new\("(joker:[^"]+)", FeatureCategory\.Joker, "([^"]+)", "([^"]+)"/g)];
  assert.equal(jokers.length, 32);
  assert.deepEqual(Object.keys(manifest.frames), jokers.map(joker => joker[1]));
  assert.equal(new Set(Object.values(manifest.frames).map(frame => frame.file)).size, 32);
  for (const [, id, key, name] of jokers) {
    const frame = manifest.frames[id];
    assert.equal(frame.name, name);
    assert.equal(frame.key, key);
    assert.equal(hash(path.resolve(sheetRoot, frame.sourceFile)), frame.sourceSha256);
    assert.equal(hash(path.resolve(sheetRoot, frame.file)), frame.sha256);
  }
  assert.equal(hash(path.resolve(sheetRoot, manifest.meta.sourceManifest)), manifest.meta.sourceManifestSha256);
  assert.equal(hash(path.resolve(sheetRoot, manifest.meta.image)), manifest.meta.sha256);
});

test('all 32 atlas crops exactly match individual sprites and all gutters are truly transparent', () => {
  const atlas = readPng(path.join(sheetRoot, manifest.meta.image));
  assert.equal(atlas.bpp, 4);
  assert.deepEqual([atlas.width, atlas.height], [2560, 1728]);
  const covered = new Uint8Array(atlas.width * atlas.height);
  for (const [id, item] of Object.entries(manifest.frames)) {
    const sprite = readPng(path.join(sheetRoot, item.file));
    assert.deepEqual([sprite.width, sprite.height], [280, 392]);
    assert.equal(sprite.bpp, 4);
    const { x, y, w, h } = item.frame;
    assert.equal(x, item.column * 320 + 20);
    assert.equal(y, item.row * 432 + 20);
    assert.deepEqual([w, h], [280, 392]);
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

function inAperture(x, y) {
  x += .5; y += .5;
  if (x < 12 || x >= 268 || y < 12 || y >= 380) return false;
  const cx = Math.max(16, Math.min(264, x));
  const cy = Math.max(16, Math.min(376, y));
  return (x - cx) ** 2 + (y - cy) ** 2 <= 16;
}

test('all Jokers have the exact same rounded outer border, thickness, colors and silhouette', () => {
  const border = manifest.meta.border;
  assert.deepEqual(border.outer, { x: 4, y: 4, w: 272, h: 384, radius: 12 });
  assert.equal(border.outlineWidth, 2);
  assert.equal(border.totalRimWidth, 8);
  const templatePath = path.join(sheetRoot, border.template);
  assert.equal(hash(templatePath), border.sha256);
  const template = readPng(templatePath);
  const reference = readPng(path.join(sheetRoot, Object.values(manifest.frames)[0].file));
  assert.deepEqual([...template.pixels.subarray((100 * 280 + 4) * 4, (100 * 280 + 4) * 4 + 4)], [27, 23, 19, 255]);
  for (let x = 6; x < 12; x++)
    assert.deepEqual([...template.pixels.subarray((100 * 280 + x) * 4, (100 * 280 + x) * 4 + 4)], [244, 231, 205, 255]);
  for (const [id, item] of Object.entries(manifest.frames)) {
    const sprite = readPng(path.join(sheetRoot, item.file));
    for (let y = 0; y < 392; y++) for (let x = 0; x < 280; x++) {
      const p = (y * 280 + x) * 4;
      if (!inAperture(x, y))
        assert.deepEqual(sprite.pixels.subarray(p, p + 4), template.pixels.subarray(p, p + 4), `${id}: shared border at ${x},${y}`);
      else assert.ok(sprite.pixels[p + 3] > 0, `${id}: no interior holes`);
      assert.equal(sprite.pixels[p + 3] > 0, reference.pixels[p + 3] > 0, `${id}: common silhouette`);
    }
    for (const [x, y] of [[0, 0], [279, 0], [0, 391], [279, 391], [4, 4], [275, 387]])
      assert.equal(sprite.pixels[(y * 280 + x) * 4 + 3], 0, `${id}: rounded transparent corner`);
  }
});

test('uniform frames preserve source scene pixels and dark JOKER lettering without resampling', () => {
  assert.equal(audit.entries.length, 32);
  for (const [id, item] of Object.entries(manifest.frames)) {
    const source = readPng(path.resolve(sheetRoot, item.sourceFile));
    const sprite = readPng(path.join(sheetRoot, item.file));
    const entry = audit.entries.find(entry => entry.id === id);
    assert.ok(entry);
    for (let y = 0; y < source.height; y++) for (let x = 0; x < source.width; x++) {
      const p = (y * source.width + x) * 4;
      const original = source.pixels.subarray(p, p + 4);
      const actual = sprite.pixels.subarray(p, p + 4);
      const scene = y >= 20 && y < 372 &&
        (x >= 42 && x < 238 || x >= 20 && x < 42 && y >= 158 || x >= 238 && x < 260 && y < 236);
      const label = x >= 15 && x < 38 && y >= 18 && y < 140 || x >= 242 && x < 264 && y >= 250 && y < 376;
      const darkInk = original[3] > 64 && Math.max(...original.subarray(0, 3)) < 96;
      if (scene || label && darkInk)
        assert.deepEqual(actual, original, `${id}: preserved ${scene ? 'scene' : 'lettering'} at ${x},${y}`);
    }
    assert.equal(entry.preservedInteriorScene, true);
    assert.ok(entry.transparentPixels > 0);
    assert.deepEqual(entry.bounds, { x: 4, y: 4, width: 272, height: 384 });
  }
});
