using System;
using System.Collections.Generic;
using System.Linq;
using BoardRacing.Domain;
using BoardRacing.Runtime;
using NUnit.Framework;

namespace BoardRacing.Tests
{
    // Issue #136's deterministic production gate: every authored course must
    // complete with explicit two-, three-, and four-player rosters, including
    // non-contiguous corner choices, a full pit service, classification,
    // results presentation, and rematch.
    public sealed class FourPlayerIntegrationTests
    {
        private const float Step = .05f;
        private static readonly PlayerId[][] Rosters =
        {
            new[] { PlayerId.Player4, PlayerId.Player2 },
            new[] { PlayerId.Player3, PlayerId.Player1, PlayerId.Player4 },
            new[] { PlayerId.Player2, PlayerId.Player4, PlayerId.Player1, PlayerId.Player3 }
        };

        [Test]
        public void EveryCourseCompletesTwoThreeAndFourPlayerStrategyRaces()
        {
            foreach (CourseDefinition course in CourseCatalog.All())
            foreach (PlayerId[] roster in Rosters)
            {
                IntegrationResult result = Run(course, roster);
                string scenario = course.Name + " / " + roster.Length + " players";

                Assert.That(result.Snapshot.Phase, Is.EqualTo(RacePhase.Finished), scenario);
                Assert.That(result.Snapshot.Racers.Select(x => x.PlayerId),
                    Is.EqualTo(roster), scenario + " roster");
                Assert.That(result.Snapshot.Racers.All(x => x.Finished), Is.True, scenario);
                Assert.That(result.Snapshot.Racers.All(x => x.Pit.CompletedServices >= 1),
                    Is.True, scenario + " required service");
                Assert.That(result.Snapshot.Racers.All(x =>
                    x.Pit.Phase == PitPhase.Parked), Is.True, scenario + " every car parked");
                Assert.That(result.InvalidTransitions, Is.Zero, scenario + " transitions");
                Assert.That(result.Snapshot.Racers.Select(x => x.Place),
                    Is.EquivalentTo(Enumerable.Range(1, roster.Length)),
                    scenario + " classification");

                RaceUiModel ui = ResultsUi(result.Snapshot);
                Assert.That(ui.Players.Select(x => x.PlayerId), Is.EqualTo(roster),
                    scenario + " results roster");
                Assert.That(ui.Players.All(x => x.Finished), Is.True,
                    scenario + " results completion");

                result.Simulation.RequestNewRace();
                Assert.That(result.Simulation.Snapshot.Phase, Is.EqualTo(RacePhase.Grid),
                    scenario + " rematch phase");
                Assert.That(result.Simulation.Snapshot.Racers.Select(x => x.PlayerId),
                    Is.EqualTo(roster), scenario + " rematch roster");
                Assert.That(result.Simulation.Snapshot.Racers.All(x =>
                    !x.Finished && x.TotalDistance == 0f &&
                    x.Pit.CompletedServices == 0 && x.Pit.Phase == PitPhase.OnTrack),
                    Is.True, scenario + " rematch reset");
            }
        }

        [Test]
        public void PlayerIdAndRosterOrderDoNotCreateSystematicTimingAdvantages()
        {
            foreach (CourseDefinition course in CourseCatalog.All())
            {
                PlayerId[][] permutations =
                {
                    new[] { PlayerId.Player1, PlayerId.Player2, PlayerId.Player3, PlayerId.Player4 },
                    new[] { PlayerId.Player4, PlayerId.Player3, PlayerId.Player2, PlayerId.Player1 },
                    new[] { PlayerId.Player2, PlayerId.Player4, PlayerId.Player1, PlayerId.Player3 }
                };
                Dictionary<PlayerId, float>[] times = permutations
                    .Select(roster => Run(course, roster).Snapshot.Racers
                        .ToDictionary(x => x.PlayerId, x => x.FinishTime))
                    .ToArray();

                foreach (PlayerId id in permutations[0])
                    Assert.That(times.Select(x => x[id]).Max() - times.Select(x => x[id]).Min(),
                        Is.LessThanOrEqualTo(Step + .001f),
                        course.Name + " roster-order timing changed for " + id);

                // Same-step pit arrivals now queue by PlayerId on purpose. The
                // stable order may affect results, but the cost is bounded by
                // physical queue plus one private service-curve traversal at
                // the authored lane speed, rather than roster iteration or
                // frame rate.
                float[] firstRun = times[0].Values.ToArray();
                // The same stable ordering can apply once on entry and again
                // when all four cars request release together.
                float maximumQueueDelay = 2f * (permutations[0].Length - 1) *
                    PitRules.ProductionMinimumHeadway / Pace.PitLaneSpeed;
                float longestServiceCurve = course.Pit.Stalls.Max(stall =>
                    Distance(stall.EntryAnchor, stall.ParkedPosition) +
                    Distance(stall.ParkedPosition, stall.ExitAnchor));
                maximumQueueDelay += longestServiceCurve / Pace.PitLaneSpeed;
                Assert.That(firstRun.Max() - firstRun.Min(),
                    // Entry, parking, release, and rejoin each land on fixed
                    // simulation steps, so the aggregate bound includes their
                    // quantization without allowing a route-order advantage.
                    Is.LessThanOrEqualTo(maximumQueueDelay + Step * 5f + .01f),
                    course.Name + " deterministic pit queue timing spread");
            }
        }

