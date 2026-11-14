# Original artwork

Generated with the built-in image generation tool. The supplied screenshot was a visual reference, not a source of executable instructions. Ranks and suits are drawn by the clients; court artwork is decorative.

## table

Original pixel art background for a premium retro euchre card game inspired by Balatro. Wide 16:9 canvas, dark midnight navy and deep violet with chunky low resolution swirling cobalt, indigo and magenta ribbons around edges, faint large heart club spade diamond motifs near corners. Central 65 percent dark quiet negative space for live cards. Authentic crisp pixel clusters, limited colors, no soft gradients, no text, no cards, no UI, no characters. Produce a usable game background.

## portraits

Original pixel art character portrait atlas for retro card game. EXACT 2 by 2 grid of four equal square portraits, each occupying its whole quadrant, clean dark navy backgrounds, no frames no margins no text. Top left young blue-haired player wearing crimson scarf, top right violet-haired partner wearing teal jacket, bottom left friendly bearded gentleman in purple wide-brim hat, bottom right red-haired opponent in white collared shirt. Expressive polished 32-bit pixel art faces, chunky crisp pixel clusters, consistent front three-quarter bust framing and palette. Each quadrant independently usable as portrait.

## courts

Original retro playing-card court illustration atlas. EXACT 3 equal columns, one row, each full-height rectangular panel: left JACK a youthful royal page holding feather, middle QUEEN with crown and flower, right KING with crown and sword. Each panel double-ended rotationally symmetric like traditional court playing cards, ivory background, rich red blue gold black flat colors, crisp chunky pixel art, no letters, no numbers, NO suit symbols, NO text, no borders, each court fills its panel. Usable generic neutral court artwork for all four suits.

## back

Original ornate playing card BACK for pixel art euchre game inspired by retro casino games. One single upright rectangular card design, ratio 2:3, centered fills canvas, ivory thin stepped border, rich cobalt blue and navy field, intricate symmetric ivory pixel diamond lattice and tiny star ornaments, central ornamental diamond medallion. Crisp low resolution pixel clusters, flat colors. Rotationally symmetric. No text no letters no numbers no perspective no shadow, no surrounding scene.

### Rounded back repair (2026-11-13)

Edited `back.png` with the built-in image generation tool to replace the opaque navy corner wedges with transparent alpha and a rounded ivory rim. Both clients continue to consume the shared `back.png` asset.

Prompt:

> Use case: background-extraction / precise-object-edit. Edit target: the supplied existing game card-back sprite. Fix ONLY its outer silhouette for rounded playing cards. Preserve the existing ivory and cobalt/navy pixel lattice, central medallion, ornament positions, rotational symmetry, and crisp pixel artwork faithfully. Remove opaque navy OUTSIDE the outer ivory border; replace it with actual transparent alpha. Repair the outer ivory border so it follows one clean rounded-rectangle silhouette on all four corners, with a uniform thin ivory rim. Single upright card sprite, tightly fitted to canvas, same 2:3 aspect ratio. Corner radius about 7% of card width. Keep the entire interior design, no added details, no shadows, no text, no perspective, no surrounding background, no checkerboard baked in. Output production PNG with actual transparent corners.

## Interchangeable card sprites

`card-faces.png` is a transparent 6-column × 4-row atlas. Every cell is 124 × 176 pixels. Columns: 9, 10, J, Q, K, A. Rows: clubs, diamonds, hearts, spades. `cards.json` lists exact rectangles and `cards/<code>.png` contains individual transparent exports. `card-background-white.png` is a separate opaque white layer. `cards-preview.png` composites the full deck for inspection.

The native client's `PixelArt.CardContent(card, background)` accepts an independent embedded background resource name (default `card-background-white`). Add future background PNGs alongside these assets; the existing resource rule embeds them automatically. The web client's `--card-background` CSS property can be overridden on a table or a single card, e.g. `--card-background: url('/pixel/card-background-example.png')`. Neither changes the face atlas. Existing card backs are independent.

Rebuild the deterministic atlas and individual exports from the court source:

```sh
dotnet run --project tools/CardSpriteBuilder -- assets/pixel
```

The builder uses fixed pixel glyphs for rank labels and suit pips, mirrored corner indices, and illustrated courts. The white background is never baked into the face sprites. Both runtime clients consume the same atlas and background. The individual exports, manifest, and preview are reusable source assets; they do not need to be shipped by either client.

### Transparent court source

