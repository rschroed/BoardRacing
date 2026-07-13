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
}
