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
            Assert.That(p1MidEntry.Position.X, Is.InRange(layout.PitLine.X, layout.Box(PlayerId.Player1).X));
            AssertPosition(p1Box, layout.Box(PlayerId.Player1));
            foreach (PlayerId player in new[]
                { PlayerId.Player1, PlayerId.Player2, PlayerId.Player3, PlayerId.Player4 })
            {
                CarPresentationPose parked = Pose(Racer(player, PitPhase.InService), layout);
                CarPresentationPose midExit = Pose(Racer(player, PitPhase.Exiting, .5f), layout);
                AssertPosition(parked, layout.Box(player));
                Assert.That(midExit.Position.X, Is.Not.EqualTo(parked.Position.X).Within(.001f),
                    player.ToString());
            }
        }

        [Test]
        public void CatalogRoutesUseTheSharedLaneAndOnlyTheOwnedStallBranch()
        {
            foreach (CourseDefinition course in CourseCatalog.All())
            {
                PitLanePresentationLayout layout =
                    PitLanePresentationLayout.ForCourse(course);
                foreach (PlayerId player in new[]
                    { PlayerId.Player1, PlayerId.Player2, PlayerId.Player3, PlayerId.Player4 })
                {
                    int stall = (int)player - 1;
                    Vec2[] entry = PitLanePresentationMapper.EntryRoute(player, layout);
                    Vec2[] exit = PitLanePresentationMapper.ExitRoute(player, layout);

                    Assert.That(entry[entry.Length - 2],
                        Is.EqualTo(layout.EntryAnchor(player)), course.Name + " entry anchor");
                    Assert.That(entry[entry.Length - 1],
                        Is.EqualTo(layout.Box(player)), course.Name + " entry box");
                    Assert.That(exit[0], Is.EqualTo(layout.Box(player)),
                        course.Name + " exit box");
                    Assert.That(exit[1], Is.EqualTo(layout.ExitAnchor(player)),
                        course.Name + " exit anchor");

                    for (int other = 0; other < layout.Boxes.Count; other++)
                    {
                        if (other == stall) continue;
                        Assert.That(entry, Has.None.EqualTo(layout.Boxes[other]),
                            course.Name + " entry traverses another stall");
                        Assert.That(exit, Has.None.EqualTo(layout.Boxes[other]),
                            course.Name + " exit traverses another stall");
                    }
                }
            }
        }

        [Test]
        public void ServiceCurvesRunFromDepartureThroughTheParkedApexToReturn()
        {
            foreach (CourseDefinition course in CourseCatalog.All())
            {
                PitLanePresentationLayout layout =
                    PitLanePresentationLayout.ForCourse(course);
                foreach (PlayerId player in new[]
                    { PlayerId.Player1, PlayerId.Player2, PlayerId.Player3, PlayerId.Player4 })
                {
                    Vec2[] curve = PitLanePresentationMapper.ServiceCurveSamples(player, layout);
                    Assert.That(curve[0], Is.EqualTo(layout.EntryAnchor(player)),
                        course.Name + " departure");
                    Assert.That(curve[curve.Length / 2], Is.EqualTo(layout.Box(player)),
                        course.Name + " service apex");
                    Assert.That(curve[curve.Length - 1], Is.EqualTo(layout.ExitAnchor(player)),
                        course.Name + " return");

                    Vec2 before = curve[curve.Length / 2 - 1];
                    Vec2 after = curve[curve.Length / 2 + 1];
                    Vec2 chord = Normalize(new Vec2(after.X - before.X, after.Y - before.Y));
                    Assert.That(Dot(chord, layout.ParkedHeading(player)),
                        Is.GreaterThan(Cos(8f)), course.Name + " parked tangent");
                }
            }
        }

        [Test]
        public void ServiceCurveNeverPointsBackAlongThePitRowAtTheParkedApex()
        {
            foreach (CourseDefinition course in CourseCatalog.All())
            {
                PitLanePresentationLayout layout =
                    PitLanePresentationLayout.ForCourse(course);
                foreach (PlayerId player in new[]
                    { PlayerId.Player1, PlayerId.Player2, PlayerId.Player3, PlayerId.Player4 })
                {
                    Vec2[] curve = PitLanePresentationMapper.ServiceCurveSamples(player, layout);
                    Vec2 rowHeading = layout.ParkedHeading(player);
                    int apex = curve.Length / 2;
                    for (int i = 1; i < curve.Length; i++)
                    {
                        Vec2 movement = new Vec2(
                            curve[i].X - curve[i - 1].X,
                            curve[i].Y - curve[i - 1].Y);
                        Assert.That(Dot(movement, rowHeading), Is.GreaterThanOrEqualTo(-.0001f),
                            $"{course.Name} {player} reverses beside the stall at sample {i}");
                        if (i <= apex)
                            Assert.That(Cross(rowHeading, movement),
                                Is.GreaterThanOrEqualTo(-.0001f),
                                $"{course.Name} {player} steers away before parking at sample {i}");
                        else
                            Assert.That(Cross(rowHeading, movement),
                                Is.LessThanOrEqualTo(.0001f),
                                $"{course.Name} {player} steers back into the stall after release at sample {i}");
                    }

                    for (int step = 0; step <= 100; step++)
                    {
                        float entryProgress = .9f + step / 1000f;
                        CarPresentationPose entry = PitLanePresentationMapper.EntryPose(
                            player, entryProgress, false, layout);
                        Assert.That(Cross(rowHeading, entry.Tangent),
                            Is.GreaterThanOrEqualTo(-.001f),
                            $"{course.Name} {player} faces away before parking at {entryProgress:0.000}");

                        float exitProgress = step / 1000f;
                        CarPresentationPose exit = PitLanePresentationMapper.ExitPose(
                            player, exitProgress, false, layout);
                        Assert.That(Cross(rowHeading, exit.Tangent),
                            Is.LessThanOrEqualTo(.001f),
                            $"{course.Name} {player} faces back into the stall after release at {exitProgress:0.000}");
                    }
                }
            }
        }

        [Test]
        public void MovingPosesMatchTheAuthoredParkedHeadingAtBothBayHandOffs()
        {
            foreach (CourseDefinition course in CourseCatalog.All())
            {
                PitLanePresentationLayout layout =
                    PitLanePresentationLayout.ForCourse(course);
                foreach (PlayerId player in new[]
                    { PlayerId.Player1, PlayerId.Player2, PlayerId.Player3, PlayerId.Player4 })
                {
                    Vec2 parked = layout.ParkedHeading(player);
                    CarPresentationPose arrival = PitLanePresentationMapper.EntryPose(
                        player, 1f, false, layout);
                    CarPresentationPose release = PitLanePresentationMapper.ExitPose(
                        player, 0f, false, layout);

                    Assert.That(Dot(arrival.Tangent, parked), Is.GreaterThan(Cos(.5f)),
                        $"{course.Name} {player} arrival heading snaps at the box");
                    Assert.That(Dot(release.Tangent, parked), Is.GreaterThan(Cos(.5f)),
                        $"{course.Name} {player} release heading snaps at the box");
                }
            }
        }

        [Test]
        public void CarHeadingTurnsContinuouslyAcrossEveryServiceBranchSeam()
        {
            const float progressStep = .002f;
            foreach (CourseDefinition course in CourseCatalog.All())
            {
                PitLanePresentationLayout layout =
                    PitLanePresentationLayout.ForCourse(course);
                foreach (PlayerId player in new[]
                    { PlayerId.Player1, PlayerId.Player2, PlayerId.Player3, PlayerId.Player4 })
                {
                    CarPresentationPose priorEntry = PitLanePresentationMapper.EntryPose(
                        player, 0f, false, layout);
                    CarPresentationPose priorExit = PitLanePresentationMapper.ExitPose(
                        player, 0f, false, layout);
                    for (float progress = progressStep;
                         progress <= 1f + .0001f; progress += progressStep)
                    {
                        CarPresentationPose entry = PitLanePresentationMapper.EntryPose(
                            player, progress, false, layout);
                        CarPresentationPose exit = PitLanePresentationMapper.ExitPose(
                            player, progress, false, layout);
                        Assert.That(Dot(priorEntry.Tangent, entry.Tangent),
                            Is.GreaterThan(Cos(6f)),
                            $"{course.Name} {player} entry heading snaps near {progress:0.000}");
                        Assert.That(Dot(priorExit.Tangent, exit.Tangent),
                            Is.GreaterThan(Cos(6f)),
                            $"{course.Name} {player} exit heading snaps near {progress:0.000}");
                        priorEntry = entry;
                        priorExit = exit;
                    }
                }
            }
        }

        [Test]
        public void QueuedCarsRenderAtTheirWorldSpaceHeadwayBehindThePitLine()
        {
            PitLanePresentationLayout layout =
                PitLanePresentationLayout.ForCourse(CourseCatalog.Wedge());
            var queued = Racer(PlayerId.Player2, PitPhase.Entering,
                traffic: PitTrafficState.Queued, queueOffset: 62f);
            CarPresentationPose pose = Pose(queued, layout);

            Vec2 heading = layout.EntryDirection;
            AssertPosition(pose, new Vec2(
                layout.PitLine.X - heading.X * 62f,
                layout.PitLine.Y - heading.Y * 62f));
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

            AssertPosition(undecided, layout.Box(PlayerId.Player1));
            AssertPosition(holdingTires, layout.Box(PlayerId.Player1));
            AssertPosition(switchedFuel, layout.Box(PlayerId.Player1));
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
        public void SharedPitExitIsAShortForwardMergeOntoTheRejoinPoint()
        {
            var layout = Layout();
            var start = PitLanePresentationMapper.SharedMergePose(0f, layout);
            var mid = PitLanePresentationMapper.SharedMergePose(.5f, layout);
            var end = PitLanePresentationMapper.SharedMergePose(1f, layout);

            // Every point of the exit moves forward with the track — no doubling
            // back toward the start line.
            Assert.That(start.Tangent.X, Is.GreaterThan(0f));
            Assert.That(mid.Tangent.X, Is.GreaterThan(0f));
            Assert.That(end.Tangent.X, Is.GreaterThan(0f));
            Assert.That(mid.Position.X,
                Is.InRange(layout.LaneWaypoints[layout.LaneWaypoints.Count - 1].X,
                    layout.ExitRejoin.X));
            AssertPosition(end, layout.ExitRejoin);
        }

        [Test]
        public void SharedPitEntryHandsOffTangentToEveryAuthoredServiceRow()
        {
            // The surface widens this exact route. A heading discontinuity at
            // either handoff becomes a triangular shoulder tooth after the
            // ribbon is offset, most visibly on Fishhook's diagonal row.
            foreach (CourseDefinition course in CourseCatalog.All())
            {
                PitLanePresentationLayout layout =
                    PitLanePresentationLayout.ForCourse(course);
                Vec2 rowHeading = layout.ParkedHeading(PlayerId.Player1);
                CarPresentationPose entryEnd =
                    PitLanePresentationMapper.SharedEntryPose(1f, layout);

                Assert.That(Dot(entryEnd.Tangent, rowHeading),
                    Is.GreaterThan(Cos(5f)), course.Name + " entry-to-lane handoff");
            }
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

            CarPresentationPose prior =
                PitLanePresentationMapper.SharedMergePose(0f, layout);
            for (float progress = .005f; progress <= 1.0001f; progress += .005f)
            {
                var next = PitLanePresentationMapper.SharedMergePose(progress, layout);
                // A 0.005 progress step over-samples a real frame: the exit legs
                // run whole seconds at the crawl (issue #110), so a 60 fps frame
                // spans ≤ ~0.008 progress even on the shortest catalog leg. The
                // S-bend onto the track legitimately turns ~8 deg per chord, so
                // the bound guards against snaps, not curvature.
                Assert.That(Dot(prior.Tangent, next.Tangent), Is.GreaterThan(Cos(30f)),
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
        public void PitEntryNeverRecoilsAtTheCourseHandOff()
        {
            // A pinned endpoint can still form a small Catmull-Rom loop even
            // when its reported tangent points the right way. On hardware that
            // reads as the car jumping backward just after it leaves the course.
            foreach (CourseDefinition course in CourseCatalog.All())
            {
                PitLanePresentationLayout layout =
                    PitLanePresentationLayout.ForCourse(course);
                Vec2 courseHeading = Normalize(layout.EntryDirection);
                foreach (PlayerId playerId in new[]
                {
                    PlayerId.Player1, PlayerId.Player2,
                    PlayerId.Player3, PlayerId.Player4
                })
                {
                    Vec2 prior = PitLanePresentationMapper
                        .EntryPose(playerId, 0f, false, layout).Position;
                    for (int step = 1; step <= 100; step++)
                    {
                        float progress = step / 1000f;
                        Vec2 current = PitLanePresentationMapper
                            .EntryPose(playerId, progress, false, layout).Position;
                        Vec2 displacement = new Vec2(
                            current.X - prior.X, current.Y - prior.Y);
                        Assert.That(Dot(displacement, courseHeading),
                            Is.GreaterThanOrEqualTo(-.001f),
                            $"{course.Name} {playerId} recoils at {progress:0.000}");
                        prior = current;
                    }
                }
            }
        }

        [Test]
        public void LateralPositionCarriesAcrossPitHandOffThenSettlesOntoTheLane()
        {
            const float lateral = 12f;
            RacerSnapshot onTrack = RacerWithLateral(
                PitPhase.OnTrack, 0f, lateral);
            RacerSnapshot atHandOff = RacerWithLateral(
                PitPhase.Entering, 0f, lateral);
            RacerSnapshot halfway = RacerWithLateral(PitPhase.Entering,
                PitLanePresentationMapper.EntryLateralSettleProgress * .5f, lateral);
            RacerSnapshot settled = RacerWithLateral(PitPhase.Entering,
                PitLanePresentationMapper.EntryLateralSettleProgress, lateral);

            Assert.That(PitLanePresentationMapper.PresentedLateralOffset(onTrack),
                Is.EqualTo(lateral));
            Assert.That(PitLanePresentationMapper.PresentedLateralOffset(atHandOff),
                Is.EqualTo(lateral), "the phase boundary must not move the car");
            Assert.That(PitLanePresentationMapper.PresentedLateralOffset(halfway),
                Is.EqualTo(lateral * .5f).Within(.001f));
            Assert.That(PitLanePresentationMapper.PresentedLateralOffset(settled),
                Is.Zero.Within(.001f));
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

            // The drawn car creeps out of the box, runs the lane above the mean,
            // and lands on the track at the crawl (issue #110 feel review — a
            // settle-to-zero at the merge read as stop-and-go): the first tenth
            // covers well under its linear share, the middle well over, and the
            // last tenth roughly its linear share, matching the speed the
            // simulation resumes the car at.
            Assert.That(Span(layout, 0f, .1f), Is.LessThan(pathLength * .06f));
            Assert.That(Span(layout, .9f, 1f), Is.InRange(pathLength * .08f, pathLength * .13f));
            Assert.That(Span(layout, .45f, .55f), Is.GreaterThan(pathLength * .08f));
        }

        [Test]
        public void PitEntryMotionArrivesAtTheCrawlAndSettlesIntoTheBox()
        {
            // The entry mirror (issue #110 feel review — a start-from-zero at
            // the line read as the car stopping dead before entering): the
            // first tenth covers roughly its linear share, because the approach
            // braking delivers the car to the line already at the crawl; the
            // last tenth creeps into the box.
            var layout = DirectedLayout();
            float pathLength = 0f;
            CarPresentationPose prior = PitLanePresentationMapper.EntryPose(PlayerId.Player1, 0f, false, layout);
            for (float progress = .01f; progress <= 1.0001f; progress += .01f)
            {
                var next = PitLanePresentationMapper.EntryPose(PlayerId.Player1, progress, false, layout);
                pathLength += Distance(prior.Position, next.Position);
                prior = next;
            }

            Assert.That(EntrySpan(layout, 0f, .1f), Is.InRange(pathLength * .08f, pathLength * .13f));
            Assert.That(EntrySpan(layout, .9f, 1f), Is.LessThan(pathLength * .06f));
        }

        private static float EntrySpan(PitLanePresentationLayout layout, float from, float to)
        {
            float covered = 0f;
            CarPresentationPose prior = PitLanePresentationMapper.EntryPose(PlayerId.Player1, from, false, layout);
            for (float progress = from + .01f; progress <= to + .0001f; progress += .01f)
            {
                var next = PitLanePresentationMapper.EntryPose(PlayerId.Player1, progress, false, layout);
                covered += Distance(prior.Position, next.Position);
                prior = next;
            }
            return covered;
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

        [TestCase(PitPhase.Entering, PitPhase.InService)]
        [TestCase(PitPhase.Parking, PitPhase.Parked)]
        public void FinalStepIntoParkedPoseIsInterpolated(PitPhase moving, PitPhase stopped)
        {
            var track = TrackCatalog.Wedge();
            var before = Race(RacePhase.Racing, 10f, RacerAt(track, 100f, 0f,
                pit: new RacerPitSnapshot(PitService.None, moving, 0f, 0, false, .96f,
                    PitTrafficState.Moving)));
            var current = Race(RacePhase.Racing, 10f + 1f / 60f,
                RacerAt(track, 100f, 0f,
                    pit: new RacerPitSnapshot(PitService.None, stopped, 0f, 0, false)));

            RacerSnapshot blended = SnapshotInterpolation
                .Blend(before, current, .5f, track).Racers[0];

            Assert.That(blended.Pit.Phase, Is.EqualTo(moving));
            Assert.That(blended.Pit.PhaseProgress, Is.EqualTo(.98f).Within(.0001f));
        }

        [Test]
        public void FirstReleasedStepOutOfParkedPoseIsInterpolated()
        {
            var track = TrackCatalog.Wedge();
            var waiting = Race(RacePhase.Racing, 10f, RacerAt(track, 100f, 0f,
                pit: new RacerPitSnapshot(PitService.None, PitPhase.Exiting, 0f, 0,
                    false, 0f, PitTrafficState.WaitingToRelease)));
            var moving = Race(RacePhase.Racing, 10f + 1f / 60f,
                RacerAt(track, 100f, 0f,
                    pit: new RacerPitSnapshot(PitService.None, PitPhase.Exiting, 0f, 0,
                        false, .04f, PitTrafficState.Moving)));

            RacerSnapshot blended = SnapshotInterpolation
                .Blend(waiting, moving, .5f, track).Racers[0];

            Assert.That(blended.Pit.Phase, Is.EqualTo(PitPhase.Exiting));
            Assert.That(blended.Pit.TrafficState, Is.EqualTo(PitTrafficState.Moving));
            Assert.That(blended.Pit.PhaseProgress, Is.EqualTo(.02f).Within(.0001f));
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
        private static float Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;
        private static Vec2 Normalize(Vec2 value)
        {
            float length = (float)System.Math.Sqrt(value.X * value.X + value.Y * value.Y);
            return new Vec2(value.X / length, value.Y / length);
        }
        private static float Cos(float degrees) => (float)System.Math.Cos(degrees * System.Math.PI / 180.0);
        private static float Distance(Vec2 a, Vec2 b) =>
            (float)System.Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        private static RacerConditionSnapshot Condition(float fuelUsed, float wear, bool fuelPenalty = false) =>
            new RacerConditionSnapshot(fuelUsed, wear, fuelPenalty, false);

        private static RacerSnapshot Racer(PlayerId id, float fuelUsed, float wear) =>
            new RacerSnapshot(id, 0f, 0f, 0, 1, false, -1f, default, 0f, false, 0f, 0,
                Condition(fuelUsed, wear), default);

        private static RacerSnapshot Racer(PlayerId id, PitPhase phase, float phaseProgress = 0f,
            PitService service = PitService.None, float serviceProgress = 0f, bool finished = false,
            PitTrafficState traffic = PitTrafficState.None, float queueOffset = 0f) =>
            new RacerSnapshot(id, 0f, 100f, 1, 1, finished, finished ? 12f : -1f,
                new TrackSample(new Vec2(5f, 5f), new Vec2(1f, 0f), 0,
                    TrackSectionKind.Straight, float.PositiveInfinity), 0f, false, 0f, 0,
                Condition(0f, 0f), new RacerPitSnapshot(service, phase, serviceProgress,
                    finished ? 1 : 0, finished, phaseProgress, traffic, queueOffset));

        private static RacerSnapshot RacerWithLateral(PitPhase phase,
            float phaseProgress, float lateralOffset) =>
            new RacerSnapshot(PlayerId.Player1, 0f, 100f, 1, 1, false, -1f,
                new TrackSample(new Vec2(5f, 5f), new Vec2(1f, 0f), 0,
                    TrackSectionKind.Straight, float.PositiveInfinity),
                lateralOffset, false, 0f, 0, Condition(0f, 0f),
                new RacerPitSnapshot(PitService.None, phase, 0f,
                    0, false, phaseProgress));

        private static PitLanePresentationLayout Layout() => new PitLanePresentationLayout(
            new Vec2(5f, 5f), new Vec2(10f, 10f), new[]
            {
                new Vec2(20f, 10f), new Vec2(30f, 10f),
                new Vec2(40f, 10f), new Vec2(50f, 10f)
            },
            new Vec2(60f, 10f), new Vec2(58f, 8f), new Vec2(62f, 5f));

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
