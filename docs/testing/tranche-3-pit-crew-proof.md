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

- A deliberate Call Pit request latches without a preselected service until the racer's next start/finish crossing; duplicate requests do not create additional stops.
- At the pit line, the simulation stops track movement and owns a `0.75`-second entry, an open-ended in-service state, and a `0.75`-second exit. Car throttle is ignored throughout those states.
- In-service progress comes from an abstract Crew action. Lost or canceled input resets progress to zero without applying service or releasing the car.
- Tires resets only tire wear. Cooling resets only motor heat. Each completion increments exactly once, and later stops remain available.
- An unserved racer may continue beyond the nominal finish but remains unclassified. Completing the required service at a later pit-line crossing makes the racer finish-eligible and classifies it after pit exit.
- Rematch clears heat, wear, selection, pit phase, progress, and completed-service count for both racers.

## Crew interaction

- Each player has one mirrored Call Pit region centered on the proven Tranche 1 service center with the same half-size as a repair region. The repair regions replace that central target within the same player area once the car is parked, so three competing targets are never active together.
- A request requires a safely armed Crew Piece to be observed outside Call Pit, move inside, and release there. A Piece initially or newly recognized inside the region, a held contact, wrong-region placement, removal, or settings reset cannot emit a request.
- Requested and entering racers carry no selected service. Once the car is in service, mirrored Tires and Cooling regions centered `190` pixels to either side of the service center activate with `140 × 120`-pixel half-size.
- Moving into Tires or Cooling selects that repair only while parked. The selected region uses the proven `0° ±15°` align-and-hold action for `1.5` seconds; changing regions before completion resets progress and changes the choice.
- Contact loss, cancellation, bad alignment, release, provider changes, and Board input settings changes clear in-progress service before any completion can be emitted and leave the car parked.
- After exit, Call Pit requires a fresh outside-to-inside released placement; a Ship left over a former repair region cannot automatically request another stop when the UI changes.
- The keyboard fallback moves, touches, and rotates the Crew Piece with the existing player-specific keys, so it exercises the same Call Pit and repair adapter as Board input.

## Presentation scaffold

- The HUD exposes normalized heat and tire wear with different meter shapes, numeric values, and normal/warning/critical text states. Required-stop, selected-service, pit-phase, service-progress, lap, place, throttle, incident, winner, and rematch feedback share the same mirrored player panel.
- Each car has two stable presentation attachment regions: a rounded `H` heat badge and a square `T` tire badge. Warning adds `!`; critical adds `!!`, so the cues do not depend on color. A stateless mapper derives both levels only from immutable racer snapshots and condition rules.
- The rendered track is compressed toward the center without altering deterministic track coordinates. A presentation-only lane places cars at distinct entry, player box, and exit positions while the simulation continues to own only the pit phase.
- While racing, one Call Pit panel per player uses the exact adapter hit region. Once parked, it is replaced by the exact Tires and Cooling repair regions. All targets are converted from bottom-left screen coordinates to top-left IMGUI coordinates, mirrored for the two table sides, and reinforced with different shapes and labels in addition to color.
- Critical heat messaging says that power is limited and leaves cooling-on-track versus a voluntary Cooling stop to the player. Nothing in the presentation requests or forces a stop.

## First-pass balance evidence

The committed deterministic matrix uses the real five-lap placeholder track, a `1/60`-second step, the production condition and pit defaults, and a `1.5`-second service hold. Entry and exit are `0.75` seconds each, so a completed stop owns at least three seconds before acceleration back to race speed. Absolute times are regression baselines, not target player times.

| Trace | Plan | Finish | Pit entry | Incidents | Result |
| --- | --- | ---: | ---: | ---: | --- |
| Managed half throttle | Tires after lap 1, entering after lap 2 | 101.31s | 39.38s | 0 | Wins by 1.19s |
| Managed half throttle | Cooling after lap 2, entering after lap 3 | 102.50s | 58.87s | 5 | Heat service has no value when heat is managed |
| Sustained full throttle | Tires after lap 1 | 85.88s | 27.85s | 20 | Loses by 9.13s |
| Sustained full throttle | Cooling after lap 2 | 76.75s | 45.85s | 20 | Heat reset changes the result |
| Optional sustained control | Timed Cooling | 76.75s | 45.85s | 20 | Beats no stop by 5.11s despite stop cost |
| Optional sustained control | No stop | 81.86s | — | 20 | Heat penalty persists |
| Optional managed control | Unneeded Cooling | 102.51s | 39.38s | 5 | Loses to no stop by 3.46s |
| Optional managed control | No stop | 99.05s | — | 5 | Avoids an irrelevant service |
| Managed tire timing | Earlier Tires | 101.31s | 39.38s | 0 | Beats the late stop |
| Managed tire timing | Late Tires | 101.55s | 78.60s | 1 | Mistiming costs 0.24s |

