using System;
using System.Collections.Generic;
using System.Linq;
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
        private GUIStyle title, heading, body, carLabel, warning, small, meter, cue;
#if UNITY_EDITOR
        private int previewScenarioIndex = -1;
#endif

        private const float TrackVerticalScale = .33f;
        private const float TrackWidth = 64f;
        private static readonly Vec2 PitEntry = new Vec2(650f, 455f);
        private static readonly Vec2 PlayerOnePitBox = new Vec2(820f, 455f);
        private static readonly Vec2 PlayerTwoPitBox = new Vec2(1100f, 455f);
        private static readonly Vec2 PitExit = new Vec2(1370f, 455f);
        private static readonly Vec2 PitReturnBend = new Vec2(1480f, 405f);
        private static readonly Vec2 PitReturnLane = new Vec2(360f, 405f);
        private static readonly Vec2 PitMergeApproach = new Vec2(390f, 438f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindObjectOfType<RacePrototype>() == null)
                new GameObject("Board Racing Race Prototype").AddComponent<RacePrototype>();
        }

        private void Awake()
        {
            inputSettings = Resources.Load<TrancheOneSettings>("TrancheOneSettings") ?? TrancheOneSettings.Defaults();
            raceSettings = Resources.Load<TrancheTwoSettings>("TrancheTwoSettings") ?? TrancheTwoSettings.Defaults();
            strategySettings = Resources.Load<TrancheThreeSettings>("TrancheThreeSettings") ?? TrancheThreeSettings.Defaults();
            boardProvider = new BoardContactInputProvider(inputSettings.throttleHysteresisDegrees * Mathf.Deg2Rad,
                inputSettings.playerRegionBoundaryY);
            fallbackProvider = new KeyboardInputProvider();
#if UNITY_ANDROID && !UNITY_EDITOR
            activeProvider = raceSettings.preferBoardInputOnDevice ? boardProvider : fallbackProvider;
#else
            activeProvider = fallbackProvider;
#endif
            foreach (PlayerId id in Enum.GetValues(typeof(PlayerId))) CreateCrewAdapter(id);
            AttachResetSource(activeProvider);
            simulation = new RaceSimulation(TrackDefinition.Placeholder(raceSettings.cornerSafeSpeed),
                raceSettings.ToRules(strategySettings.requiredServiceCount, strategySettings.ToConditionRules(),
                    strategySettings.ToPitRules()));
        }

        private void CreateCrewAdapter(PlayerId id)
        {
            Vector2 center = id == PlayerId.Player1 ? inputSettings.playerOneServiceCenter : inputSettings.playerTwoServiceCenter;
            float direction = id == PlayerId.Player1 ? 1f : -1f;
            var tires = new Vec2(center.x - strategySettings.serviceOffsetX * direction, center.y);
            var cooling = new Vec2(center.x + strategySettings.serviceOffsetX * direction, center.y);
            crewAdapters[id] = new CrewStrategyAdapter(new Vec2(center.x, center.y), tires, cooling,
                new Vec2(strategySettings.serviceHalfSize.x, strategySettings.serviceHalfSize.y),
                inputSettings.targetAngleDegrees * Mathf.Deg2Rad,
                inputSettings.alignmentToleranceDegrees * Mathf.Deg2Rad, inputSettings.holdDurationSeconds,
                strategySettings.pitCallHoldSeconds);
            crewOutputs[id] = default;
        }

        private void OnDestroy()
        {
            DetachResetSource(activeProvider);
            if (boardProvider is IDisposable disposable) disposable.Dispose();
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
                        crew.SelectedService, crew.RequestPit, crew.ServiceAction.Progress,
                        crew.ServiceAction.CompletedThisUpdate);
                }).ToArray();
                simulation.Step(step, commands); accumulator -= step;
            }
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
            heading = Style(26, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            body = Style(20, FontStyle.Normal, new Color(.9f, .92f, .95f), TextAnchor.MiddleCenter);
            carLabel = Style(22, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            warning = Style(26, FontStyle.Bold, new Color(1f, .75f, .2f), TextAnchor.MiddleCenter);
            small = Style(15, FontStyle.Bold, new Color(.87f, .9f, .94f), TextAnchor.MiddleCenter);
            meter = Style(16, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            cue = Style(13, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
        }

        private static GUIStyle Style(int size, FontStyle fontStyle, Color color, TextAnchor anchor) => new GUIStyle(GUI.skin.label)
        { fontSize = size, fontStyle = fontStyle, normal = { textColor = color }, alignment = anchor, wordWrap = true };

        private void OnGUI()
        {
            EnsureStyles();
            Matrix4x4 original = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / 1920f, Screen.height / 1080f, 1f));
            GUI.DrawTexture(new Rect(0, 0, 1920, 1080), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                new Color(.025f, .035f, .05f), 0, 0);
            RaceLayout layout = RaceLayout.Create(inputSettings.playerOneServiceCenter,
                inputSettings.playerTwoServiceCenter, strategySettings.serviceOffsetX,
                strategySettings.serviceHalfSize);
            RaceSnapshot presentedRace = simulation.Snapshot;
            RaceUiModel ui;
#if UNITY_EDITOR
            if (previewScenarioIndex >= 0)
            {
                RaceUiPreviewFrame preview = RaceUiPreviewFixtures.Create(
                    (RaceUiPreviewScenario)previewScenarioIndex, simulation.Track,
                    simulation.Rules.Conditions, raceSettings.laps);
                presentedRace = preview.Race;
                ui = preview.Ui;
            }
            else
#endif
            {
                ui = RaceUiModelBuilder.Build(presentedRace, controls, crewOutputs,
                    simulation.Rules.Conditions, raceSettings.laps);
            }
            DrawTrack();
            DrawPitLane();
            DrawCrewRegions(layout, ui);
            foreach (var racer in presentedRace.Racers) DrawCar(racer);
            DrawHud(ui.PlayerTwo, layout.PlayerTwo, new Color(.48f, .28f, .72f));
            DrawHud(ui.PlayerOne, layout.PlayerOne, new Color(.92f, .39f, .12f));
            DrawCenterMessage(ui, layout);
#if UNITY_EDITOR
            GUI.Label(new Rect(8, 527, 330, 26),
                (activeProvider == boardProvider ? "BOARD INPUT" : "KEYBOARD FALLBACK") + " · F1 provider", small);
            GUI.Label(new Rect(8, 553, 500, 26), previewScenarioIndex < 0
                ? "LIVE PRESENTATION · F2 preview"
                : "PREVIEW: " + ((RaceUiPreviewScenario)previewScenarioIndex) + " · F2 next", small);
#endif
            GUI.matrix = original;
        }

        private void DrawTrack()
        {
            foreach (var segment in simulation.Track.Segments)
            {
                Color color = segment.Kind == TrackSectionKind.Corner ? new Color(.22f, .28f, .36f) : new Color(.16f, .2f, .27f);
                DrawLine(ProjectTrack(segment.Start), ProjectTrack(segment.End), TrackWidth, color);
                DrawLine(ProjectTrack(segment.Start), ProjectTrack(segment.End), 3f, new Color(.55f, .62f, .7f, .5f));
            }
            Vec2 line = ProjectTrack(simulation.Track.Sample(0f).Position);
            GUI.DrawTexture(new Rect(line.X - 12, line.Y - 28, 24, 56), Texture2D.whiteTexture,
                ScaleMode.StretchToFill, true, 0, Color.white, 0, 0);
            GUI.Label(new Rect(610, 510, 700, 80), "BOARD RACING\n5 LAPS · DRIVE + CREW", title);
        }

        private static Vec2 ProjectTrack(Vec2 point) =>
            new Vec2(point.X, 540f + (point.Y - 540f) * TrackVerticalScale);

        private void DrawPitLane()
        {
            Vec2 start = ProjectTrack(simulation.Track.Sample(0f).Position);
            DrawLine(start, PitEntry, 30f, new Color(.08f, .11f, .15f));
            DrawLine(PitEntry, PitExit, 30f, new Color(.08f, .11f, .15f));
            DrawPitReturnLane(start, 30f, new Color(.08f, .11f, .15f));
            DrawLine(start, PitEntry, 2f, new Color(.62f, .68f, .74f, .55f));
            DrawLine(PitEntry, PitExit, 2f, new Color(.62f, .68f, .74f, .55f));
            DrawPitReturnLane(start, 2f, new Color(.62f, .68f, .74f, .55f));
            DrawPitBox(PlayerOnePitBox, "▲ P1 BOX", new Color(.92f, .39f, .12f));
            DrawPitBox(PlayerTwoPitBox, "● P2 BOX", new Color(.48f, .28f, .72f));
            GUI.Label(new Rect(865, 421, 190, 28), "PIT LANE", small);
        }

        private static void DrawPitReturnLane(Vec2 start, float width, Color color)
        {
            var layout = new PitLanePresentationLayout(start, PitEntry, PlayerOnePitBox,
                PlayerTwoPitBox, PitExit, PitReturnBend, PitReturnLane, PitMergeApproach);
            CarPresentationPose prior = PitLanePresentationMapper.ExitPose(PlayerId.Player1, 0f, false, layout);
            for (int i = 1; i <= 36; i++)
            {
                CarPresentationPose next = PitLanePresentationMapper.ExitPose(PlayerId.Player1, i / 36f, false, layout);
                DrawLine(prior.Position, next.Position, width, color);
                prior = next;
            }
        }

        private void DrawPitBox(Vec2 center, string label, Color accent)
        {
            Rect box = new Rect(center.X - 70f, center.Y - 32f, 140f, 64f);
            GUI.DrawTexture(box, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                new Color(accent.r, accent.g, accent.b, .22f), 0, 5f);
            DrawOutline(box, 3f, accent);
            GUI.Label(box, label, small);
        }

        private void DrawCrewRegions(RaceLayout layout, RaceUiModel ui)
        {
            DrawCrewRegions(layout.PlayerTwo, ui.PlayerTwo, ui.Phase, new Color(.48f, .28f, .72f));
            DrawCrewRegions(layout.PlayerOne, ui.PlayerOne, ui.Phase, new Color(.92f, .39f, .12f));
        }

        private void DrawCrewRegions(PlayerLayout playerLayout, PlayerUiModel model,
            RacePhase racePhase, Color accent)
        {
            if (model.PitPhase != PitPhase.InService)
            {
                DrawCallPitRegion(model, racePhase, playerLayout.CallPit, playerLayout.Opposite, accent);
                return;
            }

            DrawRepairRegion(model, PitService.Tires, playerLayout.Tires, playerLayout.Opposite, accent);
            DrawRepairRegion(model, PitService.Cooling, playerLayout.Cooling, playerLayout.Opposite, accent);
        }

        private void DrawCallPitRegion(PlayerUiModel model, RacePhase racePhase, Rect rect,
            bool opposite, Color accent)
        {
            Matrix4x4 original = GUI.matrix;
            if (opposite)
            {
                Vector3 pivot = new Vector3(rect.center.x, rect.center.y, 0f);
                GUI.matrix = original * Matrix4x4.Translate(pivot) *
                    Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, 180f)) * Matrix4x4.Translate(-pivot);
            }
            PitCallState state = model.CallState;
            bool emphasized = state == PitCallState.Holding || state == PitCallState.Requested ||
                model.PitPhase == PitPhase.Requested;
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                new Color(accent.r, accent.g, accent.b, emphasized ? .42f : .14f), 0, 34f);
            DrawOutline(rect, emphasized ? 6f : 3f, emphasized ? Color.white : accent);
            string titleText = "((  CALL PIT  ))";
            string instruction;
            if (model.Finished) { titleText = "FINISHED"; instruction = "CAR CLASSIFIED"; }
            else if (model.PitPhase == PitPhase.Requested)
            { titleText = "PIT CALLED ✓"; instruction = "ENTRY AT START / FINISH"; }
            else if (model.PitPhase == PitPhase.Entering)
            { titleText = "PIT ENTRY"; instruction = "CAR UNDER PIT CONTROL"; }
            else if (model.PitPhase == PitPhase.Exiting)
            { titleText = "SERVICE COMPLETE ✓"; instruction = "REJOINING"; }
            else if (racePhase != RacePhase.Racing)
                instruction = "AVAILABLE AFTER GO";
            else if (state == PitCallState.Aligning)
                instruction = "ROTATE ROBOT TO 0°";
            else if (state == PitCallState.Holding)
                instruction = "HOLD STEADY · " + Mathf.RoundToInt(model.CallAction.Progress * 100f) + "%";
            else if (state == PitCallState.Requested)
            { titleText = "PIT CALLED ✓"; instruction = "ENTRY AT START / FINISH"; }
            else instruction = "PLACE ROBOT HERE · ALIGN TO 0°";
            GUI.Label(new Rect(rect.x + 10, rect.y + 62, rect.width - 20, 48), titleText, heading);
            GUI.Label(new Rect(rect.x + 12, rect.y + 112, rect.width - 24, 58), instruction, small);
            GUI.Label(new Rect(rect.x + 12, rect.y + 18, rect.width - 24, 34),
                model.PlayerId == PlayerId.Player1 ? "▲ ORANGE PIT ROBOT" : "● PURPLE PIT ROBOT", small);
            GUI.matrix = original;
        }

        private void DrawRepairRegion(PlayerUiModel model, PitService service, Rect rect,
            bool opposite, Color accent)
        {
            Matrix4x4 original = GUI.matrix;
            if (opposite)
            {
                Vector3 pivot = new Vector3(rect.center.x, rect.center.y, 0f);
                GUI.matrix = original * Matrix4x4.Translate(pivot) *
                    Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, 180f)) * Matrix4x4.Translate(-pivot);
            }
            bool selected = model.SelectedService == service;
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                selected ? new Color(accent.r, accent.g, accent.b, .42f) :
                    new Color(accent.r, accent.g, accent.b, .14f), 0, service == PitService.Tires ? 6f : 34f);
            DrawOutline(rect, selected ? 6f : 3f, selected ? Color.white : accent);
            string shape = service == PitService.Tires ? "[ ]  TIRES  [ ]" : "( ^ )  COOLING";
            GUI.Label(new Rect(rect.x + 10, rect.y + 62, rect.width - 20, 48), shape, heading);
            PitActionResult action = model.ServiceAction;
            string instruction;
            if (!selected) instruction = model.SelectedService == PitService.None
                ? "CAR PARKED · MOVE HERE TO CHOOSE" : "MOVE HERE TO SWITCH";
            else if (action.State == PitActionState.Positioned) instruction = "ALIGN ROBOT TO SERVICE";
            else if (action.State == PitActionState.Aligning) instruction = "ROTATE ROBOT TO 0°";
            else if (action.State == PitActionState.Holding)
                instruction = "HOLD STEADY · " + Mathf.RoundToInt(action.Progress * 100f) + "%";
            else if (action.State == PitActionState.Completed) instruction = "SERVICE COMPLETE ✓";
            else instruction = "ALIGN + HOLD TO SERVICE";
            GUI.Label(new Rect(rect.x + 12, rect.y + 112, rect.width - 24, 58), instruction, small);
            GUI.Label(new Rect(rect.x + 12, rect.y + 18, rect.width - 24, 34),
                model.PlayerId == PlayerId.Player1 ? "▲ CAR PARKED · ORANGE" : "● CAR PARKED · PURPLE", small);
            GUI.matrix = original;
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

        private void DrawCar(RacerSnapshot racer)
        {
            CarPose(racer, out Vector2 center, out Vector2 tangent);
            float lateralOffset = racer.Pit.Phase == PitPhase.OnTrack || racer.Pit.Phase == PitPhase.Requested
                ? racer.LateralOffset : 0f;
            float x = center.x - tangent.y * lateralOffset, y = center.y + tangent.x * lateralOffset;
            Color color = racer.PlayerId == PlayerId.Player1 ? new Color(.92f, .39f, .12f) : new Color(.48f, .28f, .72f);
            Rect rect = new Rect(x - 27f, y - 27f, 54f, 54f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, color, 0,
                racer.PlayerId == PlayerId.Player1 ? 8f : 27f);
            GUI.Label(rect, racer.PlayerId == PlayerId.Player1 ? "▲" : "●", carLabel);
            DrawConditionCues(racer, x, y);
            if (racer.RecoveryRemaining > 0f) GUI.Label(new Rect(x - 100f, y - 72f, 200f, 36f), "SLOWDOWN!", warning);
            if (racer.Finished)
                GUI.Label(new Rect(x - 110f, y + 32f, 220f, 30f), "FINISHED · " + Ordinal(racer.Place), warning);
            else if (racer.Pit.Phase != PitPhase.OnTrack)
                GUI.Label(new Rect(x - 100f, y + 32f, 200f, 28f), CarPitLabel(racer.Pit), small);
        }

        private void CarPose(RacerSnapshot racer, out Vector2 position, out Vector2 tangent)
        {
            Vec2 trackPosition = ProjectTrack(racer.Track.Position);
            var trackTangent = new Vec2(racer.Track.Tangent.X, racer.Track.Tangent.Y * TrackVerticalScale);
            var layout = new PitLanePresentationLayout(ProjectTrack(simulation.Track.Sample(0f).Position),
                PitEntry, PlayerOnePitBox, PlayerTwoPitBox, PitExit,
                PitReturnBend, PitReturnLane, PitMergeApproach);
            CarPresentationPose pose = PitLanePresentationMapper.From(racer, trackPosition, trackTangent, layout);
            position = new Vector2(pose.Position.X, pose.Position.Y);
            tangent = new Vector2(pose.Tangent.X, pose.Tangent.Y);
        }

        private void DrawConditionCues(RacerSnapshot racer, float x, float y)
        {
            CarConditionVisualState visual = CarConditionVisualMapper.From(racer, simulation.Rules.Conditions);
            DrawConditionCue(new Rect(x - 39f, y - 42f, 32f, 24f), "H", visual.HeatLevel, true);
            DrawConditionCue(new Rect(x + 7f, y - 42f, 32f, 24f), "T", visual.TireLevel, false);
        }

        private void DrawConditionCue(Rect rect, string symbol, ConditionVisualLevel level, bool heat)
        {
            Color color = ConditionColor(level);
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, color, 0,
                heat ? rect.height * .5f : 2f);
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
                ? "CAR PARKED · CHOOSE REPAIR" : "IN BOX · " + ServiceName(pit.SelectedService);
            if (pit.Phase == PitPhase.Exiting) return "SERVICE ✓ · PIT EXIT";
            return string.Empty;
        }

        private void DrawHud(PlayerUiModel model, PlayerLayout layout, Color accent)
        {
            Rect rect = layout.DraftContextBounds;
            Matrix4x4 original = GUI.matrix;
            if (layout.Opposite)
            {
                Vector3 pivot = new Vector3(rect.center.x, rect.center.y, 0f);
                GUI.matrix = original * Matrix4x4.Translate(pivot) * Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, 180f)) * Matrix4x4.Translate(-pivot);
            }
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                new Color(accent.r * .18f, accent.g * .18f, accent.b * .18f), 0, 22);
            GUI.Label(new Rect(rect.x + 20, rect.y + 8, 310, 36), model.Identity, heading);
            GUI.Label(new Rect(rect.x + 325, rect.y + 8, rect.width - 345, 36), model.Status, heading);
            DrawMeter(new Rect(rect.x + 28, rect.y + 52, 315, 34), "HEAT ^", model.Condition.Heat,
                model.Condition.HeatLevel, false);
            DrawMeter(new Rect(rect.x + 365, rect.y + 52, 315, 34), "TIRES [ ]",
                model.Condition.TireWear, model.Condition.TireLevel, true);
            GUI.Label(new Rect(rect.x + 24, rect.y + 94, rect.width - 48, 64),
                model.PrimaryInstruction, body);
            GUI.matrix = original;
        }

        private void DrawMeter(Rect rect, string label, float value, ConditionVisualLevel level, bool wear)
        {
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                new Color(.07f, .09f, .12f), 0, wear ? 3f : rect.height * .5f);
            float visible = Mathf.Max(0f, Mathf.Min(rect.width, rect.width * value));
            if (visible > 0f)
                GUI.DrawTexture(new Rect(rect.x, rect.y, visible, rect.height), Texture2D.whiteTexture,
                    ScaleMode.StretchToFill, true, 0, ConditionColor(level), 0, wear ? 3f : rect.height * .5f);
            string status = level == ConditionVisualLevel.Critical ? " CRITICAL!!" :
                level == ConditionVisualLevel.Warning ? " WARNING!" : string.Empty;
            GUI.Label(rect, label + "  " + Mathf.RoundToInt(value * 100f) + "%" + status, meter);
        }

        private void DrawCenterMessage(RaceUiModel ui, RaceLayout layout)
        {
            if (ui.CenterMessage == null) return;
            GUI.Label(layout.CenterOverlayBounds, ui.CenterMessage, title);
        }

        private static string ServiceName(PitService service) =>
            service == PitService.Tires ? "TIRES" : service == PitService.Cooling ? "COOLING" : "NO SERVICE";

        private static string Ordinal(int place) => RaceUiModelBuilder.Ordinal(place);
    }
}
