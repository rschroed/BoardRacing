# Tranche 3 simulator and Android validation

The Tranche 3 software-readiness gate and subsequent two-person physical gate **passed on July 13, 2026**. The complete automated suites, Board SDK simulator recovery matrix, Android package inspection, paired-Board deployment, and refreshed physical race are now complete. Physical sign-off is recorded in Issue #49 and the Tranche 3 proof.

## Candidate identity

| Item | Recorded value |
| --- | --- |
| Runtime source commit | `616cfe3` |
| Unity | `2022.3.62f3` |
| Board Unity SDK | `3.3.0` |
| APK | `Builds/Android/BoardRacing-development.apk` |
| APK size | `25,272,289` bytes |
| APK SHA-256 | `b05d871a88b2c418645db98d903f9882b2e6f693233fcd1bc224cfd1143778b1` |
| Package | `com.wholestudios.boardracing` |
| Version | code `1`, name `1.0` |

The APK was rebuilt after the runtime source commit was created, then that exact local artifact was inspected, installed, launched, and captured. Documentation-only commits after the candidate do not change the runtime artifact.

## Automated results

| Suite | Passed | Failed | Duration |
| --- | ---: | ---: | ---: |
| Edit Mode | 65 | 0 | 0.57s |
| Play Mode | 13 | 0 | 32.16s |
| Total | 78 | 0 | 32.73s |

The final suites ran for the placement/rotation candidate after the first physical attempt in #49. EditMode covers the deterministic race, condition, pit, Robot adapter, three player-relative Ship stops, normalized phase progress, spline presentation boundaries, and strategy-balance contracts. PlayMode covers runtime startup, the shared two-player provider path, Call Pit and parked repair choices, a complete accelerated keyboard race through service/finish/rematch, and the production Board provider through the Board SDK simulator—including same-contact sliding, lift/place reacquisition, touch independence, and settings-reset recovery.

## Board SDK simulator matrix

The simulator tests use official Orange and Purple Robot and Ship contacts, `BoardContactInputProvider`, and the production `CrewStrategyAdapter`; they do not inject pit completions directly into the simulation.

- Two players independently select different services, place/align/hold to request a stop, and complete simultaneous Robot actions without active touch.
- Both Ship contacts remain active while Robot interactions are in progress. A Robot failure does not clear the same player's Ship throttle or affect the other player.
- Moving Player 1's Robot into the wrong player's region clears only Player 1's action progress and produces the expected warning.
- Canceling Player 2's Robot clears progress. A replacement contact can begin only from a fresh placement and cannot inherit an old action.
- Changing `BoardInput.settings` through the real SDK event resets both Robot actions; service requires a fresh placement afterward. Active touch does not change the recovered Ship command.
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

The first inspection during Issue #48 exposed stale numbered IL2CPP copies from prior Bee outputs and a 160 MB APK. The build entry point uses `BuildOptions.CleanBuildCache`; the refreshed build produced the recorded 25,272,289-byte candidate. The Unity batch build exited successfully.

## Paired-Board smoke test

| Item | Observed |
| --- | --- |
| Board model | `B5438` |
| BoardOS | `2.0.3` |
| Board Connect | `2.0.3` |
| Transport | Sweep at `192.168.0.138` |
| Install and launch | Passed |
| Render size | `1920 × 1080` |

The first deployed #69 screenshot showed the refreshed five-lap presentation and exposed the new return lane clipping the center title. PR #70 moved the return lane into the open band above the inbound lane, then rebuilt, installed, and launched the corrected candidate. The settled Board capture mechanism preserved a partial frame after relaunch, so full-screen clearance is supported by the corrected geometry and automated pose coverage but remains an explicit hands-on observation in #49.

Warning-or-higher logs for the candidate process contained the Board platform's recurring hidden-method/property-access warnings and four `Invalid base format` graphics-layer messages during startup. They contained no managed exception, Unity stack trace, Board Racing error, crash, or stuck process. The same graphics messages recur in earlier launches, and the settled 1920×1080 capture rendered correctly.

## Physical gate resolution

Automation and a single-device render smoke test could not establish whether the tactile action felt good. The refreshed two-player run subsequently demonstrated required Robot service, classification, and finish for both racers, and the owner confirmed the redesigned interaction works. Issue #49 is therefore complete.

The stricter three-race, deliberate-removal, clean-rematch, and table-side-swap matrix was not fully rerun after the interaction redesign. Those scenarios remain regression and hardening coverage rather than claims attached to this physical sign-off. Remaining concerns were judged to be UI hierarchy and affordance work suitable for a later real UI pass.

There are no forced pit stops. Critical heat limits performance, and the player remains responsible for deciding whether and when Cooling is worth a voluntary stop.
