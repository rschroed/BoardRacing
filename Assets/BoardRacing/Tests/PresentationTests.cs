using BoardRacing.Domain;
using BoardRacing.Runtime;
using NUnit.Framework;

namespace BoardRacing.Tests
{
    public sealed class PresentationTests
    {
        [Test]
        public void DisabledConditionsAlwaysMapToNormalVisuals()
        {
            var state = CarConditionVisualMapper.From(Condition(.9f, .9f), ConditionRules.Disabled);
            Assert.That(state.FuelLevel, Is.EqualTo(ConditionVisualLevel.Normal));
            Assert.That(state.TireLevel, Is.EqualTo(ConditionVisualLevel.Normal));
        }

        [Test]
        public void FuelAndTireLevelsMapIndependentlyAtStableThresholds()
        {
            var rules = ConditionRules.Defaults;
            var normal = CarConditionVisualMapper.From(Condition(.1f, .1f), rules);
            Assert.That(normal.FuelLevel, Is.EqualTo(ConditionVisualLevel.Normal));
            Assert.That(normal.TireLevel, Is.EqualTo(ConditionVisualLevel.Normal));

            var fuelLow = CarConditionVisualMapper.From(
                Condition(rules.FuelWarningThreshold, .1f), rules);
            Assert.That(fuelLow.FuelLevel, Is.EqualTo(ConditionVisualLevel.Warning));
            Assert.That(fuelLow.TireLevel, Is.EqualTo(ConditionVisualLevel.Normal));

            // Fuel is critical only when the empty-tank penalty is active, never
            // from the raw value alone.
            var fullTankUsed = CarConditionVisualMapper.From(Condition(1f, .1f), rules);
            Assert.That(fullTankUsed.FuelLevel, Is.EqualTo(ConditionVisualLevel.Warning));
            var empty = CarConditionVisualMapper.From(Condition(1f, .1f, fuelPenalty: true), rules);
            Assert.That(empty.FuelLevel, Is.EqualTo(ConditionVisualLevel.Critical));

            var tireCritical = CarConditionVisualMapper.From(
                Condition(.1f, rules.TirePenaltyThreshold), rules);
            Assert.That(tireCritical.FuelLevel, Is.EqualTo(ConditionVisualLevel.Normal));
            Assert.That(tireCritical.TireLevel, Is.EqualTo(ConditionVisualLevel.Critical));
        }

        [Test]
        public void VisualMappingPreservesNormalizedConditionValues()
        {
            var state = CarConditionVisualMapper.From(Condition(.42f, .73f), ConditionRules.Defaults);
            Assert.That(state.FuelUsed, Is.EqualTo(.42f));
            Assert.That(state.TireWear, Is.EqualTo(.73f));
        }

        [Test]
        public void SimultaneousRacersKeepIndependentConditionVisuals()
        {
            var rules = ConditionRules.Defaults;
            var playerOne = CarConditionVisualMapper.From(
                Racer(PlayerId.Player1, rules.FuelWarningThreshold, .1f), rules);
            var playerTwo = CarConditionVisualMapper.From(
                Racer(PlayerId.Player2, .1f, rules.TirePenaltyThreshold * .65f), rules);

            Assert.That(playerOne.FuelLevel, Is.EqualTo(ConditionVisualLevel.Warning));
            Assert.That(playerOne.TireLevel, Is.EqualTo(ConditionVisualLevel.Normal));
            Assert.That(playerTwo.FuelLevel, Is.EqualTo(ConditionVisualLevel.Normal));
            Assert.That(playerTwo.TireLevel, Is.EqualTo(ConditionVisualLevel.Warning));
        }

        [Test]
        public void PitLanePoseIsContinuousAcrossEveryPhaseBoundary()
        {
            var layout = Layout();
            var onTrack = Pose(Racer(PlayerId.Player1, PitPhase.OnTrack), layout);
            var entryStart = Pose(Racer(PlayerId.Player1, PitPhase.Entering, 0f), layout);
            var entryEnd = Pose(Racer(PlayerId.Player1, PitPhase.Entering, 1f), layout);
            var parked = Pose(Racer(PlayerId.Player1, PitPhase.InService), layout);
            var exitStart = Pose(Racer(PlayerId.Player1, PitPhase.Exiting, 0f), layout);
            var exitEnd = Pose(Racer(PlayerId.Player1, PitPhase.Exiting, 1f), layout);
            // The simulation resumes the car at the rejoin distance, so the first
            // on-track pose after an exit samples the track at ExitRejoin.
            var rejoined = Pose(Racer(PlayerId.Player1, PitPhase.OnTrack), layout, layout.ExitRejoin);

            AssertPosition(onTrack, entryStart.Position);
            AssertPosition(entryEnd, parked.Position);
            AssertPosition(parked, exitStart.Position);
            AssertPosition(exitEnd, rejoined.Position);
        }

