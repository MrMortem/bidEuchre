# Issue 21: local web server security

**Goal:** Remove HTTP process execution and enforce the documented local-only web interface.

**Approach:** Disable the engine-loading endpoint unconditionally; keep external engines in the desktop client. Reject non-loopback peers and hosts and foreign browser origins, including reads. Require JSON for API mutations so browser forms cannot bypass origin checks. Local hot-seat viewers remain shared by design; this is not a network multiplayer service.

**Session lifetime:** Keep at most 32 web tables and expire tables after 30 minutes without an individual-table request. Listing tables does not keep them alive. Dispose expired sessions and their bots in a periodic background service. Keep these limits in a server-specific registry so desktop behavior is unaffected.

**Files:** Server Program.cs, new LocalRequestPolicy.cs and WebSessions.cs, appsettings.json, web engine panel, README.md, tests/server/security.test.cjs, tests/BidEuchre.Server.Tests, solution and test runner.

- [x] Add HTTP regression tests and observe failure for engine loading, hostile hosts/origins, capacity and expiry. JSON-form rejection also exercises DELETE, which has no model binder to provide that check.
- [x] Implement request policy and disabled engine loading; explain desktop engine loading in the browser.
- [x] Implement bounded, expiring web sessions and background disposal.
- [x] Run server regressions and the repository test runner; inspect the diff for unrelated edits.

**Review focus:** IPv6 and mapped loopback peers; forged forwarding headers; cross-port origins and opaque origins; parallel session creation; idle disposal without any incoming requests; existing checkout edits must be preserved.

## Verification and review

The initial HTTP tests exposed six failures in the original server. All eight HTTP tests passed after the initial patch, and the repository test runner passed. Independent review found same-site requests without Origin were still accepted, and HTTP expiry tests did not prove actual disposal. Added a failing same-site request regression, tightened Fetch Metadata validation, and added a direct test retaining a started bot session until the background service disposes it. A mutation check confirmed that test fails if the reaper is never started. Direct policy tests also cover IPv6 and mapped IPv4 loopback.

Decision: keep local hot-seat viewers shared and document that boundary. This removes network exposure without adding an unrelated remote multiplayer authentication system. External executable loading is disabled over HTTP unconditionally rather than adding an unsafe opt-in flag. Existing unrelated campaign and art changes are preserved and excluded from this change.

Final verification: `./test.sh` passed after both review fixes, including all eight HTTP regressions, direct IPv4/IPv6 access-policy tests, background bot-session disposal, C++/Python/Prolog engine tests, macOS packaging, core/application tests, and desktop rendering tests. Build reported zero warnings and errors; `git diff --check` was clean.
