# Car and Pit Crew interaction

This is the working interaction specification for the first prototype. Values and gestures are hypotheses, not final requirements.

## Piece identity

Each player is associated with two distinct Glyph identities:

- **Car Piece:** continuous driving input.
- **Pit Crew Piece:** preparation, telemetry, and pit service.

The input layer must preserve these associations when contacts disappear and return. It must also surface clear recovery when Pieces are swapped, unrecognized, or placed in another player's area.

## Car Piece

The initial control hypothesis is:

- Touch and hold to apply throttle.
- Rotate to set the requested throttle amount.
- Release to coast or brake.
- Keep the physical Piece in a personal control region while the digital car travels around the track.

Throttle input should be smoothed and tolerant of minor orientation noise. The game should communicate requested throttle, actual speed, heat, and unsafe corner entry without requiring small text.

## Pit Crew Piece

Between stops, the Crew Piece prepares the next service. Candidate responsibilities include:

- Inspecting detailed telemetry while touched.
- Selecting tires, cooling, or repair.
- Charging or preparing one service at a time.
- Confirming pit entry or the next pit plan.

During a stop, the player moves the Crew Piece into the relevant service region. The first prototype should test one short tactile action, such as rotating through a target arc or aligning and holding the Piece.

## Race-state flow

1. **Grid:** associate Pieces and confirm players.
2. **Racing:** Car Piece controls throttle; Crew Piece may prepare service.
3. **Pit requested:** the car commits to pit entry at the next valid opportunity.
4. **In pit:** driving input is suspended or limited while Crew interaction determines service time.
5. **Pit exit:** control returns cleanly to the Car Piece.
6. **Finished:** Pieces remain associated for results and rematch.

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
- Players may make additional stops, but every stop has a meaningful time cost.

The Crew Piece will select one of the two services, deliberately request pit entry, and perform the proven align-and-hold action during service. Overheating should create a strong reason to choose Cooling, not remove the player's decision. Repair, forced pit stops, additional services, and final tuning are deferred until this smallest loop is physically tested.

## Feedback requirements

- Every recognized Piece receives an immediate matching highlight.
- Rotation changes produce visible response without jitter.
- Pit regions clearly indicate valid, invalid, and completed placement.
- A lost contact never silently applies stale throttle.
- Color is reinforced with shape or iconography for accessibility.

## Recovery cases

The prototype must deliberately test:

- Car Piece removed while accelerating.
- Crew Piece removed during service.
- A Piece placed in the wrong player's control area.
- Two Pieces crossing or touching simultaneously.
- Lost recognition followed by reacquisition.
- Player abandoning a race or handing control to someone else.

## Prototype success signals

- New players understand the two roles after a short demonstration.
- Players can finish a race without frequent input correction.
- Pit actions feel deliberate but not fiddly.
- Players make different pit decisions based on race state.
- The Crew Piece feels necessary rather than ornamental.
