using System;
using System.Collections.Generic;
using System.Linq;
using BoardRacing.Domain;
using NUnit.Framework;

namespace BoardRacing.Tests
{
    public sealed class StrategyBalanceTests
    {
        private const float Step = 1f / 60f;
        private const float ServiceHoldSeconds = 1.5f;

        [Test]
        public void FirstPassStrategyMatrixProducesProfileDependentWinnersAndServiceValue()
        {
            var managed = RunDuel(BalanceRules(1),
                Plan("early tires", ThrottleStep.Half, PitService.Tires, 1),
                Plan("later cooling", ThrottleStep.Half, PitService.Cooling, 2));
            var sustained = RunDuel(BalanceRules(1),
                Plan("early tires", ThrottleStep.Full, PitService.Tires, 1),
                Plan("later cooling", ThrottleStep.Full, PitService.Cooling, 2));
            var coolingValue = RunDuel(BalanceRules(0),
                Plan("timed cooling", ThrottleStep.Full, PitService.Cooling, 2),
                Plan("no stop", ThrottleStep.Full, PitService.None, 0));
            var coolingCost = RunDuel(BalanceRules(0),
                Plan("unneeded cooling", ThrottleStep.Half, PitService.Cooling, 1),
                Plan("no stop", ThrottleStep.Half, PitService.None, 0));
            var tireTiming = RunDuel(BalanceRules(1),
                Plan("early tires", ThrottleStep.Half, PitService.Tires, 1),
                Plan("late tires", ThrottleStep.Half, PitService.Tires, 3));

            Write("managed", managed);
            Write("sustained", sustained);
            Write("cooling value", coolingValue);
            Write("cooling cost", coolingCost);
            Write("tire timing", tireTiming);

            AssertValidCompletedDuel(managed, 1);
            AssertValidCompletedDuel(sustained, 1);
            AssertValidCompletedDuel(coolingValue, 0);
            AssertValidCompletedDuel(coolingCost, 0);
            AssertValidCompletedDuel(tireTiming, 1);

            Assert.That(managed.PlayerOne.FinishTime, Is.LessThan(managed.PlayerTwo.FinishTime - .5f));
            Assert.That(managed.PlayerOne.FinalPlace, Is.EqualTo(1));
            Assert.That(managed.PlayerOne.PeakTireWear, Is.LessThan(managed.PlayerTwo.PeakTireWear));
            Assert.That(managed.PlayerOne.FirstPitEntryTime, Is.LessThan(managed.PlayerTwo.FirstPitEntryTime));

            Assert.That(sustained.PlayerTwo.FinishTime, Is.LessThan(sustained.PlayerOne.FinishTime - 5f));
            Assert.That(sustained.PlayerTwo.FinalPlace, Is.EqualTo(1));
            Assert.That(sustained.PlayerOne.PeakHeat, Is.EqualTo(1f));
            Assert.That(sustained.PlayerTwo.PeakHeat, Is.EqualTo(1f));

            Assert.That(coolingValue.PlayerOne.FinishTime,
                Is.LessThan(coolingValue.PlayerTwo.FinishTime - 3f));
            Assert.That(coolingValue.PlayerOne.CompletedServices, Is.EqualTo(1));
            Assert.That(coolingValue.PlayerTwo.CompletedServices, Is.Zero);
            Assert.That(coolingValue.PlayerTwo.FirstPitEntryTime, Is.EqualTo(-1f));

            Assert.That(coolingCost.PlayerOne.FinishTime,
                Is.GreaterThan(coolingCost.PlayerTwo.FinishTime + 2f));
            Assert.That(tireTiming.PlayerOne.FinishTime, Is.LessThan(tireTiming.PlayerTwo.FinishTime - .1f));
            Assert.That(tireTiming.PlayerOne.FirstPitEntryTime,
                Is.LessThan(tireTiming.PlayerTwo.FirstPitEntryTime - 30f));
        }

