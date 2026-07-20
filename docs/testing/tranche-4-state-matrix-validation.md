# Tranche 4 state-matrix and Android validation

The Tranche 4 software-readiness gate (issue #78) **passed on July 19, 2026**: complete automated suites, deterministic preview coverage of every contract presentation state at 1920×1080, Android package inspection, paired-Board deployment, and a live completed-race observation on the deployed candidate. The two-person physical usability gate remains issue #79 and is owner-run.

## Candidate identity

| Item | Recorded value |
| --- | --- |
| Runtime source commit | `15562ad` |
| Unity | `2022.3.62f3` |
| Board Unity SDK | `3.3.0` |
| APK | `Builds/Android/BoardRacing-development.apk` |
| APK size | `25,302,681` bytes |
| APK SHA-256 | `0c3040f319287c29d2fecf5cfbd4882f8da8a79d32a349552633808a7b937d7f` |
| Package | `com.wholestudios.boardracing` |
| Version | code `1`, name `1.0` |

The recorded APK was built from `15562ad` (clear-table pause + START NEW RACE, no mandatory stop), then that exact artifact was inspected, installed, launched, and observed live. The validation branch that follows the candidate adds the capture harness, the `Paused` preview fixture, and an editor-diagnostics gate whose player compilation is a constant `false` — the runtime behavior of the recorded artifact is what shipped in `15562ad`.

## Automated results

| Suite | Passed | Failed | Duration |
| --- | ---: | ---: | ---: |
| Edit Mode | 96 | 0 | 0.57s |
| Play Mode | 13 | 0 | 34.35s |
| Total | 109 | 0 | 34.92s |

EditMode covers the deterministic race, conditions (fuel model, tire wear, penalties), pit lifecycle (call, entry-at-line, stir-to-service on the emptying stroke, manual Leave Pit, short-merge exit rejoin without lap credit), the clear-table pause (debounce timing, exact freeze, resume countdown with state intact, abort-back-to-pause, `RequestNewRace()` honored only while paused, finished racer's Ship gating neither pause nor resume), the Robot strategy adapter (including full crew suspension while paused), corner-controller contract geometry, mirrored seats, UI instruction priority with mutually exclusive primary instructions, and the deterministic preview fixtures for every review state. PlayMode covers runtime startup, the shared two-player provider path, Call Pit and parked repair choices, a complete accelerated keyboard race through service/finish/rematch, and the production Board provider through the Board SDK simulator — same-contact sliding, lift/place reacquisition, touch independence, and settings-reset recovery.

## State-matrix coverage (1920×1080 captures)

Deterministic preview fixtures render each contract state through the production presenter; the capture harness (`Assets/Editor/BoardRacingCaptures.cs`) plays the prototype and captures the Game view at exactly 1920×1080 with editor diagnostics suppressed. Captures live in `docs/captures/round3/`.

| Capture | Contract state (wireframe-ui.md) |
| --- | --- |
| `round3-01-Grid` | Grid / ready |
| `round3-02-Countdown` | Countdown (shared treatment with the resume countdown) |
| `round3-03-StableRacing` | Racing, stable |
| `round3-04-Warning` | Fuel warning threshold |
| `round3-05-Critical` | Fuel empty (limp) |
| `round3-06-PitCallHolding` | Call Pit hold in progress |
| `round3-07-InService` | Parked, Tires selected, stir ring + Leave Pit live |
| `round3-08-SplitFinish` | One racer finished, one racing |
| `round3-09-Results` | Both finished, winner banner |
| `round3-10-RematchRelease` | Rematch release prompt |
| `round3-11-Paused` | **New (#90)**: table cleared — `RACES PAUSED` overlay, `START NEW RACE` button, seats ghosted, mixed finished/unfinished per-seat instructions |
| `round3-00-BoardLive` | Live Board capture of the deployed candidate (see smoke test) |

Review notes: no clipping, no stale or cross-player content, no track obstruction by center copy (the split-finish and winner banners sit in the open band below the track), and no editor/provider diagnostics in the preview captures. The pause overlay intentionally owns the table center; content beneath it (a finished car's label) is occluded by design. The resume countdown reuses the start countdown treatment (capture 02); its distinct semantics (abort returns to the pause, state intact) are EditMode-covered and were owner-verified live on July 19.

## Android inspection

`aapt` confirms a development/debuggable package with minimum, target, and compile API 33 and native code limited to `arm64-v8a`. One copy of each expected native runtime library:

```text
lib/arm64-v8a/libil2cpp.so
lib/arm64-v8a/libmain.so
lib/arm64-v8a/libnativeBoardSDK.so
lib/arm64-v8a/libtensorflowlite.so
lib/arm64-v8a/libunity.so
```

The Unity batch build exited successfully with `BuildOptions.CleanBuildCache` in effect.

## Paired-Board smoke test

| Item | Observed |
| --- | --- |
| BoardOS | `2.1.0` (protocol `v1`) |
| Board ready | `true` |
| Install and launch | Passed (Board Connect) |
| Render size | `1920 × 1080` |

The live capture (`round3-00-BoardLive.png`) shows the deployed candidate mid-session with a **completed two-player race — `PURPLE WINS`, both racers finished** — live evidence that the issue #92 blocked-finish failure (a racer lapping forever behind the silent mandatory-stop gate) is resolved on hardware. Development-build raw-orientation readouts and the Unity watermark are present by design in development builds only.

Warning-or-higher logs for the candidate process contained the Board platform's recurring `Invalid base format` graphics-layer messages, one hidden-method access warning, and benign resource-close warnings — the same profile recorded for Tranche 3. They contained no managed exception, Unity stack trace, Board Racing error, crash, or stuck process.

## Acceptance criteria (issue #78)

- [x] All automated suites pass with counts and candidate commit recorded.
- [x] Deterministic preview coverage reaches every required presentation state, including the Tranche 4 pause states.
- [x] Android package inspection and launch succeed without Board Racing exceptions.
- [x] Captures show no clipping, essential overlap, track obstruction, stale/cross-player content, or developer-only text.
- [x] Candidate APK identity and hash are recorded (this document).

## Known limitations carried to #79

- The pause overlay text is single-orientation; it reads upside down from the purple corner (flagged at #90 review; a mirrored far-seat treatment is a candidate later pass).
- Both finish labels crowd the shared finish point when both cars park at the line (cosmetic, pre-existing).
- Owner-verified live on hardware so far: pause overlay timing, START NEW RACE tap → grid reset, and a completed race with both finishers on the no-mandatory-stop candidate. The full #79 matrix (two complete races plus recovery, two players) remains the physical gate.
