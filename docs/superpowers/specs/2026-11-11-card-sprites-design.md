# Interchangeable card sprites

Approved in chat: create sprites and update the game.

All 24 cards (9, 10, J, Q, K, A in clubs, diamonds, hearts, spades) use transparent face artwork over a separate background sprite. Start with plain white. Future paper/background assets can change independently of card identity. Preserve accessible card names, legal action outlines, fanned hands, and card flight animations.

Approaches: flatten each card and background together (simple, but requires rebuilding all 24 cards for every background); separate transparent face sprites and a shared background (selected, supports arbitrary future backgrounds). Both desktop and web clients should share assets. No background chooser is needed until additional backgrounds exist.

Implementation choices: deterministic rank/suit markings, mirrored corner indices, decorative transparent court art. Assets include a face atlas, individual faces, a white background, a mapping manifest, and a deck preview. Retain existing card backs.
