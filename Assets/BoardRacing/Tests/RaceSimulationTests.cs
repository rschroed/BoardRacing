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

        private static RaceSimulation StartedSimulation()
        {
            var result = new RaceSimulation(TrackDefinition.Placeholder(), RaceRules.Defaults);
            result.Step(1f / 60f, Commands(ThrottleStep.Off, false));
            result.Step(RaceRules.Defaults.CountdownSeconds, Commands(ThrottleStep.Off, false));
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
    }
}
