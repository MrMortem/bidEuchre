# Settled issue implementation progress

Worktree: /Users/wtcolflesh/.codex/worktrees/settled-spec-issues/Bid Euchre
Branch: codex/settled-spec-issues
Base: 174e987eeeaaff79997ce27b98be20bdddb823ee
Plan: ../plans/2026-11-14-settled-campaign-rules.md
Scope: ../specs/2026-11-14-settled-issue-scope.md

Baseline full solution build passed; test apphost 171/171 passed. Use test executable, not dotnet test.dll, because protocol tests launch themselves. Existing warnings: nullable assertions and obsolete TextBox.Watermark.

| Task | Issues | Status and evidence |
|---|---|---|
| 1 Catalogue/migration | 74,76,77,78,79,80,81 | Implemented d59323f; full core175/175; independent review PASS/APPROVE, no findings. Integration pending. |
| 2 Hand readability | 62,63,64 | Implemented, Desktop checks44 PASS plus rendered geometry/readability checks; commit/review pending. |
| 3 Close web tables | 34 | Initial38d5cdb; browser/security/web43/43. Review found stale confirmation/header-target transitions; fix round1 running with regressions. |
| 4 Effect corrections | 75,83,84,86,87,88,89,90 | Implementation started after Task1 approval. |
| 5 Bid restrictions | 69,71,72,73,91 | Pending. #70 policy excluded as unresolved. |
| 6 Ante/bonus | 67,93 | Pending. |
| 7 Gumball duplicates | 68,82 | Pending. |
| 8 Freeplay bot | 24 | Pending. |
| 9 In-match settings | 46 | Pending. |
| 10 Trump announcement | 61 | Pending. |
| 11 Score explanations | 17 | Pending. |
| 12 Exact campaign resume | 22 | Pending. |
| 13 Shop reroll/badges | 27 | Pending. |
| 14 Engine/setup persistence | 25 | Pending. |
| 15 Rematch/hotseat | 29 | Pending. |
| 16 Web campaign | 30 | Pending. |

No newly implemented issue is closed yet. Delivery, integrated verification and final review remain required. #85,#92,#95 had already been closed as resolved design/existing behavior before implementation.

Excluded pending design: #23,#26,#56-60,#65-66,#70. #94 explicitly deferred. See scope and original issue snapshot for reasons; these are not counted as completed.
