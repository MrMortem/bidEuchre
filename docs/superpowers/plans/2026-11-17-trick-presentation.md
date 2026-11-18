# Trick presentation implementation plan

- Share a single desktop frame clock across timed tweens, ambient effects, and the deal. Keep deal movement in render transforms.
- Arm a desktop-only session hold when a trick completes. Validate acknowledgments by deal and trick, retain bot pacing as a minimum, and release the hold on pause, presentation skip, or disposal.
- Record campaign results immediately and wait for collection before showing them. Cover a long scoring chain, stale acknowledgments, and cancellation.

## Responsive bot decisions

- Capture a seat-private view, authoritative revision, actor, and pause generation under the session lock.
- Let external decisions and campaign policies run outside the lock. Validate the same position before dispatch and again before applying a reply or fallback.
- Serialize driver operations separately from table access. Queue end-of-hand observations from the committed views and await outstanding work before disposal.
- Exercise slow decisions, stale replies after redeal and pause/resume, slow observations, and shutdown.
