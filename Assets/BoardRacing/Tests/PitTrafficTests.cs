using System;
using System.Collections.Generic;
using System.Linq;
using BoardRacing.Domain;
using NUnit.Framework;

namespace BoardRacing.Tests
{
    public sealed class PitTrafficTests
    {
        [Test]
        public void SameStepEntriesQueueByPlayerIdAtWorldDistanceHeadway()
        {
            PlayerId[] roster =
                { PlayerId.Player4, PlayerId.Player3, PlayerId.Player2, PlayerId.Player1 };
            RaceSimulation simulation = Start(roster, FourCarPitRules());

            simulation.Step(.3f, Commands(roster, requestPit: true));

            AssertPit(simulation, PlayerId.Player1, PitTrafficState.Moving, 0f);
            AssertPit(simulation, PlayerId.Player2, PitTrafficState.Queued, 62f);
            AssertPit(simulation, PlayerId.Player3, PitTrafficState.Queued, 124f);
            AssertPit(simulation, PlayerId.Player4, PitTrafficState.Queued, 186f);

            simulation.Step(.5f, Commands(roster));
            RacerSnapshot[] ordered = simulation.Snapshot.Racers
                .OrderBy(x => x.PlayerId).ToArray();
            float[] positions = ordered.Select(PitPosition).ToArray();
            for (int i = 1; i < positions.Length; i++)
                Assert.That(positions[i - 1] - positions[i],
                    Is.GreaterThanOrEqualTo(62f - .001f));
        }

        [Test]
        public void OccupiedStallDoesNotBlockCarsPassingOnTheSharedLane()
        {
            PlayerId[] roster =
                { PlayerId.Player1, PlayerId.Player2, PlayerId.Player3, PlayerId.Player4 };
            RaceSimulation simulation = Start(roster, FourCarPitRules());
            simulation.Step(.3f, Commands(roster, requestPit: true));

            int guard = 0;
            while (Racer(simulation, PlayerId.Player1).Pit.Phase != PitPhase.InService &&
                guard++ < 1000)
                simulation.Step(.1f, Commands(roster));

            Assert.That(guard, Is.LessThan(1000));
            Assert.That(Racer(simulation, PlayerId.Player2).Pit.Phase,
                Is.EqualTo(PitPhase.Entering));
            float progress = Racer(simulation, PlayerId.Player2).Pit.PhaseProgress;
            for (int i = 0; i < 10; i++) simulation.Step(.1f, Commands(roster));
            Assert.That(Racer(simulation, PlayerId.Player1).Pit.Phase,
                Is.EqualTo(PitPhase.InService));
            Assert.That(Racer(simulation, PlayerId.Player2).Pit.PhaseProgress,
                Is.GreaterThan(progress));
        }

        [Test]
        public void SimultaneousReleasesUseStableYieldingAndExposeTheWait()
        {
            PlayerId[] roster = { PlayerId.Player2, PlayerId.Player1 };
            RaceSimulation simulation = Start(roster, TwoCarContentionRules());
            simulation.Step(.3f, Commands(roster, requestPit: true));
            int guard = 0;
            while (!simulation.Snapshot.Racers.All(x => x.Pit.Phase == PitPhase.InService) &&
                guard++ < 1000)
                simulation.Step(.1f, Commands(roster));
            Assert.That(guard, Is.LessThan(1000));

            simulation.Step(.1f, Commands(roster, requestExit: true));

            Assert.That(Racer(simulation, PlayerId.Player1).Pit.TrafficState,
                Is.EqualTo(PitTrafficState.Moving));
            Assert.That(Racer(simulation, PlayerId.Player2).Pit.TrafficState,
                Is.EqualTo(PitTrafficState.WaitingToRelease));
            float waitStarted = simulation.Snapshot.ElapsedSeconds;
            while (Racer(simulation, PlayerId.Player2).Pit.TrafficState ==
                PitTrafficState.WaitingToRelease && guard++ < 1000)
                simulation.Step(.1f, Commands(roster));
            Assert.That(guard, Is.LessThan(1000));
            Assert.That(simulation.Snapshot.ElapsedSeconds, Is.GreaterThan(waitStarted));
        }

        [Test]
        public void StallReleaseYieldsToACarAlreadyEnteringTheSharedLane()
        {
            PlayerId[] roster = { PlayerId.Player2, PlayerId.Player1 };
            RaceSimulation simulation = Start(roster, TwoCarContentionRules());

            simulation.Step(.3f, Commands(roster, requestPitFor: PlayerId.Player1));
            int guard = 0;
            while (Racer(simulation, PlayerId.Player1).Pit.Phase != PitPhase.InService &&
                guard++ < 1000)
                simulation.Step(.1f, Commands(roster));
            Assert.That(guard, Is.LessThan(1000));

            simulation.Step(.1f, Commands(roster, requestPitFor: PlayerId.Player2));
            while (Racer(simulation, PlayerId.Player2).Pit.Phase != PitPhase.Entering &&
                guard++ < 2000)
                simulation.Step(.1f, Commands(roster));
            Assert.That(guard, Is.LessThan(2000));

            simulation.Step(.1f, Commands(roster, requestExitFor: PlayerId.Player1));
            Assert.That(Racer(simulation, PlayerId.Player1).Pit.TrafficState,
                Is.EqualTo(PitTrafficState.WaitingToRelease));

            float waitStarted = simulation.Snapshot.ElapsedSeconds;
            while (Racer(simulation, PlayerId.Player1).Pit.TrafficState ==
                PitTrafficState.WaitingToRelease && guard++ < 3000)
                simulation.Step(.1f, Commands(roster));
            Assert.That(guard, Is.LessThan(3000));
            Assert.That(simulation.Snapshot.ElapsedSeconds, Is.GreaterThan(waitStarted));
            Assert.That(Racer(simulation, PlayerId.Player1).Pit.TrafficState,
                Is.EqualTo(PitTrafficState.Moving));
        }

