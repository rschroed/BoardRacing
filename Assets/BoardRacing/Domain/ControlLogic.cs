using System;

namespace BoardRacing.Domain
{
    // Raw Ship orientations (Player 1 frame) at which the nose points at each rendered
    // throttle wedge. Hardware-measured against frame 40:23's corner cluster; the values
    // live in TrancheOneSettings so recalibration never requires a domain change.
    public readonly struct ThrottleStops
    {
        public ThrottleStops(float brakeRadians, float driveRadians, float boostRadians)
        { Brake = brakeRadians; Drive = driveRadians; Boost = boostRadians; }
        public float Brake { get; }
        public float Drive { get; }
        public float Boost { get; }
    }

    public sealed class CoarseThrottleMapper
    {
        private readonly float hysteresis;
        private readonly float orientationOffset;
        private readonly float[] stopCenters;
        private int previousSector = -1;
        public CoarseThrottleMapper(float hysteresisRadians, ThrottleStops stops,
            float orientationOffsetRadians = 0f)
        {
            hysteresis = Math.Max(0f, hysteresisRadians);
            orientationOffset = orientationOffsetRadians;
            stopCenters = new[] { stops.Brake, stops.Drive, stops.Boost };
        }
        public void Reset() { previousSector = -1; }

        public ThrottleStep Map(bool present, float radians)
        {
            if (!present) { Reset(); return ThrottleStep.Brake; }
            float angle = Normalize(radians - orientationOffset);
            int nearest = 0;
            float nearestDistance = float.MaxValue;
            for (int i = 0; i < stopCenters.Length; i++)
            {
                float distance = AngularDistance(angle, stopCenters[i]);
                if (distance < nearestDistance) { nearestDistance = distance; nearest = i; }
            }
            // Leaving the previous stop requires rotating the hysteresis margin past the
            // midpoint boundary (where the two distances differ by twice the rotation),
            // so a slightly off-center Ship cannot flicker between wedges.
            if (previousSector >= 0 && nearest != previousSector &&
                AngularDistance(angle, stopCenters[previousSector]) - nearestDistance < hysteresis * 2f)
                nearest = previousSector;
            previousSector = nearest;
            return nearest == 0 ? ThrottleStep.Brake : nearest == 1 ? ThrottleStep.Drive : ThrottleStep.Boost;
        }

        public static float Normalize(float radians)
        {
            float turn = (float)(Math.PI * 2d);
            radians %= turn;
            return radians < 0f ? radians + turn : radians;
        }

        public static float AngularDistance(float a, float b)
        {
            float turn = (float)(Math.PI * 2d);
            float delta = Math.Abs(Normalize(a) - Normalize(b));
            return Math.Min(delta, turn - delta);
        }
    }

    public readonly struct PitActionResult
    {
        public PitActionResult(PitActionState state, float progress, bool completedThisUpdate)
        { State = state; Progress = progress; CompletedThisUpdate = completedThisUpdate; }
        public PitActionState State { get; }
        public float Progress { get; }
        public bool CompletedThisUpdate { get; }
    }

    public sealed class PitActionMachine
    {
        private readonly Vec2 center;
        private readonly Vec2 halfSize;
        private readonly float targetAngle;
        private readonly float tolerance;
        private readonly float holdDuration;
        private float held;
        private bool latched;

        public PitActionMachine(Vec2 center, Vec2 halfSize, float targetAngle, float tolerance, float holdDuration)
        { this.center = center; this.halfSize = halfSize; this.targetAngle = targetAngle; this.tolerance = tolerance; this.holdDuration = Math.Max(0.01f, holdDuration); }

