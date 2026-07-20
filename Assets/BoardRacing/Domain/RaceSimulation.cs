using System;
using System.Collections.Generic;
using System.Linq;

namespace BoardRacing.Domain
{
    public sealed class RaceSimulation
    {
        private sealed class RacerState
        {
            public PlayerId Id;
            public float Speed, Distance, FinishTime = -1f, Recovery;
            public int PriorSection, Incidents;
            public bool Finished, IncidentThisStep;
            public float FuelUsed, TireWear, ServiceProgress, PitTimer;
            public PitService SelectedService;
            public PitPhase PitPhase;
            public int CompletedServices;
        }

        private readonly TrackDefinition track;
        private readonly RaceRules rules;
        private readonly RacerState[] racers;
        private RacePhase phase = RacePhase.Grid;
        private float countdown, elapsed, rematchHeld;
        private bool awaitingRematchRelease;
        private RaceSnapshot snapshot;

        public RaceSimulation(TrackDefinition track, RaceRules rules)
        {
            this.track = track ?? throw new ArgumentNullException(nameof(track));
            this.rules = rules;
            racers = new[]
            {
                new RacerState { Id = PlayerId.Player1, PriorSection = track.Sample(0f).SectionIndex },
                new RacerState { Id = PlayerId.Player2, PriorSection = track.Sample(0f).SectionIndex }
            };
            snapshot = BuildSnapshot();
        }

        public TrackDefinition Track => track;
        public RaceRules Rules => rules;
        public RaceSnapshot Snapshot => snapshot;

        public RaceSnapshot Step(float fixedDeltaSeconds, IReadOnlyList<RacerCommand> commands)
        {
            if (fixedDeltaSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(fixedDeltaSeconds));
            var byPlayer = commands?.ToDictionary(x => x.PlayerId) ?? new Dictionary<PlayerId, RacerCommand>();
            RacerCommand Command(PlayerId id) => byPlayer.TryGetValue(id, out var command)
                ? command : new RacerCommand(id, ThrottleStep.Brake, false, false);

            foreach (var racer in racers) racer.IncidentThisStep = false;
            if (phase == RacePhase.Grid)
            {
                if (racers.All(x => Command(x.Id).DrivingPiecePresent))
                { phase = RacePhase.Countdown; countdown = rules.CountdownSeconds; }
            }
            else if (phase == RacePhase.Countdown)
            {
                if (racers.Any(x => !Command(x.Id).DrivingPiecePresent)) { phase = RacePhase.Grid; countdown = 0f; }
                else if ((countdown -= fixedDeltaSeconds) <= 0f) { countdown = 0f; phase = RacePhase.Racing; }
            }
            else if (phase == RacePhase.Racing)
            {
                foreach (var racer in racers)
                {
                    var command = Command(racer.Id);
                    CaptureStrategyIntent(racer, command);
                    AdvanceRacer(racer, command, fixedDeltaSeconds);
                }
                elapsed += fixedDeltaSeconds;
                if (racers.All(x => x.Finished)) phase = RacePhase.Finished;
            }
            else HandleRematch(commands == null ? Array.Empty<RacerCommand>() : commands, fixedDeltaSeconds);

            snapshot = BuildSnapshot();
            return snapshot;
        }

        private void CaptureStrategyIntent(RacerState racer, RacerCommand command)
        {
            if (!rules.Pit.Enabled) return;
            if (racer.PitPhase == PitPhase.InService && command.SelectedService != PitService.None)
                racer.SelectedService = command.SelectedService;
            if (command.RequestPit && racer.PitPhase == PitPhase.OnTrack)
            {
                racer.SelectedService = PitService.None;
                racer.PitPhase = PitPhase.Requested;
            }
        }

