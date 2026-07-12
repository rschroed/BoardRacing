# Board input simulator smoke test

Use this smoke test after installing or upgrading the Board SDK, changing the Piece Set Model, or changing Unity input settings. It validates the vendor input path before Board Racing gameplay code is involved.

## Prerequisites

- Complete [development setup](../development-setup.md), including the ignored Board SDK, Arcade model, and official Input sample.
- Open the project with Unity `2022.3.62f3` and switch the active platform to Android.
- Confirm **Edit > Project Settings > Board > Input Settings** selects `arcade_v1.3.7.tflite`.

## Manual procedure

1. Open `Assets/Samples/Board SDK/3.3.0/Input/Scenes/BoardInput.unity`.
2. Open **Board > Input > Simulator** and enable simulation.
3. Enter Play Mode. Confirm the sample shows **Glyphs** and **Touches** readouts without errors.
4. Select one Board Arcade Piece in the simulator, move it into the Game view, and place it untouched with the right mouse button.
5. Confirm the Glyph readout shows a Glyph ID, contact ID, screen position, orientation, and `Began`. The placed Piece starts untouched.
6. Hold the left mouse button over the Piece. Confirm the simulator's touched outline appears, move it, use the mouse wheel or arrow keys to rotate it, and release it.
7. Confirm movement reports `Moved`, position and orientation change, and the touched outline clears when released. When validating through the API or debugger, `isTouched` must be true while held and false after release.
8. Click the placed Piece to lift it. Confirm `Ended`, followed by removal from the active-contact readout.
9. Place the same Piece again. Confirm it reports `Began` with a new contact ID.
10. Place a second Arcade Piece with a different Glyph. Move both Pieces in turn without removing either and confirm both remain visible with distinct Glyph and contact IDs.
11. Exit Unity, reopen the project and sample, then repeat placement and movement for one Piece. Confirm the Arcade model selection and contact behavior persist.

The default simulator bindings are left click to place touched or touch/lift an existing Piece, right click to place untouched, mouse wheel or arrow keys to rotate, Shift for faster rotation, and Escape to clear contacts.

## Expected results

| Interaction | Expected contact result |
| --- | --- |
| Place | `Began`; stable Glyph ID; new contact ID |
| Touch | `Stationary`; `isTouched = true` |
| Move | `Moved`; screen position changes |
| Rotate | `Moved`; orientation changes in radians |
| Release | `Stationary`; `isTouched = false` |
| Remove | `Ended`; contact disappears on the following input update |
| Replace | `Began`; same Glyph ID may recur, but contact ID is new |
| Two Pieces | Two active contacts with distinct contact IDs; both continue updating independently |

## Initial validation evidence

Validated on July 12, 2026 with Unity `2022.3.62f3`, Board SDK `3.3.0`, Input System `1.14.0`, and Board Arcade model `1.3.7`.

An automated, temporary Play Mode harness opened the official sample and drove Board's simulator contact implementation. It was removed after the run and added no project or gameplay code. The complete scenario passed twice in separate Unity processes:

| Check | Observed evidence |
| --- | --- |
| Official sample | `BoardInputManager` active in Play Mode |
| Place | Glyph `2`, contact `0`, `(300, 300)`, `Began`, untouched |
| Touch | Contact `0`, `Stationary`, touched |
| Move and rotate | Contact `0`, `(500, 420)`, orientation `0.7854` radians, `Moved` |
| Release | Contact `0`, `Stationary`, untouched |
| Two Pieces | Glyphs `2` and `6`, contacts `0` and `1`, both active |
| Simultaneous movement | Both contacts reported `Moved` in the same input update |
| Remove | Contact `0` reported `Ended` |
| Replace | Glyph `2` returned as new contact `2` with `Began` |
| Restart persistence | The full scenario passed again after a new Unity process loaded the committed settings |

This is simulator coverage, not physical Board coverage. Hardware validation remains in Issue [#13](https://github.com/rschroed/BoardRacing/issues/13).
