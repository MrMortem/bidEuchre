# Collection Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans for implementation and a fresh reviewer at completion.

**Goal:** Browse all game items from a Collection button on the native main menu.

**Architecture:** A desktop CollectionPage reads the existing core catalogs and
uses the game's existing artwork. MainWindow owns navigation; no save state is involved.

**Tech stack:** C#, Avalonia, existing headless desktop test executable.

**Spec:** `docs/superpowers/specs/2026-11-14-collection-design.md`

**Global constraints:** Read-only catalog, every item visible, existing art and
fallbacks, scrollable at small sizes and enlarged UI scale.

## Tasks

- [x] Add headless integration tests in `tests/BidEuchre.Desktop.Tests/CollectionTests.cs`
  and register them in `Program.cs`. Exercise the menu entry, every category,
  catalog completeness and descriptions, scrolling/reset, and back navigation.
  Run before implementation to confirm the missing API fails.
- [x] Add `CollectionPage.Build(Action back, PixelMotion motion)` in
  `src/BidEuchre.Desktop/CollectionPage.cs`. Use a category sidebar, wrapping item
  gallery, and independent vertical scrolling. Render enhancement examples with
  `PixelArt.CardContent`; render gumballs through `GumballMachine.Ball`.
- [x] Add `openCollection` after `openTable` to `MainMenu.Build`, add its menu
  entry, and connect `MainWindow.ShowCollection`. Keep every menu entry reachable
  at minimum window sizes. Update the README with the new entry point.
- [x] Run desktop headless tests and `./test.sh`, inspect the native UI, and
  request an independent review. Resolve any functional issues before completion.

## Review focus

- Missing gumball assets must use existing fallback visuals without exceptions.
- Every item must remain readable/reachable in a narrow, short logical viewport.
- Scroll offsets must reset when switching from a longer category to a shorter one.
- Keyboard scrolling must not be intercepted by menu arrow navigation.
- No catalog navigation may create or alter a campaign save.

## Verification

The new test initially failed on the missing CollectionPage API. Native visual
inspection found a null foreground hiding the category labels; a regression
test failed before the foreground inheritance fix. Compact-menu testing found
that viewer padding was excluded from its scroll extent; moving the spacing to
the content margin made the final Settings button fully reachable.

The final desktop test executable passed, including all catalog categories at
1280×900 and 653×453, real pointer clicks, wheel scrolling, PageDown/End/Home/Down,
category reset, visible labels, and menu navigation. Native gallery artwork and
enhancement scrolling were inspected. Independent final review found no
remaining actionable issues.

Full `./test.sh` passed on 2026-11-14 (including 152/152 core/app/protocol tests,
desktop UI, web/server, and installed bot suites). Verification used
`AVALONIA_TELEMETRY_OPTOUT=1`, `UseSharedCompilation=false`,
`DOTNET_CLI_USE_MSBUILD_SERVER=0`, `MSBUILDDISABLENODEREUSE=1`, and
`NuGetAudit=false` for the local test environment.
The solution build reported seven existing nullable-generic warnings in
`CampaignCatalogueTests.cs` and zero errors; the desktop-only build had no warnings.
