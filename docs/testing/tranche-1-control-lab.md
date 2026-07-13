# Tranche 1 control lab

The main scene starts the two-player control lab automatically. It is a hardware-input experiment, not a race. Player 2 occupies the top edge with its panel rotated 180 degrees for the opposite side; Player 1 occupies the bottom edge.

## Arcade Piece assignments

| Player | Car control | Pit control |
| --- | --- | --- |
| Player 1 | Orange Robot, Glyph 2 | Orange Ship, Glyph 7 |
| Player 2 | Purple Robot, Glyph 1 | Purple Ship, Glyph 6 |

Pink and Yellow Arcade Pieces are deliberately unassigned. Placing one produces a warning and cannot affect either player.

## Mouse and keyboard fallback

The editor starts with keyboard fallback. Press `F1` to switch between fallback and Board input. Press `Backspace` to reset pit completion counters.

| Action | Player 1 | Player 2 |
| --- | --- | --- |
| Toggle Car touch | `Q` | `U` |
| Toggle Crew touch | `W` | `I` |
| Toggle Car presence | `E` | `O` |
| Toggle Crew presence | `R` | `P` |
| Throttle 25/50/75/100 | `Z` / `X` / `C` / `V` | `7` / `8` / `9` / `0` |
| Rotate Crew | `A` / `D` | `J` / `L` |
| Move Crew left/right | `F` / `G` | `H` / `K` |
| Move Crew up/down | `T` / `B` | `Y` / `N` |

For a pit cycle, make the Ship present and touched, move it into the service zone, rotate it until the panel reports **HOLD STEADY**, and hold it for 1.5 seconds. The panel shows the live SDK angle, target, angular error, hold progress, and a prominent `PIT CYCLES n / 10` result. Move the Ship out of the zone before beginning another cycle.

## Tunable defaults

`Assets/Resources/TrancheOneSettings.asset` is the single source for prototype thresholds:

- throttle hysteresis: 8 degrees;
- player-region boundary: y = 540 screen pixels; Player 1 uses the lower half and Player 2 the upper half;
- Player 1 service center: (1325, 270); Player 2 service center: (595, 810), matching the rendered pit bars after Player 2's 180-degree rotation;
- service-zone half-size: 180 × 150 screen pixels;
- alignment target: SDK-reported 0 degrees;
- alignment tolerance: 15 degrees;
- uninterrupted hold: 1.5 seconds.

Changing Board input settings or the active Piece model cancels Board contacts and explicitly resets the reconciler. The next contact for each Glyph must first be observed released before touch can produce throttle or pit progress. This prevents a new contact ID from inheriting stale state.

If a Piece is first recognized while touched, its panel displays **RELEASE TO REARM**. Release it once, then touch it again to resume input.

`ALIGN SHIP` means touch is recognized but the Ship is outside the target tolerance. Rotate slowly while watching the live angle/error. The hold bar intentionally remains at zero until the state changes to **HOLD STEADY**.

## Editor smoke check

1. Enter Play Mode in `Assets/Scenes/Main.unity` and confirm both mirrored panels appear.
2. For each player, toggle Car touch and select all four throttle steps.
3. Toggle Car presence off and confirm throttle immediately reads zero.
4. Complete one pit cycle, confirm the counter increments once, and keep holding to confirm it does not repeat.
5. Move out and complete a second cycle.
6. Remove the Crew Piece mid-hold and confirm progress resets without completion.
7. Operate both players simultaneously and confirm their state remains isolated.
