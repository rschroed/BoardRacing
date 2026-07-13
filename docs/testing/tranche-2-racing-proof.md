# Tranche 2 racing-proof gate

Tranche 2 **passed on July 13, 2026**. Automated, simulator, Android, deployed-render, and two-person physical evidence support the racing-proof gate.

## Prototype rules

- Two human players using the assigned Orange and Purple Robot Car Pieces.
- Five laps on one placeholder rounded-rectangle track.
- Four requested throttle steps from the proven Tranche 1 mapping.
- Lower throttle coasts or applies engine drag; releasing the Robot brakes.
- Dark corner sections have a safe entry speed. Unsafe entry emits a corner incident, scrubs speed, and briefly limits acceleration.
- Cars pass automatically without collision or lane input; temporary lateral separation keeps close cars visible.
- Both released Cars begin the countdown. After the results, both players touch and hold their Cars, then release them to start a rematch.

## Automated and editor checks

- [x] All Tranche 1 Edit Mode and Play Mode tests remain green.
- [x] Race-domain tests pass for track sampling, fixed-step determinism, dynamics, corner incidents, standings, five-lap finish, and rematch.
- [x] Keyboard fallback command mapping and the shared two-racer runtime path pass automated coverage; deterministic full-race and rematch traces also pass.
- [x] The main scene starts one race runtime and no automatic control lab.
- [x] At 1920×1080, the deployed scene renders the mirrored HUD, track, racers, lap, place, throttle, and corner guidance clearly.

## Board simulator and Android checks

- [x] Both Robots drive through the production Board input provider in the SDK simulator suite.
- [x] Release, removal, cancellation, recognition loss, and reacquisition remain fail-safe in automated coverage.
- [x] A staged lead change shows separate cars during the automatic overtake, confirmed by the physical-test pass.
- [x] Clean and unsafe corner entries give distinct feedback, confirmed by the physical-test pass.
- [x] The development APK passes the established API 33, ARM64, IL2CPP, package, and Board-library inspection.

## Current implementation evidence

- Edit Mode: 33 of 33 tests passed, comprising the original 24 and 9 deterministic race tests.
- Play Mode: 7 of 7 tests passed, comprising the original 6 and the direct two-racer runtime integration check.
- APK: `com.wholestudios.boardracing` 1.0, development/debuggable, minimum/target/compile API 33, ARM64, IL2CPP, with Board SDK and TensorFlow Lite native libraries.
- APK SHA-256: `dd67826720cf43c0053450ba06d7656c00705cc48d504ec7426763fe783ee8fd`.
- The APK installed and launched on the paired Board. A 1920×1080 screenshot confirmed the central track, two distinct racers, start line, and mirrored player HUD. Warning-level logs contained platform/vendor messages but no Unity exception or Board Racing failure.

This is simulator and single-device smoke evidence only. It does not satisfy the staged overtake, corner-operation, full keyboard race, or two-person physical criteria below.

## Physical hardware gate

Record date, BoardOS, Board Connect, APK version/hash, and both operators before checking results.

| Criterion | Required | Observed |
| --- | ---: | --- |
| Complete five-lap races | 3 | Passed; operator-reported |
| Successful rematches | At least 1 | Passed; operator-reported |
| Stale or cross-player commands | 0 | 0 reported |
| Correct lap, standings, finish, and restart | Every race | Passed; operator-reported |
| Clean corner demonstrated by each player | Yes | Passed; operator-reported |
| Unsafe slowdown demonstrated by each player | Yes | Passed; operator-reported |
| Automatic overtake readable from both sides | Yes | Passed; operator-reported and video-supported |
| Leader, lap, mistake, winner, and rematch understood after one explanation | Yes | Passed; operator-reported |
| Both players agree throttle choices affected the result | Yes | Passed; operator-reported |
| Rematch accepted without developer recovery | Yes | Passed; operator-reported |

### Physical evidence

The repository owner posted a two-person physical-test pass and video in [Issue #40](https://github.com/rschroed/BoardRacing/issues/40#issuecomment-4960091910). The video visibly shows both assigned Robot Pieces being operated on the Board, both digital cars active at different track positions, divergent lap progress (`5 / 5` and `3 / 5` in the captured frame), correct first/second standings, and the mirrored two-sided HUD.

The clip corroborates simultaneous physical control and readable race state. Criteria that require observing the complete sequence across three races—rematch count, every finish/restart, corner demonstrations, player understanding, and the absence of developer recovery—are recorded from the owner's explicit operator attestation that the two-person physical test passed rather than inferred from a single extracted frame.

## Gate decision

**Pass.** The five-lap race is understandable and competitive using the Car Pieces alone. Tranche 3 pit-crew proof may begin. Track geometry, tuning, and presentation remain prototype quality and may change in response to pit-strategy playtests.
