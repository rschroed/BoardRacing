using System;
using System.Linq;
using BoardRacing.Domain;
using NUnit.Framework;

namespace BoardRacing.Tests
{
    // Pit transit paced by distance (issue #110): each lane leg's duration is
    // its length at the pit-lane crawl, per player, replacing the shared fixed
    // seconds that covered Player 1's ~500 px exit at 2-3× racing top speed.
    public sealed class PitPacingTests
    {
        [Test]
        public void TransitDurationsDeriveFromLaneLengthAtTheCrawl()
        {
            var rules = new PitRules(100f, 20f, 30f, 40f, 50f);
            Assert.That(rules.EntrySeconds(PlayerId.Player1), Is.EqualTo(.2f).Within(1e-5f));
            Assert.That(rules.ExitSeconds(PlayerId.Player1), Is.EqualTo(.3f).Within(1e-5f));
            Assert.That(rules.EntrySeconds(PlayerId.Player2), Is.EqualTo(.4f).Within(1e-5f));
            Assert.That(rules.ExitSeconds(PlayerId.Player2), Is.EqualTo(.5f).Within(1e-5f));
        }

        [Test]
        public void EveryCatalogCourseMeasuresItsOwnLaneLegsPerPlayer()
        {
            foreach (CourseDefinition course in CourseCatalog.All())
            {
                PitRules rules = PitRules.ForCourse(course, Pace.PitLaneSpeed);
                Vec2 pitLine = course.Track.Sample(0f).Position;
                Vec2 rejoin = course.Track.Sample(course.Pit.ExitRejoinDistance).Position;
                foreach (PlayerId player in new[] { PlayerId.Player1, PlayerId.Player2 })
                {
                    Vec2 box = course.Pit.Box(player);
                    float entry = Distance(pitLine, course.Pit.Entry) + Distance(course.Pit.Entry, box);
                    float exit = Distance(box, course.Pit.MergeApproach) +
                        Distance(course.Pit.MergeApproach, rejoin);
                    Assert.That(rules.EntryLength(player), Is.EqualTo(entry).Within(.001f), course.Name);
                    Assert.That(rules.ExitLength(player), Is.EqualTo(exit).Within(.001f), course.Name);
                }
                // Different box positions mean genuinely different transit times —
                // the single shared duration was wrong for at least one player.
                Assert.That(rules.EntrySeconds(PlayerId.Player1),
                    Is.Not.EqualTo(rules.EntrySeconds(PlayerId.Player2)).Within(.01f), course.Name);
            }
        }

        [Test]
        public void TheCrawlStaysUnderEveryCatalogCornerSpeed()
        {
            // The pit lane must read as the slowest driving on the board: the
            // crawl sits under the tightest corner's safe speed on every course,
            // and even the drawn box eases' mid-leg peak (4/3 of the mean) stays
            // under the corner-speed baseline.
            float tightestCorner = CourseCatalog.All()
                .SelectMany(course => course.Track.Segments)
                .Where(segment => segment.Kind == TrackSectionKind.Corner)
                .Min(segment => segment.SafeSpeed);
            Assert.That(Pace.PitLaneSpeed, Is.LessThan(tightestCorner));
            Assert.That(4f / 3f * Pace.PitLaneSpeed, Is.LessThan(Pace.CornerSafeSpeed));
        }

        [Test]
        public void ApproachBrakingDeliversTheCarToTheLineAtTheCrawl()
        {
            // A car with a pit call brakes on the track toward the crawl instead
            // of stopping dead on the line (owner feel review, 2026-07-23).
            var simulation = CrawlSimulation(laps: 3);
            simulation.Step(.01f, Both(requestPit: true));
            float priorSpeed = 0f, peakSpeed = 0f;
            int guard = 0;
            while (Racer(simulation, PlayerId.Player1).Pit.Phase != PitPhase.Entering && guard++ < 10000)
            {
                priorSpeed = Racer(simulation, PlayerId.Player1).Speed;
                peakSpeed = Math.Max(peakSpeed, priorSpeed);
                simulation.Step(.01f, Both());
            }
            Assert.That(guard, Is.LessThan(10000));
            // It genuinely raced before braking, and crossed at the crawl.
            Assert.That(peakSpeed, Is.GreaterThan(45f));
            Assert.That(priorSpeed, Is.EqualTo(30f).Within(3f));
        }

        [Test]
        public void ExitMergesAtTheCrawlAndAcceleratesAway()
        {
            // Resuming on track from a dead stop read as stop-and-go at the
            // rejoin (owner feel review, 2026-07-23): the car merges at the
            // crawl the exit leg was driven at, then accelerates.
            var simulation = CrawlSimulation(laps: 3);
            simulation.Step(.01f, Both(requestPit: true));
            int guard = 0;
            while (Racer(simulation, PlayerId.Player1).Pit.Phase != PitPhase.InService && guard++ < 10000)
                simulation.Step(.01f, Both());
            simulation.Step(.01f, Both(requestExit: true));
            Assert.That(Racer(simulation, PlayerId.Player1).Pit.Phase, Is.EqualTo(PitPhase.Exiting));
            while (Racer(simulation, PlayerId.Player1).Pit.Phase != PitPhase.OnTrack && guard++ < 10000)
                simulation.Step(.01f, Both());
            Assert.That(guard, Is.LessThan(10000));
            Assert.That(Racer(simulation, PlayerId.Player1).Speed, Is.EqualTo(30f).Within(.001f));
            simulation.Step(.01f, Both());
            Assert.That(Racer(simulation, PlayerId.Player1).Speed, Is.GreaterThan(30f));
        }

