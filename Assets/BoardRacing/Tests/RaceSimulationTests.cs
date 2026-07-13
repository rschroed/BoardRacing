using System;
using System.Linq;
using BoardRacing.Domain;
using NUnit.Framework;

namespace BoardRacing.Tests
{
    public sealed class RaceSimulationTests
    {
        [Test]
        public void PlaceholderTrackSamplesAndWrapsClosedPath()
        {
            var track = TrackDefinition.Placeholder();
            Assert.That(track.Segments.Count, Is.EqualTo(8));
            Assert.That(track.Length, Is.GreaterThan(3000f));
            var start = track.Sample(0f);
            var wrapped = track.Sample(track.Length);
            Assert.That(wrapped.Position.X, Is.EqualTo(start.Position.X).Within(.001f));
            Assert.That(wrapped.Position.Y, Is.EqualTo(start.Position.Y).Within(.001f));
            Assert.That(track.Segments.Count(x => x.Kind == TrackSectionKind.Corner), Is.EqualTo(4));
        }

        [Test]
        public void ThrottleTraceUsesLatestPointAtOrBeforeTime()
        {
            var trace = new ScriptedThrottleTrace(new[]
            {
                new ThrottleTracePoint(2f, ThrottleStep.Full),
                new ThrottleTracePoint(1f, ThrottleStep.Half),
                new ThrottleTracePoint(3f, ThrottleStep.Off)
            });
            Assert.That(trace.At(.5f), Is.EqualTo(ThrottleStep.Off));
            Assert.That(trace.At(1.5f), Is.EqualTo(ThrottleStep.Half));
            Assert.That(trace.At(2f), Is.EqualTo(ThrottleStep.Full));
        }

        [Test]
        public void GridCountdownAndEarlyThrottleDoNotMoveCars()
        {
            var simulation = new RaceSimulation(TrackDefinition.Placeholder(), RaceRules.Defaults);
            var touched = Commands(ThrottleStep.Full, true);
            simulation.Step(1f / 60f, touched);
            Assert.That(simulation.Snapshot.Phase, Is.EqualTo(RacePhase.Grid));
            Assert.That(simulation.Snapshot.Racers.All(x => x.TotalDistance == 0f), Is.True);

            simulation.Step(1f / 60f, Commands(ThrottleStep.Off, false));
            Assert.That(simulation.Snapshot.Phase, Is.EqualTo(RacePhase.Countdown));
            simulation.Step(1f, touched);
            Assert.That(simulation.Snapshot.Racers.All(x => x.TotalDistance == 0f), Is.True);
        }

        [Test]
        public void ReleasedThrottleBrakesMoreStronglyThanLowerThrottleDrag()
        {
            var simulation = StartedSimulation();
            for (int i = 0; i < 120; i++) simulation.Step(1f / 60f, Commands(ThrottleStep.Full, true));
            float fast = Player(simulation, PlayerId.Player1).Speed;
            simulation.Step(.25f, Commands(ThrottleStep.Half, true));
            float dragged = Player(simulation, PlayerId.Player1).Speed;
            simulation.Step(.25f, Commands(ThrottleStep.Off, false));
            float braked = Player(simulation, PlayerId.Player1).Speed;
            Assert.That(fast - dragged, Is.EqualTo(RaceRules.Defaults.Drag * .25f).Within(.01f));
            Assert.That(dragged - braked, Is.EqualTo(RaceRules.Defaults.Braking * .25f).Within(.01f));
        }

        [Test]
        public void UnsafeCornerEntryEmitsOneIncidentAndRecovers()
        {
            var simulation = StartedSimulation();
            int guard = 0;
            while (Player(simulation, PlayerId.Player1).IncidentCount == 0 && guard++ < 5000)
                simulation.Step(1f / 60f, Commands(ThrottleStep.Full, true));
            var incident = Player(simulation, PlayerId.Player1);
            Assert.That(incident.IncidentCount, Is.EqualTo(1));
            Assert.That(incident.IncidentThisStep, Is.True);
            Assert.That(incident.RecoveryRemaining, Is.GreaterThan(0f));
            for (int i = 0; i < 20; i++) simulation.Step(1f / 60f, Commands(ThrottleStep.Full, true));
            Assert.That(Player(simulation, PlayerId.Player1).IncidentCount, Is.EqualTo(1));
        }

        [Test]
        public void QuarterThrottleCanEnterPlaceholderCornersCleanly()
        {
            var simulation = StartedSimulation();
            for (int i = 0; i < 5000 && Player(simulation, PlayerId.Player1).CompletedLaps == 0; i++)
                simulation.Step(1f / 60f, Commands(ThrottleStep.Quarter, true));
            Assert.That(Player(simulation, PlayerId.Player1).CompletedLaps, Is.GreaterThanOrEqualTo(1));
            Assert.That(Player(simulation, PlayerId.Player1).IncidentCount, Is.Zero);
        }

