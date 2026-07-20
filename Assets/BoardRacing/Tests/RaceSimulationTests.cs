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
                new ThrottleTracePoint(2f, ThrottleStep.Boost),
                new ThrottleTracePoint(1f, ThrottleStep.Drive),
                new ThrottleTracePoint(3f, ThrottleStep.Brake)
            });
            Assert.That(trace.At(.5f), Is.EqualTo(ThrottleStep.Brake));
            Assert.That(trace.At(1.5f), Is.EqualTo(ThrottleStep.Drive));
            Assert.That(trace.At(2f), Is.EqualTo(ThrottleStep.Boost));
        }

        [Test]
        public void GridCountdownAndEarlyThrottleDoNotMoveCars()
        {
            var simulation = new RaceSimulation(TrackDefinition.Placeholder(), RaceRules.Defaults);
            var driving = Commands(ThrottleStep.Boost, false);
            simulation.Step(1f / 60f, new[]
            {
                new RacerCommand(PlayerId.Player1, ThrottleStep.Boost, true, false),
                new RacerCommand(PlayerId.Player2, ThrottleStep.Boost, false, false)
            });
            Assert.That(simulation.Snapshot.Phase, Is.EqualTo(RacePhase.Grid));
            Assert.That(simulation.Snapshot.Racers.All(x => x.TotalDistance == 0f), Is.True);

            simulation.Step(1f / 60f, Commands(ThrottleStep.Brake, false));
            Assert.That(simulation.Snapshot.Phase, Is.EqualTo(RacePhase.Countdown));
            simulation.Step(1f, driving);
            Assert.That(simulation.Snapshot.Racers.All(x => x.TotalDistance == 0f), Is.True);
        }

        [Test]
        public void ReleasedThrottleBrakesMoreStronglyThanLowerThrottleDrag()
        {
            var simulation = StartedSimulation();
            for (int i = 0; i < 120; i++) simulation.Step(1f / 60f, Commands(ThrottleStep.Boost, true));
            float fast = Player(simulation, PlayerId.Player1).Speed;
            simulation.Step(.25f, Commands(ThrottleStep.Drive, true));
            float dragged = Player(simulation, PlayerId.Player1).Speed;
            simulation.Step(.25f, Commands(ThrottleStep.Brake, false));
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
                simulation.Step(1f / 60f, Commands(ThrottleStep.Boost, true));
            var incident = Player(simulation, PlayerId.Player1);
            Assert.That(incident.IncidentCount, Is.EqualTo(1));
            Assert.That(incident.IncidentThisStep, Is.True);
            Assert.That(incident.RecoveryRemaining, Is.GreaterThan(0f));
            for (int i = 0; i < 20; i++) simulation.Step(1f / 60f, Commands(ThrottleStep.Boost, true));
            Assert.That(Player(simulation, PlayerId.Player1).IncidentCount, Is.EqualTo(1));
        }

        [Test]
        public void QuarterThrottleCanEnterPlaceholderCornersCleanly()
        {
            var simulation = StartedSimulation();
            for (int i = 0; i < 5000 && Player(simulation, PlayerId.Player1).CompletedLaps == 0; i++)
                simulation.Step(1f / 60f, Commands(ThrottleStep.Drive, true));
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
            simulation.Step(.5f, Commands(ThrottleStep.Boost, true));
            Assert.That(simulation.Snapshot.AwaitingRematchRelease, Is.False);
            simulation.Step(.6f, Commands(ThrottleStep.Boost, true));
            Assert.That(simulation.Snapshot.AwaitingRematchRelease, Is.True);
            simulation.Step(.1f, Commands(ThrottleStep.Brake, false));
            Assert.That(simulation.Snapshot.Phase, Is.EqualTo(RacePhase.Grid));
            Assert.That(simulation.Snapshot.Racers.All(x => x.TotalDistance == 0f), Is.True);
        }

        [Test]
        public void TrancheThreeInitialStateExposesConditionsAndMandatoryServiceEligibility()
        {
            var simulation = new RaceSimulation(TrackDefinition.Placeholder(), RaceRules.TrancheThreeDefaults);
            foreach (var racer in simulation.Snapshot.Racers)
            {
                Assert.That(racer.Condition.FuelUsed, Is.Zero);
                Assert.That(racer.Condition.TireWear, Is.Zero);
                Assert.That(racer.Condition.FuelPenaltyActive, Is.False);
                Assert.That(racer.Condition.TirePenaltyActive, Is.False);
                Assert.That(racer.Pit.SelectedService, Is.EqualTo(PitService.None));
                Assert.That(racer.Pit.Phase, Is.EqualTo(PitPhase.OnTrack));
                Assert.That(racer.Pit.ServiceProgress, Is.Zero);
                Assert.That(racer.Pit.PhaseProgress, Is.Zero);
                Assert.That(racer.Pit.CompletedServices, Is.Zero);
                Assert.That(racer.Pit.FinishEligible, Is.False);
            }
        }

        [Test]
        public void StrategyIntentIsPlayerScopedAndRequestsWithoutPreselectedService()
        {
            var simulation = StartedSimulation(RaceRules.TrancheThreeDefaults);
            simulation.Step(1f / 60f, new[]
            {
                StrategyCommand(PlayerId.Player1, PitService.Tires, true),
                StrategyCommand(PlayerId.Player2, PitService.Fuel, false)
            });

            var p1 = Player(simulation, PlayerId.Player1);
            var p2 = Player(simulation, PlayerId.Player2);
            Assert.That(p1.Pit.SelectedService, Is.EqualTo(PitService.None));
            Assert.That(p1.Pit.Phase, Is.EqualTo(PitPhase.Requested));
            Assert.That(p2.Pit.SelectedService, Is.EqualTo(PitService.None));
            Assert.That(p2.Pit.Phase, Is.EqualTo(PitPhase.OnTrack));

            simulation.Step(1f / 60f, new[] { StrategyCommand(PlayerId.Player2, PitService.None, true) });
            Assert.That(Player(simulation, PlayerId.Player2).Pit.SelectedService, Is.EqualTo(PitService.None));
            Assert.That(Player(simulation, PlayerId.Player2).Pit.Phase, Is.EqualTo(PitPhase.Requested));

            simulation = StartedSimulation(RaceRules.TrancheThreeDefaults);
            simulation.Step(1f / 60f, new[] { StrategyCommand(PlayerId.Player1, PitService.Fuel, false) });
            Assert.That(Player(simulation, PlayerId.Player1).Pit.SelectedService, Is.EqualTo(PitService.None));
            Assert.That(Player(simulation, PlayerId.Player2).Pit.Phase, Is.EqualTo(PitPhase.OnTrack));
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
                    StrategyCommand(PlayerId.Player2, i < 90 ? PitService.Fuel : PitService.None, i == 90)
                };
                first.Step(1f / 60f, commands);
                second.Step(1f / 60f, commands);
            }

            foreach (PlayerId id in Enum.GetValues(typeof(PlayerId)))
            {
                var a = Player(first, id); var b = Player(second, id);
                Assert.That(a.TotalDistance, Is.EqualTo(b.TotalDistance));
                Assert.That(a.Condition.FuelUsed, Is.EqualTo(b.Condition.FuelUsed));
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
            Assert.Throws<ArgumentException>(() => new RacerCommand(PlayerId.Player1, ThrottleStep.Brake, true, false,
                PitService.Tires, false, 1.1f));
            Assert.Throws<ArgumentException>(() => new RacerConditionSnapshot(-.01f, 0f, false, false));
            Assert.Throws<ArgumentException>(() => new RacerPitSnapshot(PitService.None, PitPhase.OnTrack, 0f, -1, false));
            Assert.Throws<ArgumentException>(() => new RacerPitSnapshot(
                PitService.None, PitPhase.Entering, 0f, 0, false, 1.01f));
            Assert.Throws<ArgumentException>(() => new RaceRules(5, 3f, 360f, 220f, 120f, 300f, .55f, 1f,
                .35f, 180f, 38f, 1f, -1));
            Assert.Throws<ArgumentException>(() => new RaceRules(5, float.NaN, 360f, 220f, 120f, 300f, .55f, 1f,
                .35f, 180f, 38f, 1f));
            Assert.Throws<ArgumentException>(() => new ConditionRules(.1f, .1f, .7f, 0f, .5f, .01f, .1f, .6f, .75f));
            Assert.Throws<ArgumentException>(() => new PitRules(0f, .5f));
            Assert.Throws<ArgumentException>(() => new RaceRules(5, 3f, 360f, 220f, 120f, 300f, .55f, 1f,
                .35f, 180f, 38f, 1f, 1, ConditionRules.Defaults));
        }

        [Test]
        public void FuelBurnsFasterAtBoostAndNeverRecovers()
        {
            var simulation = StartedSimulation(RaceRules.TrancheThreeDefaults);
            for (int i = 0; i < 300; i++)
                simulation.Step(1f / 60f, Commands(ThrottleStep.Boost, true));
            float boosted = Player(simulation, PlayerId.Player1).Condition.FuelUsed;
            Assert.That(boosted, Is.GreaterThan(.15f));

            for (int i = 0; i < 60; i++)
                simulation.Step(1f / 60f, Commands(ThrottleStep.Brake, false));
            Assert.That(Player(simulation, PlayerId.Player1).Condition.FuelUsed, Is.EqualTo(boosted));

            for (int i = 0; i < 60; i++) simulation.Step(1f / 60f, Array.Empty<RacerCommand>());
            Assert.That(Player(simulation, PlayerId.Player1).Condition.FuelUsed, Is.EqualTo(boosted));

            var driveSimulation = StartedSimulation(RaceRules.TrancheThreeDefaults);
            for (int i = 0; i < 300; i++)
                driveSimulation.Step(1f / 60f, Commands(ThrottleStep.Drive, true));
            float driven = Player(driveSimulation, PlayerId.Player1).Condition.FuelUsed;
            Assert.That(driven, Is.GreaterThan(0f));
            Assert.That(driven, Is.LessThan(boosted));
        }

        [Test]
        public void EmptyTankLimitsPerformanceWithoutRequestingPit()
        {
            var simulation = StartedSimulation(LongConditionRules());
            int guard = 0;
            while (!Player(simulation, PlayerId.Player1).Condition.FuelPenaltyActive && guard++ < 5000)
                simulation.Step(1f / 60f, Commands(ThrottleStep.Boost, true));
            Assert.That(guard, Is.LessThan(5000));
            for (int i = 0; i < 240; i++) simulation.Step(1f / 60f, Commands(ThrottleStep.Boost, true));

            var racer = Player(simulation, PlayerId.Player1);
            Assert.That(racer.Speed, Is.LessThanOrEqualTo(
                LongConditionRules().MaxSpeed * LongConditionRules().Conditions.EmptyMaximumSpeedScale + .01f));
            Assert.That(racer.Speed, Is.GreaterThan(0f));
            Assert.That(racer.Pit.Phase, Is.EqualTo(PitPhase.OnTrack));
            Assert.That(racer.Pit.SelectedService, Is.EqualTo(PitService.None));
        }

        [Test]
        public void TireWearAccumulatesOnlyAtCornerEntryAndRemainsClamped()
        {
            var simulation = StartedSimulation(LongConditionRules());
            for (int i = 0; i < 60; i++) simulation.Step(1f / 60f, Commands(ThrottleStep.Boost, true));
            Assert.That(Player(simulation, PlayerId.Player1).Condition.TireWear, Is.Zero);

            int guard = 0;
            while (Player(simulation, PlayerId.Player1).CompletedLaps < 20 && guard++ < 80000)
                simulation.Step(1f / 60f, Commands(ThrottleStep.Boost, true));
            var condition = Player(simulation, PlayerId.Player1).Condition;
            Assert.That(condition.TireWear, Is.GreaterThan(0f));
            Assert.That(condition.TireWear, Is.LessThanOrEqualTo(1f));
            Assert.That(condition.TirePenaltyActive, Is.True);
        }

        [Test]
        public void WornTiresTurnAPreviouslySafeCornerSpeedIntoAnIncident()
        {
            var conditions = new ConditionRules(.001f, .1f, .99f, 1f, 1f, .25f, 0f, .2f, .5f);
            var rules = new RaceRules(5, 0f, 360f, 220f, 120f, 300f, .55f, 1f, .35f,
                180f, 38f, 1f, 0, conditions);
            var simulation = StartedSimulation(rules);
            int guard = 0;
            while (Player(simulation, PlayerId.Player1).Condition.TireWear == 0f && guard++ < 10000)
                simulation.Step(1f / 60f, Commands(ThrottleStep.Drive, true));
            Assert.That(Player(simulation, PlayerId.Player1).IncidentCount, Is.Zero);
            Assert.That(Player(simulation, PlayerId.Player1).Condition.TirePenaltyActive, Is.True);

            while (Player(simulation, PlayerId.Player1).IncidentCount == 0 && guard++ < 20000)
                simulation.Step(1f / 60f, Commands(ThrottleStep.Drive, true));
            Assert.That(Player(simulation, PlayerId.Player1).IncidentCount, Is.GreaterThan(0));
        }

        [Test]
        public void UnsafeCornerEntryAddsMoreWearThanSafeEntry()
        {
            var conditions = new ConditionRules(.001f, .1f, .99f, 1f, 1f, .01f, .5f, .99f, .9f);
            var rules = new RaceRules(5, 0f, 360f, 220f, 120f, 300f, .55f, 1f, .35f,
                180f, 38f, 1f, 0, conditions);
            var safe = StartedSimulation(rules); var unsafeEntry = StartedSimulation(rules);
            int safeGuard = 0, unsafeGuard = 0;
            while (Player(safe, PlayerId.Player1).Condition.TireWear == 0f && safeGuard++ < 10000)
                safe.Step(1f / 60f, Commands(ThrottleStep.Drive, true));
            while (Player(unsafeEntry, PlayerId.Player1).Condition.TireWear == 0f && unsafeGuard++ < 10000)
                unsafeEntry.Step(1f / 60f, Commands(ThrottleStep.Boost, true));
            Assert.That(Player(unsafeEntry, PlayerId.Player1).Condition.TireWear,
                Is.GreaterThan(Player(safe, PlayerId.Player1).Condition.TireWear));
        }

        [Test]
        public void ConditionsRemainIndependentAcrossSimultaneousRacers()
        {
            var simulation = StartedSimulation(LongConditionRules());
            var p1FullP2Off = new[]
            {
                new RacerCommand(PlayerId.Player1, ThrottleStep.Boost, true, true),
                new RacerCommand(PlayerId.Player2, ThrottleStep.Brake, true, false)
            };
            for (int i = 0; i < 300; i++) simulation.Step(1f / 60f, p1FullP2Off);
            Assert.That(Player(simulation, PlayerId.Player1).Condition.FuelUsed, Is.GreaterThan(0f));
            Assert.That(Player(simulation, PlayerId.Player2).Condition.FuelUsed, Is.Zero);
            Assert.That(Player(simulation, PlayerId.Player2).Condition.TireWear, Is.Zero);
        }

        [Test]
        public void PitRequestLatchesUntilTheNextLineAndThenSuspendsThrottle()
        {
            var simulation = StartedPitSimulation(3);
            simulation.Step(.01f, new[] { PitCommand(PlayerId.Player1, ThrottleStep.Boost, PitService.None, true) });
            Assert.That(Player(simulation, PlayerId.Player1).Pit.Phase, Is.EqualTo(PitPhase.Requested));
            Assert.That(Player(simulation, PlayerId.Player1).Pit.SelectedService, Is.EqualTo(PitService.None));
            Assert.That(Player(simulation, PlayerId.Player1).TotalDistance, Is.LessThan(simulation.Track.Length));

            AdvanceUntilPitPhase(simulation, PlayerId.Player1, PitPhase.Entering);
            var entering = Player(simulation, PlayerId.Player1);
            Assert.That(entering.TotalDistance, Is.EqualTo(simulation.Track.Length).Within(.001f));
            Assert.That(entering.Speed, Is.Zero);
            simulation.Step(.1f, new[] { PitCommand(PlayerId.Player1, ThrottleStep.Boost) });
            Assert.That(Player(simulation, PlayerId.Player1).Speed, Is.Zero);
            Assert.That(Player(simulation, PlayerId.Player1).Pit.Phase, Is.EqualTo(PitPhase.Entering));
        }

        [Test]
        public void EntryAndExitExposeNormalizedDeterministicPhaseProgress()
        {
            var first = StartedPitSimulation(3);
            var second = StartedPitSimulation(3);
            foreach (var simulation in new[] { first, second })
            {
                simulation.Step(.01f, new[] { PitCommand(PlayerId.Player1, ThrottleStep.Boost,
                    PitService.None, true) });
                AdvanceUntilPitPhase(simulation, PlayerId.Player1, PitPhase.Entering);
                Assert.That(Player(simulation, PlayerId.Player1).Pit.PhaseProgress, Is.Zero);
                simulation.Step(.05f, new[] { PitCommand(PlayerId.Player1, ThrottleStep.Boost) });
                Assert.That(Player(simulation, PlayerId.Player1).Pit.PhaseProgress, Is.EqualTo(.25f).Within(.001f));
                AdvanceUntilPitPhase(simulation, PlayerId.Player1, PitPhase.InService);
                Assert.That(Player(simulation, PlayerId.Player1).Pit.PhaseProgress, Is.Zero);
                simulation.Step(.01f, new[] { PitCommand(PlayerId.Player1, ThrottleStep.Boost,
                    PitService.Tires, false, 1f) });
                // The finished service leaves the car parked; only Leave Pit exits.
                Assert.That(Player(simulation, PlayerId.Player1).Pit.Phase, Is.EqualTo(PitPhase.InService));
                simulation.Step(.01f, new[] { PitCommand(PlayerId.Player1, ThrottleStep.Boost,
                    requestExit: true) });
                Assert.That(Player(simulation, PlayerId.Player1).Pit.Phase, Is.EqualTo(PitPhase.Exiting));
                Assert.That(Player(simulation, PlayerId.Player1).Pit.PhaseProgress, Is.Zero);
                simulation.Step(.1f, new[] { PitCommand(PlayerId.Player1, ThrottleStep.Boost) });
                Assert.That(Player(simulation, PlayerId.Player1).Pit.PhaseProgress, Is.EqualTo(.5f).Within(.001f));
            }

            Assert.That(Player(first, PlayerId.Player1).Pit.PhaseProgress,
                Is.EqualTo(Player(second, PlayerId.Player1).Pit.PhaseProgress));
            Assert.That(Player(first, PlayerId.Player1).TotalDistance,
                Is.EqualTo(Player(second, PlayerId.Player1).TotalDistance));
        }

        [Test]
        public void TiresAndFuelDrainOnlyTheirMeterAndCannotDoubleComplete()
        {
            var tires = StartedPitSimulation(3);
            BuildConditions(tires);
            Assert.That(Player(tires, PlayerId.Player1).Condition.TireWear, Is.GreaterThan(0f));
            RequestAndAdvanceToService(tires, PlayerId.Player1, PitService.Tires);
            float fuelAtService = Player(tires, PlayerId.Player1).Condition.FuelUsed;
            float wearAtService = Player(tires, PlayerId.Player1).Condition.TireWear;
            Assert.That(fuelAtService, Is.GreaterThan(0f));

            // A partial stir drains only the selected meter; progress is the drained fraction.
            float partial = Math.Min(.1f, wearAtService * .5f);
            tires.Step(.01f, new[] { PitCommand(PlayerId.Player1, ThrottleStep.Boost, PitService.None, false, partial) });
            float wearAfterPartial = Player(tires, PlayerId.Player1).Condition.TireWear;
            Assert.That(wearAfterPartial, Is.EqualTo(wearAtService - partial).Within(.0001f));
            Assert.That(Player(tires, PlayerId.Player1).Pit.ServiceProgress,
                Is.EqualTo(1f - wearAfterPartial).Within(.0001f));
            Assert.That(Player(tires, PlayerId.Player1).Condition.FuelUsed, Is.EqualTo(fuelAtService));
            Assert.That(Player(tires, PlayerId.Player1).Pit.Phase, Is.EqualTo(PitPhase.InService));

            // Switching to the Fuel dial drains fuel instead; partial repairs persist.
            float refuel = Math.Min(.05f, fuelAtService * .5f);
            tires.Step(.01f, new[] { PitCommand(PlayerId.Player1, ThrottleStep.Boost, PitService.Fuel, false, refuel) });
            Assert.That(Player(tires, PlayerId.Player1).Pit.SelectedService, Is.EqualTo(PitService.Fuel));
            Assert.That(Player(tires, PlayerId.Player1).Condition.FuelUsed,
                Is.EqualTo(fuelAtService - refuel).Within(.0001f));
            Assert.That(Player(tires, PlayerId.Player1).Condition.TireWear, Is.EqualTo(wearAfterPartial));

            // Draining the tire meter to zero counts the service and keeps the car parked.
            tires.Step(.01f, new[] { PitCommand(PlayerId.Player1, ThrottleStep.Boost, PitService.Tires, false, 1f) });
            Assert.That(Player(tires, PlayerId.Player1).Pit.Phase, Is.EqualTo(PitPhase.InService));
            Assert.That(Player(tires, PlayerId.Player1).Condition.TireWear, Is.Zero);
            Assert.That(Player(tires, PlayerId.Player1).Condition.FuelUsed,
                Is.EqualTo(fuelAtService - refuel).Within(.0001f));
            Assert.That(Player(tires, PlayerId.Player1).Pit.CompletedServices, Is.EqualTo(1));
            // Stirring the already-empty meter cannot count the service again.
            tires.Step(.01f, new[] { PitCommand(PlayerId.Player1, ThrottleStep.Boost, PitService.Tires, false, 1f) });
            Assert.That(Player(tires, PlayerId.Player1).Pit.CompletedServices, Is.EqualTo(1));
            // Finishing the fuel meter in the same parked stop counts a second service.
            tires.Step(.01f, new[] { PitCommand(PlayerId.Player1, ThrottleStep.Boost, PitService.Fuel, false, 1f) });
            Assert.That(Player(tires, PlayerId.Player1).Pit.CompletedServices, Is.EqualTo(2));
            Assert.That(Player(tires, PlayerId.Player1).Condition.FuelUsed, Is.Zero);
            Assert.That(Player(tires, PlayerId.Player1).Pit.Phase, Is.EqualTo(PitPhase.InService));

            // Only Leave Pit ends the stop.
            tires.Step(.01f, new[] { PitCommand(PlayerId.Player1, ThrottleStep.Boost, requestExit: true) });
            Assert.That(Player(tires, PlayerId.Player1).Pit.Phase, Is.EqualTo(PitPhase.Exiting));
            AdvanceUntilPitPhase(tires, PlayerId.Player1, PitPhase.OnTrack);
            Assert.That(Player(tires, PlayerId.Player1).Pit.SelectedService, Is.EqualTo(PitService.None));
            Assert.That(Player(tires, PlayerId.Player1).Pit.CompletedServices, Is.EqualTo(2));
            Assert.That(Player(tires, PlayerId.Player1).Pit.FinishEligible, Is.True);
        }

        [Test]
        public void UnservedRacerContinuesPastFinishAndClassifiesAfterLaterService()
        {
            var simulation = StartedPitSimulation(1);
            int guard = 0;
            while (Player(simulation, PlayerId.Player1).TotalDistance < simulation.Track.Length && guard++ < 10000)
                simulation.Step(.01f, new[] { PitCommand(PlayerId.Player1, ThrottleStep.Boost) });
            var unserved = Player(simulation, PlayerId.Player1);
            Assert.That(unserved.Finished, Is.False);
            Assert.That(unserved.Pit.FinishEligible, Is.False);
            Assert.That(unserved.CompletedLaps, Is.EqualTo(1));

            RequestAndAdvanceToService(simulation, PlayerId.Player1, PitService.Fuel);
            simulation.Step(.01f, new[] { PitCommand(PlayerId.Player1, ThrottleStep.Boost, PitService.None, false, 1f) });
            simulation.Step(.01f, new[] { PitCommand(PlayerId.Player1, ThrottleStep.Boost, requestExit: true) });
            AdvanceUntilFinished(simulation, PlayerId.Player1);
            Assert.That(Player(simulation, PlayerId.Player1).Finished, Is.True);
            Assert.That(Player(simulation, PlayerId.Player1).Pit.CompletedServices, Is.EqualTo(1));
            Assert.That(simulation.Snapshot.Phase, Is.EqualTo(RacePhase.Racing));
            Assert.That(Player(simulation, PlayerId.Player2).Finished, Is.False);
        }

        [Test]
        public void SimultaneousPitCommandsRemainPlayerScoped()
        {
            var simulation = StartedPitSimulation(3);
            simulation.Step(.01f, new[]
            {
                PitCommand(PlayerId.Player1, ThrottleStep.Boost, PitService.None, true),
                PitCommand(PlayerId.Player2, ThrottleStep.Boost, PitService.None, true)
            });
            int guard = 0;
            while ((Player(simulation, PlayerId.Player1).Pit.Phase != PitPhase.InService ||
                    Player(simulation, PlayerId.Player2).Pit.Phase != PitPhase.InService) && guard++ < 10000)
                simulation.Step(.01f, new[]
                {
                    PitCommand(PlayerId.Player1, ThrottleStep.Boost), PitCommand(PlayerId.Player2, ThrottleStep.Boost)
                });
            Assert.That(guard, Is.LessThan(10000));

            simulation.Step(.01f, new[]
            {
                PitCommand(PlayerId.Player1, ThrottleStep.Boost, PitService.Tires, false, 1f),
                PitCommand(PlayerId.Player2, ThrottleStep.Boost, PitService.Fuel, false, 0f)
            });
            Assert.That(Player(simulation, PlayerId.Player1).Pit.Phase, Is.EqualTo(PitPhase.InService));
            Assert.That(Player(simulation, PlayerId.Player1).Pit.CompletedServices, Is.EqualTo(1));
            Assert.That(Player(simulation, PlayerId.Player2).Pit.CompletedServices, Is.Zero);
            Assert.That(Player(simulation, PlayerId.Player2).Pit.SelectedService, Is.EqualTo(PitService.Fuel));
            Assert.That(Player(simulation, PlayerId.Player2).Pit.ServiceProgress,
                Is.EqualTo(1f - Player(simulation, PlayerId.Player2).Condition.FuelUsed).Within(.0001f));

            // One player's Leave Pit request never moves the other car.
            simulation.Step(.01f, new[]
            {
                PitCommand(PlayerId.Player1, ThrottleStep.Boost, requestExit: true),
                PitCommand(PlayerId.Player2, ThrottleStep.Boost, PitService.Fuel)
            });
            Assert.That(Player(simulation, PlayerId.Player1).Pit.Phase, Is.EqualTo(PitPhase.Exiting));
            Assert.That(Player(simulation, PlayerId.Player2).Pit.Phase, Is.EqualTo(PitPhase.InService));
        }

        [Test]
        public void LeavePitExitsMidServiceAndKeepsPartialMeters()
        {
            var simulation = StartedPitSimulation(3);
            BuildConditions(simulation);
            RequestAndAdvanceToService(simulation, PlayerId.Player1, PitService.Fuel);
            float fuelAtService = Player(simulation, PlayerId.Player1).Condition.FuelUsed;
            float refuel = fuelAtService * .5f;
            simulation.Step(.01f, new[] { PitCommand(PlayerId.Player1, ThrottleStep.Boost,
                PitService.Fuel, false, refuel) });
            Assert.That(Player(simulation, PlayerId.Player1).Pit.CompletedServices, Is.Zero);

            // Leaving mid-refuel is allowed: no service credit, the partial fuel is kept.
            simulation.Step(.01f, new[] { PitCommand(PlayerId.Player1, ThrottleStep.Boost,
                requestExit: true) });
            Assert.That(Player(simulation, PlayerId.Player1).Pit.Phase, Is.EqualTo(PitPhase.Exiting));
            Assert.That(Player(simulation, PlayerId.Player1).Pit.SelectedService, Is.EqualTo(PitService.None));
            Assert.That(Player(simulation, PlayerId.Player1).Condition.FuelUsed,
                Is.EqualTo(fuelAtService - refuel).Within(.0001f));
            AdvanceUntilPitPhase(simulation, PlayerId.Player1, PitPhase.OnTrack);
            Assert.That(Player(simulation, PlayerId.Player1).Pit.CompletedServices, Is.Zero);
            Assert.That(Player(simulation, PlayerId.Player1).Pit.FinishEligible, Is.False);
        }

        [Test]
        public void RematchClearsAllConditionAndPitState()
        {
            var simulation = StartedPitSimulation(1);
            simulation.Step(.01f, new[]
            {
                PitCommand(PlayerId.Player1, ThrottleStep.Boost, PitService.None, true),
                PitCommand(PlayerId.Player2, ThrottleStep.Boost, PitService.None, true)
            });
            int guard = 0;
            while ((Player(simulation, PlayerId.Player1).Pit.Phase != PitPhase.InService ||
                    Player(simulation, PlayerId.Player2).Pit.Phase != PitPhase.InService) && guard++ < 10000)
                simulation.Step(.01f, new[]
                {
                    PitCommand(PlayerId.Player1, ThrottleStep.Boost), PitCommand(PlayerId.Player2, ThrottleStep.Boost)
                });
            simulation.Step(.01f, new[]
            {
                PitCommand(PlayerId.Player1, ThrottleStep.Boost, PitService.Tires, false, 1f),
                PitCommand(PlayerId.Player2, ThrottleStep.Boost, PitService.Fuel, false, 1f)
            });
            simulation.Step(.01f, new[]
            {
                PitCommand(PlayerId.Player1, ThrottleStep.Boost, requestExit: true),
                PitCommand(PlayerId.Player2, ThrottleStep.Boost, requestExit: true)
            });
            while (simulation.Snapshot.Phase != RacePhase.Finished && guard++ < 20000)
                simulation.Step(.01f, Commands(ThrottleStep.Brake, false));
            Assert.That(simulation.Snapshot.Phase, Is.EqualTo(RacePhase.Finished));

            simulation.Step(.5f, Commands(ThrottleStep.Boost, true));
            simulation.Step(.6f, Commands(ThrottleStep.Boost, true));
            simulation.Step(.01f, Commands(ThrottleStep.Brake, false));
            Assert.That(simulation.Snapshot.Phase, Is.EqualTo(RacePhase.Grid));
            foreach (var racer in simulation.Snapshot.Racers)
            {
                Assert.That(racer.Condition.FuelUsed, Is.Zero);
                Assert.That(racer.Condition.TireWear, Is.Zero);
                Assert.That(racer.Pit.SelectedService, Is.EqualTo(PitService.None));
                Assert.That(racer.Pit.Phase, Is.EqualTo(PitPhase.OnTrack));
                Assert.That(racer.Pit.ServiceProgress, Is.Zero);
                Assert.That(racer.Pit.CompletedServices, Is.Zero);
                Assert.That(racer.Pit.FinishEligible, Is.False);
            }
        }

        private static RaceSimulation StartedSimulation()
        {
            return StartedSimulation(RaceRules.Defaults);
        }

        private static RaceSimulation StartedSimulation(RaceRules rules) =>
            StartedSimulation(TrackDefinition.Placeholder(), rules);

        private static RaceSimulation StartedSimulation(TrackDefinition track, RaceRules rules)
        {
            var result = new RaceSimulation(track, rules);
            result.Step(1f / 60f, Commands(ThrottleStep.Brake, false));
            result.Step(Math.Max(1f / 60f, rules.CountdownSeconds), Commands(ThrottleStep.Brake, false));
            Assert.That(result.Snapshot.Phase, Is.EqualTo(RacePhase.Racing));
            return result;
        }

        private static RaceSnapshot RunFullRace()
        {
            var simulation = StartedSimulation();
            int guard = 0;
            while (simulation.Snapshot.Phase != RacePhase.Finished && guard++ < 30000)
                simulation.Step(1f / 60f, Commands(ThrottleStep.Boost, true));
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
            simulation.Step(.01f, Commands(ThrottleStep.Brake, false));
            simulation.Step(.01f, Commands(ThrottleStep.Brake, false));
            for (int i = 0; i < 20 && simulation.Snapshot.Phase != RacePhase.Finished; i++)
                simulation.Step(.1f, Commands(ThrottleStep.Boost, true));
            return simulation;
        }

        private static RaceRules LongConditionRules() => new RaceRules(100, 0f, 360f, 220f, 120f, 300f,
            .55f, 1f, .35f, 180f, 38f, 1f, 0, ConditionRules.Defaults);

        private static RaceSimulation StartedPitSimulation(int laps)
        {
            var track = new TrackDefinition(new[]
            {
                new TrackSegment(new Vec2(0f, 0f), new Vec2(10f, 0f), TrackSectionKind.Straight, float.PositiveInfinity),
                new TrackSegment(new Vec2(10f, 0f), new Vec2(0f, 0f), TrackSectionKind.Corner, 50f)
            });
            var conditions = new ConditionRules(.1f, .2f, .8f, .8f, .8f, .2f, .2f, .2f, .8f);
            var rules = new RaceRules(laps, 0f, 100f, 1000f, 100f, 100f, .5f, .2f, .5f,
                5f, 1f, 1f, 1, conditions, new PitRules(.2f, .2f));
            return StartedSimulation(track, rules);
        }

        private static void BuildConditions(RaceSimulation simulation)
        {
            int guard = 0;
            while ((Player(simulation, PlayerId.Player1).Condition.FuelUsed <= 0f ||
                    Player(simulation, PlayerId.Player1).Condition.TireWear <= 0f) && guard++ < 10000)
                simulation.Step(.01f, new[] { PitCommand(PlayerId.Player1, ThrottleStep.Boost) });
            Assert.That(guard, Is.LessThan(10000));
        }

        private static void RequestAndAdvanceToService(RaceSimulation simulation, PlayerId id, PitService service)
        {
            simulation.Step(.01f, new[] { PitCommand(id, ThrottleStep.Boost, PitService.None, true) });
            AdvanceUntilPitPhase(simulation, id, PitPhase.InService);
            simulation.Step(.01f, new[] { PitCommand(id, ThrottleStep.Boost, service) });
            Assert.That(Player(simulation, id).Pit.SelectedService, Is.EqualTo(service));
        }

        private static void AdvanceUntilPitPhase(RaceSimulation simulation, PlayerId id, PitPhase target)
        {
            int guard = 0;
            while (Player(simulation, id).Pit.Phase != target && guard++ < 10000)
                simulation.Step(.01f, new[] { PitCommand(id, ThrottleStep.Boost) });
            Assert.That(guard, Is.LessThan(10000));
        }

        private static void AdvanceUntilFinished(RaceSimulation simulation, PlayerId id)
        {
            int guard = 0;
            while (!Player(simulation, id).Finished && guard++ < 10000)
                simulation.Step(.01f, new[] { PitCommand(id, ThrottleStep.Boost) });
            Assert.That(guard, Is.LessThan(10000));
        }

        private static RacerSnapshot Player(RaceSimulation simulation, PlayerId id) =>
            simulation.Snapshot.Racers.Single(x => x.PlayerId == id);

        private static RacerCommand[] Commands(ThrottleStep throttle, bool touched) => new[]
        {
            new RacerCommand(PlayerId.Player1, throttle, true, touched),
            new RacerCommand(PlayerId.Player2, throttle, true, touched)
        };

        private static RacerCommand StrategyCommand(PlayerId playerId, PitService service, bool requestPit) =>
            new RacerCommand(playerId, ThrottleStep.Drive, true, true, service, requestPit, 0f);

        private static RacerCommand PitCommand(PlayerId playerId, ThrottleStep throttle,
            PitService service = PitService.None, bool requestPit = false, float drain = 0f,
            bool requestExit = false) =>
            new RacerCommand(playerId, throttle, true, throttle != ThrottleStep.Brake,
                service, requestPit, drain, requestExit);
    }
}
