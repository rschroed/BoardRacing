# Roadmap

Work is divided into gated tranches. Each tranche answers one major risk before the project invests in the next.

## 0. Development runway

**Goal:** Establish a reproducible Unity-to-Board development loop before implementing gameplay.

**Deliverables**

- Board Developer Portal and SDK access.
- A selected and pinned Unity editor version.
- Unity Android Build Support with the required SDK, NDK, and OpenJDK tooling.
- A committed Unity project with baseline project and package configuration.
- The Board Unity SDK imported and the project setup wizard completed.
- The Board Arcade Piece Set Model downloaded and configured.
- The Board input sample running in the Unity simulator.
- `board-connect` installed and its local prerequisites documented.
- A successful Android APK build from a fresh checkout.
- When hardware is available, a paired Board that can install, launch, and stream logs from the sample APK.

**Required exit criterion:** A fresh checkout opens with the documented Unity version, the Board sample responds in the simulator, and the project produces a valid Android APK by following the repository instructions.

**Hardware validation:** When a physical Board is available, the APK installs, launches, and produces readable logs through Board Connect. If hardware is unavailable during this tranche, record that validation as the first blocking check in Tranche 1 rather than holding up simulator-based work.

**Initial sign-off:** The [Tranche 0 clean-checkout rehearsal](testing/tranche-0-rehearsal.md) and [physical Board validation](testing/physical-board-validation.md) passed on July 12, 2026.

## 1. Physical-control proof

**Status:** Passed July 12, 2026. The deterministic input boundary, keyboard fallback, Board SDK simulator, Android deployment, and two-person physical hardware gate all passed. Both players completed ten simultaneous Car cycles and ten simultaneous Ship pit cycles with zero stale commands, false completions, role swaps, cross-player commands, or assisted recoveries. One brief unassigned-Glyph warning self-recovered safely; see the [Tranche 1 validation record](testing/tranche-1-validation.md).

**Goal:** Determine whether two-Piece control feels reliable and natural.

**Deliverables**

- A Board Racing input-provider boundary supporting Board contacts and mouse/keyboard fallback.
- Reliable player association for distinct Car and Pit Crew Pieces.
- Car Piece throttle-mapping experiments.
- Crew Piece placement and rotation experiments.
- Safe lost-contact, removal, and reacquisition behavior.
- Two-player simultaneous interaction testing.
- Simulator-to-hardware behavior comparison when hardware is available.

**Exit criterion:** On physical hardware, two players can reliably control cars and complete a basic pit action without frequent tracking failures or developer explanation. Simulator work may begin earlier, but the tranche cannot pass without hardware evidence.

## 2. Racing proof

**Status:** Passed July 13, 2026. The deterministic simulation, five-lap lifecycle, automatic overtaking, mirrored presentation, automated suites, Android deployment, and two-person physical playtest passed. See the [Tranche 2 racing-proof record](testing/tranche-2-racing-proof.md).

**Goal:** Determine whether the core slot-car race is fun before adding strategy.

**Deliverables**

- One placeholder track and spline-based car movement.
- Acceleration, coasting, braking, and corner-speed consequences.
- Automatic collision-free overtaking with presentation-only lateral separation; player-controlled lane changes remain deferred.
- Starting grid, laps, finish order, and restart.
- Deterministic scripted throttle traces for repeatable testing.

Player-facing solo play and AI opponents are deferred to Tranche 4 so this gate remains focused on the proven two-human Car Piece interaction.

**Exit criterion:** A five-lap race is understandable and competitive using the Car Piece alone.

## 3. Pit-crew proof

**Status:** Refreshed physical candidate deployed July 13, 2026. The first physical-gate attempt confirmed simultaneous racing but exposed unclear pit affordance, finicky touch/release actions, and a reversing pit exit. Issues #65–#68 swapped the physical roles, introduced touch-free Ship `BRAKE / DRIVE / BOOST` control and Robot placement/alignment/hold pit actions, and added a smooth presentation-only return lane. PR #70 corrected title clearance found in the first deployed screenshot. Deterministic strategy, continuous pit presentation, balance, the complete 78-test automated/simulator pass, and the exact-candidate Android deployment are recorded in the [Tranche 3 proof](testing/tranche-3-pit-crew-proof.md) and [simulator/Android validation](testing/tranche-3-simulator-android-validation.md). The tranche remains in progress until the two-person physical gate in [Issue #49](https://github.com/rschroed/BoardRacing/issues/49) passes.

**Goal:** Determine whether the second Piece deepens the race.

**Deliverables**

- Tire wear and motor heat.
- Pit entry, service, and exit.
- Robot service selection and tactile placement/alignment action.
- Condition and service feedback.
- First-pass strategy balancing.

**Exit criterion:** Players voluntarily make different pit decisions and regard the Robot pit control as essential.

## 4. Complete social game

**Goal:** Turn the prototype into a repeatable group experience.

**Deliverables**

- 1–4 human players and AI opponents.
- Profile and guest selection.
- Race setup, tutorial, results, rematch, and player rotation.
- Tournament or championship structure.
- Pause, quit, and save integration.

**Exit criterion:** A new group can launch, understand, finish, and replay the game without developer assistance.

## 5. Presentation and content

**Goal:** Establish a distinctive, coherent production experience.

**Deliverables**

- Finalized visual and audio direction.
- Cars, track, pits, effects, UI, music, and sound.
- Stable target-device performance.
- Additional tracks or variants justified by playtesting.

**Exit criterion:** The game is commercially presentable and remains readable from all four sides.

## 6. Hardening and submission

**Goal:** Produce a reliable release candidate.

**Deliverables**

- Long-session, simultaneous-input, interruption, and recovery testing.
- Accessibility and age-range usability passes.
- Save and update compatibility testing.
- Submission assets, documentation, and compliance review.

**Exit criterion:** The release candidate passes the written test matrix and Board submission requirements without known critical defects.

## Investment rule

Do not invest heavily in final art, multiple tracks, championship content, or marketing production until Tranches 1–3 pass real playtests.
