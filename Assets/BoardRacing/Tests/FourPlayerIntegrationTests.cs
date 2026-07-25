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
                    x.Pit.Phase == PitPhase.OnTrack), Is.True, scenario + " pit exit");
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

                float[] firstRun = times[0].Values.ToArray();
                Assert.That(firstRun.Max() - firstRun.Min(), Is.LessThanOrEqualTo(.25f),
                    course.Name + " pit/PlayerId timing spread");
            }
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
            for (int step = 0; step < maximumSteps &&
                simulation.Snapshot.Phase != RacePhase.Finished; step++)
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
            (from == PitPhase.Exiting && to == PitPhase.OnTrack);

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