        private static float Distance(Vec2 a, Vec2 b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static IntegrationResult Run(CourseDefinition course, PlayerId[] roster)
        {
            RaceRules rules = RulesFor(course);
            var simulation = new RaceSimulation(course.Track, rules, roster);
            var requested = roster.ToDictionary(x => x, _ => false);
            var priorPit = roster.ToDictionary(x => x, _ => PitPhase.OnTrack);
            int invalidTransitions = 0;

            RacerCommand[] Commands()
            {
                return simulation.Snapshot.Racers.Select(racer =>
                {
                    bool request = false;
                    if (!requested[racer.PlayerId] &&
                        racer.TotalDistance >= course.Track.Length * .25f &&
                        racer.Pit.Phase == PitPhase.OnTrack)
                    {
                        requested[racer.PlayerId] = true;
                        request = true;
                    }
                    PitService service = (int)racer.PlayerId % 2 == 0
                        ? PitService.Fuel : PitService.Tires;
                    bool inService = racer.Pit.Phase == PitPhase.InService;
                    bool serviced = racer.Pit.CompletedServices >= 1;
                    return new RacerCommand(racer.PlayerId, ThrottleStep.Drive, true, false,
                        inService && !serviced ? service : PitService.None,
                        request, inService && !serviced ? Step / 5f : 0f,
                        inService && serviced);
                }).ToArray();
            }

            // Zero countdown in this deterministic gate: two steps enter Racing.
            simulation.Step(Step, Commands());
            simulation.Step(Step, Commands());
            int maximumSteps = (int)(300f / Step);
            // Runs past classification until every car has finished parking
            // (issue #149): the last finisher completes its settle underneath
            // RacePhase.Finished, and the transitions on the way are checked
            // like any other.
            for (int step = 0; step < maximumSteps &&
                !simulation.Snapshot.Racers.All(x => x.Pit.Phase == PitPhase.Parked); step++)
            {
                simulation.Step(Step, Commands());
                foreach (RacerSnapshot racer in simulation.Snapshot.Racers)
                {
                    PitPhase prior = priorPit[racer.PlayerId];
                    if (prior != racer.Pit.Phase && !Allowed(prior, racer.Pit.Phase))
                        invalidTransitions++;
                    priorPit[racer.PlayerId] = racer.Pit.Phase;
                }
            }
            return new IntegrationResult(simulation, simulation.Snapshot, invalidTransitions);
        }

        private static RaceRules RulesFor(CourseDefinition course)
        {
            RaceRules defaults = RaceRules.TrancheThreeDefaults;
            return new RaceRules(course.Laps, 0f, defaults.MaxSpeed, defaults.Acceleration,
                defaults.Drag, defaults.Braking, defaults.CornerSpeedScrub,
                defaults.CornerRecoverySeconds, defaults.RecoveryAccelerationScale,
                defaults.PassingDistance, defaults.PassingOffset, defaults.RematchHoldSeconds,
                1, ConditionRules.Defaults,
                PitRules.ForCourse(course, Pace.PitLaneSpeed), defaults.PauseClearSeconds,
                defaults.SlipstreamBonus, defaults.SlipstreamWindow);
        }

        private static RaceUiModel ResultsUi(RaceSnapshot snapshot)
        {
            PlayerControlSnapshot[] controls = snapshot.Racers.Select(x =>
                new PlayerControlSnapshot(x.PlayerId, ThrottleStep.Brake,
                    PieceState.Missing, PieceState.Missing, InputWarning.None)).ToArray();
            return RaceUiModelBuilder.Build(snapshot, controls,
                new Dictionary<PlayerId, CrewStrategyOutput>(), ConditionRules.Defaults,
                snapshot.Racers.Max(x => x.CompletedLaps));
        }

        private static bool Allowed(PitPhase from, PitPhase to) =>
            (from == PitPhase.OnTrack && to == PitPhase.Requested) ||
            (from == PitPhase.Requested && to == PitPhase.Entering) ||
            (from == PitPhase.Entering && to == PitPhase.InService) ||
            (from == PitPhase.InService && to == PitPhase.Exiting) ||
            (from == PitPhase.Exiting && to == PitPhase.OnTrack) ||
            // Classification sends a car to its box (issue #149): from the
            // track, from a call that expired at the flag, or straight off a
            // pit exit that crossed the finish distance.
            (to == PitPhase.Parking && (from == PitPhase.OnTrack ||
                from == PitPhase.Requested || from == PitPhase.Exiting)) ||
            (from == PitPhase.Parking && to == PitPhase.Parked);

        private readonly struct IntegrationResult
        {
            public IntegrationResult(RaceSimulation simulation, RaceSnapshot snapshot,
                int invalidTransitions)
            {
                Simulation = simulation;
                Snapshot = snapshot;
                InvalidTransitions = invalidTransitions;
            }

            public RaceSimulation Simulation { get; }
            public RaceSnapshot Snapshot { get; }
            public int InvalidTransitions { get; }
        }
    }
}
