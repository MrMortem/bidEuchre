# Euchatro Arcade artwork

The user supplied **Plinko Body.png** and **Plinko Dropper.png** as local attachments on 2026-11-16. Their names also appeared in the Euchatro Drive project's 77-source listing. No individual Drive file IDs were available, so the manifest records the attachment origin and project URL without inventing file IDs.

- `originals/` retains the exact supplied files.
- `labeled/arcade/plinko-board.png` and `plinko-dropper.png` are identically copied, named imports.
- Runtime resources are `arcade-plinko-board.png` (1199×1312) and `arcade-plinko-dropper.png` (1312×1199).
- The board is opaque, including its black exterior. The dropper retains its supplied transparency. No backgrounds, borders or pixels were altered.
- The manifest retains all 75 previous source mappings and all 32 approved uniform Joker overrides. Rebuilding the 77-resource manifest leaves those existing textures unchanged.

The board's eleven bins read **6, 5, 4, 3, 2, 1, 2, 3, 4, 5, 6**. Its fourteen peg rows drive the saved ball path. The movable dropper is rendered above the first peg row; the ball itself is drawn by the game. Roulette uses native wheel geometry.
