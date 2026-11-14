# Retire Stinky and Emerald

Remove both enhancements and the Merchant/Beggar Tarots that apply them. The
collection and shops derive their contents from the catalog, so they will expose
eight enhancements and fourteen Tarots. Preserve remaining numeric enum IDs.

- [x] Remove retired enums, catalog entries, scoring/dealing behavior, and random-generation paths.
- [x] Migrate version-2 saves before deserialization: clear the retired card enhancements and remove retired Tarots from held/generated inventories and shop offers. Preserve progress and unknown-ID rejection.
- [x] Update shop behavior, active artwork exports/manifests, and current documentation.
- [x] Verify random pools, save round-trips, remaining effects, and Collection through the full test suite and independent review.

Historical design documents and source artwork remain as provenance; runtime
assets and generators describe only active features.

Verification: catalogue, shop random-pool, and save migration tests were observed
failing before their corresponding changes. Tests now cover all surviving
enhancements, human/bot Tarot generation across sparse enum IDs, copy overlays,
save preservation and round-trips, unknown IDs, and malformed duplicate JSON
properties. The Collection UI tests pass with the reduced catalog at both normal
and compact sizes. The artwork builder exported eight backgrounds and verified
opaque pixels and readable centers.

Full `./test.sh` passed on 2026-11-14, including desktop, web/server, and installed
bot suites. Independent review found no remaining actionable issues. Build/test
environment used the same local telemetry/compilation options as the Collection
verification. No changes were committed automatically.
