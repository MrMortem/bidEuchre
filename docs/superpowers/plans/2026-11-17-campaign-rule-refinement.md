# Campaign rule refinement

1. Record a fixed trick with overlapping on-play and on-win effects. Check the public event order and RNG state after each play, then resume a saved copy mid-trick and compare the settled state.
2. Share effective rank, suit and trick power between engine and bot. Cover Bear-created bowers, Ace wrapping, declared Wild Jacks and Low contracts.
3. Split effect resolution into named stages. Keep the order of activations and mutations explicit, and retain the existing checkpoint fields and feature identifiers.
4. Run rules, save and resume, server, and desktop regressions.