        [Test]
        public void PitLanePoseMovesContinuouslyAndUsesEachPlayersOwnBox()
        {
            var layout = Layout();
            var p1MidEntry = Pose(Racer(PlayerId.Player1, PitPhase.Entering, .5f), layout);
            var p1Box = Pose(Racer(PlayerId.Player1, PitPhase.InService), layout);
            var p2Box = Pose(Racer(PlayerId.Player2, PitPhase.InService), layout);
            var p2MidExit = Pose(Racer(PlayerId.Player2, PitPhase.Exiting, .5f), layout);

            Assert.That(p1MidEntry.Position.X, Is.InRange(layout.PitLine.X, layout.PlayerOneBox.X));
            AssertPosition(p1Box, layout.PlayerOneBox);
            AssertPosition(p2Box, layout.PlayerTwoBox);
            Assert.That(p2MidExit.Position.X, Is.Not.EqualTo(p2Box.Position.X).Within(.001f));
        }

        [Test]
        public void InServicePoseStaysParkedAcrossUndecidedSwitchAndResetStates()
        {
            var layout = Layout();
            var undecided = Pose(Racer(PlayerId.Player1, PitPhase.InService, 0f,
                PitService.None, 0f), layout);
            var holdingTires = Pose(Racer(PlayerId.Player1, PitPhase.InService, 0f,
                PitService.Tires, .7f), layout);
            var switchedFuel = Pose(Racer(PlayerId.Player1, PitPhase.InService, 0f,
                PitService.Fuel, 0f), layout);

            AssertPosition(undecided, layout.PlayerOneBox);
            AssertPosition(holdingTires, layout.PlayerOneBox);
            AssertPosition(switchedFuel, layout.PlayerOneBox);
        }

        [Test]
        public void NormalRejoinAndLateFinishBothEndAtTheExitRejoinPoint()
        {
            var layout = Layout();
            var exitEnd = Pose(Racer(PlayerId.Player2, PitPhase.Exiting, 1f), layout);
            var rejoined = Pose(Racer(PlayerId.Player2, PitPhase.OnTrack), layout, layout.ExitRejoin);
            var finished = Pose(Racer(PlayerId.Player2, PitPhase.OnTrack, 0f,
                PitService.None, 0f, true), layout, layout.ExitRejoin);

            AssertPosition(exitEnd, layout.ExitRejoin);
            AssertPosition(rejoined, layout.ExitRejoin);
            AssertPosition(finished, layout.ExitRejoin);
            Assert.That(finished.Finished, Is.True);
        }

        [Test]
        public void PitExitIsAShortForwardMergeOntoTheRejoinPoint()
        {
            var layout = Layout();
            var start = PitLanePresentationMapper.ExitPose(PlayerId.Player1, 0f, false, layout);
            var mid = PitLanePresentationMapper.ExitPose(PlayerId.Player1, .5f, false, layout);
            var end = PitLanePresentationMapper.ExitPose(PlayerId.Player1, 1f, false, layout);

            // Every point of the exit moves forward with the track — no doubling
            // back toward the start line.
            Assert.That(start.Tangent.X, Is.GreaterThan(0f));
            Assert.That(mid.Tangent.X, Is.GreaterThan(0f));
            Assert.That(end.Tangent.X, Is.GreaterThan(0f));
            Assert.That(mid.Position.X,
                Is.InRange(layout.PlayerOneBox.X, layout.ExitRejoin.X));
            AssertPosition(end, layout.ExitRejoin);
        }

        private static RacerConditionSnapshot Condition(float fuelUsed, float wear, bool fuelPenalty = false) =>
            new RacerConditionSnapshot(fuelUsed, wear, fuelPenalty, false);

        private static RacerSnapshot Racer(PlayerId id, float fuelUsed, float wear) =>
            new RacerSnapshot(id, 0f, 0f, 0, 1, false, -1f, default, 0f, false, 0f, 0,
                Condition(fuelUsed, wear), default);

        private static RacerSnapshot Racer(PlayerId id, PitPhase phase, float phaseProgress = 0f,
            PitService service = PitService.None, float serviceProgress = 0f, bool finished = false) =>
            new RacerSnapshot(id, 0f, 100f, 1, 1, finished, finished ? 12f : -1f,
                new TrackSample(new Vec2(5f, 5f), new Vec2(1f, 0f), 0,
                    TrackSectionKind.Straight, float.PositiveInfinity), 0f, false, 0f, 0,
                Condition(0f, 0f), new RacerPitSnapshot(service, phase, serviceProgress,
                    finished ? 1 : 0, finished, phaseProgress));

        private static PitLanePresentationLayout Layout() => new PitLanePresentationLayout(
            new Vec2(5f, 5f), new Vec2(10f, 10f), new Vec2(20f, 10f),
            new Vec2(30f, 10f), new Vec2(40f, 10f), new Vec2(38f, 8f),
            new Vec2(42f, 5f));

        private static CarPresentationPose Pose(RacerSnapshot racer, PitLanePresentationLayout layout) =>
            Pose(racer, layout, new Vec2(5f, 5f));

        private static CarPresentationPose Pose(RacerSnapshot racer, PitLanePresentationLayout layout,
            Vec2 trackPosition) =>
            PitLanePresentationMapper.From(racer, trackPosition, new Vec2(1f, 0f), layout);

        private static void AssertPosition(CarPresentationPose pose, Vec2 expected)
        {
            Assert.That(pose.Position.X, Is.EqualTo(expected.X).Within(.001f));
            Assert.That(pose.Position.Y, Is.EqualTo(expected.Y).Within(.001f));
        }
    }
}
