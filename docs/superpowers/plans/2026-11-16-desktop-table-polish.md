# Desktop table polish implementation plan

**Goal** Keep table controls attached across turns and animate scoring effects from authoritative card identities.

**Architecture** Keep C# Core authoritative and Avalonia in charge of rendering. Preserve the existing save format, text log, legal moves, visual pacing, and viewer isolation.

- [x] Profile a warmed headless table with a fixed deal. Measure update time and allocation before and after.
- [x] Add a regression for an unchanged card surviving a turn and for its interaction remaining current. Retain table, seat, hand and card controls within a deal and viewer. Reset across viewer and session boundaries.
- [x] Emit sequenced campaign scoring effect records with exact source card and seat, recipient and amount. Store current-hand records in an optional snapshot field and use the saved sequence as the presentation watermark. Consume the records in desktop score animation and expose them in the existing campaign projection.
- [x] Verify affected Core, desktop and server suites and report what headless profiling covers.

## Profile

Run `dotnet run -c Release --project tests/BidEuchre.Desktop.Tests -- --profile-table` on macOS. The headless window alternates two views of a fixed deal across an opponent pass, with motion off and caches warm. It measures table update and dispatcher work over 160 samples.

| Measurement | Before | After |
| --- | ---: | ---: |
| Median update | 4.610 ms | 0.089 ms |
| p95 update | 8.882 ms | 0.103 ms |
| UI thread allocation per update | 1,378,007 B | 14,877 B |

This scenario measures table update CPU time and managed allocations. It does not measure GPU frame pacing, bot latency, or shop startup.