        [Test]
        public void MandatoryStopCannotBeIgnoredByHoldingMaximumThrottle()
        {
            var duel = RunDuel(BalanceRules(1),
                Plan("served cooling", ThrottleStep.Full, PitService.Cooling, 2),
                Plan("unserved full throttle", ThrottleStep.Full, PitService.None, 0));

            Assert.That(duel.PlayerOne.Finished, Is.True);
            Assert.That(duel.PlayerOne.CompletedServices, Is.EqualTo(1));
            Assert.That(duel.PlayerOne.FinalPlace, Is.EqualTo(1));
            Assert.That(duel.PlayerTwo.Finished, Is.False);
            Assert.That(duel.PlayerTwo.FinishTime, Is.EqualTo(-1f));
            Assert.That(duel.PlayerTwo.CompletedServices, Is.Zero);
            Assert.That(duel.PlayerTwo.FinalDistance,
                Is.GreaterThanOrEqualTo(TrackDefinition.Placeholder().Length * 6f));
            Assert.That(duel.All.All(x => x.InvalidTransitions == 0), Is.True);
        }

        [Test]
        public void FullStrategyTraceIsExactlyRepeatable()
        {
            var first = RunDuel(BalanceRules(1),
                Plan("early tires", ThrottleStep.Half, PitService.Tires, 1),
                Plan("later cooling", ThrottleStep.Half, PitService.Cooling, 2));
            var second = RunDuel(BalanceRules(1),
                Plan("early tires", ThrottleStep.Half, PitService.Tires, 1),
                Plan("later cooling", ThrottleStep.Half, PitService.Cooling, 2));

            AssertEquivalent(first.PlayerOne, second.PlayerOne);
            AssertEquivalent(first.PlayerTwo, second.PlayerTwo);
        }

        private static StrategyPlan Plan(string name, ThrottleStep throttle, PitService service,
            int requestAfterCompletedLaps) =>
            new StrategyPlan(name, throttle, service, requestAfterCompletedLaps);

        private static RaceRules BalanceRules(int requiredServices)
        {
            var defaults = RaceRules.TrancheThreeDefaults;
            return new RaceRules(defaults.Laps, 0f, defaults.MaxSpeed, defaults.Acceleration, defaults.Drag,
                defaults.Braking, defaults.CornerSpeedScrub, defaults.CornerRecoverySeconds,
                defaults.RecoveryAccelerationScale, defaults.PassingDistance, defaults.PassingOffset,
                defaults.RematchHoldSeconds, requiredServices, defaults.Conditions, defaults.Pit);
        }

        private static TraceDuel RunDuel(RaceRules rules, StrategyPlan playerOne, StrategyPlan playerTwo,
            float maximumSeconds = 240f)
        {
            var simulation = new RaceSimulation(TrackDefinition.Placeholder(), rules);
            simulation.Step(Step, ReleasedCommands());
            simulation.Step(Step, ReleasedCommands());
            Assert.That(simulation.Snapshot.Phase, Is.EqualTo(RacePhase.Racing));
            var states = new Dictionary<PlayerId, TraceState>
            {
                [PlayerId.Player1] = new TraceState(playerOne),
                [PlayerId.Player2] = new TraceState(playerTwo)
            };

            int maximumSteps = (int)Math.Ceiling(maximumSeconds / Step);
            for (int i = 0; i < maximumSteps && !simulation.Snapshot.Racers.All(x => x.Finished); i++)
            {
                var before = simulation.Snapshot.Racers.ToDictionary(x => x.PlayerId);
                var commands = new[]
                {
                    Command(states[PlayerId.Player1], before[PlayerId.Player1]),
                    Command(states[PlayerId.Player2], before[PlayerId.Player2])
                };
                simulation.Step(Step, commands);
                foreach (var racer in simulation.Snapshot.Racers)
                    states[racer.PlayerId].Observe(racer, simulation.Snapshot.ElapsedSeconds);

                bool oneFinished = simulation.Snapshot.Racers.Any(x => x.Finished);
                bool unservedControlRanExtraLap = simulation.Snapshot.Racers.Any(x => !x.Finished &&
                    x.TotalDistance >= simulation.Track.Length * (rules.Laps + 1));
                if (oneFinished && unservedControlRanExtraLap) break;
            }

            return new TraceDuel(states[PlayerId.Player1].Result(PlayerId.Player1),
                states[PlayerId.Player2].Result(PlayerId.Player2));
        }

