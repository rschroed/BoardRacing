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
        private IPlayerSession playerSession;
        private PlayerLobbyPresentation lobby;
        private PlayerSeat[] raceSeats = Array.Empty<PlayerSeat>();
        private readonly Dictionary<PlayerId, Color> playerAccents =
            new Dictionary<PlayerId, Color>();
        private readonly Dictionary<PlayerId, string> playerIdentityLabels =
            new Dictionary<PlayerId, string>();
        private readonly Dictionary<PlayerId, CarResponseState> carResponseStates =
            new Dictionary<PlayerId, CarResponseState>();
        private RaceSimulation simulation;
        private IReadOnlyList<PlayerControlSnapshot> controls = Array.Empty<PlayerControlSnapshot>();
        private readonly Dictionary<PlayerId, CrewStrategyAdapter> crewAdapters =
            new Dictionary<PlayerId, CrewStrategyAdapter>();
        private readonly Dictionary<PlayerId, CrewStrategyOutput> crewOutputs =
            new Dictionary<PlayerId, CrewStrategyOutput>();
        private float accumulator;
        // The sim state one fixed step behind Snapshot: the canvas and world
        // renderer draw the blend by the accumulator fraction (#89).
        private RaceSnapshot previousSnapshot;
        // The static racing surface (track, pit complex) is a world-space mesh
        // since issue #86 round 1, and the car bodies since round 2; the seat
        // clusters and the remaining player-facing overlays are one uGUI
        // canvas. ControlLab is the sole IMGUI diagnostic exemption.
        private RaceSurfaceRenderer surface;
        private RaceSurfaceStyle surfaceStyle = RaceSurfaceStyle.Default;
        private RaceHud hud;
#if UNITY_EDITOR || (DEVELOPMENT_BUILD && UNITY_ANDROID)
        private VisualLabShell visualLab;
        private bool visualLabConsumedPress;
        private bool visualLabWireframeVisible;
        private CarStudyPresentation visualLabCarStudy = CarStudyPresentation.Live;
#endif
        // Everything the current track IS — racing line, pit complex, race
        // length — comes from one authored artifact (issue #107 phase 1).
        private CourseDefinition course;
        // Which artifact that is comes from the between-race tap-to-cycle
        // choice (issue #107 phase 5).
        private CourseSelection courseSelection;
        private bool exitConfirmationOpen;
        // One presentation state per frame, computed at the end of Update:
        // world-space cars and the canvas read the same blend.
        private RaceSnapshot presentedRace;
        private RaceUiModel presentedUi;
#if UNITY_EDITOR
        private int previewScenarioIndex = -1;

        // Set by an editor automation driver before it enters play mode, for
        // runs that need a race but cannot be batch mode. The capture harness
        // disables domain reload, so a static set before EnterPlaymode survives
        // into the play-mode session that reads it here.
        internal static bool AutomationBypassesLobby;
#endif

        // Legacy defaults remain the two-player preview colors; live races take
        // their accents from whichever Ships claimed the named seats.
        private static readonly Color PlayerOneAccent = new Color(.92f, .39f, .12f);
        private static readonly Color PlayerTwoAccent = new Color(.48f, .28f, .72f);
        private static readonly Color PlayerThreeAccent = new Color(.88f, .18f, .52f);
        private static readonly Color PlayerFourAccent = new Color(.96f, .73f, .12f);
        // Shared touch geometry for the canvas buttons in 1920×1080 reference
        // space. Board contacts are polled directly because they share the same
        // SDK stream as physical pieces.
        private static readonly Rect OverlayPrimaryButton = new Rect(700f, 560f, 520f, 70f);
        private static readonly Rect OverlaySecondaryButton = new Rect(700f, 648f, 520f, 48f);
        private static readonly Rect RaceExitButton = new Rect(855f, 20f, 210f, 52f);

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
            // The committed course treatment (#161). Absent theme asset
            // means the flat pre-#161 default, which is also the
            // deterministic fallback the gallery captures compare against.
            surfaceStyle = CourseSurfaceTheme.LoadStyleOrDefault();
            boardProvider = new BoardContactInputProvider(inputSettings.ToThrottleStops(),
                inputSettings.throttleHysteresisDegrees * Mathf.Deg2Rad,
                inputSettings.playerRegionBoundaryY);
            fallbackProvider = new KeyboardInputProvider();
