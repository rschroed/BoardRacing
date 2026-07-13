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

## Provisional condition model

- Heat changes continuously from requested throttle: full throttle adds `0.045` normalized heat per second and released throttle removes `0.08` per second. Intermediate throttle blends those rates.
- At `0.70` heat, maximum speed is limited to `60%` and acceleration to `50%` until the motor cools below the threshold. This penalty never requests or forces a pit stop.
- Every corner entry adds `0.015` tire wear plus `0.08` times the fraction of entry speed above the base safe speed.
- Tire wear progressively lowers the safe corner-speed margin to `75%` of its base value at fully worn. The warning state begins at `0.60` wear.
- Both conditions are normalized and clamped to `0–1`. These values are first-pass deterministic defaults, not final balance.

## Provisional pit lifecycle

- A deliberate request with a selected service latches until the racer's next start/finish crossing; duplicate requests do not create additional stops.
- At the pit line, the simulation stops track movement and owns a `0.75`-second entry, an open-ended in-service state, and a `0.75`-second exit. Car throttle is ignored throughout those states.
- In-service progress comes from an abstract Crew action. Lost or canceled input resets progress to zero without applying service or releasing the car.
- Tires resets only tire wear. Cooling resets only motor heat. Each completion increments exactly once, and later stops remain available.
- An unserved racer may continue beyond the nominal finish but remains unclassified. Completing the required service at a later pit-line crossing makes the racer finish-eligible and classifies it after pit exit.
- Rematch clears heat, wear, selection, pit phase, progress, and completed-service count for both racers.

## Crew interaction

- Each player has mirrored Tires and Cooling regions centered `190` pixels to either side of the proven Tranche 1 service center. Region half-size is `140 × 120` pixels.
- A released Crew Piece establishes the selected service. Touching and then releasing the same contact in the same region emits one pit request; dragging to another region, removal, wrong-region placement, or reacquisition cannot emit a request.
- Once the car is in service, the selected region uses the proven `0° ±15°` align-and-hold action for `1.5` seconds.
- Contact loss, cancellation, bad alignment, release, provider changes, and Board input settings changes clear in-progress service before any completion can be emitted.
- The keyboard fallback moves and rotates the Crew Piece with the existing player-specific keys, so it exercises the same region and align-and-hold adapter as Board input.

## Presentation scaffold

- The HUD exposes normalized heat and tire wear with different meter shapes, numeric values, and normal/warning/critical text states. Required-stop, selected-service, pit-phase, service-progress, lap, place, throttle, incident, winner, and rematch feedback share the same mirrored player panel.
- Each car has two stable presentation attachment regions: a rounded `H` heat badge and a square `T` tire badge. Warning adds `!`; critical adds `!!`, so the cues do not depend on color. A stateless mapper derives both levels only from immutable racer snapshots and condition rules.
- The rendered track is compressed toward the center without altering deterministic track coordinates. A presentation-only lane places cars at distinct entry, player box, and exit positions while the simulation continues to own only the pit phase.
- The Tires and Cooling panels use the exact Crew-adapter hit-region settings, converted from bottom-left screen coordinates to top-left IMGUI coordinates. They are mirrored for the two table sides and use different shapes and labels in addition to color.
- Critical heat messaging says that power is limited and leaves cooling-on-track versus a voluntary Cooling stop to the player. Nothing in the presentation requests or forces a stop.

## Questions the gate must answer

1. Do heat and wear change how players drive without requiring constant HUD reading?
2. Do players voluntarily choose different service types or timings?
3. Is the time saved by a good service decision understandable after the race?
4. Does aligning and holding during a live race remain deliberate rather than fiddly?
5. Are condition and pit states readable from both sides while cars are close or overtaking?
6. Do players regard the Crew Piece as essential rather than an extra confirmation control?

## Evidence status

- [x] Race-domain, Crew-adapter, and semantic presentation tests: 56 of 56 Edit Mode tests passed during the Issue #46 integration pass.
- [x] Runtime and Board SDK integration suite: 11 of 11 Play Mode tests passed during the Issue #46 integration pass. The keyboard provider now has a complete accelerated race trace covering mirrored service selection, deliberate pit requests, entry, service, exit, finish, and rematch reset through `RacePrototype`.
- [x] A development APK built, installed, and launched on a 1920×1080 Board during the Issue #46 presentation pass. The first capture exposed track/control-region overlap; the layout was tightened so the final geometry reserves separate track and Crew bands.
- [ ] Mouse/keyboard full-race traces.
- [ ] Complete Board Unity SDK simulator coverage. The Issue #45 pass proves simultaneous Crew selection/request, align-and-hold completion, and contact-loss safety through the production provider; full-race coverage remains for Issue #48.
- [ ] Final Android screenshot and log smoke-test record for the release-candidate tranche build.
- [ ] Two-person physical Board gate.
