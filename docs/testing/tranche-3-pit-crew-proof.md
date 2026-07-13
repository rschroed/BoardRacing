# Tranche 3 pit-crew proof

Tranche 3 is **in progress**. This record defines the provisional strategy hypothesis before condition mechanics, pit behavior, presentation, balance, simulator coverage, and physical testing are complete.

## Prototype hypothesis

- Motor heat and tire wear create two legible reasons to change driving behavior.
- Tires and Cooling are mutually exclusive services with different race consequences.
- Every racer completes at least one deliberately requested stop in a five-lap race.
- Critical heat reduces performance but never automatically requests or forces a pit stop.
- A racer that crosses the nominal finish without the required service remains unclassified until it services and later crosses start/finish.
- The existing Crew Piece align-and-hold action is reused rather than adding a new physical gesture.
- Repair, AI, extra tracks, final art, and production balance remain outside this gate.

## Deterministic boundary

The race simulation consumes only player-scoped throttle, service-selection, pit-request, and service-action intent. Raw Board contacts, screen-space Crew positions, and visual effects remain outside the simulation. Immutable racer snapshots expose normalized condition values and pit state for presentation.

## Questions the gate must answer

1. Do heat and wear change how players drive without requiring constant HUD reading?
2. Do players voluntarily choose different service types or timings?
3. Is the time saved by a good service decision understandable after the race?
4. Does aligning and holding during a live race remain deliberate rather than fiddly?
5. Are condition and pit states readable from both sides while cars are close or overtaking?
6. Do players regard the Crew Piece as essential rather than an extra confirmation control?

## Evidence status

- [ ] Race-domain condition and pit-lifecycle tests.
- [ ] Mouse/keyboard full-race traces.
- [ ] Board Unity SDK simulator coverage.
- [ ] Android build, deployment, screenshot, and log smoke test.
- [ ] Two-person physical Board gate.
