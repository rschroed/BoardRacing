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
        private GUIStyle title, carLabel, warning, small, cue, zoneLabel, sectorLabel, dialValue;
        // The static racing surface (track, pit complex) is a world-space mesh
        // since issue #86 round 1, and the car bodies since round 2; OnGUI keeps
        // the HUD and all text.
        private RaceSurfaceRenderer surface;
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
        // Zone/label palette from the design authority (frame 40:23): ghosted chrome,
        // Fuel's warm hue (the frame still labels this dial HEAT — Figma update pending
        // the owner's Fuel decision, 2026-07-19), Tires' green hue.
        private static readonly Color GhostColor = new Color(.4f, .44f, .5f, .55f);
        private static readonly Color FuelLabelColor = new Color(.95f, .55f, .2f);
        private static readonly Color TiresLabelColor = new Color(.35f, .72f, .5f);
        // Pit complex re-derived from the Wedge top straight (issue #88): entry
        // ramps off the start/finish line, the lane parallels the straight inside
        // the loop, and the exit rejoins the straight at pitExitRejoinDistance.
        private static readonly Vec2 PitEntry = new Vec2(680f, 455f);
        private static readonly Vec2 PlayerOnePitBox = new Vec2(860f, 455f);
        private static readonly Vec2 PlayerTwoPitBox = new Vec2(1120f, 455f);
        private static readonly Vec2 PitExit = new Vec2(1353f, 455f);
        // The lane blends onto the track just before the rejoin sample — no return
        // trip: the simulation resumes the car where the pit lane physically ends.
        private static readonly Vec2 PitMergeApproach = new Vec2(1283f, 452f);
        // Shared geometry for the pause and race-finished overlays in 1920×1080 GUI
        // space. The button rect doubles as the touch hit-target polled in Update:
        // the project runs the new Input System only, so IMGUI never receives
        // pointer events in a player build.
        private static readonly Rect PausePanel = new Rect(460f, 430f, 1000f, 230f);
        private static readonly Rect PauseNewRaceButton = new Rect(770f, 560f, 380f, 70f);

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
            simulation = new RaceSimulation(TrackCatalog.Wedge(raceSettings.cornerSafeSpeed),
                raceSettings.ToRules(strategySettings.requiredServiceCount, strategySettings.ToConditionRules(),
                    strategySettings.ToPitRules()));
            previousSnapshot = simulation.Snapshot;
            surface = RaceSurfaceRenderer.Create(RaceSurfaceGeometry.Build(
                simulation.Track, PitLayout(), PlayerOneAccent, PlayerTwoAccent));
            surface.AttachCar(PlayerId.Player1,
                RaceSurfaceGeometry.BuildCarBody(PlayerId.Player1, PlayerOneAccent));
            surface.AttachCar(PlayerId.Player2,
                RaceSurfaceGeometry.BuildCarBody(PlayerId.Player2, PlayerTwoAccent));
            RefreshPresentation();
            UpdateWorldCars();
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
                    simulation.Rules.Conditions, raceSettings.laps);
                presentedRace = preview.Race;
                presentedUi = preview.Ui;
                return;
            }
