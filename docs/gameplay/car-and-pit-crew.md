# Driving Ship and Pit Robot interaction

This is the working interaction specification for the first prototype. Values and gestures are hypotheses, not final requirements.

## Piece identity

Each player is associated with two distinct Glyph identities:

- **Driving Ship:** continuous Brake / Drive / Boost input.
- **Pit Robot:** pit request and service.

The input layer must preserve these associations when contacts disappear and return. It must also surface clear recovery when Pieces are swapped, unrecognized, or placed in another player's area.

## Driving Ship

The current control hypothesis is rotation-only:

- **Brake · 0%:** no power and the current strong deceleration curve.
- **Drive · 50%:** provisional normal pace near the fresh-tire corner-safe speed.
- **Boost · 100%:** provisional maximum acceleration/speed with greater heat and corner risk.
- Rotate through three broad player-relative sectors with hysteresis. Active touch does not affect the command.
- Keep the physical Piece in a personal control region while the digital car travels around the track.

Speed fractions, acceleration, drag, and Brake deceleration are tunable hypotheses. Input should be tolerant of minor orientation noise. Missing, invalid, duplicated, or wrong-region driving input safely commands Brake. The game should communicate the semantic stop, actual speed, heat, and unsafe corner entry without requiring small text.

## Pit Robot

Between stops, the Robot answers the tactical question of when to stop. Each player has one mirrored **Call Pit** region. Entering or newly placing the Robot there during Racing, aligning it, and holding steady for `0.75` seconds deliberately requests a stop at the next start/finish crossing. A Robot already sitting there before `GO` cannot request automatically. Active touch is ignored. Heat can make Cooling attractive, but neither the conditions nor the UI can force that request.

Once the car is parked, the interaction changes to the question of what to service. The Call Pit target deactivates and distinct Tires and Cooling repair regions activate in the same player area. Moving the Robot into a repair region selects that service; aligning and holding steady for `1.5` seconds performs it. The choice may change before completion, but changing regions, moving out of alignment, or losing valid input resets progress and leaves the car parked.

## Race-state flow

1. **Grid:** associate Pieces and confirm players.
2. **Racing:** the Ship selects Brake / Drive / Boost; placing, aligning, and holding the Robot in Call Pit requests a stop.
3. **Pit requested:** the car commits to pit entry at the next start/finish crossing; repair choices remain inactive.
4. **In pit:** driving input is suspended while the player chooses Tires or Cooling and completes the aligned hold.
5. **Pit exit:** control returns cleanly to the Driving Ship.
6. **Finished:** Pieces remain associated for results and rematch.

Pit entry and exit are presented as continuous motion, not phase teleports. The deterministic pit snapshot exposes normalized entry/exit progress while the renderer maps that progress through the start/finish pit line, lane entry, the racer's own box, lane exit, and back to the same start/finish merge. The car remains fixed in its box for the entire service decision and repair interval. This presentation path does not advance race distance or change pit timing.

## First prototype systems

- One oval or rounded-rectangle track.
- Two lanes or one path with simple passing behavior.
- Motor heat driven by sustained throttle and corner entry.
- Tire wear driven by speed through corners.
- One mandatory pit stop.
- Tires, cooling, and repair represented initially by only the smallest set needed to test decisions.

## Tranche 3 provisional strategy rules

The first pit-strategy playtest deliberately uses only two condition axes and two mutually exclusive services:

- Sustained throttle builds motor heat; coasting, braking, and pit time cool it. High heat limits performance but never automatically requests or forces a pit stop.
- Fast corner traversal builds tire wear. Worn tires reduce the safe corner-speed margin.
- **Tires** restores tire condition without cooling the motor.
- **Cooling** restores motor condition without changing tire wear.
- Each racer must complete at least one service during the five-lap race. A racer that reaches the nominal finish without service remains unclassified, continues under player control, and must complete service before it can be classified at a later start/finish crossing.
- When that later service makes a racer eligible at or beyond the nominal finish, its car completes the visible exit path to start/finish and receives an explicit per-racer `FINISHED` state. The other racer may continue normally.
- Players may make additional stops, but every stop has a meaningful time cost.

The Pit Robot deliberately requests pit entry first, then selects one of the two services after the car is parked and performs the same placement/align/hold action. Overheating should create a strong reason to call the pit and choose Cooling, not remove either player decision. Repair, forced pit stops, additional services, and final tuning are deferred until this smallest loop is physically tested.

## Feedback requirements

- Every recognized Piece receives an immediate matching highlight.
- Rotation changes produce visible response without jitter.
- Pit regions clearly indicate valid, invalid, and completed placement.
- A lost contact never silently applies stale throttle.
- Color is reinforced with shape or iconography for accessibility.

## Recovery cases

The prototype must deliberately test:

- Driving Ship removed while Boosting.
- Pit Robot removed during service.
- A Piece placed in the wrong player's control area.
- Two Pieces crossing or touching simultaneously.
- Lost recognition followed by reacquisition.
- Player abandoning a race or handing control to someone else.

## Prototype success signals

- New players understand the two roles after a short demonstration.
- Players can finish a race without frequent input correction.
- Pit actions feel deliberate but not fiddly.
- Players make different pit decisions based on race state.
- The Pit Robot feels necessary rather than ornamental.
