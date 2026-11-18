# Euchatro card enhancement asset sheet

All eight active enhancements in game catalogue order: **Lucky, Golden, Wild, Cursed, Stone, Silver, Glass, Bouncy**.

## Files

- `enhancements.png`: transparent **624 × 416 RGBA** atlas, four columns by two rows. No labels or checkerboard are baked into the sheet.
- `sprites/`: eight individually named **124 × 176** PNGs, matching the game's card substrate size.
- `enhancements.json`: stable game IDs, names, source checksums and exact sprite rectangles.
- `enhancements-labeled-preview.png`: labeled review image at 2× nearest-neighbor scale. Its checkerboard is a preview of transparency, not part of the atlas.
- `border-audit.json`: per-card exterior cleanup, protected interior and alpha measurements.

## Import settings

| Setting | Value |
| --- | --- |
| Sprite size | 124 × 176 px |
| Grid cell | 156 × 208 px |
| First sprite origin | x = 16, y = 16 |
| Gap between sprites | 32 px horizontally and vertically |
| Order | Left to right, then top to bottom |
| Origin | Top left |
| Rotation / trimming | None |
| Alpha | Straight / unpremultiplied |
| Sampling | Nearest; mipmaps off |

These are card enhancement backgrounds. Playing-card ranks and suits are separate game layers.

## Preservation

The seven opaque textures have only their perimeter-connected exterior matte removed. The export uses per-card light/dark thresholds and rejects any mask entering the protected interior. Retained RGBA pixels are unchanged. **Glass is copied byte-for-byte**, retaining its existing transparent cracks and partially transparent artwork.

The original Drive files, current runtime artwork and game code are unchanged by this export. Inputs are preserved in `assets/curated-source/2026-11-16-enhancement-sheet-inputs/`, including their source metadata. Wild uses the latest imported Wild artwork; the retired Emerald and Stinky enhancements are excluded.

## Rebuild and verify

From the repository root:

```sh
dotnet run --project tools/CuratedCardArtBuilder -- --enhancements
node --test tests/assets/enhancement-asset-sheet.test.cjs
```

Optional arguments after `--enhancements`: source directory containing `curated-art.json` and its PNGs, then output directory. The exporter never writes to the runtime asset directory by default.

Verification checks catalogue identities, source checksums, exact atlas crops, clear gutters, retained border/interior RGBA, protected interiors, and Glass's unchanged original alpha.

## Runtime import

All eight individual sprites are now imported byte-for-byte into the game through `assets/curated-source/2026-11-16-euchatro-enhancements/manifest.json`. The normal curated-art rebuild pins these approved exports by checksum and preserves their alpha. Desktop enhancement rendering uses the PNG silhouettes without an opaque rectangular backing; playing-card ink and interaction highlights remain separate.
