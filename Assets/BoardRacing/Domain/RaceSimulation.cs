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
            public float Heat, TireWear, ServiceProgress;
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
                ? command : new RacerCommand(id, ThrottleStep.Off, false, false);

            foreach (var racer in racers) racer.IncidentThisStep = false;
            if (phase == RacePhase.Grid)
            {
                if (racers.All(x => { var c = Command(x.Id); return c.CarPresent && !c.CarTouched; }))
                { phase = RacePhase.Countdown; countdown = rules.CountdownSeconds; }
            }
            else if (phase == RacePhase.Countdown)
            {
                if (racers.Any(x => !Command(x.Id).CarPresent)) { phase = RacePhase.Grid; countdown = 0f; }
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

        private static void CaptureStrategyIntent(RacerState racer, RacerCommand command)
        {
            if (command.SelectedService != PitService.None)
                racer.SelectedService = command.SelectedService;
            if (command.RequestPit && racer.SelectedService != PitService.None && racer.PitPhase == PitPhase.OnTrack)
                racer.PitPhase = PitPhase.Requested;
        }

        private void AdvanceRacer(RacerState racer, RacerCommand command, float delta)
        {
            if (racer.Finished) return;
            float target = rules.MaxSpeed * (int)command.Throttle / 100f;
            if (!command.CarPresent || !command.CarTouched) target = 0f;
            float rate;
            if (target > racer.Speed) rate = rules.Acceleration * (racer.Recovery > 0f ? rules.RecoveryAccelerationScale : 1f);
            else rate = target <= 0f ? rules.Braking : rules.Drag;
            racer.Speed = MoveTowards(racer.Speed, target, rate * delta);
            racer.Recovery = Math.Max(0f, racer.Recovery - delta);

            var before = track.Sample(racer.Distance);
            bool enteringCorner = before.Kind == TrackSectionKind.Corner && racer.PriorSection != before.SectionIndex;
            if (enteringCorner && racer.Speed > before.SafeSpeed)
            {
                racer.Speed *= rules.CornerSpeedScrub;
                racer.Recovery = rules.CornerRecoverySeconds;
                racer.IncidentThisStep = true;
                racer.Incidents++;
            }
            racer.PriorSection = before.SectionIndex;

            float prior = racer.Distance;
            racer.Distance += racer.Speed * delta;
            float finishDistance = track.Length * rules.Laps;
            if (racer.Distance >= finishDistance)
            {
                float moved = racer.Distance - prior;
                float fraction = moved <= 0f ? 1f : Math.Max(0f, Math.Min(1f, (finishDistance - prior) / moved));
                racer.Distance = finishDistance;
                racer.Finished = true;
                racer.FinishTime = elapsed + delta * fraction;
                racer.Speed = 0f;
            }
        }

        private void HandleRematch(IReadOnlyList<RacerCommand> commands, float delta)
        {
            bool allTouched = racers.All(x => commands.Any(c => c.PlayerId == x.Id && c.CarPresent && c.CarTouched));
            bool allReleased = racers.All(x => commands.Any(c => c.PlayerId == x.Id && c.CarPresent && !c.CarTouched));
            if (!awaitingRematchRelease)
            {
                rematchHeld = allTouched ? rematchHeld + delta : 0f;
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
                racer.Heat = racer.TireWear = racer.ServiceProgress = 0f;
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
                var condition = new RacerConditionSnapshot(racer.Heat, racer.TireWear, false, false);
                var pit = new RacerPitSnapshot(racer.SelectedService, racer.PitPhase, racer.ServiceProgress,
                    racer.CompletedServices, racer.CompletedServices >= rules.RequiredServiceCount);
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

        private static float CircularDistance(float a, float b, float length)
        {
            float delta = Math.Abs((a % length) - (b % length));
            return Math.Min(delta, length - delta);
        }
    }
}