        private static RacerCommand Command(TraceState state, RacerSnapshot racer)
        {
            bool request = false;
            PitService selection = PitService.None;
            if (!state.Requested && state.Plan.Service != PitService.None &&
                racer.Pit.Phase == PitPhase.OnTrack &&
                racer.CompletedLaps >= state.Plan.RequestAfterCompletedLaps)
            {
                state.Requested = true;
                request = true;
                selection = state.Plan.Service;
            }

            float progress = 0f;
            bool complete = false;
            if (racer.Pit.Phase == PitPhase.InService)
            {
                state.HoldSeconds += Step;
                progress = Math.Min(1f, state.HoldSeconds / ServiceHoldSeconds);
                complete = state.HoldSeconds >= ServiceHoldSeconds;
            }

            return new RacerCommand(racer.PlayerId, state.Plan.Throttle, true, true,
                selection, request, progress, complete);
        }

        private static RacerCommand[] ReleasedCommands() => new[]
        {
            new RacerCommand(PlayerId.Player1, ThrottleStep.Off, true, false),
            new RacerCommand(PlayerId.Player2, ThrottleStep.Off, true, false)
        };

        private static void AssertValidCompletedDuel(TraceDuel duel, int requiredServices)
        {
            foreach (var result in duel.All)
            {
                Assert.That(result.Finished, Is.True, result.Name);
                Assert.That(result.CompletedServices, Is.GreaterThanOrEqualTo(requiredServices), result.Name);
                Assert.That(result.InvalidTransitions, Is.Zero, result.Name);
                Assert.That(result.PeakHeat, Is.InRange(0f, 1f), result.Name);
                Assert.That(result.PeakTireWear, Is.InRange(0f, 1f), result.Name);
                Assert.That(result.FinalHeat, Is.InRange(0f, 1f), result.Name);
                Assert.That(result.FinalTireWear, Is.InRange(0f, 1f), result.Name);
            }
        }

        private static void AssertEquivalent(StrategyTraceResult first, StrategyTraceResult second)
        {
            Assert.That(second.Finished, Is.EqualTo(first.Finished));
            Assert.That(second.FinishTime, Is.EqualTo(first.FinishTime).Within(.0001f));
            Assert.That(second.FirstPitEntryTime, Is.EqualTo(first.FirstPitEntryTime).Within(.0001f));
            Assert.That(second.CompletedServices, Is.EqualTo(first.CompletedServices));
            Assert.That(second.Incidents, Is.EqualTo(first.Incidents));
            Assert.That(second.InvalidTransitions, Is.EqualTo(first.InvalidTransitions));
            Assert.That(second.PeakHeat, Is.EqualTo(first.PeakHeat).Within(.0001f));
            Assert.That(second.PeakTireWear, Is.EqualTo(first.PeakTireWear).Within(.0001f));
            Assert.That(second.FinalHeat, Is.EqualTo(first.FinalHeat).Within(.0001f));
            Assert.That(second.FinalTireWear, Is.EqualTo(first.FinalTireWear).Within(.0001f));
            Assert.That(second.FinalPlace, Is.EqualTo(first.FinalPlace));
            Assert.That(second.FinalDistance, Is.EqualTo(first.FinalDistance).Within(.0001f));
        }

        private static void Write(string label, TraceDuel duel)
        {
            foreach (var result in duel.All)
                TestContext.WriteLine(label + " / " + result.Name + ": finish=" +
                    result.FinishTime.ToString("F2") + "s, service=" + result.FirstPitEntryTime.ToString("F2") +
                    "s, stops=" + result.CompletedServices + ", incidents=" + result.Incidents +
                    ", peak heat=" + result.PeakHeat.ToString("F2") + ", peak wear=" +
                    result.PeakTireWear.ToString("F2") + ", final heat=" + result.FinalHeat.ToString("F2") +
                    ", final wear=" + result.FinalTireWear.ToString("F2"));
        }

        private sealed class StrategyPlan
        {
            public StrategyPlan(string name, ThrottleStep throttle, PitService service,
                int requestAfterCompletedLaps)
            {
                Name = name; Throttle = throttle; Service = service;
                RequestAfterCompletedLaps = requestAfterCompletedLaps;
            }
            public string Name { get; }
            public ThrottleStep Throttle { get; }
            public PitService Service { get; }
            public int RequestAfterCompletedLaps { get; }
        }

