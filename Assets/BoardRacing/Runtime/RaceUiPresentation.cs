using System;
using System.Collections.Generic;
using System.Linq;
using BoardRacing.Domain;
using UnityEngine;

namespace BoardRacing.Runtime
{
    internal readonly struct ServiceTargets
    {
        public ServiceTargets(Vector2 callPit, Vector2 tires, Vector2 cooling)
        {
            CallPit = callPit;
            Tires = tires;
            Cooling = cooling;
        }

        public Vector2 CallPit { get; }
        public Vector2 Tires { get; }
        public Vector2 Cooling { get; }
    }

    internal readonly struct CornerControllerLayout
    {
        public CornerControllerLayout(Vector2 center, float coreRadius, float throttleRadius,
            float dialRadius, Rect identityBounds, Rect stateWordBounds, Rect statusBounds,
            Rect instructionBounds, Rect heatLabelBounds, Rect tiresLabelBounds,
            Rect brakeBounds, Rect driveBounds, Rect boostBounds)
        {
            Center = center;
            CoreRadius = coreRadius;
            ThrottleRadius = throttleRadius;
            DialRadius = dialRadius;
            IdentityBounds = identityBounds;
            StateWordBounds = stateWordBounds;
            StatusBounds = statusBounds;
            InstructionBounds = instructionBounds;
            HeatLabelBounds = heatLabelBounds;
            TiresLabelBounds = tiresLabelBounds;
            BrakeBounds = brakeBounds;
            DriveBounds = driveBounds;
            BoostBounds = boostBounds;
        }

        public Vector2 Center { get; }
        public float CoreRadius { get; }
        public float ThrottleRadius { get; }
        public float DialRadius { get; }
        public Rect IdentityBounds { get; }
        public Rect StateWordBounds { get; }
        public Rect StatusBounds { get; }
        public Rect InstructionBounds { get; }
        public Rect HeatLabelBounds { get; }
        public Rect TiresLabelBounds { get; }
        public Rect BrakeBounds { get; }
        public Rect DriveBounds { get; }
        public Rect BoostBounds { get; }

        public Rect ThrottleBounds(ThrottleStep throttle) => throttle == ThrottleStep.Brake
            ? BrakeBounds : throttle == ThrottleStep.Drive ? DriveBounds : BoostBounds;
    }

    internal readonly struct PlayerLayout
    {
        public PlayerLayout(PlayerId playerId, float rotationDegrees, Rect cornerBounds,
            Rect safeContentBounds, CornerControllerLayout controller, Rect callPit, Rect tires, Rect cooling)
        {
            PlayerId = playerId;
            RotationDegrees = rotationDegrees;
            CornerBounds = cornerBounds;
            SafeContentBounds = safeContentBounds;
            Controller = controller;
            CallPit = callPit;
            Tires = tires;
            Cooling = cooling;
        }

        public PlayerId PlayerId { get; }
        public float RotationDegrees { get; }
        public bool Opposite => Mathf.Approximately(RotationDegrees, 180f);
        public Rect CornerBounds { get; }
        public Rect SafeContentBounds { get; }
        public CornerControllerLayout Controller { get; }
        public Rect CallPit { get; }
        public Rect Tires { get; }
        public Rect Cooling { get; }
    }

