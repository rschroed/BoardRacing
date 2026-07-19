# Tranche 4 wireframe UI contract

This document defines the layout and information architecture for the current two-player Board Racing loop. It is an implementation contract at wireframe fidelity: hierarchy, ownership, orientation, state priority, copy purpose, and physical affordances are fixed here. Exact spacing, wrapping, arc construction, and visual styling are resolved through the staged code-and-capture reviews in Issue #77.

Final fonts, art, effects, animation, audio, player setup, profiles, AI, tutorials, tournaments, and championships are outside this contract.

## Design direction and authority

- Figma file: [Board Racing](https://www.figma.com/design/6lRFxoVugQBKedYIh8OiLl/Board-Racing) — this file supersedes the earlier Tranche 4 Wireframes file (`6UK6Kgvfamg4UU0UpkChlg`), which is retained only as history.
- Owner-approved named frames (recorded in issue #75, revised in issue #85 and in the issue #77 Round 1 review):
  - Throttle treatment — frame `17:14`: Ship tucked into the corner as the needle; arc and `BRAKE / DRIVE / BOOST` labels fan toward the track. Its separate large state word is superseded by frame `40:23`, where the lit sector's fill and accent label carry the throttle read.
  - Condition dials — frame `35:2`: two fixed-size round dials (Tires, Heat), fuller = worse; the same dial doubles as the parked repair target.
  - Full four-seat board — frame `40:23`: four corner seats around the central race area; Call Pit against each seat's short edge; two diagonal seats active in Tranche 4. **This frame is the measured source for seat-cluster geometry** — the right-side seat is component `44:124`, the left-side seat is component `44:229`.
- Reference frame: 1920×1080.
- Owner decision: use Corner Controllers as the primary layout and information-architecture direction, then refine it through full-resolution implementation captures.

Round 1 of issue #77 established that "architectural reference" was too loose: a port that moved only the detection centers while keeping older chrome (square zone panels, caption bars, a per-seat text column) did not reflect the approved design. As of Round 2, seat-cluster positions, sizes, and label transforms in this contract are **measured from the `40:23` components** and the rendered treatment must match the frame's vocabulary: outline circles, rim dials, and a fan arc — no zone panels and no per-seat copy. Wireframe stroke construction (IMGUI scalloped arcs) may approximate Figma's smooth strokes; positions may not drift.

## Current UI audit

The pre-Tranche 4 presentation proves the mechanics but does not yet behave as a coherent player-facing interface:

- Race, pit, and finish messages repeat in the HUD, action regions, and around the car, creating competing instructions.
- Finish and pit labels crowd the moving cars and obscure the race rather than clarifying player state.
- Brake, Drive, and Boost appear mostly as text in a status sentence; the current Ship state has no strong, stable spatial read.
- Heat, tires, corner recovery, pit state, lap, place, and mandatory-stop status compete at similar emphasis.
- Large physical target treatments can cover the track because their mechanical overlap with the track is not handled as a layout constraint.
- The player areas do not reserve dependable space for hands and Pieces, and essential text can sit beneath the interaction zones.
- Provider/debug diagnostics appear inside the shared playfield in the editor and are not part of the player hierarchy.

Tranche 4 corrects hierarchy and legibility. It does not redesign the proven mechanics.

## Layout ownership

### Player corners

The board is authored as a **four-corner seat layout** (issue #85). Each seat is one corner cluster: the Ship and throttle arc tuck into the physical corner and open toward the central race area, the two condition dials sit between the corner and the seat's short edge, and Call Pit sits against the **short (left or right) edge** of the board. Seats come in two variants — left-side seats push Call Pit toward the left edge, right-side seats toward the right edge — and diagonal pairs are 180° rotations of the same variant. The long top and bottom edges stay clear.

For Tranche 4:

- **Orange / Player 1** owns the lower-right corner and reads from the bottom edge; its Robot targets sit against the right short edge.
- **Purple / Player 2** owns the upper-left corner and reads from the top edge; its player-facing content is rotated 180°, and its Robot targets sit against the left short edge.
- Only these two diagonal seats are active. The lower-left and upper-right corners do not show inactive player UI in the two-player experience.
- The four-seat geometry exists so a later player-count expansion does not re-layout the board; it must not weaken the two active seats in Tranche 4.

Each active corner owns:

- Player identity through its accent color and board position (each player faces their own seat).
- A stable player-relative `BRAKE / DRIVE / BOOST` orientation map.
- An unmistakable current throttle state: the lit sector's fill plus its accent label.
- Heat and tire condition as dial fill values.
- The player's currently active Robot action regions.

**Per-seat status and instruction text was removed by owner decision (issue #77 Round 2, 2026-07-18).** Frame `40:23` renders seats with no lap counter, no mandatory-stop line, no instruction sentence, and no separate state word; the seats show exactly what the frame shows. The dominant-instruction model (below) still governs *semantic priority* — which zone lights, emphasizes, or rings — and remains fully computed and tested in the UI model. If playtests (issue #79) show that lap/stop-required information is missed during play, its home gets designed in Figma first and then ported.

Treatment decisions fixed by owner approval (issue #85 and the issue #77 Round 1 review):

- **Throttle** is a fan arc around the Ship well. The physical Ship is the needle: it rests on the corner diagonal and its rotation selects the lit sector. The lit sector renders as a deep accent wedge with its label riding the arc tangent; unlit sectors are thin quiet bands. There is no separate state word.
- **Condition meters are fixed-size round dials** (Tires and Heat) that fill as the condition worsens — fuller = worse, empty = healthy. Dials are freestanding rings — no zone panel around them. Each dial **doubles as the parked repair target**: when the car is parked, a target ring surrounds the dial, the Robot seats on it, a hold-progress ring fills, and completing service drains the meter. Dials do not move or resize between states. A critical red-flash treatment and a repair animation are deliberately deferred.
- **Call Pit is an outline circle** with its tilted label inside — no panel, no embedded copy. Ghosted before the race starts and while parked; accent-lit while available; white with a hold-progress ring while holding or requested.

Stroke fidelity, exact type sizing, and state-emphasis weights remain review decisions in Issue #77.

### Shared center

The center belongs to the race: track, cars, start/finish, pit lane, and brief global transitions. Player panels may frame the race but may not turn the track into background decoration.

The center overlay may show only shared information:

- Countdown.
- `GO`.
- A brief split-finish announcement.
- Final winner.
- A brief transition into a rematch.

It never carries a player-specific control instruction. Player-specific warnings, pit guidance, and rematch readiness remain in the relevant player corner.

## Per-player information hierarchy

Every player state follows this order:

1. **Identity and ownership** — whose corner, car, and targets these are.
2. **Dominant next action** — exactly one instruction the player should act on now.
3. **Active control state** — current throttle, pit-call interaction, or service interaction.
4. **Race status** — lap, place, and mandatory-stop completion.
5. **Conditions** — heat and tires with normalized value and Normal, Warning, or Critical severity.
6. **Secondary explanation** — only when it adds information and does not restate the dominant instruction.

Identity, active control state, and both condition values remain stable enough to find at a glance. The dominant instruction changes with context.

As of the issue #77 Round 2 owner decision, this hierarchy is expressed **spatially, not textually**, at the seats: the dominant instruction manifests as which zone is lit, emphasized, or ringed (and, for missing pieces, as the dimmed Ship well), not as a rendered sentence. Race status (lap, place, mandatory stop) has no per-seat rendering this tranche. The UI model retains all instruction and status strings — tests verify their priority, and shared center overlays still use their copy where the state table permits.

## Dominant-instruction priority

The presentation chooses one instruction per player using the first applicable category:

1. A missing or invalid Piece that prevents the required action.
   - A missing Ship always qualifies because throttle safely falls to Brake.
   - A missing, wrong-region, or invalid Robot qualifies while calling the pit or while the car is parked for service.
   - A missing Robot during ordinary racing does not outrank an urgent race condition.
2. Active service: choose a service, align, hold, recover lost progress, or acknowledge completion.
3. Active pit lifecycle: Call Pit placement/hold, confirmed request, entry, or exit.
4. Corner-speed recovery.
5. Critical condition.
6. Warning condition.
7. General driving or pit-availability guidance.

When heat and tires have the same severity, Heat wins the instruction tie for deterministic behavior; both condition reads remain visible. A higher severity always outranks a lower one. Progress and selection may also appear inside the active physical target, but the corner must not repeat the same sentence.

## State contract

The "dominant instruction" column below defines semantic priority in the UI model. At the seats it renders as zone lighting, emphasis, and progress rings — not text (issue #77 Round 2 owner decision). Copy in this table survives only in the model, in tests, and in permitted shared-center overlays.

| State | Shared center | Player's dominant instruction | Stable player information and affordances |
| --- | --- | --- | --- |
| Grid / not ready | Track and grid remain visible; no player-specific copy | Place the missing Ship; otherwise identify the Ship control | Identity, Brake/Drive/Boost map, current safe Brake state, readiness, Call Pit shown as unavailable |
| Grid / ready | Brief shared `READY` is permitted | Hold ready for the start | Identity, throttle map/state, lap `1/5`, place, stop required, initial conditions |
| Countdown | One shared `3`, `2`, `1` sequence | Get ready; rotate only after Go | Same stable information; Call Pit remains unavailable; losing a Ship returns that player to placement guidance and the race to Grid |
| Go | Brief shared `GO` | Begin driving, unless a missing Ship overrides it | Current throttle becomes live; Call Pit becomes available only after the race starts |
| Stable racing | Track remains unobstructed by global copy | General Ship guidance or Robot pit availability | Current throttle, lap/place/stop, heat, tires, and the exact Call Pit boundary |
| Warning | No global warning | Respond to the highest warning | Warning name and value remain visible on its condition read; other stable information does not move |
| Critical | No global warning | Respond to the highest critical condition or active recovery | `CRITICAL` text plus value and non-color cue; any performance consequence is explicit |
| Corner recovery | No global warning | Recover from the too-fast corner entry | Recovery state outranks condition advice; throttle and both conditions remain visible |
| Call Pit — placement | No global message | Place the Robot in Call Pit | Full mechanical Call Pit boundary, owner, and state cue |
| Call Pit — holding | No global message | Hold the Robot steady | Hold percentage/progress inside the target; no duplicate progress sentence elsewhere |
| Pit requested | Track and pit path remain primary | Expect entry at start/finish | `PIT CALLED` confirmation replaces Call Pit action; throttle, conditions, and race status remain visible |
| Pit entering | Track and moving car remain primary | Wait while the car enters automatically | Throttle is visibly locked; entry state and progress are clear; service targets are not active early |
| In service — choose | No global message | Move the Robot to Tires or Cooling | Call Pit ghosts in place; both service zones light up at their fixed dial positions; car parked and 0% progress are explicit |
| In service — selected/holding | No global message | Hold the Robot in the selected service | Selected service uses text/shape/boundary emphasis; the other service remains available to switch |
| In service — holding | No global message | Hold the Robot steady | Selected service and percentage are co-located with the physical target |
| In service — progress reset | No global message | Return, realign, and restart the hold | Progress immediately reads 0%; the cause is named when known; switching services visibly selects the new service and restarts progress |
| Service complete / pit exit | Track and moving car remain primary | Wait for automatic rejoin | Completion is explicit, the service zone returns to its ghosted dial state with the meter drained, stop status updates, and Ship control is visibly restored only after exit |
| Split finish — this player finished | Brief `<PLAYER> FINISHED` is permitted | Wait for the other racer | Result/place replaces live driving guidance; no Call Pit or condition instruction remains active |
| Split finish — this player racing | Brief announcement must not displace live guidance | Continue the highest-priority live action | Full racing hierarchy and physical affordances remain available |
| Final results | Winner in the center | Move both Ships to Brake and hold for rematch | Each player sees result/place, stop completion, and rematch hold progress; racing guidance is removed |
| Rematch release | Brief rematch transition is permitted | Rotate both Ships out of Brake to restart | Confirmation is explicit and shared; release prevents an accidental immediate restart |

Copy may shorten during implementation, but its semantic purpose and priority may not change without revising this contract.

## Physical action geometry

Runtime Piece positions use a bottom-left origin. IMGUI uses a top-left origin. For a runtime center `(cx, cy)` and half-size `(hx, hy)`, the visible target rectangle is:

```text
x = cx - hx
y = 1080 - cy - hy
width = 2 × hx
height = 2 × hy
```

Detection centers are the **measured dial and circle centers from frame `40:23`, component `44:124`** (issue #77 Round 2): the condition dial is the service target, so the detection center is the dial center. The active race constructs Call Pit, Tires, and Cooling with `TrancheThreeSettings.serviceHalfSize = (50, 50)` — the Robot placement slop around each center, ±50 px on each axis. This was revised from `(110, 110)`: the measured dials sit only ≈148 px apart center-to-center, so 220 px square zones would overlap; a 100 px square around a 92 px dial still accepts the 90 px Robot disc anywhere the disc meaningfully covers the dial. The detection rectangle is *not* a rendered shape — the visible affordance is the dial ring (radius 46) or Call Pit circle (radius 59). `TrancheThreeSettings.serviceHalfSize` is the single authoritative slop value: the legacy `TrancheOneSettings.serviceHalfSize` field was removed, and `ControlLab` reads the Tranche Three value like the active race.

| Target | Runtime center | Visible shape | IMGUI detection rectangle |
| --- | --- | --- | --- |
| Orange Call Pit | `(1832, 398)` | circle r 59 | `x 1782, y 632, w 100, h 100` |
| Orange Tires | `(1692, 321)` | dial r 46 | `x 1642, y 709, w 100, h 100` |
| Orange Cooling (Heat dial) | `(1590, 212)` | dial r 46 | `x 1540, y 818, w 100, h 100` |
| Purple Call Pit | `(88, 682)` | circle r 59 | `x 38, y 348, w 100, h 100` |
| Purple Tires | `(228, 759)` | dial r 46 | `x 178, y 271, w 100, h 100` |
| Purple Cooling (Heat dial) | `(330, 868)` | dial r 46 | `x 280, y 162, w 100, h 100` |

Purple targets are the exact 180° mirror of Orange (`x → 1920 − x`, `y → 1080 − y`).

The rest of the seat cluster is fixed by the same measurement (IMGUI coordinates, Orange seat; Purple is the 180° mirror with label rotations offset by the seat's 180°):

| Element | Value |
| --- | --- |
| Throttle arc center / radius | `(1863, 1025)` / `250` |
| Sector center angles (IMGUI degrees) | Brake `190`, Drive `226`, Boost `260`; sweep `32` per sector |
| Ship well center / footprint radius | `(1787, 938)` / `138` |
| BRAKE label center / rotation | `(1630, 984)` / `−84°` |
| DRIVE label center / rotation | `(1703, 859)` / `−49°` |
| BOOST label center / rotation | `(1820, 795)` / `−9°` |
| TIRES label center / rotation | `(1665, 707)` / `−30°` |
| HEAT label center / rotation | `(1537, 842)` / `−64°` |
| CALL PIT label center / rotation | `(1832, 682)` / `−60°` |

The two currently inactive seats (lower-left, upper-right) use the left-side variant, component `44:229`; its geometry gets measured and recorded here when those seats activate.

Additional immovable mechanical constraints:

| Constraint | Runtime value |
| --- | ---: |
| Player-region boundary | `y = 540` in runtime coordinates |
| Robot action activation (Call Pit, Tires, Cooling) | placement only — no orientation gate |
| Call Pit hold | `0.75 s` |
| Tires/Cooling hold | `1.5 s` |
| Rematch hold | `1.0 s` with both Ships at Brake |
| Throttle stops (raw Ship orientation, Player 1) | Brake `275°` · Drive `225°` · Boost `175°` |

The throttle stops are **hardware-measured** (issue #77 hardware review, 2026-07-19): each value is the raw SDK orientation with the Ship's nose pointing at the center of the rendered wedge. Sector selection picks the nearest stop, with switch boundaries at the midpoints (`250°`, `200°`) plus the configured hysteresis, so an off-center Ship reads stably. Player 2 applies its 180° seat rotation before the comparison. The values live in `TrancheOneSettings`; re-measure them if the corner cluster geometry ever moves.

All Robot actions are **placement-only** (issue #77 hardware review, 2026-07-19): placing the Robot inside a target starts its hold at any orientation — there is no rotate-to-0° step anywhere. The raw SDK Robot orientation has no player-visible meaning on the disc piece, so the earlier `0° ± 15°` alignment gate could not be performed deliberately on hardware. Call Pit additionally requires a fresh placement (a Robot parked inside the circle does not re-trigger).

Placement invariants every target satisfies (numerically checked in issue #77 Round 2 and enforced by the presentation tests):

- Every detection rectangle lies fully on the board and fully inside its player's region half.
- Tires and Cooling detection rectangles do not overlap one another (they clear by 2 px on the x axis — `RaceLayout.Create` rejects any drift into overlap).
- Both dials clear the Ship's rotational footprint (well radius 138 + dial radius 46; measured clearances ≈ 19 px and 25 px).
- Every dial and Call Pit circle clears the throttle arc band.
- Call Pit's circle edge sits within ≈ 30 px of its short board edge.

Call Pit is active before service; Tires and Cooling are active only while parked. Therefore, approximate proximity between Call Pit and a service target is not simultaneous interactive ambiguity. Tires and Cooling do not overlap one another.

The visible ring must honestly mark the target: the dial or circle is the affordance the Robot seats on, and the ±50 px detection slop is centered on it. The established centers, half-sizes, orientation, tolerance, hold behavior, activation rules, and player-region behavior do not change in Tranche 4.

These physical targets overlap part of the central track envelope. Implementation must preserve the exact targets while ensuring their treatment does not erase cars, pit movement, or start/finish information. This is a capture-review problem, not permission to move the regions.

## Orientation, occlusion, and accessibility rules

- All Purple player-facing text and symbols read from the top edge; all Orange content reads from the bottom edge.
- Cars and global messages remain neutral to the long edges where possible; no player must read the opponent's HUD to understand the race.
- Essential status and instructions may not sit underneath the expected resting area of a Ship or Robot.
- Physical action regions must remain understandable when a Robot covers their center.
- Color never carries meaning alone. Triangle/circle identity, named throttle states, named severity, selected outlines/shapes, percentages, check marks, and explicit completion text reinforce color.
- Normal, Warning, Critical, selected, holding, complete, finished, and unavailable states require a non-color distinction.
- Editor-only provider diagnostics stay outside player-facing hierarchy and are excluded from device builds.

## Fixed decisions versus code-review decisions

Fixed by this contract:

- Four-corner seat geometry with short-edge Robot targets; two active diagonal corners in Tranche 4: Purple upper-left and Orange lower-right.
- No inactive-seat UI in the other two corners.
- Central ownership by the race and brief global overlays only.
- Per-player information hierarchy and dominant-instruction priority.
- Stable throttle/condition grouping in each corner.
- Throttle treatment: fan arc with the Ship as the needle; the lit sector's fill and label are the state read — no separate state word (frame `40:23`, superseding `17:14`'s state word).
- Meter construction: fixed-size freestanding round dials, fuller = worse, dial doubles as the parked repair target (frames `35:2`, `40:23`).
- No per-seat status or instruction copy (issue #77 Round 2 owner decision); instruction priority renders as zone emphasis.
- Full state inventory and semantic priority in the UI model.
- Exact physical action geometry, seat-cluster geometry, and mechanics (measured from `40:23` components).
- Opposite-edge orientation and non-color redundancy.

Resolved through the remaining owner-reviewed capture rounds in Issue #77:

- Stroke fidelity of arcs and rings within IMGUI (scalloped wireframe strokes versus smooth).
- Weights and emphasis of lit / ghost / selected / holding states.
- Whether the dial's severity read needs a non-color cue at the dial itself (the car-side `H!!`/`T!!` cues currently carry the non-color channel).
- Type sizing within IMGUI.
- Exact duration and treatment of brief global transition messages.
- (Deferred beyond Tranche 4: critical red-flash and repair animation; a designed home for lap/stop status if playtests miss it.)

No code-review decision may move a physical target, add a second dominant instruction, populate inactive corners, or reduce the central race to accommodate player chrome.

## Verification and tranche evidence

- Issue #76 tests semantic priority, mutually exclusive instructions, deterministic state fixtures, mirrored/rotated transforms, bounds, and runtime-derived target alignment.
- Issue #77 produces 1920×1080 captures for stable racing, pressure/service states, and lifecycle/results, with an owner stop after each round.
- Issue #78 verifies the full state matrix on Android.
- Issue #79 runs the two-person physical Board readability gate with players swapping sides.

The Tranche 4 exit criterion remains: two players can complete and rematch the full race from opposite sides without developer assistance interpreting the UI.
