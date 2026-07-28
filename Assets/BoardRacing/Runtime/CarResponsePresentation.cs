using System;
using BoardRacing.Domain;

namespace BoardRacing.Runtime
{
    /// <summary>
    /// The four bounded response channels owned by a car's visual children.
    /// None of them can express position, heading, or race progress.
    /// </summary>
    public readonly struct CarResponseState
    {
        public CarResponseState(float drive, float brake, float boost, float corner)
        {
            Drive = Clamp01(drive);
            Brake = Clamp01(brake);
            Boost = Clamp01(boost);
            Corner = Clamp01(corner);
        }

        public float Drive { get; }
        public float Brake { get; }
        public float Boost { get; }
        public float Corner { get; }

        public static CarResponseState Still => new CarResponseState(0f, 0f, 0f, 0f);

        private static float Clamp01(float value) =>
            Math.Max(0f, Math.Min(1f, value));
    }

    /// <summary>
    /// Pure translation from established race/input state into authored visual
    /// ceilings. This is presentation only: the result contains no transform or
    /// simulation command and cannot move a car away from race truth.
    /// </summary>
    public static class CarResponsePresentation
    {
        public const float MinimumCueSpeed = Pace.BasePace * .04f;
        public const float DriveIntensity = .55f;

        public static CarResponseState Targets(RacePhase phase, bool onRacingLine,
            bool finished, bool drivingPiecePresent, ThrottleStep throttle, float speed,
            float brakeDive, float driftDegrees)
        {
            bool active = phase == RacePhase.Racing && onRacingLine && !finished;
            if (!active) return CarResponseState.Still;

            bool moving = speed > MinimumCueSpeed;
            float drive = drivingPiecePresent && moving && throttle == ThrottleStep.Drive
                ? DriveIntensity : 0f;
            float boost = drivingPiecePresent && moving && throttle == ThrottleStep.Boost
                ? 1f : 0f;
            // The red rear cue answers the command immediately; measured dive
            // adds weight without being required for the input to read.
            float brake = drivingPiecePresent && moving && throttle == ThrottleStep.Brake
                ? .45f + .55f * Clamp01(brakeDive) : 0f;
            // Existing drift is already curvature × overspeed with a tested
            // deterministic cap, so it is the single source of scrub truth.
            float corner = Clamp01(Math.Abs(driftDegrees) /
                Math.Max(.001f, CornerCharacter.MaxDriftDegrees));
            return new CarResponseState(drive, brake, boost, corner);
        }

        public static CarResponseState Step(CarResponseState current,
            CarResponseState target, float deltaSeconds)
        {
            float delta = Math.Max(0f, Math.Min(.25f, deltaSeconds));
            return new CarResponseState(
                Move(current.Drive, target.Drive, delta, .10f, .14f),
                Move(current.Brake, target.Brake, delta, .06f, .18f),
                Move(current.Boost, target.Boost, delta, .05f, .16f),
                Move(current.Corner, target.Corner, delta, .08f, .12f));
        }

        // A deterministic engine beat. Player phase prevents four simultaneous
        // cars from reading as one global screen pulse.
        public static float Pulse(float raceSeconds, PlayerId playerId)
        {
            double phase = (int)playerId * 1.61803398875;
            return .5f + .5f * (float)Math.Sin(raceSeconds * 2.0 * Math.PI * 5.2 + phase);
        }

        private static float Move(float current, float target, float delta,
            float attackSeconds, float releaseSeconds)
        {
            float duration = target > current ? attackSeconds : releaseSeconds;
            float maximum = duration <= 0f ? 1f : delta / duration;
            if (current < target) return Math.Min(target, current + maximum);
            return Math.Max(target, current - maximum);
        }

        private static float Clamp01(float value) =>
            Math.Max(0f, Math.Min(1f, value));
    }
}
