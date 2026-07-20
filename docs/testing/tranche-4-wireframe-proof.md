# Tranche 4 wireframe proof

Evidence record for the two-person physical Board wireframe gate (issue #79), the
final requirement for Tranche 4 passage. Owner sign-off recorded 2026-07-19.

## Gate candidate

- Commit: `460cd8e` (merged `main`: wireframe implementation through #77/#84,
  clear-table pause #90, mandatory-stop drop #92, final-lap pit fix #95,
  RACE FINISHED overlay #97, validation record #94)
- APK: `Builds/Android/BoardRacing-development.apk`,
  SHA-256 `ca760ac061f347aaa4ae4dd06647565836e0fa0b3293a4b4400310f8cebfff58`
- Automated evidence: EditMode 97/97, PlayMode 13/13; the 11-state Android
  capture matrix and APK inspection are recorded in the
  [state-matrix validation](tranche-4-state-matrix-validation.md) (#78).

## How the gate ran

The gate was played on the physical Board across the 2026-07-19 hardware
sessions: live two-player races, no developer interpretation of the UI beyond
the initial control briefing. Failures found during the runs were filed as
focused issues, fixed, redeployed to the table, and the affected scenario was
rerun on hardware before sign-off:

| Live finding | Issue | Fix | Rerun result |
| --- | --- | --- | --- |
| No readable way to pause or restart mid-race; pause overlay appeared with a 2 s lag and its button was dead | #90 | Clear-table pause (0.75 s), 3-2-1 resume countdown, START NEW RACE touch button | Owner: "that works!" |
| A racer without the then-mandatory pit stop lapped forever while the opponent finished | #92 | Mandatory-stop requirement dropped (owner decision); finish gates on laps alone | Live completed race captured from the Board (`round3-00-BoardLive.png`) |
| A pit call pending on the final lap diverted the car into the pit, then jerked it to the line | #95 | Finish check precedes pit entry; a pending call expires with the race | Owner-verified on the table |
| A finished race offered no discoverable restart (the Brake-hold rematch gesture is unguided) | #97 | RACE FINISHED overlay with the winner and a START NEW RACE button, read through the Board SDK finger-contact stream | Owner-verified on the table |

## Acceptance criteria

Confirmed by owner sign-off, 2026-07-19 ("We're good to close 79"):

- [x] Both players complete two races and a rematch without developer UI explanation.
- [x] Neither player confuses the opponent's status or action regions with their own.
- [x] Essential information remains readable from both edges and is not lost beneath hands or Pieces.
- [x] Every forced state is correctly interpreted and recovered (each live failure above was fixed and its scenario rerun).
- [x] Any discovered failure has a focused issue and the affected scenario is rerun (#90, #92, #95, #97).
- [x] Roadmap and README mark Tranche 4 passed only after evidence is recorded (this document).

## Accepted limitations

Deferred with owner awareness to the real UI pass, not gate blockers:

- The pause and race-finished overlay text renders in a single orientation and
  reads upside down from the purple corner.
- Finish labels crowd at the shared finish line on a close finish.

## Sign-off

Owner sign-off on 2026-07-19 closes issue #79 and passes Tranche 4: two players
can read, complete, and rematch the race on the physical Board without
developer assistance interpreting the UI.