        [Test]
        public void CloseRacersReceiveStableOppositePassingOffsets()
        {
            var simulation = StartedSimulation();
            var p1 = Player(simulation, PlayerId.Player1);
            var p2 = Player(simulation, PlayerId.Player2);
            Assert.That(p1.LateralOffset, Is.LessThan(0f));
            Assert.That(p2.LateralOffset, Is.GreaterThan(0f));
            Assert.That(p1.Place, Is.EqualTo(1));
            Assert.That(p2.Place, Is.EqualTo(2));
        }

        [Test]
        public void ScriptedFullRacesAreDeterministicAndFinishExactlyFiveLaps()
        {
            var first = RunFullRace();
            var second = RunFullRace();
            Assert.That(first.Phase, Is.EqualTo(RacePhase.Finished));
            Assert.That(first.Racers.All(x => x.CompletedLaps == 5 && x.Finished), Is.True);
            foreach (PlayerId id in Enum.GetValues(typeof(PlayerId)))
            {
                Assert.That(first.Racers.Single(x => x.PlayerId == id).FinishTime,
                    Is.EqualTo(second.Racers.Single(x => x.PlayerId == id).FinishTime).Within(.0001f));
            }
        }

        [Test]
        public void FinishedRaceRequiresDualTouchThenReleaseForRematch()
        {
            var simulation = FastFinishedSimulation();
            Assert.That(simulation.Snapshot.Phase, Is.EqualTo(RacePhase.Finished));
            simulation.Step(.5f, Commands(ThrottleStep.Full, true));
            Assert.That(simulation.Snapshot.AwaitingRematchRelease, Is.False);
            simulation.Step(.6f, Commands(ThrottleStep.Full, true));
            Assert.That(simulation.Snapshot.AwaitingRematchRelease, Is.True);
            simulation.Step(.1f, Commands(ThrottleStep.Off, false));
            Assert.That(simulation.Snapshot.Phase, Is.EqualTo(RacePhase.Grid));
            Assert.That(simulation.Snapshot.Racers.All(x => x.TotalDistance == 0f), Is.True);
        }

        [Test]
        public void TrancheThreeInitialStateExposesConditionsAndMandatoryServiceEligibility()
        {
            var simulation = new RaceSimulation(TrackDefinition.Placeholder(), RaceRules.TrancheThreeDefaults);
            foreach (var racer in simulation.Snapshot.Racers)
            {
                Assert.That(racer.Condition.Heat, Is.Zero);
                Assert.That(racer.Condition.TireWear, Is.Zero);
                Assert.That(racer.Condition.HeatPenaltyActive, Is.False);
                Assert.That(racer.Condition.TirePenaltyActive, Is.False);
                Assert.That(racer.Pit.SelectedService, Is.EqualTo(PitService.None));
                Assert.That(racer.Pit.Phase, Is.EqualTo(PitPhase.OnTrack));
                Assert.That(racer.Pit.ServiceProgress, Is.Zero);
                Assert.That(racer.Pit.CompletedServices, Is.Zero);
                Assert.That(racer.Pit.FinishEligible, Is.False);
            }
        }

        [Test]
        public void StrategyIntentIsPlayerScopedAndRequiresASelectedService()
        {
            var simulation = StartedSimulation(RaceRules.TrancheThreeDefaults);
            simulation.Step(1f / 60f, new[]
            {
                StrategyCommand(PlayerId.Player1, PitService.Tires, true),
                StrategyCommand(PlayerId.Player2, PitService.Cooling, false)
            });

            var p1 = Player(simulation, PlayerId.Player1);
            var p2 = Player(simulation, PlayerId.Player2);
            Assert.That(p1.Pit.SelectedService, Is.EqualTo(PitService.Tires));
            Assert.That(p1.Pit.Phase, Is.EqualTo(PitPhase.Requested));
            Assert.That(p2.Pit.SelectedService, Is.EqualTo(PitService.Cooling));
            Assert.That(p2.Pit.Phase, Is.EqualTo(PitPhase.OnTrack));

            simulation.Step(1f / 60f, new[] { StrategyCommand(PlayerId.Player1, PitService.None, false) });
            Assert.That(Player(simulation, PlayerId.Player2).Pit.SelectedService, Is.EqualTo(PitService.Cooling));
            Assert.That(Player(simulation, PlayerId.Player2).Pit.Phase, Is.EqualTo(PitPhase.OnTrack));

            simulation = StartedSimulation(RaceRules.TrancheThreeDefaults);
            simulation.Step(1f / 60f, new[] { StrategyCommand(PlayerId.Player1, PitService.None, true) });
            Assert.That(Player(simulation, PlayerId.Player1).Pit.Phase, Is.EqualTo(PitPhase.OnTrack));
            Assert.That(Player(simulation, PlayerId.Player2).Pit.SelectedService, Is.EqualTo(PitService.None));
        }