        private void AdvanceRacer(RacerState racer, RacerCommand command, float delta)
        {
            if (racer.Finished) return;
            if (racer.PitPhase == PitPhase.Entering || racer.PitPhase == PitPhase.InService ||
                racer.PitPhase == PitPhase.Exiting)
            {
                AdvancePit(racer, command, delta);
                return;
            }
            float throttleFraction = command.DrivingPiecePresent ? (int)command.Throttle / 100f : 0f;
            BurnFuel(racer, command.DrivingPiecePresent ? command.Throttle : ThrottleStep.Brake, delta);
            bool fuelPenalty = FuelPenaltyActive(racer);
            float maximumSpeed = rules.MaxSpeed * (fuelPenalty ? rules.Conditions.EmptyMaximumSpeedScale : 1f);
            float target = maximumSpeed * throttleFraction;
            float rate;
            if (target > racer.Speed)
                rate = rules.Acceleration * (racer.Recovery > 0f ? rules.RecoveryAccelerationScale : 1f) *
                    (fuelPenalty ? rules.Conditions.EmptyAccelerationScale : 1f);
            else rate = target <= 0f ? rules.Braking : rules.Drag;
            racer.Speed = MoveTowards(racer.Speed, target, rate * delta);
            racer.Recovery = Math.Max(0f, racer.Recovery - delta);

            var before = track.Sample(racer.Distance);
            bool enteringCorner = before.Kind == TrackSectionKind.Corner && racer.PriorSection != before.SectionIndex;
            float cornerEntrySpeed = racer.Speed;
            float effectiveSafeSpeed = before.SafeSpeed;
            if (before.Kind == TrackSectionKind.Corner && rules.Conditions.Enabled)
                effectiveSafeSpeed *= 1f - racer.TireWear * (1f - rules.Conditions.FullyWornSafeSpeedScale);
            if (enteringCorner && racer.Speed > effectiveSafeSpeed)
            {
                racer.Speed *= rules.CornerSpeedScrub;
                racer.Recovery = rules.CornerRecoverySeconds;
                racer.IncidentThisStep = true;
                racer.Incidents++;
            }
            if (enteringCorner) AddCornerWear(racer, before.SafeSpeed, cornerEntrySpeed);
            racer.PriorSection = before.SectionIndex;

            float prior = racer.Distance;
            racer.Distance += racer.Speed * delta;
            float finishDistance = track.Length * rules.Laps;
            bool crossedPitLine = (int)(prior / track.Length) < (int)(racer.Distance / track.Length);
            if (racer.PitPhase == PitPhase.Requested && rules.Pit.Enabled && crossedPitLine)
            {
                racer.Distance = ((int)(prior / track.Length) + 1) * track.Length;
                racer.Speed = 0f; racer.PitPhase = PitPhase.Entering; racer.PitTimer = 0f;
                return;
            }
            if (racer.Distance >= finishDistance && racer.CompletedServices >= rules.RequiredServiceCount)
            {
                float moved = racer.Distance - prior;
                float fraction = moved <= 0f ? 1f : Math.Max(0f, Math.Min(1f, (finishDistance - prior) / moved));
                FinishRacer(racer, finishDistance, elapsed + delta * fraction);
            }
        }

        private void AdvancePit(RacerState racer, RacerCommand command, float delta)
        {
            racer.Speed = 0f;
            if (racer.PitPhase == PitPhase.Entering)
            {
                racer.ServiceProgress = 0f;
                racer.PitTimer += delta;
                if (racer.PitTimer >= rules.Pit.EntrySeconds)
                {
                    racer.PitPhase = PitPhase.InService; racer.PitTimer = 0f;
                }
                return;
            }
            if (racer.PitPhase == PitPhase.InService)
            {
                // The pit stop never ends itself: the player leaves by holding the
                // Robot in Leave Pit — allowed at any time, even mid-service.
                if (command.RequestExit)
                {
                    racer.SelectedService = PitService.None;
                    racer.ServiceProgress = 0f;
                    racer.PitPhase = PitPhase.Exiting; racer.PitTimer = 0f;
                    return;
                }
                if (racer.SelectedService == PitService.None) { racer.ServiceProgress = 0f; return; }
                float meterBefore = racer.SelectedService == PitService.Tires
                    ? racer.TireWear : racer.FuelUsed;
                if (command.ServiceDrain > 0f)
                {
                    if (racer.SelectedService == PitService.Tires)
                        racer.TireWear = Math.Max(0f, racer.TireWear - command.ServiceDrain);
                    else racer.FuelUsed = Math.Max(0f, racer.FuelUsed - command.ServiceDrain);
                }
                float meter = racer.SelectedService == PitService.Tires ? racer.TireWear : racer.FuelUsed;
                racer.ServiceProgress = 1f - meter;
                // Count the service only on the emptying stroke so stirring an
                // already-empty meter cannot count it again; both dials may be
                // serviced in one parked stop.
                if (meterBefore > 0f && meter <= 0f) racer.CompletedServices++;
                return;
            }

            racer.PitTimer += delta;
            if (racer.PitTimer < rules.Pit.ExitSeconds) return;
            racer.PitPhase = PitPhase.OnTrack; racer.PitTimer = racer.ServiceProgress = 0f;
            racer.SelectedService = PitService.None;
            float finishDistance = track.Length * rules.Laps;
            if (racer.Distance >= finishDistance && racer.CompletedServices >= rules.RequiredServiceCount)
            {
                FinishRacer(racer, racer.Distance, elapsed + delta);
                return;
            }
            // The pit lane rejoins the track where it physically ends, not back at
            // the start/finish line the car entered from.
            racer.Distance += rules.Pit.ExitRejoinDistance;
            racer.PriorSection = track.Sample(racer.Distance).SectionIndex;
        }

        private static void FinishRacer(RacerState racer, float distance, float finishTime)
        {
            racer.Distance = distance; racer.Finished = true; racer.FinishTime = finishTime; racer.Speed = 0f;
        }

