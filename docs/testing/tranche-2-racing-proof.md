# Tranche 2 racing-proof gate

Tranche 2 remains **in progress** until the automated, simulator, Android, and two-person physical checks below are recorded. This document is the procedure and pending record; it is not evidence that hardware testing has occurred.

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
- [ ] Keyboard fallback completes a simultaneous two-player race and rematch.
- [x] The main scene starts one race runtime and no automatic control lab.
- [x] At 1920×1080, the deployed scene renders the mirrored HUD, track, racers, lap, place, throttle, and corner guidance clearly.

## Board simulator and Android checks

- [x] Both Robots drive through the production Board input provider in the SDK simulator suite.
- [x] Release, removal, cancellation, recognition loss, and reacquisition remain fail-safe in automated coverage.
- [ ] A staged lead change shows separate cars during the automatic overtake.
- [ ] Clean and unsafe corner entries give distinct feedback.
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
| Complete five-lap races | 3 | Pending |
| Successful rematches | At least 1 | Pending |
| Stale or cross-player commands | 0 | Pending |
| Correct lap, standings, finish, and restart | Every race | Pending |
| Clean corner demonstrated by each player | Yes | Pending |
| Unsafe slowdown demonstrated by each player | Yes | Pending |
| Automatic overtake readable from both sides | Yes | Pending |
| Leader, lap, mistake, winner, and rematch understood after one explanation | Yes | Pending |
| Both players agree throttle choices affected the result | Yes | Pending |
| Rematch accepted without developer recovery | Yes | Pending |

## Gate decision

Pending. Do not mark Tranche 2 passed until every physical criterion succeeds. Create focused follow-up issues for failures rather than silently relaxing thresholds.