    internal readonly struct RaceLayout
    {
        public const float ReferenceWidth = 1920f;
        public const float ReferenceHeight = 1080f;

        public RaceLayout(PlayerLayout playerOne, PlayerLayout playerTwo)
        {
            PlayerOne = playerOne;
            PlayerTwo = playerTwo;
        }

        public Rect Canvas => new Rect(0f, 0f, ReferenceWidth, ReferenceHeight);

        // Reserved ownership bounds, not final rendered panel geometry.
        public Rect SharedRaceBounds => new Rect(240f, 240f, 1440f, 600f);
        public Rect CenterOverlayBounds => new Rect(710f, 640f, 500f, 90f);
        public PlayerLayout PlayerOne { get; }
        public PlayerLayout PlayerTwo { get; }

        public PlayerLayout For(PlayerId id) => id == PlayerId.Player1 ? PlayerOne : PlayerTwo;

        public static RaceLayout Create(ServiceTargets playerOneTargets, ServiceTargets playerTwoTargets,
            Vector2 serviceHalfSize)
        {
            if (serviceHalfSize.x <= 0f || serviceHalfSize.y <= 0f)
                throw new ArgumentException("Service target half-size must be positive.", nameof(serviceHalfSize));

            Rect Target(Vector2 runtimeCenter) => new Rect(runtimeCenter.x - serviceHalfSize.x,
                ReferenceHeight - runtimeCenter.y - serviceHalfSize.y,
                serviceHalfSize.x * 2f, serviceHalfSize.y * 2f);

            if (Target(playerOneTargets.Tires).Overlaps(Target(playerOneTargets.Cooling)))
                throw new ArgumentException("Tires and Cooling zones must not overlap.",
                    nameof(playerOneTargets));
            if (Target(playerTwoTargets.Tires).Overlaps(Target(playerTwoTargets.Cooling)))
                throw new ArgumentException("Tires and Cooling zones must not overlap.",
                    nameof(playerTwoTargets));

            Rect MirrorRect(Rect rect) => new Rect(ReferenceWidth - rect.xMax,
                ReferenceHeight - rect.yMax, rect.width, rect.height);
            Vector2 MirrorPoint(Vector2 point) => new Vector2(ReferenceWidth - point.x,
                ReferenceHeight - point.y);

            // Corner cluster per the approved frames (17:14 dial/arc treatment, 40:23 board):
            // Ship well and throttle arc tuck into the corner; state word, status, and
            // instruction occupy the safe content band clear of every Robot zone.
            var playerOneController = new CornerControllerLayout(
                new Vector2(ReferenceWidth, ReferenceHeight), 170f, 250f, 46f,
                new Rect(1000f, 590f, 240f, 40f),
                new Rect(1000f, 910f, 250f, 80f),
                new Rect(1000f, 860f, 240f, 40f),
                new Rect(1000f, 640f, 420f, 60f),
                new Rect(1260f, 1000f, 220f, 26f),
                new Rect(1510f, 830f, 220f, 26f),
                new Rect(1616f, 1008f, 90f, 30f),
                new Rect(1688f, 882f, 90f, 30f),
                new Rect(1820f, 806f, 90f, 30f));
            var playerTwoController = new CornerControllerLayout(
                MirrorPoint(playerOneController.Center), playerOneController.CoreRadius,
                playerOneController.ThrottleRadius, playerOneController.DialRadius,
                MirrorRect(playerOneController.IdentityBounds), MirrorRect(playerOneController.StateWordBounds),
                MirrorRect(playerOneController.StatusBounds), MirrorRect(playerOneController.InstructionBounds),
                MirrorRect(playerOneController.HeatLabelBounds), MirrorRect(playerOneController.TiresLabelBounds),
                MirrorRect(playerOneController.BrakeBounds), MirrorRect(playerOneController.DriveBounds),
                MirrorRect(playerOneController.BoostBounds));

            return new RaceLayout(
                new PlayerLayout(PlayerId.Player1, 0f,
                    new Rect(960f, 540f, 960f, 540f),
                    new Rect(1000f, 580f, 880f, 460f),
                    playerOneController,
                    Target(playerOneTargets.CallPit), Target(playerOneTargets.Tires),
                    Target(playerOneTargets.Cooling)),
                new PlayerLayout(PlayerId.Player2, 180f,
                    new Rect(0f, 0f, 960f, 540f),
                    new Rect(40f, 40f, 880f, 460f),
                    playerTwoController,
                    Target(playerTwoTargets.CallPit), Target(playerTwoTargets.Tires),
                    Target(playerTwoTargets.Cooling)));
        }
    }

    internal enum PlayerUiInstructionKind
    {
        PlaceShip,
        GridReady,
        CountdownReady,
        DriveAndPit,
        PlaceRobotForService,
        MoveRobotToPlayerRegion,
        ChooseService,
        AlignService,
        HoldService,
        ServiceComplete,
        AlignPitCall,
        HoldPitCall,
        PitRequested,
        PitEntering,
        PitExiting,
        CornerRecovery,
        HeatCritical,
        TiresCritical,
        HeatWarning,
        TiresWarning,
        WaitForOtherRacer,
        RematchHold,
        RematchRelease
    }

    internal enum CenterMessageKind { None, Countdown, Go, SplitFinish, Winner }