The initial values are retained because the matrix already answers the balance question with the smallest rule set:

- Full throttle adds `0.045` heat per second, released throttle removes `0.08`, and the `0.70` threshold limits maximum speed to `60%` and acceleration to `50%`. Half throttle manages heat while full throttle creates a strong but recoverable reason to choose Cooling.
- Each corner adds `0.015` wear plus unsafe-speed wear at `0.08`; worn tires reduce the safe margin toward `75%` at full wear. This makes Tires beat Cooling in the managed profile without making Tires universally dominant.
- The three-second stop floor is large enough that an irrelevant service loses, while a relevant Cooling service can repay the cost. No production constant was changed merely to widen deterministic margins.
- With one service required, a no-service full-throttle control remains unclassified after traveling more than six laps; it cannot turn ignoring the Crew Piece into a winning shortcut.

## Tranche 3 playtest protocol

### Simulator and keyboard

1. Run the complete two-player keyboard trace through Call Pit placement/release, request, entry, parked service selection, aligned hold, exit, five-lap finish, and rematch; then repeat the production provider flow with two simultaneous SDK simulator Crew contacts.
2. Exercise different in-box Tires and Cooling choices, switching before completion, one lost-contact reset during service, a wrong-region placement, Call Pit rearming, reacquisition, and a rematch. Record any invalid transition, duplicate request/completion, stale progress, or cross-player command as a failure.
3. Review captures from both table orientations at normal, warning, critical, requested, in-service, service-complete/exit, finished, and rematch states. Heat/tire and player/service identity must remain readable without color alone.

### Android smoke test

1. Build the exact candidate commit, install and launch it with Board Connect, and record app ID, BoardOS, resolution, build hash, screenshot, and warning-or-higher logs.
2. Confirm a 1920×1080 frame exposes both mirrored HUDs, all four exact Crew regions, both on-car cues, the lane and boxes, and no editor-only provider hint.
3. On-device, complete one service of each type and force one contact-loss recovery. Any Unity exception, stuck pit phase, forced stop, or throttle ownership during entry/service/exit fails the smoke test.

### Two-person physical gate

1. Two players complete three five-lap races, swapping table sides after the first. Explain the controls and mandatory-stop rule once; after `GO`, do not name a preferred service or pit lap.
2. For every racer record service type, request lap, pit-entry lap, finish order, peak condition cues noticed, assistance, false/lost contacts, and the player's stated reason for the decision.
3. In the second race, deliberately remove one Crew Piece during service and require the player to recover from the visible prompt. In the third, allow both players to revise strategy based only on prior results and live feedback.

“Voluntary different decisions” passes when at least one race contains a different service choice or pit timing chosen without a post-start prompt, and at least one player changes a later plan for a stated heat, tire, opponent, or time-loss reason. “Crew Piece is essential” passes when both players independently select, request, and complete every required stop, recover the deliberate loss without the developer operating a Piece, and can identify a race consequence caused by their Crew decision. Any automatic/forced stop, skipped physical Crew action, or unclassified no-stop win fails the gate.

## Questions the gate must answer

1. Do heat and wear change how players drive without requiring constant HUD reading?
2. Do players voluntarily choose different service types or timings?
3. Is the time saved by a good service decision understandable after the race?
4. Does aligning and holding during a live race remain deliberate rather than fiddly?
5. Are condition and pit states readable from both sides while cars are close or overtaking?
6. Do players regard the Crew Piece as essential rather than an extra confirmation control?

## Evidence status

- [x] Race-domain, Crew-adapter, semantic presentation, and balance-trace coverage: 59 of 59 Edit Mode tests passed at the Issue #48 candidate commit.
- [x] Runtime and Board SDK integration coverage: 12 of 12 Play Mode tests passed. The keyboard provider has a complete accelerated strategy race through mirrored selection, deliberate request, entry, service, exit, finish, and rematch.
- [x] Complete Board SDK simulator recovery matrix through the production provider: independent simultaneous service, concurrent Car control, wrong-region reset, contact cancellation, new-ID release gating, settings reset, reacquisition, and exactly-once recovery all passed without cross-player commands.
- [x] Final Android inspection and paired-Board smoke test: the exact 24 MB candidate is API 33, ARM64, IL2CPP, contains one copy of every expected Board/runtime library, installs, launches, and renders the complete mirrored 1920×1080 presentation without a Unity exception or Board Racing failure.
- [x] Exact candidate commit, test counts, APK hash, package inspection, Board environment, screenshot review, logs, and hardware-only carryover are recorded in the [Tranche 3 simulator and Android validation](tranche-3-simulator-android-validation.md).
- [ ] Two-person physical Board gate in [Issue #49](https://github.com/rschroed/BoardRacing/issues/49).
