# Screen artwork preload

1. Measure startup and first shop, collection and boss arena builds in fresh headless Skia processes. Estimate decoded bitmap bytes as width × height × 4, counting each source bitmap once.
2. Use a shared, thread-safe bitmap cache with one background preloader. Resolve resource names through the same code the renderer uses.
3. Warm campaign shop stock while the result or Continue screen is visible, warm the next table from the ante, and await its artwork before the table opens. Load only the selected collection category after navigation.
4. Repeat the profile and verify that startup still decodes only its existing assets and that preloaded destination builds perform no duplicate decoding.

## Measurements

Release build, Apple Silicon, headless Skia. Each sample uses a fresh process with an empty decoded bitmap cache. Medians use three samples per destination and nine for startup. Startup runs from main window construction to the first captured frame. Destination measurements cover construction, layout, and first captured frame. The boss sample isolates the arena backdrop.

| Scenario | Cold build | Build after preload | Background preload | Decoded bitmap cache after screen |
| --- | ---: | ---: | ---: | ---: |
| Startup | 322.6 ms | 333.7 ms after change | None | 32.09 MiB (7 bitmaps) |
| Boss backdrop | 27.4 ms | 9.2 ms | 19.7 ms | 38.09 MiB |
| Shop | 95.0 ms | 48.1 ms | 48.5 ms | 48.99 MiB |
| Collection, Joker tab | 99.3 ms | 62.2 ms | 41.2 ms | 45.49 MiB |

The startup samples overlap. Startup loaded the same seven bitmaps in both builds and does not launch background preloading. The warmed destination builds decoded zero additional bitmaps. Preload time still counts toward click-to-screen time when the player arrives before warming finishes. Cache size estimates 32-bit pixels; it excludes object overhead, GPU textures, and process memory.

Run the profile from the workspace with:

```sh
BIDEUCHRE_RENDER_TESTS=/tmp dotnet run -c Release --project tests/BidEuchre.Desktop.Tests -- --profile-artwork shop --preload
```

Use `collection` or `boss` in place of `shop`. Omit `--preload` to measure a synchronous cold destination build.