    internal readonly struct PlayerUiModel
    {
        public PlayerUiModel(PlayerId playerId, string identity, string status,
            PlayerUiInstructionKind primaryInstructionKind, string primaryInstruction,
            ThrottleStep throttle, CarConditionVisualState condition, PitPhase pitPhase,
            PitService selectedService, float serviceProgress, PitCallState callState,
            PitActionResult callAction, PitActionResult serviceAction, bool shipPresent,
            bool robotPresent, InputWarning inputWarnings, bool finishEligible, bool finished)
        {
            if (string.IsNullOrWhiteSpace(primaryInstruction))
                throw new ArgumentException("Every player model needs one primary instruction.",
                    nameof(primaryInstruction));
            PlayerId = playerId;
            Identity = identity;
            Status = status;
            PrimaryInstructionKind = primaryInstructionKind;
            PrimaryInstruction = primaryInstruction;
            Throttle = throttle;
            Condition = condition;
            PitPhase = pitPhase;
            SelectedService = selectedService;
            ServiceProgress = serviceProgress;
            CallState = callState;
            CallAction = callAction;
            ServiceAction = serviceAction;
            ShipPresent = shipPresent;
            RobotPresent = robotPresent;
            InputWarnings = inputWarnings;
            FinishEligible = finishEligible;
            Finished = finished;
        }

        public PlayerId PlayerId { get; }
        public string Identity { get; }
        public string Status { get; }
        public PlayerUiInstructionKind PrimaryInstructionKind { get; }
        public string PrimaryInstruction { get; }
        public ThrottleStep Throttle { get; }
        public CarConditionVisualState Condition { get; }
        public PitPhase PitPhase { get; }
        public PitService SelectedService { get; }
        public float ServiceProgress { get; }
        public PitCallState CallState { get; }
        public PitActionResult CallAction { get; }
        public PitActionResult ServiceAction { get; }
        public bool ShipPresent { get; }
        public bool RobotPresent { get; }
        public InputWarning InputWarnings { get; }
        public bool FinishEligible { get; }
        public bool Finished { get; }
    }

    internal readonly struct RaceUiModel
    {
        public RaceUiModel(RacePhase phase, PlayerUiModel playerOne, PlayerUiModel playerTwo,
            CenterMessageKind centerMessageKind, string centerMessage)
        {
            Phase = phase;
            PlayerOne = playerOne;
            PlayerTwo = playerTwo;
            CenterMessageKind = centerMessageKind;
            CenterMessage = centerMessage;
        }

        public RacePhase Phase { get; }
        public PlayerUiModel PlayerOne { get; }
        public PlayerUiModel PlayerTwo { get; }
        public CenterMessageKind CenterMessageKind { get; }
        public string CenterMessage { get; }
        public PlayerUiModel For(PlayerId id) => id == PlayerId.Player1 ? PlayerOne : PlayerTwo;
    }

    internal static class RaceUiModelBuilder
    {
        private readonly struct Instruction
        {
            public Instruction(PlayerUiInstructionKind kind, string copy)
            {
                Kind = kind;
                Copy = copy;
            }

            public PlayerUiInstructionKind Kind { get; }
            public string Copy { get; }
        }

        public static RaceUiModel Build(RaceSnapshot race, IReadOnlyList<PlayerControlSnapshot> controls,
            IReadOnlyDictionary<PlayerId, CrewStrategyOutput> crewOutputs, ConditionRules conditionRules,
            int laps)
        {
            if (race.Racers == null) throw new ArgumentException("Race snapshot has no racers.", nameof(race));
            controls = controls ?? Array.Empty<PlayerControlSnapshot>();
            crewOutputs = crewOutputs ?? new Dictionary<PlayerId, CrewStrategyOutput>();

            PlayerUiModel BuildPlayer(PlayerId id)
            {
                RacerSnapshot racer = race.Racers.Single(x => x.PlayerId == id);
                PlayerControlSnapshot control = controls.FirstOrDefault(x => x.PlayerId == id);
                CrewStrategyOutput crew = crewOutputs.TryGetValue(id, out var output) ? output : default;
                CarConditionVisualState condition = CarConditionVisualMapper.From(racer, conditionRules);
                PitService selected = racer.Pit.SelectedService != PitService.None
                    ? racer.Pit.SelectedService : crew.SelectedService;
                Instruction instruction = PrimaryInstruction(race, racer, control, crew, condition);
                return new PlayerUiModel(id, Identity(id), Status(racer, selected, laps),
                    instruction.Kind, instruction.Copy, control.Throttle, condition, racer.Pit.Phase,
                    selected, racer.Pit.ServiceProgress, crew.CallState, crew.CallAction,
                    crew.ServiceAction, control.Car.Present, control.Crew.Present, control.Warnings,
                    racer.Pit.FinishEligible, racer.Finished);
            }

            (CenterMessageKind kind, string copy) = CenterMessage(race);
            return new RaceUiModel(race.Phase, BuildPlayer(PlayerId.Player1), BuildPlayer(PlayerId.Player2),
                kind, copy);
        }

