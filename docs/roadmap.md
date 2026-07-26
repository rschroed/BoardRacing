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
- Automatic collision-free overtaking with presentation-only lateral separation; player-controlled lane changes remain deferred. **Superseded in Tranche 5 (issue #147):** lateral position is now modeled, so a car's line costs it real distance against the corner and cars cannot drive through each other. Line choice stays automatic and player-controlled lane changes stay deferred, so what this criterion was gating — a race that is understandable and competitive on the Car Piece alone — is unchanged.
- Starting grid, laps, finish order, and restart.
- Deterministic scripted throttle traces for repeatable testing.

Player-facing solo play and AI opponents are deferred to Tranche 5 so this gate remains focused on the proven two-human Car Piece interaction.

**Exit criterion:** A five-lap race is understandable and competitive using the Car Piece alone.

## 3. Pit-crew proof

**Status:** Passed July 13, 2026. The first physical attempt exposed unclear pit affordance, finicky touch/release actions, and a reversing pit exit. Issues #65–#68 then swapped the physical roles, introduced touch-free Ship `BRAKE / DRIVE / BOOST` control and Robot placement/alignment/hold pit actions, and added a smooth presentation-only return lane. In the refreshed two-player run, both racers completed the required pit loop, classified, and finished; the owner confirmed the interaction works and explicitly deferred remaining tightening to a real UI pass. Deterministic strategy, continuous pit presentation, balance, the 78-test automated/simulator pass, Android deployment, and physical sign-off are recorded in the [Tranche 3 proof](testing/tranche-3-pit-crew-proof.md), [simulator/Android validation](testing/tranche-3-simulator-android-validation.md), and [Issue #49](https://github.com/rschroed/BoardRacing/issues/49).

**Goal:** Determine whether the second Piece deepens the race.

**Deliverables**

- Tire wear and motor heat.
- Pit entry, service, and exit.
- Robot service selection and tactile placement/alignment action.
- Condition and service feedback.
- First-pass strategy balancing.

**Exit criterion:** Players voluntarily make different pit decisions and regard the Robot pit control as essential.

## 4. Wireframe experience proof

**Status:** Passed July 19, 2026. The approved [wireframe UI contract](gameplay/wireframe-ui.md) was implemented and validated end to end: automated suites (97 EditMode / 13 PlayMode), the 11-state Android capture matrix, and live two-player races on the physical Board. Gate-week hardware findings — the clear-table pause (#90), the mandatory-stop drop (#92), the final-lap pit call (#95), and the post-race restart (#97) — were each fixed, redeployed, and rerun on the table before owner sign-off. See the [Tranche 4 wireframe proof](testing/tranche-4-wireframe-proof.md) and [state-matrix validation](testing/tranche-4-state-matrix-validation.md). Overlay text orientation and finish-line label crowding are accepted limitations deferred to the real UI pass.

**Goal:** Prove that the established two-player race can be read and operated as a coherent player experience.

**Deliverables**

- A complete inventory of grid, countdown, racing, warning, pit, split-finish, results, and rematch states.
- Approved low-fidelity wireframes covering the full state matrix (historically
  approved in Figma; new visual design work uses Paper).
- A user-facing hierarchy with one dominant next action per player.
- Physical action affordances aligned exactly to the established Call Pit, Tires, and Cooling hit regions.
- Results and rematch treatment that works from opposite table sides.
- Android and physical-Board readability evidence at the fixed 1920×1080 target.

**Exit criterion:** Two players can complete and rematch the full race from opposite sides without developer assistance interpreting the UI.

## 5. Complete social game

**Status:** In progress. The race itself is complete for one to four players — explicit rosters, four-car pits, seat activation and Piece claiming, the setup loop, results and rematch (#124, #133, #134, #135, #136, #139). Player identity, including guests, is handled by the platform's own profile selectors rather than reimplemented, so that deliverable was met by not building it. Simulation fidelity work that came out of four-player racing closed alongside (#143, #147, #149). Onboarding, player rotation, AI opponents, tournament structure, and save integration remain.

**Sequencing (July 25, 2026):** The onboarding deliverable is deliberately taken *after* Tranche 6 establishes its visual language, not before. A tutorial teaches players to read the screen, and the screen is about to change substantially; built first, it would be built twice. Two known readability defects are already waiting on the same decisions — a parked finisher reads as a car being serviced, and the results overlay covers the pit complex — and a tutorial would otherwise have to explain around both. This tranche therefore closes after Tranche 6 opens, and its exit criterion is judged then.

**Goal:** Turn the prototype into a repeatable group experience.

**Deliverables**

- 1–4 human players and AI opponents.
- Profile and guest selection.
- Race setup, tutorial, results, rematch, and player rotation.
- Tournament or championship structure.
- Pause, quit, and save integration.

**Exit criterion:** A new group can launch, understand, finish, and replay the game without developer assistance.

## 6. Presentation and content

**Status:** Opened July 25, 2026, ahead of Tranche 5 closing — see the sequencing note there. The investment rule below is discharged: Tranche 4 proved the two-player experience readable on hardware on July 19.

**Constraints carried in**

- **Car and track dimensions are load-bearing, not drawing choices.** Since #147 the separation guarantees are expressed in them: 54×26 bodies, ±16 lanes on a 64 px ribbon, 114 px of grid depth, a 62 px following gap. Restyling a silhouette is free; changing car size or ribbon width re-opens the body-clearance maths and the corner path-cost tuning, and `LateralModelTests` fails when they stop holding.
- **Some screens do not exist yet.** Onboarding, rotation, tournament and save/resume flows are unbuilt, so this tranche can finish the screens that exist but cannot finalize a system for screens it has never seen. Aim it at the visual language — type, color, spacing, component vocabulary — so the unbuilt flows inherit it later, rather than at finishing every screen.
- **"Additional tracks justified by playtesting" is thinner than it reads.** With AI opponents unbuilt, playtest evidence comes only from live two-to-four-human sessions.

**Goal:** Establish a distinctive, coherent production experience.

**Deliverables**

- Finalized visual and audio direction.
- Cars, track, pits, effects, UI, music, and sound.
- Stable target-device performance.
- Additional tracks or variants justified by playtesting.

**Exit criterion:** The game is commercially presentable and remains readable from all four sides.

## 7. Hardening and submission

**Goal:** Produce a reliable release candidate.

**Deliverables**

- Long-session, simultaneous-input, interruption, and recovery testing.
- Accessibility and age-range usability passes.
- Save and update compatibility testing.
- Submission assets, documentation, and compliance review.

**Exit criterion:** The release candidate passes the written test matrix and Board submission requirements without known critical defects.

## Investment rule

Do not invest heavily in final art, multiple tracks, championship content, or marketing production until Tranche 4 proves the complete two-player experience is readable on physical hardware.

**Discharged July 19, 2026**, when Tranche 4 passed on hardware. Recorded rather than deleted: the rule is why the prototype ran on wireframe visuals for four tranches, and the reasoning stands if a later tranche is ever tempted to spend ahead of its proof.
