# Settled issue implementation progress

Worktree: /Users/wtcolflesh/.codex/worktrees/settled-spec-issues/Bid Euchre
Branch: codex/settled-spec-issues
Base: 174e987eeeaaff79997ce27b98be20bdddb823ee
Plan: ../plans/2026-11-14-settled-campaign-rules.md
Scope: ../specs/2026-11-14-settled-issue-scope.md

Baseline full solution build passed; test apphost 171/171 passed. Use test executable, not dotnet test.dll, because protocol tests launch themselves. Existing warnings: nullable assertions and obsolete TextBox.Watermark.

| Task | Issues | Status and evidence |
|---|---|---|
| 1 Catalogue/migration | 74,76,77,78,79,80,81 | Implemented d59323f; full core175/175; independent review PASS/APPROVE, no findings. Integration13f7ae2 full build passed. |
| 2 Hand readability | 62,63,64 | Implemented0dcaed5; normal/Skia44PASS each and rendered geometry checks; independent PASS/APPROVE; integrated build passed. |
| 3 Close web tables | 34 | Implemented38d5cdb plus3295df7 review fixes; independent PASS/APPROVE; browser/security/web47/47; published smoke1/1 and server lifecycle passed. |
| 4 Effect corrections | 75,83,84,86,87,88,89,90 | Implemented634509a; Core182/182 and Desktop45PASS; independent PASS/APPROVE. |
| 5 Bid restrictions | 69,71,72,73,91 | Implementeda7a424d; Core192/192; independent PASS/APPROVE. #70 excluded as unresolved. |
| 6 Ante/bonus | 67,93 | Implementation in progress. |
| 7 Gumball duplicates | 68,82 | Pending. |
| 8 Freeplay bot | 24 | Implemented59a0d92; Core205/205; independent PASS/APPROVE. |
| 9 In-match settings | 46 | Implemented4bc6b89; Desktop52PASS normal/Skia; review PASS/APPROVE with sorting P3 carried into Task10. |
| 10 Trump announcement | 61 | Implementation in progress; includes Task9 sorting retry regression. |
| 11 Score explanations | 17 | Pending. |
| 12 Exact campaign resume | 22 | Read-only architecture preparation in progress. |
| 13 Shop reroll/badges | 27 | Implementation preparation; reordered before duplicates/resume with field-preservation contract. |
| 14 Engine/setup persistence | 25 | Pending. |
| 15 Rematch/hotseat | 29 | Pending. |
| 16 Web campaign | 30 | Read-only architecture prepared; implementation pending other shared APIs. |

No newly implemented issue is closed yet. Delivery, integrated verification and final review remain required. #85,#92,#95 had already been closed as resolved design/existing behavior before implementation.

Excluded pending design: #23,#26,#56-60,#65-66,#70. #94 explicitly deferred. See scope and original issue snapshot for reasons; these are not counted as completed.

Integration13f7ae2 used a clean archive at /private/tmp/bideuchre-wave1-13f7ae2 to isolate current work. Full build0errors with8knownbaselinewarnings; Server.Tests exit0 and published smoke1/1 passed. Logs /private/tmp/bideuchre-wave1-build.log and /private/tmp/bideuchre-wave1-published.log.

Draft PR96: https://github.com/MrMortem/bidEuchre/pull/96, published through a7a424d (25 issue closures queued for future merge). No merge or actual issue closure yet. Local reviewed freeplay change59a0d92 will be included in a subsequent update.