        [Test]
        public void AFinalLapCallThatLosesToTheLineDoesNotSlowTheRunIn()
        {
            // Issue #95: an eligible finish beats the pending call — so the
            // approach braking must leave the final run to the line alone.
            var simulation = CrawlSimulation(laps: 1, requiredServiceCount: 0);
            simulation.Step(.01f, Both(requestPit: true));
            float priorSpeed = 0f;
            int guard = 0;
            while (!Racer(simulation, PlayerId.Player1).Finished && guard++ < 10000)
            {
                priorSpeed = Racer(simulation, PlayerId.Player1).Speed;
                simulation.Step(.01f, Both());
            }
            Assert.That(guard, Is.LessThan(10000));
            Assert.That(priorSpeed, Is.GreaterThan(90f));
        }

        [Test]
        public void SimulationPacesEachPlayersTransitByTheirOwnLegs()
        {
            // Player 2's entry leg is twice Player 1's: with both cars diverting
            // on the same line crossing, Player 1 must reach the box while
            // Player 2 is still driving the lane.
            var track = new TrackDefinition(new[]
            {
                new TrackSegment(new Vec2(0f, 0f), new Vec2(10f, 0f), TrackSectionKind.Straight,
                    float.PositiveInfinity),
                new TrackSegment(new Vec2(10f, 0f), new Vec2(0f, 0f), TrackSectionKind.Corner, 50f)
            });
            var rules = new RaceRules(3, 0f, 100f, 1000f, 100f, 100f, .5f, .2f, .5f, 5f, 1f, 1f, 0,
                new ConditionRules(.1f, .2f, .8f, .8f, .8f, .2f, .2f, .2f, .8f),
                new PitRules(100f, 20f, 20f, 40f, 20f));
            var simulation = new RaceSimulation(track, rules);
            simulation.Step(.01f, BothPresent());
            simulation.Step(.01f, BothPresent());
            Assert.That(simulation.Snapshot.Phase, Is.EqualTo(RacePhase.Racing));

            simulation.Step(.01f, Both(requestPit: true));
            int guard = 0;
            while (Racer(simulation, PlayerId.Player1).Pit.Phase != PitPhase.InService && guard++ < 10000)
                simulation.Step(.01f, Both());
            Assert.That(guard, Is.LessThan(10000));
            Assert.That(Racer(simulation, PlayerId.Player2).Pit.Phase, Is.EqualTo(PitPhase.Entering));

            for (int i = 0; i < 25; i++) simulation.Step(.01f, Both());
            Assert.That(Racer(simulation, PlayerId.Player2).Pit.Phase, Is.EqualTo(PitPhase.InService));
        }

        private static RacerSnapshot Racer(RaceSimulation simulation, PlayerId id) =>
            simulation.Snapshot.Racers.Single(x => x.PlayerId == id);

        // Top speed 100 over a 200 px lap with a 30 px/s crawl: enough road for
        // the approach braking to show before every line crossing.
        private static RaceSimulation CrawlSimulation(int laps, int requiredServiceCount = 1)
        {
            var track = new TrackDefinition(new[]
            {
                new TrackSegment(new Vec2(0f, 0f), new Vec2(100f, 0f), TrackSectionKind.Straight,
                    float.PositiveInfinity),
                new TrackSegment(new Vec2(100f, 0f), new Vec2(0f, 0f), TrackSectionKind.Corner, 50f)
            });
            var rules = new RaceRules(laps, 0f, 100f, 1000f, 100f, 100f, .5f, .2f, .5f, 5f, 1f, 1f,
                requiredServiceCount, new ConditionRules(.1f, .2f, .8f, .8f, .8f, .2f, .2f, .2f, .8f),
                new PitRules(30f, 15f, 15f, 15f, 15f));
            var simulation = new RaceSimulation(track, rules);
            simulation.Step(.01f, BothPresent());
            simulation.Step(.01f, BothPresent());
            return simulation;
        }

        private static RacerCommand[] BothPresent() => new[]
        {
            new RacerCommand(PlayerId.Player1, ThrottleStep.Brake, true, false),
            new RacerCommand(PlayerId.Player2, ThrottleStep.Brake, true, false)
        };

        private static RacerCommand[] Both(bool requestPit = false, bool requestExit = false) => new[]
        {
            new RacerCommand(PlayerId.Player1, ThrottleStep.Boost, true, true,
                PitService.None, requestPit, 0f, requestExit),
            new RacerCommand(PlayerId.Player2, ThrottleStep.Boost, true, true,
                PitService.None, requestPit, 0f, requestExit)
        };

        private static float Distance(Vec2 a, Vec2 b)
        {
            float x = b.X - a.X, y = b.Y - a.Y;
            return (float)Math.Sqrt(x * x + y * y);
        }
    }
}
