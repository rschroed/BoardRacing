# Tranche 1 validation record

Tranche 1 **passed on July 12, 2026**. Automated domain, fallback, control-lab, Board SDK simulator, Android deployment, and the two-person physical-hardware gate all passed at their appropriate verification levels.

## Implementation baseline

| Item | Value |
| --- | --- |
| Unity | 2022.3.62f3 |
| Board SDK | 3.3.0 |
| Piece model | Board Arcade 1.3.7 |
| Throttle steps | 25%, 50%, 75%, 100%; untouched/missing is 0% |
| Hysteresis | 8 degrees |
| Pit alignment | 0 degrees ±15 degrees |
| Pit hold | 1.5 seconds uninterrupted |
| Service centers | Player 1 `(1325, 270)`; Player 2 `(595, 810)`, aligned with the rendered bars |

## Automated result

On July 12, 2026, the Unity Edit Mode suite passed 24 of 24 tests and the Play Mode suite passed 6 of 6 tests. Coverage includes assignment validation, duplicate/missing assignments, all throttle sectors and documented fallback keys, simultaneous two-player fallback state, angular wraparound, hysteresis, persistent contact-ID tracking, required safe release, missing/ended/canceled contacts, new-ID reacquisition, duplicate and unassigned Glyphs, wrong-region warnings, simultaneous crossing contacts, settings resets, pit completion latching, rearming, release, misalignment, contact loss, control-lab startup, repeated control-lab cycles, and production-provider integration through the Board SDK simulator.

The Edit Mode results are domain and fallback evidence. The Play Mode results include Board SDK simulator evidence through the production provider. Neither substitutes for the physical interaction gate.

## Android build result

The initial Tranche 1 development APK built successfully on July 12, 2026 and remains ignored under `Builds/Android`.

| Check | Observed |
| --- | --- |
| Package | `com.wholestudios.boardracing` 1.0 (version code 1) |
| Android APIs | Minimum 33, target 33, compile 33 |
| Build type | Development/debuggable |
| Architecture | ARM64 |
| Runtime | IL2CPP |
| Board libraries | `libnativeBoardSDK.so`, `libtensorflowlite.so` |
| APK SHA-256 | `2a68822ce8d3639704887a59f1c392040f71cbc3d824b8c6c8a01c23c35133c2` |

The prior APK installed and launched successfully on the paired Board. Board Connect screenshots confirmed the complete 1920×1080 two-panel lab rendered with Board input selected, no Android-only keyboard hints, and the Player 2 panel correctly rotated for the opposite side. Both physical Ships reported `POSITIONED` in their visible pit areas, confirming the mirrored service-zone correction. Physical Arcade Pieces remained safely released at zero throttle when untouched; touched new contacts expose `RELEASE TO REARM` until released.

The latest APK adds live Ship angle, target and angular-error diagnostics, contextual alignment/hold guidance, and prominent `PIT CYCLES n / 10` counters after a physical Orange Ship check remained in `ALIGNING`. It passed 24 Edit Mode and 6 Play Mode tests before building. This exact hash was installed and launched on the paired Board; a 1920×1080 Board Connect screenshot confirmed the new counter and empty-state guidance render correctly. The subsequent physical run confirmed that Orange and Purple both complete the configured action reliably.

## Simulator checklist

Record pass/fail and observations for each item:

- [x] Both assigned Robots are recognized and map to the correct player.
- [x] Both assigned Ships are recognized and map to the correct player.
- [x] Pink and Yellow Pieces show unassigned warnings and issue no commands.
- [x] All four throttle sectors and angular boundaries pass deterministic coverage.
- [x] Car release, removal, cancellation, and recognition loss clear throttle.
- [x] Crew release, removal, cancellation, and recognition loss reset progress.
- [x] A valid place-align-hold action completes once and rearms only after exit.
- [x] Duplicate Glyph input fails safe.
- [x] Crossing and simultaneous Piece manipulation do not swap roles.
- [x] Ten simultaneous SDK-simulator cycles per player produce no stale, false, or cross-player command.

The simulator suite uses the SDK's `BoardContactSimulation` and Arcade icon assets to place, touch, rotate, move, cancel, lift, and replace contacts. Events flow through `BoardInput` and the production `BoardContactInputProvider`; this is not a domain-only mock.

## Physical hardware gate

Environment and operators:

| Field | Result |
| --- | --- |
| Date | July 12, 2026 |
| BoardOS | 2.0.3; Board Connect protocol 1 |
| APK version/build | `com.wholestudios.boardracing` 1.0; SHA-256 `2a68822ce8d3639704887a59f1c392040f71cbc3d824b8c6c8a01c23c35133c2` |
| Operator 1 | Operator A, Player 1 Orange |
| Operator 2 | Operator B, Player 2 Purple |

Required results:

| Criterion | Required | Observed |
| --- | ---: | --- |
| Car touch/release cycles per player | 10 | 10 Orange and 10 Purple reported complete during simultaneous operation |
| Pit cycles per player | 10 | 10 Orange and 10 Purple confirmed |
| Stale throttle | 0 | 0 confirmed |
| False pit completion | 0 | 0 confirmed |
| Role reassignment/cross-player command | 0 | 0 confirmed |
| Recoverable recognition interruptions | At most 1 per player | One brief unassigned Pink/Yellow warning; self-cleared |
| Developer-assisted recoveries | 0 | No intervention needed for the observed warning |
| Roles and flow understood after brief explanation | Yes | Confirmed by both operators |

Every physical criterion succeeded during simultaneous two-player operation. No threshold was changed after the run.

### Partial physical evidence

An initial physical-board photo from the diagnostic build showed both assigned Ships simultaneously latched at `PIT COMPLETE`, both hold bars at 100%, and both counters at `1 / 10`. This confirmed the Orange Ship could enter the configured alignment arc and complete the same place-align-hold cycle as Purple.

A subsequent physical-board photo and operator report show both assigned Ships at `PIT COMPLETE` with both counters at `10 / 10`; both Robots are visibly released at 0%. The user confirmed that two people performed the cycles simultaneously. The required simultaneous two-player pit-cycle count is therefore complete.

The same two operators subsequently reported completing ten simultaneous Robot touch/rotate/release cycles per player successfully. During the run, the UI briefly displayed the Pink/Yellow unassigned warning and then recovered without intervention. Because unassigned Glyphs are deliberately command-inert, this is recorded as a recoverable recognition interruption rather than a cross-player command. Both operators confirmed zero stale throttle, false pit completions, role reassignments, cross-player commands, or assisted recoveries, and confirmed that the roles and flow were understandable after the brief introduction.

### Simulator-to-hardware comparison

Simulator and physical hardware matched for Piece assignment, throttle release, pit completion, completion latching, simultaneous input, and safe command isolation. Physical hardware produced one brief unassigned Pink/Yellow warning that did not occur during the scripted simulator cycles; it self-cleared and issued no player command. The final 8-degree throttle hysteresis, 0-degree ±15-degree pit alignment, and 1.5-second hold values required no retroactive relaxation.

## Gate result

**Pass.** All eight Tranche 1 issues are complete, and Tranche 2 racing-proof work may begin.
