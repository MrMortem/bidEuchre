# Euchatro Joker asset sheet

All 32 active Jokers, in game catalogue order. Boomerang Joker uses the approved Boomerang artwork under its existing `joker:double-down` ID.

## Files

- `jokers.png`: transparent 2560 × 1728 RGBA sheet, 8 columns × 4 rows. No labels or checkerboard are baked into this image.
- `sprites/`: 32 individually named, transparent 280 × 392 PNGs.
- `jokers.json`: stable game IDs, display names, individual filenames, source hashes and exact atlas rectangles.
- `jokers-labeled-preview.png`: labeled review sheet on a checkerboard background. Use for reference, not as the game texture.
- `border-audit.json`: per-card transparency and exterior cleanup measurements.
- `joker-frame.png`: the reusable, identical rounded frame applied to every card.

## Import settings

Use the JSON rectangles, or slice a regular grid with these settings:

| Setting | Value |
| --- | --- |
| Sprite size | 280 × 392 px |
| Grid cell | 320 × 432 px |
| First sprite origin | x = 20, y = 20 |
| Gap between sprites | 40 px horizontally and vertically |
| Ordering | Left to right, then top to bottom |
| Coordinate origin | Top left |
| Rotation / trimming | None |
| Alpha | Straight / unpremultiplied |
| Texture sampling | Nearest; mipmaps off |

Every card has the same outer bounds, corner radius, border thickness and color. The outer frame occupies `(4,4)` to `(276,388)` with a 12px radius, a 2px dark outline (`#1B1713`) and an 8px total cream rim (`#F4E7CD`). Four-by-four subpixel sampling smooths the outer corners consistently. The interior aperture occupies `(12,12)` to `(268,380)` with a 4px radius.

Bully keeps its narrower interior scene within the common frame. No scene or Joker lettering is stretched or resampled.

## Cleanup and verification

The user approved an exterior export mask. Sixteen cards had opaque white/cream outside their rounded frames. A perimeter-connected mask removes that matte without globally removing white, damaging the printed border or opening holes in the interior. The other sixteen cards have a conservative cleanup of faint exterior alpha and detached specks. The original near-opaque alpha of those cards is preserved.

The old outer strokes are removed along each card's own silhouette before applying the common frame, preventing doubled outlines and mismatched corner fragments. Connected printed border paper is harmonized to the shared cream; interior scenes and dark Joker lettering retain their original RGBA pixels. Original Drive artwork and pre-frame inputs are preserved.

All 32 approved individual PNGs are now imported byte-for-byte into `assets/pixel/curated-joker-*.png`, replacing the old artwork in the desktop client. The active import manifest is `assets/curated-source/2026-11-16-euchatro-joker-frames/manifest.json`. It pins the approved exports by checksum so ordinary asset rebuilds retain the new borders.

Verified all 32 identities, exact source hashes, matching corner silhouettes and border pixels, transparent corners/gutters, preserved scenes and dark Joker lettering, and byte-for-byte agreement between atlas crops and individual sprites. Visual review found no clipped lettering or doubled outer strokes. Import checks additionally compare game textures and desktop embedded resources against the approved sprites.

Rebuild from the repository root:

```sh
dotnet run --project tools/CuratedCardArtBuilder -- --jokers
dotnet run --project tools/CuratedCardArtBuilder
node --test tests/assets/joker-asset-sheet.test.cjs tests/assets/curated-campaign-art.test.cjs
```

The sheet builder defaults to preserved pre-frame inputs in `assets/curated-source/2026-11-16-joker-frame-inputs`, so rebuilding never applies the frame a second time to already imported game sprites. Optional arguments after `--jokers`: source pixel directory, then output directory. The source directory must contain `curated-art.json` and its PNGs. The exporter verifies that its source IDs exactly match the current game catalogue. Intentional changes to the approved sprite bytes also require updating their `runtimeSha256` checksums in the active import manifest.
