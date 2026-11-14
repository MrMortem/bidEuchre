# Task 1 report

Status: complete. Implementation commit: `d59323f` (`Retire campaign items and preserve saved feature identities`). Report is committed separately after that implementation commit.

## Requirements covered

Read the task-1 brief, only the plan's Global Constraints section, and the complete snapshot issue bodies for #74, #76, #77, #78, #79, #80, and #81.

- Active Sigils are exactly Bear, Eagle, Fish, Spider, Frog, Duck, Vulture, and Chameleon. Surviving numeric values remain 1, 2, 3, 4, 6, 8, 9, and 10. Coin, Snake, Owl, and Peacock are removed from enums, catalogue, effects, visibility, acquisition, and active artwork metadata.
- Active Tarots are exactly Festival, Stone mason, Prospector, Devilish Deal, Gambler, Traitor, Astronomer, Ectoplasm, and Taxes. Surviving internal keys, numeric IDs (4, 5, 6, 7, 8, 11, 12, 14, 15), serialized IDs, and asset paths are preserved. LovePotion, Nightshade, CloverTonic, CrystalElixir, and Glassblower are retired. Enum-driven Vulture generation therefore uses only the nine approved Tarots; catalogue-driven shops and developer acquisition use the same active set.
- Saves migrate retired Sigils on deck cards to null, remove retired held Sigils, remove all five retired Tarots from held inventory and every seat's GeneratedTarot, and remove retired shop offers whether sold or unsold. Existing Emerald/Stinky and Merchant/Beggar migration remains intact. Unknown/malformed IDs remain invalid; load does not rewrite the source save.
- Display names are Safety Net, Festival, Stone mason, Ectoplasm, and Taxes. Chameleon Sigil is unchanged. Taxes payout logs use Taxes. Developer catalogue rows now show display names (narrow scope extension approved by controller), while both internal keys and names remain accepted for surviving entries.
- Renamed Tarots retain one-target Wild/Stone/Bouncy enhancement behavior. Taxes retains zero targets and min(wallet, 20) gain; Safety Net retains bid 2. All nine Gumballs, including High/Low, remain unchanged.
- README current campaign rules list exact active rosters/counts and explain retirement migration. Active placeholder manifest removes retired entries and changes display names, preserving source artwork files and historical archives.

## Files

Production:
- `src/BidEuchre.Core/CampaignInventory.cs`
- `src/BidEuchre.Core/FeatureCatalog.cs`
- `src/BidEuchre.Core/TarotEffects.cs`
- `src/BidEuchre.Core/CampaignEffects.cs`
- `src/BidEuchre.Core/GameEngine.Campaign.cs`
- `src/BidEuchre.App/CampaignStore.cs`
- `src/BidEuchre.App/CampaignShop.cs`
- `src/BidEuchre.App/DeveloperConsole.cs`

Tests:
- New `tests/BidEuchre.Tests/SettledCatalogueTests.cs`: exact roster/IDs/names, all saved retirement locations, unknown IDs and purchases, renamed effects/logs, Taxes edge wallets, Safety Net bidding.
- `tests/BidEuchre.Tests/Program.cs`: register four new behavioral tests.
- `tests/BidEuchre.Tests/DeveloperConsoleTests.cs`: visible names, active Festival/Bear fixtures, retired/unknown developer acquisition rejection.
- Updated obsolete active-item fixtures/counts in CampaignEffectsTests, CampaignIntegrationTests, CampaignItemPreviewTests, CampaignJokerScoringTests, CampaignModelTests, CampaignReviewRegressionTests, CampaignSaveTests, and CampaignShopTests.

Documentation/art:
- `README.md`
- `assets/pixel/campaign-placeholders/manifest.json`

No Desktop or Server paths, source-art archives, other agents' files, or task planning files were committed.

## Verification

All commands ran from `/Users/wtcolflesh/.codex/worktrees/settled-spec-issues/Bid Euchre`.

Build command (used for red and green builds):

```sh
env DOTNET_ROOT=/Users/wtcolflesh/.dotnet DOTNET_CLI_HOME=/private/tmp/bideuchre-goal-dotnet-home DOTNET_CLI_TELEMETRY_OPTOUT=1 MSBUILDDISABLENODEREUSE=1 /Users/wtcolflesh/.dotnet/dotnet build tests/BidEuchre.Tests/BidEuchre.Tests.csproj --disable-build-servers -m:1 -p:UseSharedCompilation=false
```

Final result: exit 0, build succeeded, 0 warnings, 0 errors. Final incremental build log: `/private/tmp/task1-verified-build.log`. Earlier full recompilation emitted seven existing CS8714 nullable-assert warnings in CampaignCatalogueTests; no new warnings were introduced.

Behavioral red commands:

```sh
env DOTNET_ROOT=/Users/wtcolflesh/.dotnet tests/BidEuchre.Tests/bin/Debug/net10.0/BidEuchre.Tests --filter 'Settled campaign'
env DOTNET_ROOT=/Users/wtcolflesh/.dotnet tests/BidEuchre.Tests/bin/Debug/net10.0/BidEuchre.Tests --filter 'Developer console manages Tarot'
```

- Initial settled tests: 0/3 passed, exit 1. Expected failures: extra active Sigils, migration unable to load retired saved content, retired purchase accepted.
- Developer catalogue regression: 0/1 passed, exit 1, specifically failed because Festival was not displayed. Then changed catalogue rows from Key to Name.
- Settled green tests: 4/4 passed, exit 0.
- Developer catalogue green test: 1/1 passed, exit 0.

Full suite command:

```sh
env DOTNET_ROOT=/Users/wtcolflesh/.dotnet tests/BidEuchre.Tests/bin/Debug/net10.0/BidEuchre.Tests
```

Final result: **175/175 passed, exit 0**, including self-spawning protocol/Arena/session integrations and Vulture generation for humans/bots across seeded runs. Final log: `/private/tmp/task1-final-suite.log`.

Initial full suite after fixture replacements was 173/174: `Campaign review Sticky copies preserve underlying cards` failed because replacing Coin with Bear changed the trick winner. Corrected that fixture to Fish to retain its intended winning-card copy. Final full run includes the corrected fixture and the additional named-effects test; all pass. Last-trick money fixture now uses Golden, with hand-calculated cumulative wallet expectations of 10 after five tricks and 14 after six, preserving Deep Digger/Last Laugh timing coverage.

`git diff --check`: exit 0. Manual JSON/catalogue consistency check confirmed exact 8-Sigil and 9-Tarot manifest rosters and matching IDs, keys, and display names; Safety Net's legacy planet artwork ID/path remain preserved. Self-review inspected all owned diffs and confirmed no opponent bidding tuning or unrelated rule changes.

## Concerns and coordination

No unresolved Task 1 concerns. Desktop fixture enum retirements were communicated before the shared enum edit; Desktop agent owns those fixture updates. Controller was notified when core builds/tests were released. Full-solution/Desktop/Server verification is left to controller and those task owners. No push, merge, or GitHub edits were performed.