        [Test]
        public void IdenticalStrategyTracesProduceIdenticalSnapshots()
        {
            var first = StartedSimulation(RaceRules.TrancheThreeDefaults);
            var second = StartedSimulation(RaceRules.TrancheThreeDefaults);
            for (int i = 0; i < 120; i++)
            {
                var commands = new[]
                {
                    StrategyCommand(PlayerId.Player1, i < 60 ? PitService.Tires : PitService.None, i == 60),
                    StrategyCommand(PlayerId.Player2, i < 90 ? PitService.Cooling : PitService.None, i == 90)
                };
                first.Step(1f / 60f, commands);
                second.Step(1f / 60f, commands);
            }

            foreach (PlayerId id in Enum.GetValues(typeof(PlayerId)))
            {
                var a = Player(first, id); var b = Player(second, id);
                Assert.That(a.TotalDistance, Is.EqualTo(b.TotalDistance));
                Assert.That(a.Condition.Heat, Is.EqualTo(b.Condition.Heat));
                Assert.That(a.Condition.TireWear, Is.EqualTo(b.Condition.TireWear));
                Assert.That(a.Pit.SelectedService, Is.EqualTo(b.Pit.SelectedService));
                Assert.That(a.Pit.Phase, Is.EqualTo(b.Pit.Phase));
                Assert.That(a.Pit.CompletedServices, Is.EqualTo(b.Pit.CompletedServices));
                Assert.That(a.Pit.FinishEligible, Is.EqualTo(b.Pit.FinishEligible));
            }
        }

        [Test]
        public void StrategyContractsRejectInvalidValues()
        {
            Assert.Throws<ArgumentException>(() => new RacerCommand(PlayerId.Player1, ThrottleStep.Off, true, false,
                PitService.Tires, false, 1.1f, false));
            Assert.Throws<ArgumentException>(() => new RacerConditionSnapshot(-.01f, 0f, false, false));
            Assert.Throws<ArgumentException>(() => new RacerPitSnapshot(PitService.None, PitPhase.OnTrack, 0f, -1, false));
            Assert.Throws<ArgumentException>(() => new RaceRules(5, 3f, 360f, 220f, 120f, 300f, .55f, 1f,
                .35f, 180f, 38f, 1f, -1));
            Assert.Throws<ArgumentException>(() => new RaceRules(5, float.NaN, 360f, 220f, 120f, 300f, .55f, 1f,
                .35f, 180f, 38f, 1f));
        }

        private static RaceSimulation StartedSimulation()
        {
            return StartedSimulation(RaceRules.Defaults);
        }

        private static RaceSimulation StartedSimulation(RaceRules rules)
        {
            var result = new RaceSimulation(TrackDefinition.Placeholder(), rules);
            result.Step(1f / 60f, Commands(ThrottleStep.Off, false));
            result.Step(rules.CountdownSeconds, Commands(ThrottleStep.Off, false));
            Assert.That(result.Snapshot.Phase, Is.EqualTo(RacePhase.Racing));
            return result;
        }

        private static RaceSnapshot RunFullRace()
        {
            var simulation = StartedSimulation();
            int guard = 0;
            while (simulation.Snapshot.Phase != RacePhase.Finished && guard++ < 30000)
                simulation.Step(1f / 60f, Commands(ThrottleStep.ThreeQuarters, true));
            Assert.That(guard, Is.LessThan(30000));
            return simulation.Snapshot;
        }

        private static RaceSimulation FastFinishedSimulation()
        {
            var track = new TrackDefinition(new[]
            {
                new TrackSegment(new Vec2(0f, 0f), new Vec2(10f, 0f), TrackSectionKind.Straight, float.PositiveInfinity),
                new TrackSegment(new Vec2(10f, 0f), new Vec2(0f, 0f), TrackSectionKind.Straight, float.PositiveInfinity)
            });
            var rules = new RaceRules(1, 0f, 100f, 1000f, 100f, 100f, .5f, 1f, .5f, 5f, 1f, 1f);
            var simulation = new RaceSimulation(track, rules);
            simulation.Step(.01f, Commands(ThrottleStep.Off, false));
            simulation.Step(.01f, Commands(ThrottleStep.Off, false));
            for (int i = 0; i < 20 && simulation.Snapshot.Phase != RacePhase.Finished; i++)
                simulation.Step(.1f, Commands(ThrottleStep.Full, true));
            return simulation;
        }

        private static RacerSnapshot Player(RaceSimulation simulation, PlayerId id) =>
            simulation.Snapshot.Racers.Single(x => x.PlayerId == id);

        private static RacerCommand[] Commands(ThrottleStep throttle, bool touched) => new[]
        {
            new RacerCommand(PlayerId.Player1, throttle, true, touched),
            new RacerCommand(PlayerId.Player2, throttle, true, touched)
        };

        private static RacerCommand StrategyCommand(PlayerId playerId, PitService service, bool requestPit) =>
            new RacerCommand(playerId, ThrottleStep.Half, true, true, service, requestPit, 0f, false);
    }
}