        public PitActionResult Update(PieceState crew, float deltaSeconds)
        {
            bool inZone = crew.Present && Math.Abs(crew.Position.X - center.X) <= halfSize.X && Math.Abs(crew.Position.Y - center.Y) <= halfSize.Y;
            if (!inZone) { held = 0f; latched = false; return new PitActionResult(crew.Present ? PitActionState.Idle : PitActionState.Canceled, 0f, false); }
            if (latched) return new PitActionResult(PitActionState.Completed, 1f, false);
            if (CoarseThrottleMapper.AngularDistance(crew.OrientationRadians, targetAngle) > tolerance)
            { held = 0f; return new PitActionResult(PitActionState.Aligning, 0f, false); }
            held += Math.Max(0f, deltaSeconds);
            if (held >= holdDuration) { latched = true; return new PitActionResult(PitActionState.Completed, 1f, true); }
            return new PitActionResult(PitActionState.Holding, Math.Min(1f, held / holdDuration), false);
        }

        public void Reset() { held = 0f; latched = false; }
    }

    /// <summary>
    /// Converts circular Robot motion inside a service zone into meter drain.
    /// The player stirs the Robot around the dial; accumulated angular travel
    /// around the zone center drains the meter — direction does not matter.
    /// </summary>
    public sealed class StirServiceMachine
    {
        // Angle samples closer to the center than this are too noisy to trust,
        // and a single-update jump larger than this cap is a contact teleport,
        // not a stir; both would otherwise produce phantom progress.
        public const float MinimumStirRadius = 12f;
        private const float MaximumStepRadians = 1.5f;

        private readonly Vec2 center, halfSize;
        private readonly float fullStirRadians;
        private bool hasPriorAngle;
        private float priorAngle;

        public StirServiceMachine(Vec2 center, Vec2 halfSize, float stirTurnsForFullService)
        {
            if (halfSize.X <= 0f || halfSize.Y <= 0f || stirTurnsForFullService <= 0f ||
                float.IsNaN(stirTurnsForFullService) || float.IsInfinity(stirTurnsForFullService))
                throw new ArgumentException("Stir service zones must be positive.");
            this.center = center; this.halfSize = halfSize;
            fullStirRadians = stirTurnsForFullService * 2f * (float)Math.PI;
        }

        /// <returns>State plus the fraction of a full meter drained by this update's motion.</returns>
        public (PitActionState State, float Drain) Update(PieceState crew)
        {
            bool inZone = crew.Present && Math.Abs(crew.Position.X - center.X) <= halfSize.X &&
                Math.Abs(crew.Position.Y - center.Y) <= halfSize.Y;
            if (!inZone) { hasPriorAngle = false; return (crew.Present ? PitActionState.Idle : PitActionState.Canceled, 0f); }
            float dx = crew.Position.X - center.X, dy = crew.Position.Y - center.Y;
            if (Math.Sqrt(dx * dx + dy * dy) < MinimumStirRadius)
            { hasPriorAngle = false; return (PitActionState.Stirring, 0f); }
            float angle = (float)Math.Atan2(dy, dx);
            float drain = 0f;
            if (hasPriorAngle)
            {
                float step = Math.Abs(SignedAngleDelta(angle, priorAngle));
                drain = Math.Min(step, MaximumStepRadians) / fullStirRadians;
            }
            priorAngle = angle; hasPriorAngle = true;
            return (PitActionState.Stirring, drain);
        }

        public void Reset() { hasPriorAngle = false; }

        private static float SignedAngleDelta(float a, float b)
        {
            float delta = (a - b) % (2f * (float)Math.PI);
            if (delta > Math.PI) delta -= 2f * (float)Math.PI;
            if (delta < -Math.PI) delta += 2f * (float)Math.PI;
            return delta;
        }
    }

    public readonly struct CrewStrategyOutput
    {
        public CrewStrategyOutput(PitService selectedService, bool requestPit, PitCallState callState,
            PitActionResult callAction, PitActionResult serviceAction, float serviceDrain = 0f,
            bool requestExit = false)
        {
            SelectedService = selectedService; RequestPit = requestPit;
            CallState = callState; CallAction = callAction; ServiceAction = serviceAction;
            ServiceDrain = serviceDrain; RequestExit = requestExit;
        }
        public PitService SelectedService { get; }
        public bool RequestPit { get; }
        public PitCallState CallState { get; }
        public PitActionResult CallAction { get; }
        public PitActionResult ServiceAction { get; }
        public float ServiceDrain { get; }
        public bool RequestExit { get; }
    }

