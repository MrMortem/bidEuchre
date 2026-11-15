# Integrated verification

Tested commit: `93263a1` on `codex/settled-spec-issues`.
Clean archive: `/private/tmp/bideuchre-final-93263a1`.
Full log: `/private/tmp/bideuchre-final-93263a1-tests.log`.

The committed source was exported with `git archive`. Existing Playwright dependencies were linked into the disposable archive; no shared project build output was used. The original checkout and its untracked artwork were untouched.

Command, run from the archive:

```sh
env BIDEUCHRE_DOTNET=/Users/wtcolflesh/.dotnet/dotnet   DOTNET_ROOT=/Users/wtcolflesh/.dotnet   DOTNET_CLI_TELEMETRY_OPTOUT=1   DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1   MSBUILDDISABLENODEREUSE=1 UseSharedCompilation=false ./test.sh
```

Exit status: **0**.

| Check | Result |
|---|---|
| Full .NET 10 solution build | Passed, 0 errors; 8 existing warnings |
| Core/App tests | 246/246 passed, including available C++ and Prolog process integration |
| Desktop checks | 78 PASS, 0 failures |
| Server lifecycle, campaign API, persistence and isolation groups | 10 passed |
| Web unit tests | 28/28 passed, 0 skipped |
| HTTP security, API, real browser and published-server checks | 40/40 passed, 0 skipped |
| C++ engine unit, transcript and recovery checks | Passed |
| Prolog engine unit, transcript and recovery checks | Passed; 12 unit tests |
| macOS app packaging/signing | Passed, including shipped-resource tampering rejection and rebuild |

The 40 server-side Node checks include 14 campaign browser journeys, 10 freeplay browser regressions, 5 campaign HTTP checks, 9 security checks and 2 published-server asset checks. Real journeys cover independent browser identities, mid-match restart/continue, earned results, full storage Buy & Use, target selection, duplicate pulls, rerolls, sales, stale state and accepted-but-lost responses. Rare command/visibility UI cases also use explicit public DTO fixtures; server tests separately exercise restored pending choices against Core.

Optional Python CFR standard-library tests were skipped because the script found no Python 3.10+ runtime. Optional PyTorch tests/process integration were skipped because no qualifying PyTorch environment was installed. These were reported skips, not passing checks. No Windows runtime validation is claimed.

The eight build warnings predate this final integration: seven nullable assertions in CampaignCatalogueTests and the obsolete TextBox.Watermark use in DeveloperConsoleView. Double Down uses an existing generic card-back placeholder.

Earlier task validation also inspected actual desktop/compact browser captures and focused Avalonia Skia renders for hand layout, settings, contract announcements, shop, hotseat, score history and resume. The complete final script above used the normal headless Desktop test mode.

The subsequent96eba61 change corrects only the current README Joker count from31 to32; its scoped review and whitespace checks passed. No product code changed after the full run. Final report-only commits likewise do not alter tested behavior.
