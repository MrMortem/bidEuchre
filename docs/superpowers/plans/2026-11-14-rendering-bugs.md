# Open rendering bugs implementation plan

**Goal:** Fix GitHub bugs #11–#14 and open one PR with closing references.
**Architecture:** Keep the authoritative game actions unchanged. Give table choices a separate scrollable column, select multi-action cards through a card flyout, and replace strip shrinking with wrapped status and a scrollable inventory. Cap effective UI scale to the current viewport and keep Ante navigation outside scrolling content.
**Tech stack:** C#, Avalonia, existing headless executable tests.
**Spec:** GitHub issues MrMortem/bidEuchre #11, #12, #13, #14.

- [x] Add failing headless regressions for table/action separation, readable status, free-play inventory, Wild card choices, ordered readable bid labels, compact Ante navigation, and scale fitting.
- [x] Update CampaignTableControls.cs and MainWindow.cs for labels, bid order, tooltips and card selection. Verify clicks still submit the exact legal command.
- [x] Update MainWindow.cs layout and scale handling; update CampaignAnteScreen.cs navigation. Check 1320×860, 1280×720 and 980×680 with requested scales 100% and 150%.
- [x] Run ./test.sh and inspect the final diff; prepare codex/fix-open-rendering-bugs for a PR closing #11–#14.

Review focus: all unlocks, multiple Wild cards, long/custom seat names, free-play session reuse, resizing after choosing a large UI scale. Tests must exercise rendered bounds and action routing rather than source text.

Validation: `./test.sh` exits 0 with zero build warnings/errors and 159/159 core/app tests passing. The desktop executable passes 34 checks, including 18 new rendering/input checks. Skia renders cover campaign and free-play at all three reported sizes with requested 100%/150% scale. A real campaign session confirms a pointer click opens the Wild suit picker without playing, followed by exactly one play in the chosen suit. Branch review found no outstanding issues.