        private static string Status(RacerSnapshot racer, PitService selected, int laps)
        {
            if (racer.Finished)
                return "FINISHED · " + Ordinal(racer.Place) + " · " +
                    (racer.Pit.FinishEligible ? "STOP ✓" : "STOP REQUIRED");
            if (racer.Pit.Phase == PitPhase.InService)
                return selected == PitService.None
                    ? "CAR PARKED · CHOOSE SERVICE · 0%"
                    : "CAR PARKED · " + ServiceName(selected) + " · " +
                        Mathf.RoundToInt(racer.Pit.ServiceProgress * 100f) + "%";
            if (racer.Pit.Phase == PitPhase.Requested) return "PIT CALLED · ENTRY AT LINE";
            if (racer.Pit.Phase == PitPhase.Entering) return "PIT ENTRY · THROTTLE LOCKED";
            if (racer.Pit.Phase == PitPhase.Exiting) return "SERVICE COMPLETE ✓ · REJOINING";
            return "LAP " + Math.Min(laps, racer.CompletedLaps + 1) + " / " + laps + " · " +
                Ordinal(racer.Place) + " · " + (racer.Pit.FinishEligible ? "STOP ✓" : "STOP REQUIRED");
        }

        private static Instruction PrimaryInstruction(RaceSnapshot race, RacerSnapshot racer,
            PlayerControlSnapshot control, CrewStrategyOutput crew, CarConditionVisualState condition)
        {
            if (racer.Finished)
            {
                if (race.Phase != RacePhase.Finished)
                    return new Instruction(PlayerUiInstructionKind.WaitForOtherRacer,
                        "WAITING FOR " + (racer.PlayerId == PlayerId.Player1 ? "PURPLE" : "ORANGE") +
                        " TO FINISH");
                if (!control.Car.Present)
                    return new Instruction(PlayerUiInstructionKind.PlaceShip,
                        "PLACE YOUR SHIP · REQUIRED FOR REMATCH");
                return race.AwaitingRematchRelease
                    ? new Instruction(PlayerUiInstructionKind.RematchRelease,
                        "ROTATE BOTH SHIPS OUT OF BRAKE TO RESTART")
                    : new Instruction(PlayerUiInstructionKind.RematchHold,
                        "BOTH SHIPS TO BRAKE · HOLD FOR REMATCH");
            }

            if (!control.Car.Present)
                return new Instruction(PlayerUiInstructionKind.PlaceShip,
                    "PLACE YOUR SHIP · THROTTLE IS SAFELY AT BRAKE");
            if (race.Phase == RacePhase.Grid)
                return new Instruction(PlayerUiInstructionKind.GridReady,
                    "READY · SHIP CONTROLS BRAKE / DRIVE / BOOST");
            if (race.Phase == RacePhase.Countdown)
                return new Instruction(PlayerUiInstructionKind.CountdownReady,
                    "GET READY · ROTATE SHIP AFTER GO");

            if (racer.Pit.Phase == PitPhase.InService)
                return ServiceInstruction(racer, control, crew);
            if (racer.Pit.Phase == PitPhase.Entering)
                return new Instruction(PlayerUiInstructionKind.PitEntering,
                    "CAR UNDER PIT CONTROL · WAIT FOR SERVICE");
            if (racer.Pit.Phase == PitPhase.Exiting)
                return new Instruction(PlayerUiInstructionKind.PitExiting,
                    "SERVICE COMPLETE · CAR REJOINS AUTOMATICALLY");
            if (racer.Pit.Phase == PitPhase.Requested)
                return new Instruction(PlayerUiInstructionKind.PitRequested,
                    "PIT CALLED · ENTER AT START / FINISH");
            if (crew.CallState == PitCallState.Aligning)
                return new Instruction(PlayerUiInstructionKind.AlignPitCall,
                    "ROTATE PIT ROBOT TO 0° TO CALL");
            if (crew.CallState == PitCallState.Holding)
                return new Instruction(PlayerUiInstructionKind.HoldPitCall,
                    "HOLD PIT ROBOT STEADY · " + Mathf.RoundToInt(crew.CallAction.Progress * 100f) + "%");
            if (racer.RecoveryRemaining > 0f)
                return new Instruction(PlayerUiInstructionKind.CornerRecovery,
                    "TOO FAST INTO CORNER · SPEED RECOVERING");
            if (racer.Condition.HeatPenaltyActive)
                return new Instruction(PlayerUiInstructionKind.HeatCritical,
                    "HEAT CRITICAL · POWER LIMITED · COOL OR CALL PIT");
            if (racer.Condition.TirePenaltyActive)
                return new Instruction(PlayerUiInstructionKind.TiresCritical,
                    "TIRES CRITICAL · CORNER GRIP LIMITED · CALL PIT");
            if (condition.HeatLevel == ConditionVisualLevel.Warning)
                return new Instruction(PlayerUiInstructionKind.HeatWarning,
                    "HEAT WARNING · EASE OFF OR PREPARE COOLING");
            if (condition.TireLevel == ConditionVisualLevel.Warning)
                return new Instruction(PlayerUiInstructionKind.TiresWarning,
                    "TIRES WARNING · PROTECT CORNER SPEED OR PREPARE TIRES");
            return new Instruction(PlayerUiInstructionKind.DriveAndPit,
                "DRIVE WITH SHIP · ROBOT CAN CALL PIT");
        }

