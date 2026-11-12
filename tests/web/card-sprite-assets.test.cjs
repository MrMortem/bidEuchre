const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { inflateSync } = require('node:zlib');

// Decode our 8-bit RGBA PNGs so alpha and atlas crops are checked without image dependencies.
function readPng(name) {
  const png = fs.readFileSync(path.join(__dirname, '../../assets/pixel', name));
  assert.equal(png.subarray(1, 4).toString(), 'PNG');
  const width = png.readUInt32BE(16), height = png.readUInt32BE(20);
  assert.equal(png[24], 8);
  assert.equal(png[25], 6, 'Sprite must preserve RGBA channels');
  assert.equal(png[28], 0, 'Non-interlaced export');
  const chunks = [];
  for (let offset = 8; offset < png.length;) {
    const size = png.readUInt32BE(offset);
    if (png.toString('ascii', offset + 4, offset + 8) === 'IDAT')
      chunks.push(png.subarray(offset + 8, offset + 8 + size));
    offset += size + 12;
  }
  const raw = inflateSync(Buffer.concat(chunks));
  const stride = width * 4, pixels = Buffer.alloc(stride * height);
  for (let y = 0; y < height; y++) {
    const filter = raw[y * (stride + 1)];
    assert.ok(filter <= 4);
    for (let x = 0; x < stride; x++) {
      const i = y * stride + x;
      const a = x >= 4 ? pixels[i - 4] : 0;
      const b = y > 0 ? pixels[i - stride] : 0;
      const c = y > 0 && x >= 4 ? pixels[i - stride - 4] : 0;
      const p = a + b - c;
      const pa = Math.abs(p - a), pb = Math.abs(p - b), pc = Math.abs(p - c);
      const predictor = [0, a, b, Math.floor((a + b) / 2), pa <= pb && pa <= pc ? a : pb <= pc ? b : c][filter];
      pixels[i] = (raw[y * (stride + 1) + x + 1] + predictor) & 255;
    }
  }
  return { width, height, pixels };
}

test('every exported face is transparent and matches its exact atlas cell', () => {
  const manifest = require('../../assets/pixel/cards.json');
  const atlas = readPng(manifest.atlas);
  assert.equal(atlas.width, manifest.cardWidth * 6);
  assert.equal(atlas.height, manifest.cardHeight * 4);
  for (const [code, rect] of Object.entries(manifest.cards)) {
    const face = readPng(`cards/${code}.png`);
    assert.equal(face.width, manifest.cardWidth);
    assert.equal(face.height, manifest.cardHeight);
    let transparent = 0, opaque = 0;
    for (let i = 3; i < face.pixels.length; i += 4) {
      if (face.pixels[i] === 0) transparent++;
      if (face.pixels[i] === 255) opaque++;
    }
    assert.ok(transparent > face.width * face.height * .3, `${code} must expose its background`);
    assert.ok(opaque > 100, `${code} must contain visible artwork`);
    assert.equal(face.pixels[3], 0, `${code} upper left corner must be transparent`);
    assert.equal(face.pixels[face.pixels.length - 1], 0, `${code} lower right corner must be transparent`);
    for (let y = 0; y < face.height; y++) {
      const start = ((rect.y + y) * atlas.width + rect.x) * 4;
      assert.deepEqual(face.pixels.subarray(y * face.width * 4, (y + 1) * face.width * 4),
        atlas.pixels.subarray(start, start + face.width * 4), `${code} atlas row ${y}`);
    }
  }
});

test('white background is a separate opaque, pure white sprite', () => {
  const paper = readPng('card-background-white.png');
  assert.equal(paper.width, 124);
  assert.equal(paper.height, 176);
  assert.ok(paper.pixels.every(channel => channel === 255));
});

test('table layers share an opaque canvas and four independently transparent corner suits', () => {
  const base = readPng('table-flames.png');
  for (let i = 3; i < base.pixels.length; i += 4) assert.equal(base.pixels[i], 255);
  const suits = ['heart', 'club', 'spade', 'diamond'];
  for (const [index, suit] of suits.entries()) {
    const layer = readPng(`table-${suit}.png`);
    assert.equal(layer.width, base.width);
    assert.equal(layer.height, base.height);
    let visible = 0;
    for (let y = 0; y < layer.height; y++) for (let x = 0; x < layer.width; x++) {
      if (layer.pixels[(y * layer.width + x) * 4 + 3] === 0) continue;
      visible++;
      assert.equal(x < layer.width / 2, index === 0 || index === 2, `${suit} horizontal corner`);
      assert.equal(y < layer.height / 2, index < 2, `${suit} vertical corner`);
    }
    assert.ok(visible > 1000, `${suit} needs visible artwork`);
    assert.ok(visible < layer.width * layer.height * .05, `${suit} must leave the table transparent`);
    const center = (Math.floor(layer.height / 2) * layer.width + Math.floor(layer.width / 2)) * 4 + 3;
    assert.equal(layer.pixels[center], 0);
  }
});