        private sealed class TraceState
        {
            private PitPhase priorPhase = PitPhase.OnTrack;
            private bool sawSnapshot;
            private bool finished;
            private float finishTime = -1f, firstPitEntryTime = -1f, peakHeat, peakWear, finalHeat, finalWear;
            private float finalDistance;
            private int completedServices, incidents, invalidTransitions, finalPlace;

            public TraceState(StrategyPlan plan) { Plan = plan; }
            public StrategyPlan Plan { get; }
            public bool Requested { get; set; }
            public float HoldSeconds { get; set; }

            public void Observe(RacerSnapshot racer, float elapsedSeconds)
            {
                if (sawSnapshot && racer.Pit.Phase != priorPhase && !Allowed(priorPhase, racer.Pit.Phase))
                    invalidTransitions++;
                if (firstPitEntryTime < 0f && racer.Pit.Phase == PitPhase.Entering)
                    firstPitEntryTime = elapsedSeconds;
                sawSnapshot = true; priorPhase = racer.Pit.Phase;
                peakHeat = Math.Max(peakHeat, racer.Condition.Heat);
                peakWear = Math.Max(peakWear, racer.Condition.TireWear);
                finalHeat = racer.Condition.Heat; finalWear = racer.Condition.TireWear;
                completedServices = racer.Pit.CompletedServices; incidents = racer.IncidentCount;
                finished = racer.Finished; finishTime = racer.FinishTime;
                finalPlace = racer.Place; finalDistance = racer.TotalDistance;
            }

            public StrategyTraceResult Result(PlayerId id) => new StrategyTraceResult(id, Plan.Name, finished,
                finishTime, firstPitEntryTime, completedServices, incidents, invalidTransitions,
                peakHeat, peakWear, finalHeat, finalWear, finalPlace, finalDistance);

            private static bool Allowed(PitPhase from, PitPhase to)
            {
                return (from == PitPhase.OnTrack && to == PitPhase.Requested) ||
                    (from == PitPhase.Requested && to == PitPhase.Entering) ||
                    (from == PitPhase.Entering && to == PitPhase.InService) ||
                    (from == PitPhase.InService && to == PitPhase.Exiting) ||
                    (from == PitPhase.Exiting && to == PitPhase.OnTrack);
            }
        }

        private readonly struct TraceDuel
        {
            public TraceDuel(StrategyTraceResult playerOne, StrategyTraceResult playerTwo)
            { PlayerOne = playerOne; PlayerTwo = playerTwo; }
            public StrategyTraceResult PlayerOne { get; }
            public StrategyTraceResult PlayerTwo { get; }
            public IEnumerable<StrategyTraceResult> All => new[] { PlayerOne, PlayerTwo };
        }

        private readonly struct StrategyTraceResult
        {
            public StrategyTraceResult(PlayerId playerId, string name, bool finished, float finishTime,
                float firstPitEntryTime, int completedServices, int incidents, int invalidTransitions,
                float peakHeat, float peakTireWear, float finalHeat, float finalTireWear,
                int finalPlace, float finalDistance)
            {
                PlayerId = playerId; Name = name; Finished = finished; FinishTime = finishTime;
                FirstPitEntryTime = firstPitEntryTime; CompletedServices = completedServices;
                Incidents = incidents; InvalidTransitions = invalidTransitions; PeakHeat = peakHeat;
                PeakTireWear = peakTireWear; FinalHeat = finalHeat; FinalTireWear = finalTireWear;
                FinalPlace = finalPlace; FinalDistance = finalDistance;
            }
            public PlayerId PlayerId { get; }
            public string Name { get; }
            public bool Finished { get; }
            public float FinishTime { get; }
            public float FirstPitEntryTime { get; }
            public int CompletedServices { get; }
            public int Incidents { get; }
            public int InvalidTransitions { get; }
            public float PeakHeat { get; }
            public float PeakTireWear { get; }
            public float FinalHeat { get; }
            public float FinalTireWear { get; }
            public int FinalPlace { get; }
            public float FinalDistance { get; }
        }
    }
}