    public sealed class CrewStrategyAdapter
    {
        private readonly Vec2 callCenter, tiresCenter, fuelCenter, halfSize;
        private readonly PitActionMachine callAction, leaveAction;
        private readonly StirServiceMachine tiresAction, fuelAction;
        private int contactId = -1;
        private bool wasInsideCall, callPlacementArmed, suppressNextObservedContact = true;
        private bool suppressServiceUntilPlacement;
        private bool leaveInitialized, leaveArmed, wasInsideLeave;

        public CrewStrategyAdapter(Vec2 callCenter, Vec2 tiresCenter, Vec2 fuelCenter, Vec2 halfSize,
            float stirTurnsForFullService, float callHoldDuration = .75f)
        {
            if (halfSize.X <= 0f || halfSize.Y <= 0f) throw new ArgumentException("Crew service zones must be positive.");
            this.callCenter = callCenter; this.tiresCenter = tiresCenter;
            this.fuelCenter = fuelCenter; this.halfSize = halfSize;
            // Robot actions are placement-only (issue #77 hardware review): the raw SDK
            // Robot orientation has no player-visible meaning on the disc piece. Call Pit
            // holds after placement (full-circle tolerance); services drain by stirring.
            callAction = new PitActionMachine(callCenter, halfSize, 0f, (float)Math.PI, callHoldDuration);
            // The same circle doubles as Leave Pit while the car is parked.
            leaveAction = new PitActionMachine(callCenter, halfSize, 0f, (float)Math.PI, callHoldDuration);
            tiresAction = new StirServiceMachine(tiresCenter, halfSize, stirTurnsForFullService);
            fuelAction = new StirServiceMachine(fuelCenter, halfSize, stirTurnsForFullService);
        }

