using System;
using System.Collections.Generic;
using System.Linq;
using Board.Input;
using BoardRacing.Domain;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BoardRacing.Runtime
{
    public sealed class RacePrototype : MonoBehaviour
    {
        private TrancheOneSettings inputSettings;
        private TrancheTwoSettings raceSettings;
        private TrancheThreeSettings strategySettings;
        private IPlayerInputProvider boardProvider, fallbackProvider, activeProvider;
        private RaceSimulation simulation;
        private IReadOnlyList<PlayerControlSnapshot> controls = Array.Empty<PlayerControlSnapshot>();
        private readonly Dictionary<PlayerId, CrewStrategyAdapter> crewAdapters =
            new Dictionary<PlayerId, CrewStrategyAdapter>();
        private readonly Dictionary<PlayerId, CrewStrategyOutput> crewOutputs =
            new Dictionary<PlayerId, CrewStrategyOutput>();
        private float accumulator;
        // The sim state one fixed step behind Snapshot: OnGUI draws the blend of
        // the two by the accumulator fraction (SnapshotInterpolation, issue #89).
        private RaceSnapshot previousSnapshot;
        private GUIStyle title, carLabel, warning, small, cue;
        // The static racing surface (track, pit complex) is a world-space mesh
        // since issue #86 round 1, and the car bodies since round 2; the seat
        // clusters are a uGUI canvas since round 3. OnGUI keeps the car-riding
        // labels, pit text, center overlays, and dev readouts.
        private RaceSurfaceRenderer surface;
        private RaceHud hud;
        // Everything the current track IS — racing line, pit complex, race
        // length — comes from one authored artifact (issue #107 phase 1).
        private CourseDefinition course;
        // Which artifact that is comes from the between-race tap-to-cycle
        // choice (issue #107 phase 5).
        private CourseSelection courseSelection;
        // One presentation state per frame, computed at the end of Update: the
        // world-space cars and every OnGUI event (IMGUI raises several per
        // frame) read the same blend instead of each rebuilding it.
        private RaceSnapshot presentedRace;
        private RaceUiModel presentedUi;
#if UNITY_EDITOR
        private int previewScenarioIndex = -1;
        // Set by the capture harness (BoardRacingCaptures) so review captures show
        // only the player-facing UI — no provider/preview labels, no raw readouts.
        public static bool SuppressEditorDiagnostics;
#endif

        // Player accents from the design authority (frame 40:23): P1 orange, P2 purple.
        private static readonly Color PlayerOneAccent = new Color(.92f, .39f, .12f);
        private static readonly Color PlayerTwoAccent = new Color(.48f, .28f, .72f);
        // Shared geometry for the pause and race-finished overlays in 1920×1080 GUI
        // space. The button rect doubles as the touch hit-target polled in Update:
        // the project runs the new Input System only, so IMGUI never receives
        // pointer events in a player build.
        private static readonly Rect PausePanel = new Rect(460f, 430f, 1000f, 290f);
        private static readonly Rect PauseNewRaceButton = new Rect(770f, 560f, 380f, 70f);
        // The course chip under the button: shows what the next race runs on,
        // tap to cycle the catalog (issue #107 phase 5).
        private static readonly Rect NextCourseChip = new Rect(770f, 648f, 380f, 48f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindObjectOfType<RacePrototype>() == null)
                new GameObject("Board Racing Race Prototype").AddComponent<RacePrototype>();
        }

        private void Awake()
        {
            // Unity's mobile default caps rendering at 30 fps, which read as
            // visible car stepping in the rounds 1+2 hardware review (#86).
            // Presentation interpolates every rendered frame, so rendering at
            // the panel's native refresh is pure smoothness.
            int refresh = Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value);
            Application.targetFrameRate = refresh > 0 ? refresh : 60;
            inputSettings = Resources.Load<TrancheOneSettings>("TrancheOneSettings") ?? TrancheOneSettings.Defaults();
            raceSettings = Resources.Load<TrancheTwoSettings>("TrancheTwoSettings") ?? TrancheTwoSettings.Defaults();
            strategySettings = Resources.Load<TrancheThreeSettings>("TrancheThreeSettings") ?? TrancheThreeSettings.Defaults();
            boardProvider = new BoardContactInputProvider(inputSettings.ToThrottleStops(),
                inputSettings.throttleHysteresisDegrees * Mathf.Deg2Rad,
                inputSettings.playerRegionBoundaryY);
            fallbackProvider = new KeyboardInputProvider();
#if UNITY_ANDROID && !UNITY_EDITOR
            activeProvider = raceSettings.preferBoardInputOnDevice ? boardProvider : fallbackProvider;
#else
            activeProvider = fallbackProvider;
#endif
            foreach (PlayerId id in Enum.GetValues(typeof(PlayerId))) CreateCrewAdapter(id);
            AttachResetSource(activeProvider);
            courseSelection = new CourseSelection(CourseCatalog.All(raceSettings.CornerSafeSpeed));
            course = courseSelection.Current;
            BuildRace();
            hud = RaceHud.Create(RaceLayout.Create(ServiceTargetsFor(PlayerId.Player1),
                ServiceTargetsFor(PlayerId.Player2), strategySettings.serviceHalfSize),
                PlayerOneAccent, PlayerTwoAccent);
            RefreshPresentation();
            UpdateWorldCars();
        }

        // Everything owned by the course on the table: the simulation and the
        // world-space surface. Called at boot and again whenever the between-race
        // choice lands on a different course (issue #107 phase 5).
        private void BuildRace()
        {
            simulation = new RaceSimulation(course.Track,
                raceSettings.ToRules(course.Laps, strategySettings.requiredServiceCount,
                    strategySettings.ToConditionRules(raceSettings.basePace),
                    strategySettings.ToPitRules(course, raceSettings.basePace)));
            previousSnapshot = simulation.Snapshot;
            if (surface != null) Destroy(surface.gameObject);
            surface = RaceSurfaceRenderer.Create(RaceSurfaceGeometry.Build(
                simulation.Track, PitLayout(), PlayerOneAccent, PlayerTwoAccent));
            surface.AttachCar(PlayerId.Player1,
                RaceSurfaceGeometry.BuildCarBody(PlayerId.Player1, PlayerOneAccent));
            surface.AttachCar(PlayerId.Player2,
                RaceSurfaceGeometry.BuildCarBody(PlayerId.Player2, PlayerTwoAccent));
        }

        private void CreateCrewAdapter(PlayerId id)
        {
            ServiceTargets targets = ServiceTargetsFor(id);
            crewAdapters[id] = new CrewStrategyAdapter(
                new Vec2(targets.CallPit.x, targets.CallPit.y),
                new Vec2(targets.Tires.x, targets.Tires.y),
                new Vec2(targets.Fuel.x, targets.Fuel.y),
                new Vec2(strategySettings.serviceHalfSize.x, strategySettings.serviceHalfSize.y),
                strategySettings.serviceStirTurnsForFullService,
                strategySettings.pitCallHoldSeconds);
            crewOutputs[id] = default;
        }

        private ServiceTargets ServiceTargetsFor(PlayerId id) => id == PlayerId.Player1
            ? new ServiceTargets(inputSettings.playerOneServiceCenter,
                strategySettings.playerOneTiresCenter, strategySettings.playerOneFuelCenter)
            : new ServiceTargets(inputSettings.playerTwoServiceCenter,
                strategySettings.playerTwoTiresCenter, strategySettings.playerTwoFuelCenter);

        private void OnDestroy()
        {
            DetachResetSource(activeProvider);
            if (boardProvider is IDisposable disposable) disposable.Dispose();
            if (surface != null) Destroy(surface.gameObject);
            if (hud != null) Destroy(hud.gameObject);
        }

        private void Update()
        {
#if UNITY_EDITOR
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
                SetInputProvider(activeProvider == boardProvider ? fallbackProvider : boardProvider);
            if (Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame)
            {
                previewScenarioIndex++;
                if (previewScenarioIndex >= Enum.GetValues(typeof(RaceUiPreviewScenario)).Length)
                    previewScenarioIndex = -1;
            }
#endif
            controls = activeProvider.ReadSnapshots();
            PollNewRaceTouch();
            accumulator += Mathf.Min(Time.unscaledDeltaTime, .25f);
            float step = Mathf.Max(.001f, raceSettings.fixedStepSeconds);
            while (accumulator >= step)
            {
                var commands = controls.Select(control =>
                {
                    var racer = simulation.Snapshot.Racers.Single(x => x.PlayerId == control.PlayerId);
                    var crew = crewAdapters[control.PlayerId].Update(control, simulation.Snapshot.Phase, racer.Pit, step);
                    crewOutputs[control.PlayerId] = crew;
                    bool rematchConfirming = simulation.Snapshot.Phase == RacePhase.Finished &&
                        control.Car.Present && control.Throttle == ThrottleStep.Brake;
                    return new RacerCommand(control.PlayerId, control.Throttle, control.Car.Present, rematchConfirming,
                        crew.SelectedService, crew.RequestPit, crew.ServiceDrain, crew.RequestExit);
                }).ToArray();
                previousSnapshot = simulation.Snapshot;
                simulation.Step(step, commands); accumulator -= step;
            }
            RefreshPresentation();
            UpdateWorldCars();
        }

        private void RefreshPresentation()
        {
            presentedRace = SnapshotInterpolation.Blend(previousSnapshot, simulation.Snapshot,
                accumulator / Mathf.Max(.001f, raceSettings.fixedStepSeconds), simulation.Track);
#if UNITY_EDITOR
            if (previewScenarioIndex >= 0)
            {
                RaceUiPreviewFrame preview = RaceUiPreviewFixtures.Create(
                    (RaceUiPreviewScenario)previewScenarioIndex, simulation.Track,
                    simulation.Rules.Conditions, course.Laps);
                presentedRace = preview.Race;
                presentedUi = preview.Ui;
                return;
            }
#endif
            presentedUi = RaceUiModelBuilder.Build(presentedRace, controls, crewOutputs,
                simulation.Rules.Conditions, course.Laps);
        }

        private void LateUpdate() => hud.Apply(presentedUi);

        private void UpdateWorldCars()
        {
            foreach (var racer in presentedRace.Racers)
                surface.SetCarPose(racer.PlayerId, CarCenter(racer));
        }

        // The drawn car center: the smoothed pose plus the racing-line lateral
        // offset (suppressed once the car is physically in the pit complex).
        private Vector2 CarCenter(RacerSnapshot racer)
        {
            CarPose(racer, out Vector2 center, out Vector2 tangent);
            float lateralOffset = racer.Pit.Phase == PitPhase.OnTrack || racer.Pit.Phase == PitPhase.Requested
                ? racer.LateralOffset : 0f;
            return new Vector2(center.x - tangent.y * lateralOffset, center.y + tangent.x * lateralOffset);
        }

        // The START NEW RACE button appears only when no race is running: while
        // paused (pieces off the table, issue #90) and after the finish (issue #97).
        // It is the game's one touch control, center-table where pieces never rest.
        private void PollNewRaceTouch()
        {
            RacePhase phase = simulation.Snapshot.Phase;
            // Fed every frame so the selection sees the transition INTO the
            // overlay and arms its default exactly once (issue #107 phase 5).
            courseSelection.ObservePhase(phase);
            if (phase != RacePhase.Paused && phase != RacePhase.Finished) return;
            // On the Board every contact — fingers included — arrives through the
            // SDK's native contact pipeline, not Unity's Touchscreen, so a tap is a
            // Finger contact in its Began phase (same stream the pieces ride as
            // Glyph contacts).
            foreach (var finger in BoardInput.GetActiveContacts(BoardContactType.Finger))
                if (finger.phase == BoardContactPhase.Began &&
                    HandleOverlayTap(finger.screenPosition))
                    return;
            // Desktop editor runs have a mouse and no Board contact stream.
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                HandleOverlayTap(mouse.position.ReadValue());
        }

        // The overlay's two touch targets: START NEW RACE confirms the pending
        // course and races it; the chip below cycles the pending course.
        private bool HandleOverlayTap(Vector2 screenPosition)
        {
            Vector2 gui = new Vector2(screenPosition.x * 1920f / Screen.width,
                (Screen.height - screenPosition.y) * 1080f / Screen.height);
            if (PauseNewRaceButton.Contains(gui)) { StartNewRace(); return true; }
            if (NextCourseChip.Contains(gui)) { courseSelection.CycleNext(); return true; }
            return false;
        }

        private void StartNewRace()
        {
            CourseDefinition next = courseSelection.ConfirmNext();
            if (ReferenceEquals(next, course))
            {
                simulation.RequestNewRace();
                return;
            }
            // A different course means a different track, rules, and surface:
            // rebuild the race whole rather than teaching the simulation to
            // swap tracks mid-life.
            course = next;
            BuildRace();
            accumulator = 0f;
            RefreshPresentation();
            UpdateWorldCars();
        }

        public RaceSnapshot GetRaceSnapshot() => simulation.Snapshot;
        public CrewStrategyOutput GetCrewStrategy(PlayerId playerId) =>
            crewOutputs.TryGetValue(playerId, out var output) ? output : default;
        public void SetInputProvider(IPlayerInputProvider provider)
        {
            DetachResetSource(activeProvider);
            activeProvider = provider ?? throw new ArgumentNullException(nameof(provider));
            AttachResetSource(activeProvider);
            foreach (var adapter in crewAdapters.Values) adapter.Reset();
        }

        private void AttachResetSource(IPlayerInputProvider provider)
        {
            if (provider is IInputResetSource source) source.InputReset += OnInputReset;
        }

        private void DetachResetSource(IPlayerInputProvider provider)
        {
            if (provider is IInputResetSource source) source.InputReset -= OnInputReset;
        }

        private void OnInputReset()
        {
            foreach (var adapter in crewAdapters.Values) adapter.Reset();
            foreach (PlayerId id in crewOutputs.Keys.ToArray()) crewOutputs[id] = default;
        }

        private void EnsureStyles()
        {
            if (title != null) return;
            title = Style(42, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            carLabel = Style(22, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            warning = Style(26, FontStyle.Bold, new Color(1f, .75f, .2f), TextAnchor.MiddleCenter);
            small = Style(15, FontStyle.Bold, new Color(.87f, .9f, .94f), TextAnchor.MiddleCenter);
            cue = Style(13, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
        }

        private static GUIStyle Style(int size, FontStyle fontStyle, Color color, TextAnchor anchor) => new GUIStyle(GUI.skin.label)
        { fontSize = size, fontStyle = fontStyle, normal = { textColor = color }, alignment = anchor, wordWrap = true };

        private void OnGUI()
        {
            // Repaint only: everything draws at explicit rects, so the Layout
            // pass (and any input events — touch is polled in Update) would just
            // re-issue the same thousand draw calls for nothing. IMGUI raises
            // several events per frame and the seat HUD is draw-call bound on
            // device (#86 hardware review: 9-20 fps).
            if (Event.current.type != EventType.Repaint) return;
            EnsureStyles();
            Matrix4x4 original = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / 1920f, Screen.height / 1080f, 1f));
            RaceLayout layout = RaceLayout.Create(ServiceTargetsFor(PlayerId.Player1),
                ServiceTargetsFor(PlayerId.Player2), strategySettings.serviceHalfSize);
            RaceUiModel ui = presentedUi;
            DrawPitLabels();
            foreach (var racer in presentedRace.Racers) DrawCar(racer);
#if UNITY_EDITOR
            // The rotated glyph stands in for the physical Ship on desktop only;
            // on Board hardware the real piece sits on the uGUI seat's well ring.
            DrawShipGlyph(layout.PlayerTwo.Controller, layout.PlayerTwo, PlayerTwoAccent,
                ui.PlayerTwo.ShipPresent);
            DrawShipGlyph(layout.PlayerOne.Controller, layout.PlayerOne, PlayerOneAccent,
                ui.PlayerOne.ShipPresent);
#endif
            DrawCenterMessage(ui, layout);
            // Development builds only: raw Ship orientation per seat so the throttle
            // mapper can be calibrated against the rendered wedges on real hardware
            // (issue #77 hardware review). Never present in release builds.
            if (Debug.isDebugBuild && !EditorDiagnosticsSuppressed())
            {
                DrawRawAngleReadout(layout.PlayerOne);
                DrawRawAngleReadout(layout.PlayerTwo);
            }
#if UNITY_EDITOR
            if (!SuppressEditorDiagnostics)
            {
                GUI.Label(new Rect(1500, 8, 412, 24),
                    (activeProvider == boardProvider ? "BOARD INPUT" : "KEYBOARD FALLBACK") + " · F1 provider", small);
                GUI.Label(new Rect(1500, 34, 412, 24), previewScenarioIndex < 0
                    ? "LIVE PRESENTATION · F2 preview"
                    : "PREVIEW: " + ((RaceUiPreviewScenario)previewScenarioIndex) + " · F2 next", small);
            }
#endif
            GUI.matrix = original;
        }

        private void DrawRawAngleReadout(PlayerLayout layout)
        {
            PlayerControlSnapshot control = controls.FirstOrDefault(x => x.PlayerId == layout.PlayerId);
            string Reading(PieceState piece) => piece.Present
                ? Mathf.RoundToInt(Mathf.Repeat(piece.OrientationRadians * Mathf.Rad2Deg, 360f)) + "°"
                : "—";
            string text = (layout.PlayerId == PlayerId.Player1 ? "▲ SHIP RAW " : "● SHIP RAW ") +
                Reading(control.Car) + " · ROBOT " + Reading(control.Crew) +
                // Frame pacing readout for the #86 motion review: rendered fps
                // vs the target the Awake unlock requested vs the sim tick.
                " · FPS " + Mathf.RoundToInt(1f / Mathf.Max(.001f, Time.smoothDeltaTime)) +
                "/" + Application.targetFrameRate +
                " · SIM " + Mathf.RoundToInt(1f / Mathf.Max(.001f, raceSettings.fixedStepSeconds)) + "Hz";
            Rect bounds = layout.Opposite ? new Rect(530f, 6f, 360f, 30f) : new Rect(1030f, 1044f, 360f, 30f);
            DrawRotatedLabel(bounds, text, layout.RotationDegrees, small, Color.white);
        }

        // The pit geometry itself is world-space mesh (RaceSurfaceGeometry); text
        // stays IMGUI until the migration settles a world-space text stack.
        private void DrawPitLabels()
        {
            Vec2 oneBox = course.Pit.PlayerOneBox, twoBox = course.Pit.PlayerTwoBox;
            GUI.Label(new Rect(oneBox.X - 70f, oneBox.Y - 32f, 140f, 64f), "▲ P1 BOX", small);
            GUI.Label(new Rect(twoBox.X - 70f, twoBox.Y - 32f, 140f, 64f), "● P2 BOX", small);
            GUI.Label(new Rect(865, 421, 190, 28), "PIT LANE", small);
        }

        private static void DrawOutline(Rect rect, float width, Color color)
        {
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, width), Texture2D.whiteTexture,
                ScaleMode.StretchToFill, true, 0, color, 0, 0);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - width, rect.width, width), Texture2D.whiteTexture,
                ScaleMode.StretchToFill, true, 0, color, 0, 0);
            GUI.DrawTexture(new Rect(rect.x, rect.y, width, rect.height), Texture2D.whiteTexture,
                ScaleMode.StretchToFill, true, 0, color, 0, 0);
            GUI.DrawTexture(new Rect(rect.xMax - width, rect.y, width, rect.height), Texture2D.whiteTexture,
                ScaleMode.StretchToFill, true, 0, color, 0, 0);
        }

        // The car body is a world-space mesh (issue #86 round 2); IMGUI keeps the
        // piece glyph, condition cues, and status labels riding the same pose.
        private void DrawCar(RacerSnapshot racer)
        {
            Vector2 carCenter = CarCenter(racer);
            float x = carCenter.x, y = carCenter.y;
            Rect rect = new Rect(x - 27f, y - 27f, 54f, 54f);
            GUI.Label(rect, racer.PlayerId == PlayerId.Player1 ? "▲" : "●", carLabel);
            DrawConditionCues(racer, x, y);
            if (racer.RecoveryRemaining > 0f) GUI.Label(new Rect(x - 100f, y - 72f, 200f, 36f), "SLOWDOWN!", warning);
            if (racer.Finished)
                GUI.Label(new Rect(x - 110f, y + 32f, 220f, 30f), "FINISHED · " + Ordinal(racer.Place), warning);
            else if (racer.Pit.Phase != PitPhase.OnTrack)
                GUI.Label(new Rect(x - 100f, y + 32f, 200f, 28f), CarPitLabel(racer.Pit), small);
        }

        private PitLanePresentationLayout PitLayout() =>
            PitLanePresentationLayout.ForCourse(course);

        private void CarPose(RacerSnapshot racer, out Vector2 position, out Vector2 tangent)
        {
            CarPresentationPose pose = PitLanePresentationMapper.From(racer, racer.Track.Position,
                TrackPresentation.SmoothHeading(simulation.Track, racer.TotalDistance), PitLayout());
            position = new Vector2(pose.Position.X, pose.Position.Y);
            tangent = new Vector2(pose.Tangent.X, pose.Tangent.Y);
        }

        private void DrawConditionCues(RacerSnapshot racer, float x, float y)
        {
            CarConditionVisualState visual = CarConditionVisualMapper.From(racer, simulation.Rules.Conditions);
            DrawConditionCue(new Rect(x - 39f, y - 42f, 32f, 24f), "F", visual.FuelLevel, true);
            DrawConditionCue(new Rect(x + 7f, y - 42f, 32f, 24f), "T", visual.TireLevel, false);
        }

        private void DrawConditionCue(Rect rect, string symbol, ConditionVisualLevel level, bool rounded)
        {
            Color color = RaceHud.ConditionColor(level);
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, color, 0,
                rounded ? rect.height * .5f : 2f);
            GUI.Label(rect, symbol + (level == ConditionVisualLevel.Critical ? "!!" :
                level == ConditionVisualLevel.Warning ? "!" : ""), cue);
        }

        private static string CarPitLabel(RacerPitSnapshot pit)
        {
            if (pit.Phase == PitPhase.Requested) return "PIT @ LINE";
            if (pit.Phase == PitPhase.Entering) return "PIT ENTRY";
            if (pit.Phase == PitPhase.InService) return pit.SelectedService == PitService.None
                ? "CAR PARKED · REPAIR OR LEAVE" : "IN BOX · " + ServiceName(pit.SelectedService);
            if (pit.Phase == PitPhase.Exiting) return "PIT EXIT";
            return string.Empty;
        }

        private static void DrawShipGlyph(CornerControllerLayout controller, PlayerLayout layout,
            Color accent, bool present)
        {
            // True Ship piece proportions (146×244 px) on the corner diagonal, nose toward
            // the board center; matches the piece overlay in the design frames.
            Rect rect = new Rect(controller.ShipWellCenter.x - 73f,
                controller.ShipWellCenter.y - 122f, 146f, 244f);
            Matrix4x4 original = GUI.matrix;
            Vector3 pivot = new Vector3(controller.ShipWellCenter.x, controller.ShipWellCenter.y, 0f);
            GUI.matrix = original * Matrix4x4.Translate(pivot) *
                Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, -45f + layout.RotationDegrees)) *
                Matrix4x4.Translate(-pivot);
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                new Color(accent.r, accent.g, accent.b, present ? .92f : .25f), 0, 40f);
            GUI.matrix = original;
        }

        private static void DrawRotatedLabel(Rect rect, string text, float rotationDegrees,
            GUIStyle style, Color color)
        {
            Color prior = GUI.contentColor;
            GUI.contentColor = color;
            DrawRotatedLabel(rect, text, rotationDegrees, style);
            GUI.contentColor = prior;
        }

        private static void DrawRotatedLabel(Rect rect, string text, float rotationDegrees,
            GUIStyle style)
        {
            Matrix4x4 original = GUI.matrix;
            Vector3 pivot = new Vector3(rect.center.x, rect.center.y, 0f);
            GUI.matrix = original * Matrix4x4.Translate(pivot) *
                Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, rotationDegrees)) *
                Matrix4x4.Translate(-pivot);
            GUI.Label(rect, text, style);
            GUI.matrix = original;
        }

        private void DrawCenterMessage(RaceUiModel ui, RaceLayout layout)
        {
            if (ui.CenterMessage == null) return;
            if (ui.CenterMessageKind == CenterMessageKind.Paused)
            {
                DrawNewRaceOverlay("RACES PAUSED", ui.CenterMessage);
                return;
            }
            if (ui.CenterMessageKind == CenterMessageKind.Winner)
            {
                // A finished race owns the center (owner decision, issue #97): the
                // winner plus the way into the next race.
                DrawNewRaceOverlay("RACE FINISHED", ui.CenterMessage);
                return;
            }
            GUI.Label(layout.CenterOverlayBounds, ui.CenterMessage, title);
        }

        // Presentation only — the button's tap is polled in PollNewRaceTouch.
        private void DrawNewRaceOverlay(string heading, string subLine)
        {
            GUI.DrawTexture(PausePanel, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                new Color(.03f, .04f, .06f, .93f), 0, 12f);
            DrawOutline(PausePanel, 2f, new Color(.62f, .68f, .74f, .8f));
            GUI.Label(new Rect(PausePanel.x, PausePanel.y + 16f, PausePanel.width, 52f),
                heading, title);
            GUI.Label(new Rect(PausePanel.x, PausePanel.y + 76f, PausePanel.width, 40f),
                subLine, warning);
            GUI.DrawTexture(PauseNewRaceButton, Texture2D.whiteTexture, ScaleMode.StretchToFill,
                true, 0, new Color(.14f, .2f, .3f), 0, 10f);
            DrawOutline(PauseNewRaceButton, 3f, Color.white);
            GUI.Label(PauseNewRaceButton, "START NEW RACE", carLabel);
            // The tap-to-cycle course chip (issue #107 phase 5): quieter chrome
            // than the button — it informs by default, invites a tap to change.
            GUI.DrawTexture(NextCourseChip, Texture2D.whiteTexture, ScaleMode.StretchToFill,
                true, 0, new Color(.09f, .12f, .18f), 0, 8f);
            DrawOutline(NextCourseChip, 2f, new Color(.62f, .68f, .74f, .8f));
            GUI.Label(NextCourseChip,
                "NEXT COURSE: " + courseSelection.Next.Name.ToUpperInvariant(), carLabel);
        }

        private static bool EditorDiagnosticsSuppressed()
        {
#if UNITY_EDITOR
            return SuppressEditorDiagnostics;
#else
            return false;
#endif
        }

        private static string ServiceName(PitService service) =>
            service == PitService.Tires ? "TIRES" : service == PitService.Fuel ? "FUEL" : "NO SERVICE";

        private static string Ordinal(int place) => RaceUiModelBuilder.Ordinal(place);
    }
}