        [Test]
        public void QueueTimingIsIndependentOfFixedStepSize()
        {
            Dictionary<PlayerId, float> coarse = ServiceArrivalTimes(.1f);
            Dictionary<PlayerId, float> fine = ServiceArrivalTimes(.05f);
            foreach (PlayerId player in coarse.Keys)
                Assert.That(Math.Abs(coarse[player] - fine[player]),
                    Is.LessThanOrEqualTo(.11f), player.ToString());
        }

        private static Dictionary<PlayerId, float> ServiceArrivalTimes(float step)
        {
            PlayerId[] roster =
                { PlayerId.Player4, PlayerId.Player2, PlayerId.Player3, PlayerId.Player1 };
            RaceSimulation simulation = Start(roster, FourCarPitRules(), step);
            simulation.Step(.3f, Commands(roster, requestPit: true));
            var arrivals = new Dictionary<PlayerId, float>();
            for (int guard = 0; guard < 4000 && arrivals.Count < roster.Length; guard++)
            {
                simulation.Step(step, Commands(roster));
                foreach (RacerSnapshot racer in simulation.Snapshot.Racers)
                    if (racer.Pit.Phase == PitPhase.InService &&
                        !arrivals.ContainsKey(racer.PlayerId))
                        arrivals[racer.PlayerId] = simulation.Snapshot.ElapsedSeconds;
            }
            Assert.That(arrivals.Count, Is.EqualTo(roster.Length));
            return arrivals;
        }

        private static RaceSimulation Start(PlayerId[] roster, PitRules pit, float step = .1f)
        {
            var track = new TrackDefinition(new[]
            {
                new TrackSegment(new Vec2(0f, 0f), new Vec2(10f, 0f),
                    TrackSectionKind.Straight, float.PositiveInfinity),
                new TrackSegment(new Vec2(10f, 0f), new Vec2(0f, 0f),
                    TrackSectionKind.Corner, 100f)
            });
            var rules = new RaceRules(100, 0f, 100f, 1000f, 1000f, 1000f,
                .5f, 0f, 1f, 0f, 0f, 1f, 0,
                ConditionRules.Disabled, pit);
            var simulation = new RaceSimulation(track, rules, roster);
            simulation.Step(step, Commands(roster));
            simulation.Step(step, Commands(roster));
            Assert.That(simulation.Snapshot.Phase, Is.EqualTo(RacePhase.Racing));
            return simulation;
        }

        private static PitRules FourCarPitRules() => new PitRules(20f,
            new[] { 80f, 100f, 120f, 140f },
            new[] { 10f, 10f, 10f, 10f },
            new[] { 10f, 10f, 10f, 10f },
            new[] { 120f, 100f, 80f, 60f },
            new[] { 80f, 100f, 120f, 140f },
            minimumHeadway: 62f, exitRejoinDistance: 5f);

        private static PitRules TwoCarContentionRules() => new PitRules(20f,
            new[] { 40f, 80f },
            new[] { 10f, 10f },
            new[] { 10f, 10f },
            new[] { 60f, 20f },
            new[] { 40f, 80f },
            minimumHeadway: 62f, exitRejoinDistance: 5f);

        private static RacerCommand[] Commands(IEnumerable<PlayerId> roster,
            bool requestPit = false, bool requestExit = false,
            PlayerId? requestPitFor = null, PlayerId? requestExitFor = null) => roster.Select(id =>
                new RacerCommand(id, ThrottleStep.Boost, true, false,
                    PitService.None, requestPit || requestPitFor == id, 0f,
                    requestExit || requestExitFor == id)).ToArray();

        private static RacerSnapshot Racer(RaceSimulation simulation, PlayerId player) =>
            simulation.Snapshot.Racers.Single(x => x.PlayerId == player);

        private static float PitPosition(RacerSnapshot racer) =>
            racer.Pit.TrafficState == PitTrafficState.Queued
                ? -racer.Pit.QueueOffset
                : racer.Pit.PhaseProgress * (racer.PlayerId == PlayerId.Player1 ? 90f :
                    racer.PlayerId == PlayerId.Player2 ? 110f :
                    racer.PlayerId == PlayerId.Player3 ? 130f : 150f);

        private static void AssertPit(RaceSimulation simulation, PlayerId player,
            PitTrafficState traffic, float queueOffset)
        {
            RacerPitSnapshot pit = Racer(simulation, player).Pit;
            Assert.That(pit.Phase, Is.EqualTo(PitPhase.Entering), player.ToString());
            Assert.That(pit.TrafficState, Is.EqualTo(traffic), player.ToString());
            Assert.That(pit.QueueOffset, Is.EqualTo(queueOffset).Within(.001f),
                player.ToString());
        }
    }
}