        public CrewStrategyOutput Update(PlayerControlSnapshot controls, RacePhase racePhase,
            RacerPitSnapshot pit, float deltaSeconds)
        {
            if (!controls.Crew.Present || controls.Warnings.HasFlag(InputWarning.WrongRegion))
            {
                ResetForContactLoss();
                return default;
            }

            if (racePhase != RacePhase.Racing)
            {
                contactId = controls.Crew.ContactId;
                wasInsideCall = Inside(controls.Crew.Position, callCenter);
                callPlacementArmed = false;
                suppressNextObservedContact = false;
                callAction.Reset();
                ResetActions();
                ResetLeave();
                return default;
            }

            bool newContact = controls.Crew.ContactId != contactId;
            bool deliberateNewContact = newContact && !suppressNextObservedContact;
            if (controls.Crew.ContactId != contactId)
            {
                contactId = controls.Crew.ContactId;
                callAction.Reset();
                bool inside = Inside(controls.Crew.Position, callCenter);
                callPlacementArmed = inside && !suppressNextObservedContact;
                wasInsideCall = inside;
                suppressNextObservedContact = false;
            }

            if (pit.Phase == PitPhase.InService)
            {
                callPlacementArmed = false;
                callAction.Reset();
                PitService service = ServiceAt(controls.Crew.Position);
                if (suppressServiceUntilPlacement)
                {
                    ResetActions();
                    ResetLeave();
                    if (service == PitService.None) suppressServiceUntilPlacement = false;
                    return new CrewStrategyOutput(PitService.None, false,
                        PitCallState.Unavailable, default, default);
                }

                // Leave Pit arms only on a deliberate entry: a Robot already resting
                // in the circle when the car parks must leave it (or be re-placed)
                // before it can trigger an exit.
                bool insideLeave = Inside(controls.Crew.Position, callCenter);
                bool freshLeaveState = !leaveInitialized;
                leaveInitialized = true;
                if (!insideLeave)
                {
                    leaveArmed = false;
                    leaveAction.Reset();
                }
                else if (deliberateNewContact || (!freshLeaveState && !wasInsideLeave))
                    leaveArmed = true;
                wasInsideLeave = insideLeave;
                wasInsideCall = insideLeave;

                if (leaveArmed && insideLeave)
                {
                    ResetActions();
                    PitActionResult leave = leaveAction.Update(controls.Crew, deltaSeconds);
                    if (leave.CompletedThisUpdate)
                    {
                        leaveArmed = false;
                        return new CrewStrategyOutput(PitService.None, false, PitCallState.Requested,
                            leave, default, 0f, requestExit: true);
                    }
                    PitCallState leaveState = leave.State == PitActionState.Holding
                        ? PitCallState.Holding : PitCallState.NeedsPlacement;
                    return new CrewStrategyOutput(PitService.None, false, leaveState, leave, default);
                }

                PitActionState serviceState;
                float drain;
                if (service == PitService.Tires)
                {
                    fuelAction.Reset();
                    (serviceState, drain) = tiresAction.Update(controls.Crew);
                }
                else if (service == PitService.Fuel)
                {
                    tiresAction.Reset();
                    (serviceState, drain) = fuelAction.Update(controls.Crew);
                }
                else
                {
                    ResetActions();
                    (serviceState, drain) = (default, 0f);
                }
                return new CrewStrategyOutput(service, false, PitCallState.NeedsPlacement, default,
                    new PitActionResult(serviceState, 0f, false), drain);
            }

            ResetActions();
            ResetLeave();
            suppressServiceUntilPlacement = false;
            if (pit.Phase != PitPhase.OnTrack)
            {
                callPlacementArmed = false;
                callAction.Reset();
                return new CrewStrategyOutput(PitService.None, false, PitCallState.Requested, default, default);
            }

            bool insideCall = Inside(controls.Crew.Position, callCenter);
            if (!insideCall)
            {
                wasInsideCall = false;
                callPlacementArmed = false;
                callAction.Reset();
                return new CrewStrategyOutput(PitService.None, false, PitCallState.NeedsPlacement, default, default);
            }

            if (!newContact && !wasInsideCall)
            {
                callPlacementArmed = true;
                callAction.Reset();
            }
            wasInsideCall = true;

            if (!callPlacementArmed)
                return new CrewStrategyOutput(PitService.None, false, PitCallState.NeedsPlacement, default, default);

            PitActionResult call = callAction.Update(controls.Crew, deltaSeconds);
            PitCallState state = call.State == PitActionState.Aligning ? PitCallState.Aligning :
                call.State == PitActionState.Holding ? PitCallState.Holding :
                call.State == PitActionState.Completed ? PitCallState.Requested : PitCallState.NeedsPlacement;
            if (!call.CompletedThisUpdate)
                return new CrewStrategyOutput(PitService.None, false, state, call, default);

            callPlacementArmed = false;
            return new CrewStrategyOutput(PitService.None, true, PitCallState.Requested, call, default);
        }

        public void Reset()
        {
            contactId = -1;
            wasInsideCall = callPlacementArmed = false;
            suppressNextObservedContact = true;
            suppressServiceUntilPlacement = true;
            callAction.Reset(); ResetActions(); ResetLeave();
        }

        private PitService ServiceAt(Vec2 position)
        {
            if (Inside(position, tiresCenter)) return PitService.Tires;
            if (Inside(position, fuelCenter)) return PitService.Fuel;
            return PitService.None;
        }

        private bool Inside(Vec2 position, Vec2 center) =>
            Math.Abs(position.X - center.X) <= halfSize.X && Math.Abs(position.Y - center.Y) <= halfSize.Y;

        private void ResetForContactLoss()
        {
            contactId = -1;
            wasInsideCall = callPlacementArmed = false;
            suppressNextObservedContact = false;
            suppressServiceUntilPlacement = false;
            callAction.Reset(); ResetActions(); ResetLeave();
        }

        private void ResetActions()
        {
            tiresAction.Reset(); fuelAction.Reset();
        }

        private void ResetLeave()
        {
            leaveInitialized = leaveArmed = wasInsideLeave = false;
            leaveAction.Reset();
        }
    }
}
