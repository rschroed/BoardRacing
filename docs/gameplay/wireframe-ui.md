# Tranche 4 wireframe UI contract

This document defines the layout and information architecture for the current two-player Board Racing loop. It is an implementation contract at wireframe fidelity: hierarchy, ownership, orientation, state priority, copy purpose, and physical affordances are fixed here. Exact spacing, wrapping, arc construction, and visual styling are resolved through the staged code-and-capture reviews in Issue #77.

Final fonts, art, effects, animation, audio, player setup, profiles, AI, tutorials, tournaments, and championships are outside this contract.

## Design direction and authority

- Figma file: [Board Racing](https://www.figma.com/design/6lRFxoVugQBKedYIh8OiLl/Board-Racing) — this file supersedes the earlier Tranche 4 Wireframes file (`6UK6Kgvfamg4UU0UpkChlg`), which is retained only as history.
- Owner-approved named frames (recorded in issue #75, revised in issue #85):
  - Throttle treatment — frame `17:14`: Ship tucked into the corner as the needle; arc and `BRAKE / DRIVE / BOOST` labels fan toward the track; a large state word carries legibility.
  - Condition dials — frame `35:2`: two fixed-size round dials (Tires, Heat), fuller = worse; the same dial doubles as the parked repair target.
  - Full four-seat board — frame `40:23`: four corner seats around the central race area; Call Pit against each seat's short edge; two diagonal seats active in Tranche 4.
- Reference frame: 1920×1080.
- Owner decision: use Corner Controllers as the primary layout and information-architecture direction, then refine it through full-resolution implementation captures.

The frames are an architectural reference, not a pixel specification. They establish diagonal player ownership, a central race area, the corner throttle-arc-with-state-word treatment, dial-style condition meters that double as repair targets, and short-edge Robot targets. Approximate Figma positions and copy do not override the runtime geometry fixed in this contract or later owner-reviewed code captures.

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

- Player identity using both color and shape: Orange/triangle or Purple/circle.
- A stable player-relative `BRAKE / DRIVE / BOOST` orientation map.
- An unmistakable current throttle state that does not depend on color alone.
- Heat and tire condition, each with value and named severity.
- Compact lap, place, and mandatory-stop status.
- Exactly one dominant instruction.
- The player's currently active Robot action regions.

Two treatment decisions are now fixed by owner approval (issue #85, frames `17:14` and `35:2`):

- **Throttle** is a corner arc plus an explicit state word. The physical Ship is the needle: it rests in the corner and its rotation selects the lit sector. The large `BRAKE / DRIVE / BOOST` state word, not the arc alone, carries legibility.
- **Condition meters are fixed-size round dials** (Tires and Heat) that fill as the condition worsens — fuller = worse, empty = healthy. Each dial sits at its service target's center and **doubles as the parked repair target**: when the car is parked, the same dial becomes the active Tires/Cooling zone, the Robot seats on it, a hold-progress ring fills around it, and completing service drains the meter. Dials do not move or resize between states. A critical red-flash treatment and a repair animation are deliberately deferred.

Exact arc radius, band thickness, safe-edge inset, type sizing, and copy line breaks remain review decisions in Issue #77.

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

Identity, active control state, race status, and both condition values remain stable enough to find at a glance. The dominant instruction changes with context.

## Dominant-instruction priority

The presentation chooses one instruction per player using the first applicable category:

1. A missing or invalid Piece that prevents the required action.
   - A missing Ship always qualifies because throttle safely falls to Brake.
   - A missing, wrong-region, or invalid Robot qualifies while calling the pit or while the car is parked for service.
   - A missing Robot during ordinary racing does not outrank an urgent race condition.
2. Active service: choose a service, align, hold, recover lost progress, or acknowledge completion.
3. Active pit lifecycle: Call Pit alignment/hold, confirmed request, entry, or exit.
4. Corner-speed recovery.
5. Critical condition.
6. Warning condition.
7. General driving or pit-availability guidance.

When heat and tires have the same severity, Heat wins the instruction tie for deterministic behavior; both condition reads remain visible. A higher severity always outranks a lower one. Progress and selection may also appear inside the active physical target, but the corner must not repeat the same sentence.

## State contract

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
| Call Pit — placement/alignment | No global message | Place or rotate the Robot in Call Pit | Full mechanical Call Pit boundary, owner, alignment requirement, and state cue |
| Call Pit — holding | No global message | Hold the Robot steady | Hold percentage/progress inside the target; no duplicate progress sentence elsewhere |
| Pit requested | Track and pit path remain primary | Expect entry at start/finish | `PIT CALLED` confirmation replaces Call Pit action; throttle, conditions, and race status remain visible |
| Pit entering | Track and moving car remain primary | Wait while the car enters automatically | Throttle is visibly locked; entry state and progress are clear; service targets are not active early |
| In service — choose | No global message | Move the Robot to Tires or Cooling | Call Pit ghosts in place; both service zones light up at their fixed dial positions; car parked and 0% progress are explicit |
| In service — selected/alignment | No global message | Align the Robot in the selected service | Selected service uses text/shape/boundary emphasis; the other service remains available to switch |
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

The active race constructs Call Pit, Tires, and Cooling with `TrancheThreeSettings.serviceHalfSize = (110, 110)` — square 220×220 zones. This half-size was revised from `(140, 120)` in issue #85: the dial-as-target design centers a 92 px dial in each zone, the Robot disc is 90 px (±65 px placement slack remains), and three 280×240 zones cannot fit the short-edge arrangement around the corner cluster without Tires/Cooling overlap. The legacy `TrancheOneSettings.serviceHalfSize = (180, 150)` is not used by `RacePrototype` for these strategy targets and must not be used for their visuals.

Targets are placed per seat around the corner cluster (issue #85, validated against every constraint below): Call Pit against the seat's short edge near the player-region boundary, Tires between Call Pit and the corner cluster, and Cooling along the long edge beyond the throttle arc. Tires and Cooling centers are explicit settings values, no longer derived from a horizontal offset off Call Pit.

| Target | Runtime center | IMGUI rectangle |
| --- | --- | --- |
| Orange Call Pit | `(1790, 430)` | `x 1680, y 540, w 220, h 220` |
| Orange Tires | `(1620, 310)` | `x 1510, y 660, w 220, h 220` |
| Orange Cooling | `(1370, 140)` | `x 1260, y 830, w 220, h 220` |
| Purple Call Pit | `(130, 650)` | `x 20, y 320, w 220, h 220` |
| Purple Tires | `(300, 770)` | `x 190, y 200, w 220, h 220` |
| Purple Cooling | `(550, 940)` | `x 440, y 30, w 220, h 220` |

Purple targets are the exact 180° mirror of Orange (`x → 1920 − x`, `y → 1080 − y`).

Additional immovable mechanical constraints:

| Constraint | Runtime value |
| --- | ---: |
| Player-region boundary | `y = 540` in runtime coordinates |
| Target orientation | `0°` |
| Alignment tolerance | `±15°` |
| Call Pit hold | `0.75 s` |
| Tires/Cooling hold | `1.5 s` |
| Rematch hold | `1.0 s` with both Ships at Brake |

Placement invariants every target satisfies (numerically checked in issue #85):

- Every zone rectangle lies fully on the board and fully inside its player's region half.
- Tires and Cooling zones do not overlap one another.
- Every zone clears the seat's corner cluster (throttle arc band reach ≈ 272 px from the corner point).
- Call Pit's outer edge sits within 20 px of its short board edge.

Call Pit is active before service; Tires and Cooling are active only while parked. Therefore, approximate overlap between the Call Pit rectangle and a service rectangle is not simultaneous interactive ambiguity. Tires and Cooling do not overlap one another.

The entire active mechanical boundary must be visible. An inner label or quieter fill may improve legibility, but it may not imply a smaller target. The established centers, half-sizes, orientation, tolerance, hold behavior, activation rules, and player-region behavior do not change in Tranche 4.

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
- Throttle treatment: corner arc plus explicit state word, with the Ship as the needle (frame `17:14`).
- Meter construction: fixed-size round dials, fuller = worse, dial doubles as the parked repair target (frame `35:2`).
- Full state inventory and semantic copy purpose.
- Exact physical action geometry and mechanics (issue #85 revision).
- Opposite-edge orientation and non-color redundancy.

Resolved through the three owner-reviewed capture rounds in Issue #77:

- Exact arc radius, band thickness, and dial sizing.
- Exact placement of lap/place/stop, the state word, and the dominant instruction.
- Safe-edge inset, line breaks, and type sizing within IMGUI.
- Normal, warning, critical, missing-Piece, active, and finished visual variants.
- How to reduce target fill/obstruction while retaining the full boundary.
- Exact duration and treatment of brief global transition messages.
- (Deferred beyond Tranche 4: critical red-flash and repair animation.)

No code-review decision may move a physical target, add a second dominant instruction, populate inactive corners, or reduce the central race to accommodate player chrome.

## Verification and tranche evidence

- Issue #76 tests semantic priority, mutually exclusive instructions, deterministic state fixtures, mirrored/rotated transforms, bounds, and runtime-derived target alignment.
- Issue #77 produces 1920×1080 captures for stable racing, pressure/service states, and lifecycle/results, with an owner stop after each round.
- Issue #78 verifies the full state matrix on Android.
- Issue #79 runs the two-person physical Board readability gate with players swapping sides.

The Tranche 4 exit criterion remains: two players can complete and rematch the full race from opposite sides without developer assistance interpreting the UI.
