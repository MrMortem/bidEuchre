# Approved transparent enhancement import

Imports all eight approved sprites from `assets/sheets/enhancements/sprites/` into their existing `assets/pixel/curated-enhancement-*.png` runtime resources. Names and gameplay IDs remain Lucky, Golden, Wild, Cursed, Stone, Silver, Glass and Bouncy.

The manifest preserves all 77 original source mappings and checksums. It adds final `runtimeFile` and `runtimeSha256` mappings for the eight enhancements while retaining the 32 approved Joker overrides. The importer verifies dimensions and checksums, then copies the exact PNG bytes. It does not resample, flatten transparency, or run the exterior mask again.

Seven runtime textures gain transparent exteriors. Glass already has its approved bytes and keeps all existing internal transparency. The other 69 curated textures remain byte-identical, including the Jokers and Plinko artwork.

The enhancement sheet rebuild uses its preserved inputs in `assets/curated-source/2026-11-16-enhancement-sheet-inputs/`, so rebuilding cannot mask an already-imported sprite a second time. Original Drive files and the previous import manifest in `2026-11-16-euchatro-arcade` remain intact.

Rebuild the runtime import from the repository root:

```sh
dotnet run --project tools/CuratedCardArtBuilder
node --test tests/assets/enhancement-asset-sheet.test.cjs tests/assets/curated-campaign-art.test.cjs tests/assets/arcade-art.test.cjs
```

Desktop embeds these same PNGs and the server copies them into its `/pixel/` assets. Enhanced-card rendering uses transparent backing so the approved silhouettes and Glass alpha remain visible; ranks, suits, Sigils and selection cues remain separate layers.
