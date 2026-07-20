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

        [Test]
        public void ExitSplineLandsOnTheRejoinHeadingWithoutSnapping()
        {
            // The simulation resumes the car on the track the moment the exit
            // finishes; the drawn exit must already point down the track then
            // (issue #89 — the old spline ended ~40° off the rejoin heading).
            var layout = DirectedLayout();
            var end = PitLanePresentationMapper.ExitPose(PlayerId.Player1, 1f, false, layout);
            Assert.That(Dot(end.Tangent, new Vec2(1f, 0f)), Is.GreaterThan(Cos(8f)));

            CarPresentationPose prior = PitLanePresentationMapper.ExitPose(PlayerId.Player1, 0f, false, layout);
            for (float progress = .02f; progress <= 1.0001f; progress += .02f)
            {
                var next = PitLanePresentationMapper.ExitPose(PlayerId.Player1, progress, false, layout);
                // A 0.02 progress step is sub-frame at the real 0.75 s exit; the
                // S-bend onto the track legitimately turns ~8 deg per chord, so
                // the bound guards against snaps, not curvature.
                Assert.That(Dot(prior.Tangent, next.Tangent), Is.GreaterThan(Cos(20f)),
                    $"heading snaps near progress {progress:0.00}");
                prior = next;
            }
        }

        [Test]
        public void EnteringSplineLeavesTheTrackAlongItsHeading()
        {
            var layout = DirectedLayout();
            var justEntered = PitLanePresentationMapper.From(
                Racer(PlayerId.Player1, PitPhase.Entering, .02f), new Vec2(5f, 5f),
                new Vec2(1f, 0f), layout);
            Assert.That(Dot(justEntered.Tangent, new Vec2(1f, 0f)), Is.GreaterThan(Cos(10f)));
        }

        [Test]
        public void PitExitMotionEasesOutOfTheBoxAndIntoTheTrack()
        {
            var layout = DirectedLayout();
            float pathLength = 0f;
            CarPresentationPose prior = PitLanePresentationMapper.ExitPose(PlayerId.Player1, 0f, false, layout);
            for (float progress = .01f; progress <= 1.0001f; progress += .01f)
            {
                var next = PitLanePresentationMapper.ExitPose(PlayerId.Player1, progress, false, layout);
                pathLength += Distance(prior.Position, next.Position);
                prior = next;
            }

            // The drawn car creeps out of the box and settles onto the track:
            // the first and last tenths of the stop cover well under their
            // linear share of the path, the middle well over.
            Assert.That(Span(layout, 0f, .1f), Is.LessThan(pathLength * .06f));
            Assert.That(Span(layout, .9f, 1f), Is.LessThan(pathLength * .06f));
            Assert.That(Span(layout, .45f, .55f), Is.GreaterThan(pathLength * .08f));
        }

        [Test]
        public void OnTrackHeadingTurnsContinuouslyAcrossChordSeams()
        {
            // The simulation tangent steps ≤12° at every chord seam of a designed
            // corner; the drawn heading spans the seams (issue #89).
            var track = TrackCatalog.Wedge();
            Vec2 prior = TrackPresentation.SmoothHeading(track, 0f);
            for (float distance = 4f; distance <= track.Length + 4f; distance += 4f)
            {
                Vec2 heading = TrackPresentation.SmoothHeading(track, distance);
                Assert.That(Dot(prior, heading), Is.GreaterThan(Cos(4f)),
                    $"heading pops at distance {distance:0}");
                Vec2 chord = track.Sample(distance).Tangent;
                Assert.That(Dot(heading, chord), Is.GreaterThan(Cos(10f)),
                    $"heading strays from the racing line at distance {distance:0}");
                prior = heading;
            }
        }

        [Test]
        public void BlendedMotionAdvancesByTheAccumulatorFraction()
        {
            // One 1/60 s sim step at racing speed moves the car ~4 px; a frame
            // landing between steps draws the fraction it has actually waited
            // (issue #89 — the zero-or-two-steps-per-frame stutter).
            var track = TrackCatalog.Wedge();
            var previous = Race(RacePhase.Racing, 10f, RacerAt(track, 100f, 240f));
            var current = Race(RacePhase.Racing, 10f + 1f / 60f, RacerAt(track, 104f, 244f));

            var mid = SnapshotInterpolation.Blend(previous, current, .5f, track).Racers[0];
            Assert.That(mid.TotalDistance, Is.EqualTo(102f).Within(.001f));
            Assert.That(mid.Speed, Is.EqualTo(242f).Within(.001f));
            var expected = track.Sample(102f).Position;
            Assert.That(mid.Track.Position.X, Is.EqualTo(expected.X).Within(.001f));
            Assert.That(mid.Track.Position.Y, Is.EqualTo(expected.Y).Within(.001f));

            Assert.That(SnapshotInterpolation.Blend(previous, current, 0f, track)
                .Racers[0].TotalDistance, Is.EqualTo(100f).Within(.001f));
            Assert.That(SnapshotInterpolation.Blend(previous, current, 1f, track)
                .Racers[0].TotalDistance, Is.EqualTo(104f).Within(.001f));
        }

        [Test]
        public void BlendKeepsDiscreteStateFromTheCurrentStep()
        {
            // Laps, places and flags are the current step's truth even while the
            // drawn position still trails it — a counter may tick ~17 ms early,
            // a car may never be drawn in a stale state.
            var track = TrackCatalog.Wedge();
            var previous = Race(RacePhase.Racing, 10f, RacerAt(track, track.Length - 2f, 240f, laps: 0));
            var current = Race(RacePhase.Racing, 10f + 1f / 60f, RacerAt(track, track.Length + 2f, 240f, laps: 1));

            var mid = SnapshotInterpolation.Blend(previous, current, .25f, track).Racers[0];
            Assert.That(mid.CompletedLaps, Is.EqualTo(1));
            Assert.That(mid.TotalDistance, Is.EqualTo(track.Length - 1f).Within(.001f));
        }

        [Test]
        public void PhaseChangesAndDistanceResetsSnapToTheCurrentState()
        {
            var track = TrackCatalog.Wedge();
            // A new race resets distances to zero; blending across the phase
            // change would sweep the car backwards through the whole course.
            var finished = Race(RacePhase.Finished, 90f, RacerAt(track, 3000f, 0f));
            var restarted = Race(RacePhase.Countdown, 0f, RacerAt(track, 0f, 0f));
            var blended = SnapshotInterpolation.Blend(finished, restarted, .5f, track);
            Assert.That(blended.Phase, Is.EqualTo(RacePhase.Countdown));
            Assert.That(blended.Racers[0].TotalDistance, Is.Zero);

            // Same guard when only the distance regresses within a phase.
            var before = Race(RacePhase.Racing, 10f, RacerAt(track, 500f, 200f));
            var reset = Race(RacePhase.Racing, 10f, RacerAt(track, 100f, 200f));
            Assert.That(SnapshotInterpolation.Blend(before, reset, .5f, track)
                .Racers[0].TotalDistance, Is.EqualTo(100f));
        }

        [Test]
        public void PitHandOffsNeverInterpolateAcrossTheTeleport()
        {
            var track = TrackCatalog.Wedge();
            // OnTrack → Entering moves the car onto the lane spline; the rejoin
            // jumps TotalDistance forward. Both are phase changes: snap.
            var onTrack = Race(RacePhase.Racing, 10f, RacerAt(track, 100f, 200f));
            var entering = Race(RacePhase.Racing, 10f, RacerAt(track, 100f, 60f,
                pit: new RacerPitSnapshot(PitService.None, PitPhase.Entering, 0f, 0, false, .05f)));
            var snapped = SnapshotInterpolation.Blend(onTrack, entering, .5f, track).Racers[0];
            Assert.That(snapped.Pit.Phase, Is.EqualTo(PitPhase.Entering));
            Assert.That(snapped.Pit.PhaseProgress, Is.EqualTo(.05f).Within(.0001f));
            Assert.That(snapped.Speed, Is.EqualTo(60f));

            // Within one pit phase the lane progress interpolates like distance,
            // but a progress reset (service complete, phase turnover) never
            // blends backwards.
            var early = Race(RacePhase.Racing, 10f, RacerAt(track, 100f, 60f,
                pit: new RacerPitSnapshot(PitService.Tires, PitPhase.Entering, .8f, 0, false, .2f)));
            var late = Race(RacePhase.Racing, 10f, RacerAt(track, 100f, 60f,
                pit: new RacerPitSnapshot(PitService.Tires, PitPhase.Entering, .1f, 0, false, .3f)));
            var blended = SnapshotInterpolation.Blend(early, late, .5f, track).Racers[0];
            Assert.That(blended.Pit.PhaseProgress, Is.EqualTo(.25f).Within(.0001f));
            Assert.That(blended.Pit.ServiceProgress, Is.EqualTo(.1f).Within(.0001f));
        }

        private static RacerSnapshot RacerAt(TrackDefinition track, float distance, float speed,
            int laps = 0, RacerPitSnapshot pit = default) =>
            new RacerSnapshot(PlayerId.Player1, speed, distance, laps, 1, false, -1f,
                track.Sample(distance), 0f, false, 0f, 0, Condition(0f, 0f), pit);

        private static RaceSnapshot Race(RacePhase phase, float elapsed, params RacerSnapshot[] racers) =>
            new RaceSnapshot(phase, 0f, elapsed, racers, 0f, false);

        private static float Span(PitLanePresentationLayout layout, float from, float to)
        {
            float covered = 0f;
            CarPresentationPose prior = PitLanePresentationMapper.ExitPose(PlayerId.Player1, from, false, layout);
            for (float progress = from + .01f; progress <= to + .0001f; progress += .01f)
            {
                var next = PitLanePresentationMapper.ExitPose(PlayerId.Player1, progress, false, layout);
                covered += Distance(prior.Position, next.Position);
                prior = next;
            }
            return covered;
        }

        // The directed layout mirrors the real wiring: both track hand-offs run
        // eastward, deliberately misaligned with the lane's own last chords.
        private static PitLanePresentationLayout DirectedLayout() => new PitLanePresentationLayout(
            new Vec2(5f, 5f), new Vec2(10f, 10f), new Vec2(20f, 10f),
            new Vec2(30f, 10f), new Vec2(40f, 10f), new Vec2(38f, 8f),
            new Vec2(42f, 5f), new Vec2(1f, 0f), new Vec2(1f, 0f));

        private static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
        private static float Cos(float degrees) => (float)System.Math.Cos(degrees * System.Math.PI / 180.0);
        private static float Distance(Vec2 a, Vec2 b) =>
            (float)System.Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

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
