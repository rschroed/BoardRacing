using System;

namespace BoardRacing.Domain
{
    public sealed class CoarseThrottleMapper
    {
        private readonly float hysteresis;
        private int previousSector = -1;
        public CoarseThrottleMapper(float hysteresisRadians) { hysteresis = Math.Max(0f, hysteresisRadians); }
        public void Reset() { previousSector = -1; }

        public ThrottleStep Map(bool present, bool touched, float radians)
        {
            if (!present || !touched) { Reset(); return ThrottleStep.Off; }
            float angle = Normalize(radians);
            int nearest = (int)Math.Floor((angle + Math.PI / 4d) / (Math.PI / 2d)) % 4;
            if (previousSector >= 0 && nearest != previousSector)
            {
                float previousCenter = previousSector * (float)(Math.PI / 2d);
                if (AngularDistance(angle, previousCenter) < (float)Math.PI / 4f + hysteresis)
                    nearest = previousSector;
            }
            previousSector = nearest;
            return (ThrottleStep)((nearest + 1) * 25);
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
            if (!crew.Touched) { held = 0f; return new PitActionResult(PitActionState.Positioned, 0f, false); }
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
        public CrewStrategyOutput(PitService selectedService, bool requestPit, PitActionResult serviceAction)
        { SelectedService = selectedService; RequestPit = requestPit; ServiceAction = serviceAction; }
        public PitService SelectedService { get; }
        public bool RequestPit { get; }
        public PitActionResult ServiceAction { get; }
    }

    public sealed class CrewStrategyAdapter
    {
        private readonly Vec2 tiresCenter, coolingCenter, halfSize;
        private readonly PitActionMachine tiresAction, coolingAction;
        private int contactId = -1, armedContactId = -1;
        private PitService lastReleasedService, armedService;
        private bool requestArmed;

        public CrewStrategyAdapter(Vec2 tiresCenter, Vec2 coolingCenter, Vec2 halfSize,
            float targetAngle, float alignmentTolerance, float holdDuration)
        {
            if (halfSize.X <= 0f || halfSize.Y <= 0f) throw new ArgumentException("Crew service zones must be positive.");
            this.tiresCenter = tiresCenter; this.coolingCenter = coolingCenter; this.halfSize = halfSize;
            tiresAction = new PitActionMachine(tiresCenter, halfSize, targetAngle, alignmentTolerance, holdDuration);
            coolingAction = new PitActionMachine(coolingCenter, halfSize, targetAngle, alignmentTolerance, holdDuration);
        }

        public CrewStrategyOutput Update(PlayerControlSnapshot controls, RacePhase racePhase,
            RacerPitSnapshot pit, float deltaSeconds)
        {
            bool invalid = !controls.Crew.Present || controls.Crew.RequiresRelease ||
                controls.Warnings.HasFlag(InputWarning.WrongRegion);
            if (invalid || racePhase != RacePhase.Racing)
            {
                Reset();
                return default;
            }

            if (controls.Crew.ContactId != contactId)
            {
                ResetRequest();
                lastReleasedService = PitService.None;
                contactId = controls.Crew.ContactId;
            }

            if (pit.Phase == PitPhase.InService)
            {
                ResetRequest();
                PitActionResult action;
                if (pit.SelectedService == PitService.Tires)
                {
                    coolingAction.Reset();
                    action = tiresAction.Update(controls.Crew, deltaSeconds);
                }
                else if (pit.SelectedService == PitService.Cooling)
                {
                    tiresAction.Reset();
                    action = coolingAction.Update(controls.Crew, deltaSeconds);
                }
                else
                {
                    ResetActions();
                    action = default;
                }
                return new CrewStrategyOutput(PitService.None, false, action);
            }

            ResetActions();
            if (pit.Phase != PitPhase.OnTrack)
            {
                ResetRequest();
                return default;
            }

            PitService service = ServiceAt(controls.Crew.Position);
            bool request = false;
            if (!controls.Crew.Touched)
            {
                if (requestArmed && armedContactId == controls.Crew.ContactId && armedService == service &&
                    service != PitService.None)
                    request = true;
                ResetRequest();
                lastReleasedService = service;
            }
            else if (!requestArmed && service != PitService.None && service == lastReleasedService)
            {
                requestArmed = true; armedContactId = controls.Crew.ContactId; armedService = service;
            }
            else if (requestArmed && (armedContactId != controls.Crew.ContactId || armedService != service))
            {
                ResetRequest();
            }

            return new CrewStrategyOutput(service, request, default);
        }

        public void Reset()
        {
            contactId = -1; lastReleasedService = PitService.None;
            ResetRequest(); ResetActions();
        }

        private PitService ServiceAt(Vec2 position)
        {
            if (Inside(position, tiresCenter)) return PitService.Tires;
            if (Inside(position, coolingCenter)) return PitService.Cooling;
            return PitService.None;
        }

        private bool Inside(Vec2 position, Vec2 center) =>
            Math.Abs(position.X - center.X) <= halfSize.X && Math.Abs(position.Y - center.Y) <= halfSize.Y;

        private void ResetRequest()
        {
            requestArmed = false; armedContactId = -1; armedService = PitService.None;
        }

        private void ResetActions()
        {
            tiresAction.Reset(); coolingAction.Reset();
        }
    }
}
