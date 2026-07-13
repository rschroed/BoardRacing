# Tranche 3 simulator and Android validation

The Tranche 3 software-readiness gate **passed on July 13, 2026**. The complete automated suites, Board SDK simulator recovery matrix, Android package inspection, and paired-Board launch/render smoke test passed. This record does not pass the two-person physical gate in Issue #49.

## Candidate identity

| Item | Recorded value |
| --- | --- |
| Runtime source commit | `778d43d` |
| Unity | `2022.3.62f3` |
| Board Unity SDK | `3.3.0` |
| APK | `Builds/Android/BoardRacing-development.apk` |
| APK size | `25,262,713` bytes |
| APK SHA-256 | `ade133269871e4da50baa7fe2c321c54fe2bb214a1f625a70e57e273199ebeae` |
| Package | `com.wholestudios.boardracing` |
| Version | code `1`, name `1.0` |

The APK was rebuilt after the runtime source commit was created, then that exact local artifact was inspected, installed, launched, and captured. Documentation-only commits after the candidate do not change the runtime artifact.

## Automated results

| Suite | Passed | Failed | Duration |
| --- | ---: | ---: | ---: |
| Edit Mode | 65 | 0 | 0.57s |
| Play Mode | 13 | 0 | 32.16s |
| Total | 78 | 0 | 32.73s |

The final suites ran at the candidate source commit after the physical Call Pit follow-up from #49. Edit Mode covers the deterministic race, condition, pit, Crew-adapter, normalized phase progress, presentation-pose boundaries, and strategy-balance contracts. Play Mode covers runtime startup, the shared two-player provider path, Call Pit and parked repair choices, a complete accelerated keyboard race through service/finish/rematch, and the production Board provider through the Board SDK simulator—including both same-contact sliding and lift/place reacquisition followed by a safe release and deliberate touch/release.

## Board SDK simulator matrix

The simulator tests use official Orange and Purple Robot and Ship contacts, `BoardContactInputProvider`, and the production `CrewStrategyAdapter`; they do not inject pit completions directly into the simulation.

- Two players independently select different services, touch and release to request a stop, and complete simultaneous align-and-hold actions.
- Both Car contacts remain active while Crew interactions are in progress. A Crew failure does not clear the same player's Car throttle or affect the other player.
- Moving Player 1's Crew contact into the wrong player's region clears only Player 1's action progress and produces the expected wrong-region warning.
- Canceling Player 2's Crew contact clears progress. A replacement contact starts release-gated, cannot complete while gated, and rearms only after a safe release.
- Changing `BoardInput.settings` through the real SDK event resets both throttles and both Crew actions, release-gates every active Piece, and permits both players to recover after release and retouch.
- After recovery, both different services complete exactly once; repeated updates cannot duplicate completion or cross-assign a command.

The broader simulator suite also retains duplicate/unassigned Piece, crossing-region, removal, cancellation, recognition-loss, new-contact-ID, and ten simultaneous two-player cycle coverage.

## Android inspection

`aapt` confirmed a development/debuggable package with minimum, target, and compile API 33 and native code limited to `arm64-v8a`. The archive contains one copy of each expected native runtime library:

```text
lib/arm64-v8a/libil2cpp.so
lib/arm64-v8a/libmain.so
lib/arm64-v8a/libnativeBoardSDK.so
lib/arm64-v8a/libtensorflowlite.so
lib/arm64-v8a/libunity.so
```

The first inspection during Issue #48 exposed stale numbered IL2CPP copies from prior Bee outputs and a 160 MB APK. The build entry point now uses `BuildOptions.CleanBuildCache`; a clean rebuild produced the recorded 25,262,713-byte candidate with no numbered duplicate libraries. The Unity batch build exited successfully. Its only error-level build diagnostic was an early license-token refresh message; compilation, IL2CPP, Gradle packaging, and the final build result succeeded.

## Paired-Board smoke test

| Item | Observed |
| --- | --- |
| Board model | `B5438` |
| BoardOS | `2.0.3` |
| Board Connect | `2.0.3` |
| Transport | Sweep at `192.168.0.138` |
| Install and launch | Passed |
| Render size | `1920 × 1080` |

After the startup frame settled, a direct Board screenshot showed the refreshed five-lap race presentation: two mirrored player HUDs, one exact Call Pit region per player, explicit `CALL PIT · TOUCH + RELEASE` status, distinct pit lane and player boxes, heat and tire meters, and both on-car `H`/`T` attachment cues. No editor-only provider hint was present. Automated pose coverage proves continuous phase endpoints; the remaining physical gate explicitly observes the complete motion and the dynamic replacement of Call Pit with the new `CAR PARKED · CHOOSE REPAIR` Tires/Cooling state.

Warning-or-higher logs for the candidate process contained the Board platform's recurring hidden-method/property-access warnings and four `Invalid base format` graphics-layer messages during startup. They contained no managed exception, Unity stack trace, Board Racing error, crash, or stuck process. The same graphics messages recur in earlier launches, and the settled 1920×1080 capture rendered correctly.

## Hardware criteria carried to Issue #49

Automation and a single-device render smoke test cannot establish whether the tactile action feels good or whether players make voluntary strategy decisions. Issue #49 therefore still requires:

- two operators completing three five-lap races and a clean rematch;
- both physical service types and different voluntary service choices or timing;
- a deliberate Crew removal during service followed by unassisted release/reacquisition recovery;
- zero stale throttle, false/duplicate service, cross-player service, role swap, or unrecovered recognition failure;
- readability of heat, wear, selection, commitment, progress, and completion from both table sides; and
- each player explaining a result affected by Crew choice/timing and whether the Crew Piece is meaningfully essential.

There are no forced pit stops. Critical heat limits performance, and the player remains responsible for deciding whether and when Cooling is worth a voluntary stop.
