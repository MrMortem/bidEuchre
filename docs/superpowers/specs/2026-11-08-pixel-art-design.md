# Bid Euchre pixel-art redesign

Status: proposed for user review.

## Intent

Restyle `MrMortem/bidEuchre` with original pixel artwork inspired by Balatro and the user's attached table reference. Apply the visual identity to both the Avalonia desktop client and the optional web client. Keep the existing C# rules engine, sessions, bots, and BEUCI support.

## Visual design

- Deep navy table with pixelated purple, cobalt, and magenta swirls; oversized suit motifs around the perimeter. Keep the central play area quiet enough to read cards.
- Chunky stepped panel edges, gold highlights, dark offset shadows, cream lettering, blue partnership accents, and crimson opponent accents.
- Four original pixel portraits in gold frames. Portraits identify seats and remain consistent when switching the viewed hand. Names, dealer indicators, and current-turn highlights remain live text/UI.
- Cream card faces with sharply readable ranks and suits, illustrated pixel court figures, and ornate cobalt card backs. Shared artwork covers the game's complete 24-card deck: nine through ace in all four suits. Both clients use the same assets and nearest-neighbor scaling.
- Gold outlines identify legal cards; hover/focus lifts a selectable card. Preserve keyboard access and readable accessible card names.
- Bundle a redistributable pixel-style font with its license; use a readable fallback for long rules, logs, and engine settings. No external font service is required at runtime.

## Table layout

Use the supplied image as the desktop/wide-screen composition: information rail on the left, partner above, opponents left and right, viewed hand below, and the four played cards in the center. The information rail contains team scores with the target of 40, current hand/phase/dealer/contract/trump, team trick totals, and a compact contract guide. Place required bidding, contract, and exchange controls in a clearly visible action area. Keep the table log available without letting it crowd the play area.

The displayed scores, hands, cards, and trick counters always come from game state; the sample numbers in the reference are not defaults. Preserve neutral team labels for spectator mode and correct team identity when viewing either partnership. Do not present team tricks as an individual player's tricks.

On narrow browser screens, stack information and actions around a compact table, reduce decoration, and retain usable cards and controls without horizontal scrolling. Preserve the native client's supported minimum window size. Setup, engines, and rules screens adopt the palette, font, and panel treatment without sacrificing their existing functions.

## Approaches

The user selected original pixel artwork over a simpler code-drawn retro theme. Share raster assets across clients to give portraits, cards, and background a consistent illustrated appearance. Use native UI and CSS for live labels, layout, borders, focus, and interactions. A purely generated screen image cannot represent interactive game state and is unsuitable as the interface itself.

## Constraints and implementation calls

Create original art rather than importing Balatro assets. Generate artwork as separate reusable elements, with card identity rendered deterministically so ranks and suits cannot become ambiguous. Store shared assets in the repository and package them for both clients. Keep hot-seat privacy, sitting-out states, bot pacing, completed-trick display, engine management, and all legal actions working.

Exact portrait characters, ornament details, responsive breakpoints, and font selection are implementation choices. Avoid adding new game modes, changing rules or bot behavior, or altering the iOS repository. Deployment is outside this change.

## Validation

Build both .NET clients and run the existing rules/protocol tests. Visually inspect desktop and web setup and gameplay, including bidding, legal-card selection, completed tricks, spectator/hot-seat views, and narrow browser layout. Confirm hidden cards stay hidden, rank/suit labels match artwork, focus is visible, and all assets load from packaged/local paths. Record any validation that the available environment cannot run.