        private void BurnFuel(RacerState racer, ThrottleStep step, float delta)
        {
            if (!rules.Conditions.Enabled) return;
            float burn = step == ThrottleStep.Boost ? rules.Conditions.FuelBurnPerSecondAtBoost
                : step == ThrottleStep.Drive ? rules.Conditions.FuelBurnPerSecondAtDrive : 0f;
            racer.FuelUsed = Clamp01(racer.FuelUsed + burn * delta);
        }

        private void AddCornerWear(RacerState racer, float baseSafeSpeed, float entrySpeed)
        {
            if (!rules.Conditions.Enabled) return;
            float unsafeRatio = baseSafeSpeed <= 0f ? 0f : Math.Max(0f, entrySpeed / baseSafeSpeed - 1f);
            racer.TireWear = Clamp01(racer.TireWear + rules.Conditions.TireWearPerCorner +
                unsafeRatio * rules.Conditions.TireWearPerUnsafeSpeed);
        }

        private bool FuelPenaltyActive(RacerState racer) =>
            rules.Conditions.Enabled && racer.FuelUsed >= 1f;

        private bool TirePenaltyActive(RacerState racer) =>
            rules.Conditions.Enabled && racer.TireWear >= rules.Conditions.TirePenaltyThreshold;

        private void HandleRematch(IReadOnlyList<RacerCommand> commands, float delta)
        {
            bool allConfirming = racers.All(x => commands.Any(c => c.PlayerId == x.Id &&
                c.DrivingPiecePresent && c.RematchConfirming));
            bool allReleased = racers.All(x => commands.Any(c => c.PlayerId == x.Id &&
                c.DrivingPiecePresent && !c.RematchConfirming));
            if (!awaitingRematchRelease)
            {
                rematchHeld = allConfirming ? rematchHeld + delta : 0f;
                if (rematchHeld >= rules.RematchHoldSeconds) awaitingRematchRelease = true;
            }
            else if (allReleased) ResetForRematch();
        }

        private void ResetForRematch()
        {
            phase = RacePhase.Grid; countdown = elapsed = rematchHeld = 0f; awaitingRematchRelease = false;
            foreach (var racer in racers)
            {
                racer.Speed = racer.Distance = racer.Recovery = 0f; racer.FinishTime = -1f;
                racer.Finished = racer.IncidentThisStep = false; racer.Incidents = 0;
                racer.PriorSection = track.Sample(0f).SectionIndex;
                racer.FuelUsed = racer.TireWear = racer.ServiceProgress = racer.PitTimer = 0f;
                racer.SelectedService = PitService.None; racer.PitPhase = PitPhase.OnTrack;
                racer.CompletedServices = 0;
            }
        }

        private RaceSnapshot BuildSnapshot()
        {
            var ordered = racers.OrderBy(x => x.Finished ? 0 : 1)
                .ThenBy(x => x.Finished ? x.FinishTime : -x.Distance).ThenBy(x => x.Id).ToArray();
            bool close = CircularDistance(racers[0].Distance, racers[1].Distance, track.Length) <= rules.PassingDistance;
            var result = racers.Select(racer =>
            {
                int place = Array.IndexOf(ordered, racer) + 1;
                float offset = close ? (racer.Id == PlayerId.Player1 ? -rules.PassingOffset : rules.PassingOffset) : 0f;
                var condition = new RacerConditionSnapshot(racer.FuelUsed, racer.TireWear,
                    FuelPenaltyActive(racer), TirePenaltyActive(racer));
                float phaseProgress = racer.PitPhase == PitPhase.Entering
                    ? Clamp01(racer.PitTimer / rules.Pit.EntrySeconds)
                    : racer.PitPhase == PitPhase.Exiting
                        ? Clamp01(racer.PitTimer / rules.Pit.ExitSeconds) : 0f;
                var pit = new RacerPitSnapshot(racer.SelectedService, racer.PitPhase, racer.ServiceProgress,
                    racer.CompletedServices, racer.CompletedServices >= rules.RequiredServiceCount, phaseProgress);
                return new RacerSnapshot(racer.Id, racer.Speed, racer.Distance,
                    Math.Min(rules.Laps, (int)(racer.Distance / track.Length)), place, racer.Finished, racer.FinishTime,
                    track.Sample(racer.Distance), offset, racer.IncidentThisStep, racer.Recovery, racer.Incidents,
                    condition, pit);
            }).ToArray();
            float progress = rules.RematchHoldSeconds <= 0f ? 1f : Math.Min(1f, rematchHeld / rules.RematchHoldSeconds);
            return new RaceSnapshot(phase, countdown, elapsed, result, progress, awaitingRematchRelease);
        }

        private static float MoveTowards(float current, float target, float maximumDelta)
        { return Math.Abs(target - current) <= maximumDelta ? target : current + Math.Sign(target - current) * maximumDelta; }

        private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));

        private static float CircularDistance(float a, float b, float length)
        {
            float delta = Math.Abs((a % length) - (b % length));
            return Math.Min(delta, length - delta);
        }
    }
}