#if UNITY_ANDROID && !UNITY_EDITOR
            activeProvider = raceSettings.preferBoardInputOnDevice ? boardProvider : fallbackProvider;
#else
            activeProvider = fallbackProvider;
#endif
            AttachResetSource(activeProvider);
            courseSelection = new CourseSelection(CourseCatalog.All(raceSettings.CornerSafeSpeed));
            course = courseSelection.Current;
            BuildLobbySurface();
            hud = RaceHud.CreateFour(FourSeatRaceLayout(),
                RaceSurfaceGeometry.InactivePitBoxAccent,
                RaceSurfaceGeometry.InactivePitBoxAccent,
                RaceSurfaceGeometry.InactivePitBoxAccent,
                RaceSurfaceGeometry.InactivePitBoxAccent);
#if UNITY_ANDROID && !UNITY_EDITOR
            playerSession = new BoardPlayerSession();
            CreateLobby();
#else
            playerSession = new FallbackPlayerSession();
            CreateLobby();
#if UNITY_EDITOR
            // Existing automated race probes intentionally bypass the interactive
            // lobby; lobby behavior has its own deterministic and PlayMode tests.
            // isBatchMode alone is not the right test: the capture harness must
            // run a HEADED editor for a real Game view backbuffer, so gating on
            // batch mode silently left it photographing the lobby instead of the
            // scenarios (found by #153; the last good set predates the #139
            // lobby). Automation declares itself instead of being inferred.
            if (Application.isBatchMode || AutomationBypassesLobby)
            {
                playerSession.AddPlayer().GetAwaiter().GetResult();
                playerSession.AddPlayer().GetAwaiter().GetResult();
                lobby.Coordinator.AssignPlayer(playerSession.Players[0], PlayerId.Player1);
                lobby.Coordinator.AssignPlayer(playerSession.Players[1], PlayerId.Player2);
                lobby.Coordinator.ClaimForFallback(PlayerId.Player1, 7);
                lobby.Coordinator.ClaimForFallback(PlayerId.Player2, 6);
                BeginRace();
            }
#endif
#endif
#if UNITY_EDITOR || (DEVELOPMENT_BUILD && UNITY_ANDROID)
            // Editor capture automation starts with the lab unavailable, so its
            // established comparison images stay clean. F10 explicitly exposes
            // it. Board development players expose the launcher immediately.
            bool startAvailable = true;
#if UNITY_EDITOR
            startAvailable = false;
#endif
            visualLab = VisualLabShell.Create(transform,
                SetVisualLabCarsVisible, SetVisualLabHudVisible, startAvailable);
            visualLab.Register(new CourseSurfaceComposerPanel(
                () => course.Name,
                () => lobby != null,
                CycleSetupCourse,
                surfaceStyle,
                ApplyVisualLabSurfaceStyle,
                SetVisualLabWireframeVisible));
            visualLab.Register(new CarsVisualLabPanel(ApplyVisualLabCarStudy));
