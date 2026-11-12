# Table artwork layers

Request: apply the same interchangeable-layer treatment as card sprites to the table, with four separate suit PNGs and a purple flame background without suits. Keep the current corner arrangement and integrate into the C# GUI and web client.

- [x] Generate one opaque flame-only base and four transparent standalone suits, preserving the existing palette and style.
- [x] Verify transparent corners and visible suit pixels; save all five originals in assets/pixel without replacing table.png.
- [x] Compose the five images at runtime in the desktop table brush (also used by main menu), and corresponding independent web background layers.
- [x] Build and test, inspect a composite preview, rebuild Mac app, document asset positions and prompts.

Implementation choice: keep suit images independently addressable. The original 1672x941 table defines relative placement. Native composition shares a single cover transform across all layers; the web composition uses the same aspect ratio and positions so suit and flame layers stay aligned during resizing.


## Verification

- Observed the new table asset test fail before creating layers, then pass with the final five PNGs.
- Actual PNG alpha/dimensions verified: opaque base, four visible transparent suit layers confined to their respective corners, shared1672x941 canvas.
- Solution build succeeded with zero warnings/errors. Full test.sh succeeded:22 Node tests,41/41 .NET tests, installed optional engine suites and native package checks.
- Independently reviewed composition, cover transforms, alpha and packaging; no concrete bugs found.
- Rebuilt dist/osx-arm64/Bid Euchre.app successfully.
- Launched a separate temporary copy of the Mac app to preserve the running user session. Visually confirmed the layered background in the native C# main menu; screenshot: docs/previews/table-layers-native.png. Closed the temporary preview after verification.
- Preserved original table.png and all pre-existing/concurrent work.