        private static Instruction ServiceInstruction(RacerSnapshot racer, PlayerControlSnapshot control,
            CrewStrategyOutput crew)
        {
            if (!control.Crew.Present)
                return new Instruction(PlayerUiInstructionKind.PlaceRobotForService,
                    "PLACE ROBOT IN TIRES OR COOLING · PROGRESS RESET");
            if (control.Warnings.HasFlag(InputWarning.WrongRegion))
                return new Instruction(PlayerUiInstructionKind.MoveRobotToPlayerRegion,
                    "MOVE ROBOT TO YOUR SERVICE ZONES · PROGRESS RESET");
            if (racer.Pit.SelectedService == PitService.None && crew.SelectedService == PitService.None)
                return new Instruction(PlayerUiInstructionKind.ChooseService,
                    "MOVE ROBOT TO TIRES OR COOLING · ALIGN + HOLD");
            PitService selected = racer.Pit.SelectedService != PitService.None
                ? racer.Pit.SelectedService : crew.SelectedService;
            string service = ServiceName(selected);
            if (crew.ServiceAction.State == PitActionState.Positioned ||
                crew.ServiceAction.State == PitActionState.Aligning)
                return new Instruction(PlayerUiInstructionKind.AlignService,
                    "ROTATE ROBOT TO 0° IN " + service + " · HOLD STARTS WHEN ALIGNED");
            if (crew.ServiceAction.State == PitActionState.Holding)
                return new Instruction(PlayerUiInstructionKind.HoldService,
                    "HOLD ROBOT STEADY · " + Mathf.RoundToInt(crew.ServiceAction.Progress * 100f) + "%");
            if (crew.ServiceAction.State == PitActionState.Completed)
                return new Instruction(PlayerUiInstructionKind.ServiceComplete,
                    service + " SERVICE COMPLETE ✓");
            return new Instruction(PlayerUiInstructionKind.AlignService,
                "PLACE ROBOT IN HIGHLIGHTED " + service + " ZONE");
        }

        private static (CenterMessageKind, string) CenterMessage(RaceSnapshot race)
        {
            if (race.Phase == RacePhase.Countdown)
                return (CenterMessageKind.Countdown,
                    Math.Max(1, Mathf.CeilToInt(race.CountdownRemaining)).ToString());
            if (race.Phase == RacePhase.Racing && race.ElapsedSeconds < 1f)
                return (CenterMessageKind.Go, "GO!");
            if (race.Phase == RacePhase.Racing && race.Racers.Count(x => x.Finished) == 1)
            {
                RacerSnapshot finished = race.Racers.Single(x => x.Finished);
                return (CenterMessageKind.SplitFinish,
                    (finished.PlayerId == PlayerId.Player1 ? "▲ ORANGE" : "● PURPLE") + " FINISHED");
            }
            if (race.Phase != RacePhase.Finished) return (CenterMessageKind.None, null);
            RacerSnapshot winner = race.Racers.OrderBy(x => x.Place).First();
            return (CenterMessageKind.Winner,
                (winner.PlayerId == PlayerId.Player1 ? "▲ ORANGE" : "● PURPLE") + " WINS");
        }

        private static string Identity(PlayerId id) => id == PlayerId.Player1
            ? "▲ PLAYER 1 · ORANGE" : "● PLAYER 2 · PURPLE";
        internal static string ServiceName(PitService service) => service == PitService.Tires
            ? "TIRES" : service == PitService.Cooling ? "COOLING" : "NO SERVICE";
        internal static string ThrottleName(ThrottleStep throttle) => throttle == ThrottleStep.Boost
            ? "BOOST" : throttle == ThrottleStep.Drive ? "DRIVE" : "BRAKE";
        internal static string Ordinal(int place) => place == 1 ? "1ST" : "2ND";
    }
}
