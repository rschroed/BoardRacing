using System;

namespace BoardRacing.Domain
{
    public sealed class CoarseThrottleMapper
    {
        private readonly float hysteresis;
        private readonly float orientationOffset;
        private int previousSector = -1;
        public CoarseThrottleMapper(float hysteresisRadians, float orientationOffsetRadians = 0f)
        { hysteresis = Math.Max(0f, hysteresisRadians); orientationOffset = orientationOffsetRadians; }
        public void Reset() { previousSector = -1; }

        public ThrottleStep Map(bool present, float radians)
        {
            if (!present) { Reset(); return ThrottleStep.Brake; }
            float sectorAngle = (float)(Math.PI * 2d / 3d);
            float angle = Normalize(radians - orientationOffset);
            int nearest = (int)Math.Floor((angle + sectorAngle * .5f) / sectorAngle) % 3;
            if (previousSector >= 0 && nearest != previousSector)
            {
                float previousCenter = previousSector * sectorAngle;
                if (AngularDistance(angle, previousCenter) < sectorAngle * .5f + hysteresis)
                    nearest = previousSector;
            }
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

    public readonly struct CrewStrategyOutput
    {
        public CrewStrategyOutput(PitService selectedService, bool requestPit, PitCallState callState,
            PitActionResult callAction, PitActionResult serviceAction)
        {
            SelectedService = selectedService; RequestPit = requestPit;
            CallState = callState; CallAction = callAction; ServiceAction = serviceAction;
        }
        public PitService SelectedService { get; }
        public bool RequestPit { get; }
        public PitCallState CallState { get; }
        public PitActionResult CallAction { get; }
        public PitActionResult ServiceAction { get; }
    }

    public sealed class CrewStrategyAdapter
    {
        private readonly Vec2 callCenter, tiresCenter, coolingCenter, halfSize;
        private readonly PitActionMachine callAction, tiresAction, coolingAction;
        private int contactId = -1;
        private bool wasInsideCall, callPlacementArmed, suppressNextObservedContact = true;
        private bool suppressServiceUntilPlacement;

        public CrewStrategyAdapter(Vec2 callCenter, Vec2 tiresCenter, Vec2 coolingCenter, Vec2 halfSize,
            float targetAngle, float alignmentTolerance, float holdDuration, float callHoldDuration = .75f)
        {
            if (halfSize.X <= 0f || halfSize.Y <= 0f) throw new ArgumentException("Crew service zones must be positive.");
            this.callCenter = callCenter; this.tiresCenter = tiresCenter;
            this.coolingCenter = coolingCenter; this.halfSize = halfSize;
            callAction = new PitActionMachine(callCenter, halfSize, targetAngle, alignmentTolerance, callHoldDuration);
            tiresAction = new PitActionMachine(tiresCenter, halfSize, targetAngle, alignmentTolerance, holdDuration);
            coolingAction = new PitActionMachine(coolingCenter, halfSize, targetAngle, alignmentTolerance, holdDuration);
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
                return default;
            }

            bool newContact = controls.Crew.ContactId != contactId;
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
                    if (service == PitService.None) suppressServiceUntilPlacement = false;
                    return new CrewStrategyOutput(PitService.None, false,
                        PitCallState.Unavailable, default, default);
                }
                PitActionResult action;
                if (service == PitService.Tires)
                {
                    coolingAction.Reset();
                    action = tiresAction.Update(controls.Crew, deltaSeconds);
                }
                else if (service == PitService.Cooling)
                {
                    tiresAction.Reset();
                    action = coolingAction.Update(controls.Crew, deltaSeconds);
                }
                else
                {
                    ResetActions();
                    action = default;
                }
                return new CrewStrategyOutput(service, false, PitCallState.Unavailable, default, action);
            }

            ResetActions();
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
            callAction.Reset(); ResetActions();
        }

        private PitService ServiceAt(Vec2 position)
        {
            if (Inside(position, tiresCenter)) return PitService.Tires;
            if (Inside(position, coolingCenter)) return PitService.Cooling;
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
            callAction.Reset(); ResetActions();
        }

        private void ResetActions()
        {
            tiresAction.Reset(); coolingAction.Reset();
        }
    }
}