#endif
        }

        // Everything owned by the course on the table: the simulation and the
        // world-space surface. Called at boot and again whenever the between-race
        // choice lands on a different course (issue #107 phase 5).
        private void BuildRace()
        {
            simulation = new RaceSimulation(course.Track,
                raceSettings.ToRules(course.Laps, strategySettings.requiredServiceCount,
                    strategySettings.ToConditionRules(raceSettings.basePace),
                    strategySettings.ToPitRules(course, raceSettings.basePace)),
                raceSeats.Select(x => x.PlayerId).ToArray());
            previousSnapshot = simulation.Snapshot;
            if (surface != null) Destroy(surface.gameObject);
            surface = RaceSurfaceRenderer.Create(
                BuildSurfaceData(simulation.Track, true), surfaceStyle);
            carResponseStates.Clear();
#if UNITY_EDITOR || (DEVELOPMENT_BUILD && UNITY_ANDROID)
            surface.SetWireframeVisible(visualLabWireframeVisible);
#endif
            var pitIdentities = new Dictionary<PlayerId, PieceIdentity>();
            foreach (PlayerSeat seat in raceSeats)
            {
                PieceIdentity identity = seat.PieceIdentity.HasValue
                    ? seat.PieceIdentity.Value
                    : PhysicalPieceCatalog.All[(int)seat.PlayerId - 1];
                pitIdentities[seat.PlayerId] = identity;
                surface.AttachCar(seat.PlayerId, identity);
                carResponseStates[seat.PlayerId] = CarResponseState.Still;
            }
            surface.AttachPitComplex(PitLayout(), pitIdentities);
            surface.SetPitPresentation(simulation.Snapshot.Racers,
                simulation.Snapshot.ElapsedSeconds, 0f);
#if UNITY_EDITOR || (DEVELOPMENT_BUILD && UNITY_ANDROID)
            surface.SetCarStudy(visualLabCarStudy);
            visualLab?.ReapplyStageComposition();
#endif
        }

        private void BuildLobbySurface()
        {
            if (surface != null) Destroy(surface.gameObject);
            surface = RaceSurfaceRenderer.Create(
                BuildSurfaceData(course.Track, false), surfaceStyle);
            surface.AttachPitComplex(PitLayout());
            surface.SetPitPresentation(null, 0f, 0f);
#if UNITY_EDITOR || (DEVELOPMENT_BUILD && UNITY_ANDROID)
            surface.SetWireframeVisible(visualLabWireframeVisible);
            surface.SetCarStudy(visualLabCarStudy);
            visualLab?.ReapplyStageComposition();
#endif
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

        private ServiceTargets ServiceTargetsFor(PlayerId id)
        {
            if (id == PlayerId.Player1)
                return new ServiceTargets(inputSettings.playerOneServiceCenter,
                    strategySettings.playerOneTiresCenter, strategySettings.playerOneFuelCenter);
            if (id == PlayerId.Player2)
                return new ServiceTargets(inputSettings.playerTwoServiceCenter,
                    strategySettings.playerTwoTiresCenter, strategySettings.playerTwoFuelCenter);
            if (id == PlayerId.Player3)
                return new ServiceTargets(
                    new Vector2(RaceLayout.ReferenceWidth - inputSettings.playerOneServiceCenter.x,
                        inputSettings.playerOneServiceCenter.y),
                    new Vector2(RaceLayout.ReferenceWidth - strategySettings.playerOneTiresCenter.x,
                        strategySettings.playerOneTiresCenter.y),
                    new Vector2(RaceLayout.ReferenceWidth - strategySettings.playerOneFuelCenter.x,
                        strategySettings.playerOneFuelCenter.y));
            return new ServiceTargets(
                new Vector2(RaceLayout.ReferenceWidth - inputSettings.playerTwoServiceCenter.x,
                    inputSettings.playerTwoServiceCenter.y),
                new Vector2(RaceLayout.ReferenceWidth - strategySettings.playerTwoTiresCenter.x,
                    strategySettings.playerTwoTiresCenter.y),
                new Vector2(RaceLayout.ReferenceWidth - strategySettings.playerTwoFuelCenter.x,
                    strategySettings.playerTwoFuelCenter.y));
        }

        private RaceLayout FourSeatRaceLayout() => RaceLayout.CreateFour(
            ServiceTargetsFor(PlayerId.Player1), ServiceTargetsFor(PlayerId.Player2),
            ServiceTargetsFor(PlayerId.Player3), ServiceTargetsFor(PlayerId.Player4),
            strategySettings.serviceHalfSize);

        private void OnDestroy()
        {
            DetachResetSource(activeProvider);
            if (boardProvider is IDisposable disposable) disposable.Dispose();
            lobby?.Dispose();
            playerSession?.Dispose();
            if (surface != null) Destroy(surface.gameObject);
            if (hud != null) Destroy(hud.gameObject);
        }

        private void Update()
        {
#if UNITY_EDITOR || (DEVELOPMENT_BUILD && UNITY_ANDROID)
            visualLab?.PollEditorShortcut();
            visualLabConsumedPress = visualLab != null && visualLab.PollInput();
#endif
            if (lobby != null)
            {
                IReadOnlyList<RawPieceContact> contacts = activeProvider == boardProvider
                    ? ((BoardContactInputProvider)boardProvider).ReadRawContacts()
                    : Array.Empty<RawPieceContact>();
                lobby.Update(contacts,
#if UNITY_EDITOR || (DEVELOPMENT_BUILD && UNITY_ANDROID)
                    visualLabConsumedPress
#else
                    false
#endif
                );
                if (activeProvider == boardProvider)
                    lobby.SetReadyPlayers(LobbyPlayersOnDrive(contacts));
                if (lobby.ConsumeStartRequest() && lobby.AllPlayersReady)
                    BeginRace();
                return;
            }
            AdvanceFrame(Time.unscaledDeltaTime);
        }

        private IReadOnlyList<PlayerId> LobbyPlayersOnDrive(
            IReadOnlyList<RawPieceContact> contacts)
        {
            RawPieceContact[] active = contacts.Where(x => x.IsActive).ToArray();
            var ready = new List<PlayerId>();
            foreach (PlayerSeat seat in lobby.Coordinator.Seats)
            {
                if (!seat.PieceIdentity.HasValue) continue;
                PieceIdentity identity = seat.PieceIdentity.Value;
                RawPieceContact[] matches = active
                    .Where(x => x.GlyphId == identity.ShipGlyphId).ToArray();
                SeatClaimRegion region = FourSeatLayout.For(seat.PlayerId);
                if (matches.Length != 1 || !region.Contains(matches[0].Position))
                    continue;
                var mapper = new CoarseThrottleMapper(
                    inputSettings.throttleHysteresisDegrees * Mathf.Deg2Rad,
                    inputSettings.ToThrottleStops(), region.SeatRotationRadians,
                    region.MirroredOrientation);
                if (mapper.Map(true, matches[0].OrientationRadians) == ThrottleStep.Drive)
                    ready.Add(seat.PlayerId);
            }
            return ready;
        }

        private void BeginRace()
        {
            raceSeats = lobby.Coordinator.Seats.OrderBy(x => x.PlayerId).ToArray();
            course = courseSelection.ConfirmNext();
            playerAccents.Clear();
            playerIdentityLabels.Clear();
            foreach (PlayerSeat seat in raceSeats)
            {
                PieceIdentity identity = seat.PieceIdentity.Value;
                playerAccents[seat.PlayerId] = PlayerColors.For(identity);
                playerIdentityLabels[seat.PlayerId] = identity.Symbol + " " +
                    seat.Player.DisplayName.ToUpperInvariant() + " · " +
                    identity.ColorName.ToUpperInvariant();
            }

            var ids = raceSeats.Select(x => x.PlayerId).ToArray();
            ((BoardContactInputProvider)boardProvider).Configure(
                lobby.Coordinator.BuildPieceAssignments(), ids,
                ids.Select(FourSeatLayout.InputFor));
            ((KeyboardInputProvider)fallbackProvider).ConfigureRoster(ids);
            crewAdapters.Clear();
            crewOutputs.Clear();
            foreach (PlayerId id in ids) CreateCrewAdapter(id);

            lobby.Dispose();
            lobby = null;
            playerSession.HideProfileSwitcher();
            BuildRace();
            if (hud != null)
            {
                Destroy(hud.gameObject);
                hud = null;
            }
            hud = RaceHud.CreateFour(FourSeatRaceLayout(),
                PlayerAccent(PlayerId.Player1), PlayerAccent(PlayerId.Player2),
                PlayerAccent(PlayerId.Player3), PlayerAccent(PlayerId.Player4));
#if UNITY_EDITOR || (DEVELOPMENT_BUILD && UNITY_ANDROID)
            visualLab?.ReapplyStageComposition();
#endif
            RefreshPresentation();
            UpdateWorldCars();
        }

        // Kept separate from Unity's clock so accelerated PlayMode coverage can
        // advance an exact amount of simulation time regardless of editor load.
        private void AdvanceFrame(float unscaledDeltaTime)
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
            PollRaceNavigation();
            if (lobby != null || exitConfirmationOpen) return;
            accumulator += Mathf.Min(unscaledDeltaTime, .25f);
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
                    bool startReady = control.Car.Present &&
                        ((simulation.Snapshot.Phase != RacePhase.Grid &&
                          simulation.Snapshot.Phase != RacePhase.Countdown) ||
                         control.Throttle == ThrottleStep.Drive);
                    return new RacerCommand(control.PlayerId, control.Throttle, startReady, rematchConfirming,
                        crew.SelectedService, crew.RequestPit, crew.ServiceDrain, crew.RequestExit);
                }).ToArray();
                previousSnapshot = simulation.Snapshot;
                simulation.Step(step, commands); accumulator -= step;
            }
            RefreshPresentation();
            UpdateWorldCars(unscaledDeltaTime);
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
                simulation.Rules.Conditions, course.Laps, playerIdentityLabels);
            if (simulation.Snapshot.Phase == RacePhase.Finished)
                playerSession.ShowProfileSwitcher();
            else
                playerSession.HideProfileSwitcher();
        }

        private void LateUpdate()
        {
            if (hud == null) return;
            if (lobby != null)
            {
                hud.SetAccents(lobby.AccentFor(PlayerId.Player1),
                    lobby.AccentFor(PlayerId.Player2),
                    lobby.AccentFor(PlayerId.Player3),
                    lobby.AccentFor(PlayerId.Player4));
                hud.ApplyLobby(BuildLobbyCockpitUi(), lobby.BuildUiModel());
                return;
            }
            if (presentedUi.Players != null)
                hud.ApplyRace(presentedUi, exitConfirmationOpen,
                    BuildCarAnnotations(), BuildPitAnnotations());
        }

        private RaceUiModel BuildLobbyCockpitUi()
        {
            PlayerUiModel Player(PlayerId id)
            {
                bool hasShip = lobby.HasShip(id);
                bool ready = lobby.IsReady(id);
                return new PlayerUiModel(id, "SETUP", string.Empty,
                    PlayerUiInstructionKind.GridReady,
                    !hasShip ? "PLACE ANY SHIP" : ready ? "READY" : "SET SHIP TO DRIVE",
                    ready ? ThrottleStep.Drive : ThrottleStep.Brake,
                    new CarConditionVisualState(0f, 0f, ConditionVisualLevel.Normal,
                        ConditionVisualLevel.Normal),
                    PitPhase.OnTrack, PitService.None, 0f, PitCallState.Unavailable,
                    default, default, hasShip, false, InputWarning.None, true, false,
                    driveOnlyThrottle: true);
            }
            return new RaceUiModel(RacePhase.Grid, new[]
                {
                    Player(PlayerId.Player1), Player(PlayerId.Player2),
                    Player(PlayerId.Player3), Player(PlayerId.Player4)
                },
                CenterMessageKind.None, null);
        }

        private void UpdateWorldCars(float deltaSeconds = 0f)
        {
            foreach (var racer in presentedRace.Racers)
            {
                CarPose(racer, out Vector2 center, out Vector2 tangent);
                float deceleration = Deceleration(racer.PlayerId);
                // Corner character (issue #117) belongs to a car racing the
                // track; the pit lane and the finished pose stay composed.
                CarAttitude attitude = OnRacingLine(racer) && !racer.Finished
                    ? CornerCharacter.Attitude(simulation.Track, DrawnDistance(racer), racer.Speed,
                        deceleration, simulation.Rules.Braking)
                    : CarAttitude.Neutral;
                surface.SetCarPose(racer.PlayerId, OffsetCenter(racer, center, tangent),
                    Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg + attitude.DriftDegrees +
                    LaunchTwitchFor(racer).YawDegrees,
                    new Vector2(attitude.SquashAlong, attitude.StretchAcross));

                bool hasControl = TryControl(racer.PlayerId,
                    out PlayerControlSnapshot control);
                CarResponseState target = CarResponsePresentation.Targets(
                    presentedRace.Phase, OnRacingLine(racer), racer.Finished,
                    hasControl && control.Car.Present,
                    hasControl ? control.Throttle : ThrottleStep.Brake,
                    racer.Speed,
                    CornerCharacter.BrakeDive(deceleration, simulation.Rules.Braking),
                    attitude.DriftDegrees);
                CarResponseState current = carResponseStates.TryGetValue(racer.PlayerId,
                    out CarResponseState response) ? response : CarResponseState.Still;
                CarResponseState next = CarResponsePresentation.Step(current, target,
                    deltaSeconds);
                carResponseStates[racer.PlayerId] = next;
                surface.SetCarResponse(racer.PlayerId, next,
                    CarResponsePresentation.Pulse(presentedRace.ElapsedSeconds,
                        racer.PlayerId));
            }
            surface.SetPitPresentation(presentedRace.Racers,
                presentedRace.ElapsedSeconds, deltaSeconds);
        }

        private bool TryControl(PlayerId playerId, out PlayerControlSnapshot result)
        {
            for (int i = 0; i < controls.Count; i++)
                if (controls[i].PlayerId == playerId)
                {
                    result = controls[i];
                    return true;
                }
            result = default;
            return false;
        }

        private float DrawnDistance(RacerSnapshot racer) =>
            racer.TotalDistance -
            Mathf.Min(LaunchTwitchFor(racer).Lag, Mathf.Max(0f, racer.TotalDistance));

        // The launch twitch (issue #119): drawn hesitation off the line,
        // gone within a second of GO. ElapsedSeconds accumulates only in the
        // Racing phase and resets on rematch, so it IS time-since-GO — the
        // grid, countdown, and every later read see exact stillness. The lag
        // clamp in DrawnDistance pins a slow-digging car AT the line rather
        // than ever drawing it behind where it started — floored at zero,
        // because a real starting grid (issue #147) sits at NEGATIVE
        // distance, and clamping the lag to that instead subtracted the whole
        // grid offset: every car drew stacked on the line, and stayed pinned
        // there until its true distance reached zero, which read on hardware
        // as the back row launching late.
        private LaunchTwitch LaunchTwitchFor(RacerSnapshot racer) =>
            PresentationLife.Launch(presentedRace.ElapsedSeconds,
                PresentationLife.LaunchPhase((int)racer.PlayerId, simulation.Track.Length));

        // The sim's braking answers "how hard CAN a car slow"; the dive reads
        // how hard this one is slowing, one fixed step against the last.
        private float Deceleration(PlayerId playerId)
        {
            if (previousSnapshot.Racers == null ||
                previousSnapshot.Phase != simulation.Snapshot.Phase) return 0f;
            foreach (var before in previousSnapshot.Racers)
                foreach (var after in simulation.Snapshot.Racers)
                    if (before.PlayerId == playerId && after.PlayerId == playerId)
                        return Mathf.Max(0f, (before.Speed - after.Speed) /
                            Mathf.Max(.001f, raceSettings.fixedStepSeconds));
            return 0f;
        }

        private static bool OnRacingLine(RacerSnapshot racer) =>
            racer.Pit.Phase == PitPhase.OnTrack || racer.Pit.Phase == PitPhase.Requested;

        // The drawn car center: the smoothed pose plus the racing-line lateral
        // offset (suppressed once the car is physically in the pit complex).
        private Vector2 CarCenter(RacerSnapshot racer)
        {
            CarPose(racer, out Vector2 center, out Vector2 tangent);
            return OffsetCenter(racer, center, tangent);
        }

        private Vector2 OffsetCenter(RacerSnapshot racer, Vector2 center, Vector2 tangent)
        {
            // The split tapers toward a floor through corners (issue #117):
            // drawn at full width, the outside car swept a wider arc at the
            // same angular rate — a phantom speed-up the sim never granted.
            // On straights the duel breath (issue #119) flares it outward.
            // Mid-exchange the pass clearance outranks the corner taper: the
            // passing body swings around its rival at full width even in a
            // corner — brief, real relative motion — rather than ghosting
            // through the file. Under all of it sits the body floor (issue
            // #143): the taper and the nose-to-tail pad ramp independently,
            // and halfway through a corner approach neither has finished its
            // job, so the split holds whatever width the two bodies need
            // until the file itself is long enough to keep them apart.
            // Modeled lateral (issue #147) is drawn exactly while racing. Pit
            // entry carries that same position through the course handoff and
            // then eases it onto the pit centerline; discarding it on the phase
            // change made the car jump despite coincident route centerlines.
            float lateralOffset =
                PitLanePresentationMapper.PresentedLateralOffset(racer);
            return new Vector2(center.x - tangent.y * lateralOffset, center.y + tangent.x * lateralOffset);
        }

        private void PollRaceNavigation()
        {
#if UNITY_EDITOR || (DEVELOPMENT_BUILD && UNITY_ANDROID)
            if (visualLabConsumedPress) return;
#endif
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

        private bool HandleOverlayTap(Vector2 screenPosition)
        {
            Vector2 gui = new Vector2(screenPosition.x * 1920f / Screen.width,
                (Screen.height - screenPosition.y) * 1080f / Screen.height);
            RacePhase phase = simulation.Snapshot.Phase;
            if (exitConfirmationOpen)
            {
                if (OverlayPrimaryButton.Contains(gui)) { ReturnToSetup(); return true; }
                if (OverlaySecondaryButton.Contains(gui))
                {
                    exitConfirmationOpen = false;
                    return true;
                }
                return false;
            }
            if (phase == RacePhase.Finished)
            {
                if (OverlayPrimaryButton.Contains(gui)) { StartRematch(); return true; }
                if (OverlaySecondaryButton.Contains(gui)) { ReturnToSetup(); return true; }
                return false;
            }
            if (phase == RacePhase.Paused)
            {
                if (OverlayPrimaryButton.Contains(gui)) { ReturnToSetup(); return true; }
                return false;
            }
            if (RaceExitButton.Contains(gui))
            {
                exitConfirmationOpen = true;
                return true;
            }
            return false;
        }

        private void StartRematch()
        {
            courseSelection.KeepCurrentForNext();
            simulation.RequestNewRace();
            foreach (PlayerId id in carResponseStates.Keys.ToArray())
                carResponseStates[id] = CarResponseState.Still;
            accumulator = 0f;
            RefreshPresentation();
            UpdateWorldCars();
        }

        private void ReturnToSetup()
        {
            PlayerSeat[] restoredSeats = raceSeats.ToArray();
            exitConfirmationOpen = false;
            course = courseSelection.KeepCurrentForNext();
            CreateLobby(restoredSeats);
            BuildLobbySurface();
            if (hud != null) Destroy(hud.gameObject);
            hud = RaceHud.CreateFour(FourSeatRaceLayout(),
                RaceSurfaceGeometry.InactivePitBoxAccent,
                RaceSurfaceGeometry.InactivePitBoxAccent,
                RaceSurfaceGeometry.InactivePitBoxAccent,
                RaceSurfaceGeometry.InactivePitBoxAccent);
#if UNITY_EDITOR || (DEVELOPMENT_BUILD && UNITY_ANDROID)
            visualLab?.ReapplyStageComposition();
#endif
        }

#if UNITY_EDITOR || (DEVELOPMENT_BUILD && UNITY_ANDROID)
        private void SetVisualLabCarsVisible(bool visible)
        {
            if (surface != null) surface.SetCarsVisible(visible);
        }

        private void SetVisualLabHudVisible(bool visible)
        {
            if (hud != null) hud.gameObject.SetActive(visible);
        }

        private void ApplyVisualLabSurfaceStyle(RaceSurfaceStyle style)
        {
            surfaceStyle = style;
            if (surface == null) return;
            bool racing = lobby == null && simulation != null;
            TrackDefinition track = racing ? simulation.Track : course.Track;
            surface.ReplaceSurface(
                BuildSurfaceData(track, racing), surfaceStyle);
        }

        private void SetVisualLabWireframeVisible(bool visible)
        {
            visualLabWireframeVisible = visible;
            if (surface != null) surface.SetWireframeVisible(visible);
        }

        private void ApplyVisualLabCarStudy(CarStudyPresentation presentation)
        {
            visualLabCarStudy = presentation;
            if (surface != null) surface.SetCarStudy(presentation);
        }
#endif

        private SurfaceMeshData BuildSurfaceData(TrackDefinition track, bool includeRaceAccents)
        {
            IReadOnlyDictionary<PlayerId, Color> accents = includeRaceAccents
                ? raceSeats.ToDictionary(x => x.PlayerId, x => PlayerAccent(x.PlayerId))
                : new Dictionary<PlayerId, Color>();
            return RaceSurfaceGeometry.Build(
                track, PitLayout(), accents, surfaceStyle);
        }

        private void CreateLobby(IEnumerable<PlayerSeat> restoredSeats = null)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            const bool fallback = false;
#else
            const bool fallback = true;
#endif
            lobby?.Dispose();
            lobby = new PlayerLobbyPresentation(playerSession, fallback, restoredSeats,
                () => courseSelection.Next.Name, CycleSetupCourse);
        }

        private void CycleSetupCourse()
        {
            courseSelection.CycleNext();
            course = courseSelection.Next;
            BuildLobbySurface();
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

        private Color PlayerAccent(PlayerId id) =>
            playerAccents.TryGetValue(id, out Color accent) ? accent :
            id == PlayerId.Player1 ? PlayerOneAccent :
            id == PlayerId.Player2 ? PlayerTwoAccent :
            id == PlayerId.Player3 ? PlayerThreeAccent : PlayerFourAccent;

        private IReadOnlyList<PitAnnotationUiModel> BuildPitAnnotations()
        {
            var result = new List<PitAnnotationUiModel>();
            foreach (PlayerSeat seat in raceSeats)
            {
                Vec2 box = course.Pit.Boxes[(int)seat.PlayerId - 1];
                result.Add(new PitAnnotationUiModel(seat.PlayerId,
                    new Vector2(box.X, box.Y), seat.PieceIdentity.Value.Symbol));
            }
            return result;
        }

        private IReadOnlyList<CarAnnotationUiModel> BuildCarAnnotations()
        {
            return presentedRace.Racers.Select(racer =>
            {
                string status = null;
                bool statusAbove = false;
                if (racer.RecoveryRemaining > 0f)
                {
                    status = "SLOWDOWN!";
                    statusAbove = true;
                }
                else if (racer.Finished)
                    status = "FINISHED · " + Ordinal(racer.Place);
                else if (racer.Pit.Phase != PitPhase.OnTrack)
                    status = CarPitLabel(racer.Pit);
                return new CarAnnotationUiModel(racer.PlayerId, CarCenter(racer),
                    status, statusAbove);
            }).ToArray();
        }

        private PitLanePresentationLayout PitLayout() =>
            PitLanePresentationLayout.ForCourse(course);

        private void CarPose(RacerSnapshot racer, out Vector2 position, out Vector2 tangent)
        {
            // The drawn distance folds in the launch twitch, so the
            // track sample, heading, and every overlay riding the car agree.
            float drawnDistance = DrawnDistance(racer);
            CarPresentationPose pose = PitLanePresentationMapper.From(racer,
                simulation.Track.Sample(drawnDistance).Position,
                TrackPresentation.SmoothHeading(simulation.Track, drawnDistance), PitLayout());
            position = new Vector2(pose.Position.X, pose.Position.Y);
            tangent = new Vector2(pose.Tangent.X, pose.Tangent.Y);
        }

        private static string CarPitLabel(RacerPitSnapshot pit)
        {
            if (pit.Phase == PitPhase.Requested) return "PIT @ LINE";
            if (pit.TrafficState == PitTrafficState.Queued) return "PIT QUEUE";
            if (pit.TrafficState == PitTrafficState.WaitingToRelease)
                return "WAITING FOR PIT LANE";
            if (pit.Phase == PitPhase.Entering) return "PIT ENTRY";
            if (pit.Phase == PitPhase.InService) return pit.SelectedService == PitService.None
                ? "CAR PARKED · REPAIR OR LEAVE" : "IN BOX · " + ServiceName(pit.SelectedService);
            if (pit.Phase == PitPhase.Exiting) return "PIT EXIT";
            return string.Empty;
        }

        private static string ServiceName(PitService service) =>
            service == PitService.Tires ? "TIRES" : service == PitService.Fuel ? "FUEL" : "NO SERVICE";

        private static string Ordinal(int place) => RaceUiModelBuilder.Ordinal(place);
    }
}