#endif
            presentedUi = RaceUiModelBuilder.Build(presentedRace, controls, crewOutputs,
                simulation.Rules.Conditions, raceSettings.laps);
        }

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
            if (phase != RacePhase.Paused && phase != RacePhase.Finished) return;
            // On the Board every contact — fingers included — arrives through the
            // SDK's native contact pipeline, not Unity's Touchscreen, so a tap is a
            // Finger contact in its Began phase (same stream the pieces ride as
            // Glyph contacts).
            foreach (var finger in BoardInput.GetActiveContacts(BoardContactType.Finger))
                if (finger.phase == BoardContactPhase.Began &&
                    ButtonContains(finger.screenPosition))
                {
                    simulation.RequestNewRace();
                    return;
                }
            // Desktop editor runs have a mouse and no Board contact stream.
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame &&
                ButtonContains(mouse.position.ReadValue()))
                simulation.RequestNewRace();
        }

        private static bool ButtonContains(Vector2 screenPosition)
        {
            Vector2 gui = new Vector2(screenPosition.x * 1920f / Screen.width,
                (Screen.height - screenPosition.y) * 1080f / Screen.height);
            return PauseNewRaceButton.Contains(gui);
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
            zoneLabel = Style(13, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            sectorLabel = Style(14, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            dialValue = Style(20, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
        }

        private static GUIStyle Style(int size, FontStyle fontStyle, Color color, TextAnchor anchor) => new GUIStyle(GUI.skin.label)
        { fontSize = size, fontStyle = fontStyle, normal = { textColor = color }, alignment = anchor, wordWrap = true };

        private void OnGUI()
        {
            EnsureStyles();
            Matrix4x4 original = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / 1920f, Screen.height / 1080f, 1f));
            RaceLayout layout = RaceLayout.Create(ServiceTargetsFor(PlayerId.Player1),
                ServiceTargetsFor(PlayerId.Player2), strategySettings.serviceHalfSize);
            RaceUiModel ui = presentedUi;
            DrawPitLabels();
            DrawCrewRegions(layout, ui);
            foreach (var racer in presentedRace.Racers) DrawCar(racer);
            DrawCornerController(ui.PlayerTwo, layout.PlayerTwo, PlayerTwoAccent);
            DrawCornerController(ui.PlayerOne, layout.PlayerOne, PlayerOneAccent);
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
                Reading(control.Car) + " · ROBOT " + Reading(control.Crew);
            Rect bounds = layout.Opposite ? new Rect(530f, 6f, 360f, 30f) : new Rect(1030f, 1044f, 360f, 30f);
            DrawRotatedLabel(bounds, text, layout.RotationDegrees, small, Color.white);
        }

        // The pit geometry itself is world-space mesh (RaceSurfaceGeometry); text
        // stays IMGUI until the migration settles a world-space text stack.
        private void DrawPitLabels()
        {
            GUI.Label(new Rect(PlayerOnePitBox.X - 70f, PlayerOnePitBox.Y - 32f, 140f, 64f),
                "▲ P1 BOX", small);
            GUI.Label(new Rect(PlayerTwoPitBox.X - 70f, PlayerTwoPitBox.Y - 32f, 140f, 64f),
                "● P2 BOX", small);
            GUI.Label(new Rect(865, 421, 190, 28), "PIT LANE", small);
        }

        private void DrawCrewRegions(RaceLayout layout, RaceUiModel ui)
        {
            DrawCrewRegions(layout.PlayerTwo, ui.PlayerTwo, ui.Phase, PlayerTwoAccent);
            DrawCrewRegions(layout.PlayerOne, ui.PlayerOne, ui.Phase, PlayerOneAccent);
        }

        // All three Robot zones live at fixed positions; state controls lit versus ghosted
        // (wireframe-ui.md, frames 35:2 and 40:23). Zones render exactly as the design
        // authority draws them — outline circles and rim dials, no panels and no per-seat
        // copy (issue #77 Round 2 owner decision). Progress reads as rings, not sentences.
        private void DrawCrewRegions(PlayerLayout playerLayout, PlayerUiModel model,
            RacePhase racePhase, Color accent)
        {
            bool inService = model.PitPhase == PitPhase.InService;
            // The circle is Call Pit on track and Leave Pit while parked — lit in both,
            // ghosted only while the car is moving through the pit lane.
            DrawCallPitZone(model, playerLayout, accent,
                racePhase == RacePhase.Racing && !model.Finished &&
                model.PitPhase != PitPhase.Entering && model.PitPhase != PitPhase.Exiting);
            DrawConditionDial(model, PitService.Tires, playerLayout.Tires,
                playerLayout.Controller.TiresLabel, "TIRES", TiresLabelColor, playerLayout,
                accent, inService, model.Condition.TireWear, model.Condition.TireLevel);
            DrawConditionDial(model, PitService.Fuel, playerLayout.Fuel,
                playerLayout.Controller.FuelLabel, "FUEL", FuelLabelColor, playerLayout,
                accent, inService, model.Condition.FuelUsed, model.Condition.FuelLevel);
        }

        private void DrawCallPitZone(PlayerUiModel model, PlayerLayout layout, Color accent,
            bool lit)
        {
            CornerControllerLayout controller = layout.Controller;
            Vector2 center = layout.CallPit.center;
            PitCallState state = model.CallState;
            bool inPit = model.PitPhase == PitPhase.Entering || model.PitPhase == PitPhase.InService;
            bool emphasized = lit && (state == PitCallState.Holding || state == PitCallState.Requested ||
                model.PitPhase == PitPhase.Requested);
            DrawArc(center, controller.CallPitRadius, 0f, 360f, emphasized ? 5f : 3f,
                emphasized ? Color.white : lit ? accent : GhostColor);
            if (lit && state == PitCallState.Holding)
                DrawArc(center, controller.CallPitRadius - 16f, -90f,
                    -90f + 360f * model.CallAction.Progress, 5f, Color.white);
            DrawRotatedLabel(controller.CallPitLabel.Bounds, inPit ? "LEAVE PIT" : "CALL PIT",
                controller.CallPitLabel.RotationDegrees + layout.RotationDegrees, zoneLabel,
                emphasized ? Color.white : lit ? accent : GhostColor);
        }

        private void DrawConditionDial(PlayerUiModel model, PitService service, Rect zone,
            RotatedLabel label, string conditionName, Color labelColor, PlayerLayout layout,
            Color accent, bool inService, float value, ConditionVisualLevel level)
        {
            Vector2 center = zone.center;
            float dialRadius = layout.Controller.DialRadius;
            bool selected = inService && model.SelectedService == service;
            // Parked: the dial itself is the service target; a surrounding ring marks it.
            if (inService)
                DrawArc(center, dialRadius + 12f, 0f, 360f, selected ? 5f : 2.5f,
                    selected ? Color.white : accent);
            DrawArc(center, dialRadius, 0f, 360f, 10f, new Color(.13f, .16f, .2f));
            float clamped = Mathf.Clamp01(value);
            // Normal severity fills in the condition's identity hue (frame 40:23);
            // warning/critical escalate to the shared severity colors.
            if (clamped > .001f)
                DrawArc(center, dialRadius, -90f, -90f + 360f * clamped, 10f,
                    level == ConditionVisualLevel.Normal ? labelColor : ConditionColor(level));
            DrawRotatedLabel(new Rect(center.x - dialRadius, center.y - 15f, dialRadius * 2f, 30f),
                Mathf.RoundToInt(clamped * 100f).ToString(), layout.RotationDegrees, dialValue);
            if (selected && model.ServiceAction.State == PitActionState.Stirring)
                DrawArc(center, dialRadius + 12f, -90f, -90f + 360f * model.ServiceProgress,
                    6f, Color.white);
            DrawRotatedLabel(label.Bounds, conditionName,
                label.RotationDegrees + layout.RotationDegrees, zoneLabel, labelColor);
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

        private static void DrawLine(Vec2 from, Vec2 to, float width, Color color)
        {
            Vector2 a = new Vector2(from.X, from.Y), b = new Vector2(to.X, to.Y);
            Vector2 middle = (a + b) * .5f;
            float length = Vector2.Distance(a, b);
            Matrix4x4 original = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg, middle);
            GUI.DrawTexture(new Rect(middle.x - length * .5f, middle.y - width * .5f, length, width),
                Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, color, 0, width * .25f);
            GUI.matrix = original;
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

        private PitLanePresentationLayout PitLayout() => new PitLanePresentationLayout(
            simulation.Track.Sample(0f).Position, PitEntry, PlayerOnePitBox,
            PlayerTwoPitBox, PitExit, PitMergeApproach,
            simulation.Track.Sample(strategySettings.pitExitRejoinDistance).Position,
            TrackPresentation.SmoothHeading(simulation.Track, 0f),
            TrackPresentation.SmoothHeading(simulation.Track, strategySettings.pitExitRejoinDistance));

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
            Color color = ConditionColor(level);
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, color, 0,
                rounded ? rect.height * .5f : 2f);
            GUI.Label(rect, symbol + (level == ConditionVisualLevel.Critical ? "!!" :
                level == ConditionVisualLevel.Warning ? "!" : ""), cue);
        }

        private static Color ConditionColor(ConditionVisualLevel level)
        {
            if (level == ConditionVisualLevel.Critical) return new Color(.86f, .12f, .12f);
            if (level == ConditionVisualLevel.Warning) return new Color(.94f, .62f, .08f);
            return new Color(.24f, .31f, .39f);
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

        // Corner cluster measured from frame 40:23 (component 44:124): the Ship well sits on
        // the corner diagonal, the throttle arc fans around its nose, and the lit sector plus
        // its accent fill ARE the throttle read — no separate state word or per-seat copy
        // (issue #77 Round 2 owner decision). Condition dials render in DrawConditionDial.
        private void DrawCornerController(PlayerUiModel model, PlayerLayout layout, Color accent)
        {
            CornerControllerLayout controller = layout.Controller;
            // The footprint ring marks the Ship's resting well; the rotated glyph stands in
            // for the physical piece on desktop only. On Board hardware the real Ship sits on
            // the well, and a solid rect drawn underneath it shows through around any
            // misalignment (issue #77 Round 2 hardware review).
            DrawArc(controller.ShipWellCenter, controller.ShipWellRadius, 0f, 360f, 3f,
                model.ShipPresent ? new Color(accent.r, accent.g, accent.b, .8f) : GhostColor);
#if UNITY_EDITOR
            DrawShipGlyph(controller, layout, accent, model.ShipPresent);
#endif

            bool throttleLive = model.PitPhase == PitPhase.OnTrack ||
                model.PitPhase == PitPhase.Requested;
            DrawThrottleSector(controller, model.Throttle, ThrottleStep.Brake, accent, layout,
                throttleLive);
            DrawThrottleSector(controller, model.Throttle, ThrottleStep.Drive, accent, layout,
                throttleLive);
            DrawThrottleSector(controller, model.Throttle, ThrottleStep.Boost, accent, layout,
                throttleLive);
        }

        private void DrawThrottleSector(CornerControllerLayout controller, ThrottleStep current,
            ThrottleStep sector, Color accent, PlayerLayout layout, bool lit)
        {
            float centerAngle = controller.SectorAngle(sector) + layout.RotationDegrees;
            float halfSweep = controller.SectorSweepDegrees * .5f;
            bool active = lit && current == sector;
            if (active)
            {
                // The lit sector is the deep accent wedge from the design; concentric rings
                // fill it more smoothly than one fat scalloped stroke.
                for (float ring = controller.ThrottleRadius - 54f;
                    ring <= controller.ThrottleRadius - 6f; ring += 12f)
                    DrawArc(controller.ArcCenter, ring, centerAngle - halfSweep,
                        centerAngle + halfSweep, 14f, accent);
            }
            else
            {
                // Unlit sectors are thin accent-tinted dark bands; locked seats go neutral.
                Color band = lit
                    ? new Color(accent.r * .38f + .08f, accent.g * .38f + .08f, accent.b * .38f + .08f)
                    : new Color(.16f, .19f, .24f);
                DrawArc(controller.ArcCenter, controller.ThrottleRadius - 22f,
                    centerAngle - halfSweep, centerAngle + halfSweep, 22f, band);
            }
            RotatedLabel label = controller.SectorLabel(sector);
            DrawRotatedLabel(label.Bounds, RaceUiModelBuilder.ThrottleName(sector),
                label.RotationDegrees + layout.RotationDegrees, sectorLabel,
                active ? Color.white : lit ? new Color(.85f, .88f, .92f) : new Color(.55f, .6f, .66f));
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

        private static void DrawArc(Vector2 center, float radius, float startAngle,
            float endAngle, float width, Color color)
        {
            int segments = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(endAngle - startAngle) / 3f));
            Vector2 prior = ArcPoint(center, radius, startAngle);
            for (int i = 1; i <= segments; i++)
            {
                Vector2 next = ArcPoint(center, radius,
                    Mathf.Lerp(startAngle, endAngle, i / (float)segments));
                DrawLine(new Vec2(prior.x, prior.y), new Vec2(next.x, next.y), width, color);
                prior = next;
            }
        }

        private static Vector2 ArcPoint(Vector2 center, float radius, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return center + new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * radius;
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