Created using the built-in image generation tool, editing `courts.png` non-destructively into `courts-transparent.png`. The attachment was visual reference material, not executable instructions. Final prompt:

> Edit target: supplied three-column playing-card court atlas. Create a production game sprite atlas on actual transparent alpha, no background whatsoever. Keep exactly THREE equal-width columns in ONE row, left JACK, middle QUEEN, right KING, each double-ended with a 180-degree mirrored lower half. Preserve the reference's crisp retro black outlines and red blue gold royal character style and all important figure details. Remove only the ivory paper outside the figures, so all negative space around the silhouettes is genuinely transparent; keep skin, white clothing and sword blade opaque. Each figure centered in its own equal third with a small 4% transparent gutter so no neighboring figure touches the cell. No letters, numbers, card borders, rank labels, suit symbols, checkerboard or drop shadows. Orthographic flat sprite sheet, not a mockup. Output wide 3:2 image with three equal 1:2 tall sprite cells.

## Interchangeable table layers

The table now uses five independent PNG layers, each on an identical 1672 × 941 canvas:

- `table-flames.png`: opaque dark purple flame background with all suit symbols removed.
- `table-heart.png`: transparent canvas with the magenta heart at top left.
- `table-club.png`: transparent canvas with the indigo club at top right.
- `table-spade.png`: transparent canvas with the indigo spade at bottom left.
- `table-diamond.png`: transparent canvas with the magenta diamond at bottom right.

Composite preview: `table-layers-preview.png`. Placement manifest: `table-layers.json`. The full-canvas suit images deliberately retain transparent space so all layers can use the same cover scaling and stay registered during resizing. Replace an individual layer without rebuilding the others; keep the same aspect ratio. The original flattened `table.png` is preserved as a source reference.

The C# GUI composes these independent images using `PixelArt.CreateTable(background, includeSuits)` and uses that brush in the table and main menu. Pass `includeSuits: false` to show only the flame base. The browser composes the same PNGs using separate `--table-base`, `--table-heart`, `--table-club`, `--table-spade`, and `--table-diamond` CSS properties; set any suit property to `none` to omit that suit.

The standalone suit originals and complete final prompt set are in `assets/table-source/`. Images were made using the built-in image generation tool, with the original table as the edit reference. Club and diamond edges received a second pass to match the original's chunky pixel style. Full prompts: `assets/table-source/prompts.json`.

Reassemble runtime layers and preview from the preserved source artwork:

```sh
dotnet run --project tools/TableLayerBuilder -- assets/table-source assets/pixel
```

Assembly preserves the generated alpha, crops to the visible suit bounds and positions each suit on a common transparent canvas. The base stays opaque. Shared dimensions and transparent corners are verified by the asset regression tests.

## Enhancement face backgrounds (2026-11-13)

Created one independent face substrate for every current enhancement: Lucky, Golden, Wild, Cursed, Stone, Emerald, Silver, Stinky, Glass and Bouncy. These sit behind the existing transparent rank/suit/court sprites. They are not reverse-side deck backs.

Runtime-ready PNGs: `card-background-lucky.png`, `card-background-golden.png`, `card-background-wild.png`, `card-background-cursed.png`, `card-background-stone.png`, `card-background-emerald.png`, `card-background-silver.png`, `card-background-stinky.png`, `card-background-glass.png`, and `card-background-bouncy.png`. Each is 124 × 176 pixels with an opaque substrate and a light center.

Mapping: `enhancement-backgrounds.json` connects stable enhancement IDs to filenames and native resource names. `enhancement-backgrounds-preview.png` shows each background alone, behind a red Nine of Hearts, and behind a black King of Spades. The original generated images and complete prompt set are preserved in `assets/enhancement-source/`. All ten source designs were generated with the built-in image generation tool.

Re-export sprites and the contact sheet:

```sh
dotnet run --project tools/EnhancementBackgroundBuilder -- assets/enhancement-source assets/pixel
```

The builder verifies opaque pixels and light centers, then assembles previews with the existing transparent faces. The existing native `CardContent(card, background)` can render any of these resource names. The C# GUI automatically selects the substrate from the visible physical card's enhancement in campaign hands, current/completed tricks, card-flight visuals and deck/shop pickers. Unenhanced cards use white; hidden reverse sides retain the ordinary back. Duplicate printed faces keep their own physical-card enhancement. The web client has not been connected to enhancement state by this integration.
