using System;
using System.Collections.Generic;
using BoardRacing.Domain;
using BoardRacing.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace BoardRacing.Tests
{
    public sealed class WireframePresentationTests
    {
        [Test]
        public void ReferenceLayoutDefinesDiagonalSeatsAndProtectedCentralRaceSpace()
        {
            RaceLayout layout = Layout();

            Assert.That(layout.PlayerOne.CornerBounds, Is.EqualTo(new Rect(960f, 540f, 960f, 540f)));
            Assert.That(layout.PlayerTwo.CornerBounds, Is.EqualTo(new Rect(0f, 0f, 960f, 540f)));
            Assert.That(layout.PlayerOne.RotationDegrees, Is.Zero);
            Assert.That(layout.PlayerTwo.RotationDegrees, Is.EqualTo(180f));
            AssertContained(layout.PlayerOne.CornerBounds, layout.PlayerOne.SafeContentBounds);
            AssertContained(layout.PlayerTwo.CornerBounds, layout.PlayerTwo.SafeContentBounds);
            AssertContained(layout.Canvas, layout.SharedRaceBounds);
            AssertContained(layout.SharedRaceBounds, layout.CenterOverlayBounds);
        }

        [Test]
        public void CornerControllerGeometryMatchesTheMeasuredSeatComponentAndMirrors()
        {
            RaceLayout layout = Layout();

            AssertMirrored(layout.PlayerOne.CornerBounds, layout.PlayerTwo.CornerBounds);
            AssertMirrored(layout.PlayerOne.SafeContentBounds, layout.PlayerTwo.SafeContentBounds);
            CornerControllerLayout one = layout.PlayerOne.Controller;
            CornerControllerLayout two = layout.PlayerTwo.Controller;
            // Measured from frame 40:23, right-variant component 44:124 (issue #77 Round 2).
            Assert.That(one.ArcCenter, Is.EqualTo(new Vector2(1863f, 1025f)));
            Assert.That(one.ShipWellCenter, Is.EqualTo(new Vector2(1787f, 938f)));
            Assert.That(two.ArcCenter, Is.EqualTo(new Vector2(57f, 55f)));
            Assert.That(two.ShipWellCenter, Is.EqualTo(new Vector2(133f, 142f)));
            Assert.That(one.ThrottleRadius, Is.EqualTo(250f));
            Assert.That(one.SectorSweepDegrees, Is.EqualTo(32f));
            Assert.That(one.SectorAngle(ThrottleStep.Brake), Is.EqualTo(190f));
            Assert.That(one.SectorAngle(ThrottleStep.Drive), Is.EqualTo(226f));
            Assert.That(one.SectorAngle(ThrottleStep.Boost), Is.EqualTo(260f));
            foreach (ThrottleStep step in new[]
                { ThrottleStep.Brake, ThrottleStep.Drive, ThrottleStep.Boost })
            {
                // Angles are shared; the opposite seat's 180° comes from RotationDegrees.
                Assert.That(two.SectorAngle(step), Is.EqualTo(one.SectorAngle(step)));
                AssertMirrored(one.SectorLabel(step).Bounds, two.SectorLabel(step).Bounds);
                Assert.That(two.SectorLabel(step).RotationDegrees,
                    Is.EqualTo(one.SectorLabel(step).RotationDegrees));
                // Sector labels ride the arc band.
                float distance = Vector2.Distance(one.SectorLabel(step).Bounds.center, one.ArcCenter);
                Assert.That(distance, Is.InRange(one.ThrottleRadius - 62f, one.ThrottleRadius));
            }
            AssertMirrored(one.TiresLabel.Bounds, two.TiresLabel.Bounds);
            AssertMirrored(one.FuelLabel.Bounds, two.FuelLabel.Bounds);
            AssertMirrored(one.CallPitLabel.Bounds, two.CallPitLabel.Bounds);
            foreach (PlayerLayout player in new[] { layout.PlayerOne, layout.PlayerTwo })
            {
                CornerControllerLayout controller = player.Controller;
                // Dial labels ride each dial's rim; Call Pit's label sits inside its circle.
                Assert.That(Vector2.Distance(controller.TiresLabel.Bounds.center,
                    player.Tires.center), Is.LessThanOrEqualTo(70f));
                Assert.That(Vector2.Distance(controller.FuelLabel.Bounds.center,
                    player.Fuel.center), Is.LessThanOrEqualTo(70f));
                Assert.That(controller.CallPitLabel.Bounds.center, Is.EqualTo(player.CallPit.center));
                // The Ship's rotational footprint clears both dial rings.
                foreach (Rect dial in new[] { player.Tires, player.Fuel })
                    Assert.That(Vector2.Distance(controller.ShipWellCenter, dial.center),
                        Is.GreaterThan(controller.ShipWellRadius + controller.DialRadius));
                foreach (RotatedLabel label in new[] { controller.BrakeLabel, controller.DriveLabel,
                    controller.BoostLabel, controller.TiresLabel, controller.FuelLabel,
                    controller.CallPitLabel })
                    AssertContained(layout.Canvas, label.Bounds);
            }
        }

        [Test]
        public void StableRacingUsesSpatialThrottleGuideAndOneCompactInstruction()
        {
            RaceUiModel model = Build(RacePhase.Racing, Racer(PlayerId.Player1),
                Racer(PlayerId.Player2), Controls());

            Assert.That(model.PlayerOne.Status, Is.EqualTo("LAP 3 / 5 · 1ST · STOP REQUIRED"));
            Assert.That(model.PlayerOne.Status, Does.Not.Contain("DRIVE"));
            Assert.That(model.PlayerOne.PrimaryInstruction,
                Is.EqualTo("DRIVE WITH SHIP · ROBOT CAN CALL PIT"));
            AssertSingleInstruction(model.PlayerOne);
        }

        [Test]
        public void ActionTargetsExactlyMatchRuntimeCentersSizesAndMirrors()
        {
            RaceLayout layout = Layout();

            Assert.That(layout.PlayerOne.CallPit, Is.EqualTo(new Rect(1782f, 632f, 100f, 100f)));
            Assert.That(layout.PlayerOne.Tires, Is.EqualTo(new Rect(1642f, 709f, 100f, 100f)));
            Assert.That(layout.PlayerOne.Fuel, Is.EqualTo(new Rect(1540f, 818f, 100f, 100f)));
            Assert.That(layout.PlayerTwo.CallPit, Is.EqualTo(new Rect(38f, 348f, 100f, 100f)));
            Assert.That(layout.PlayerTwo.Tires, Is.EqualTo(new Rect(178f, 271f, 100f, 100f)));
            Assert.That(layout.PlayerTwo.Fuel, Is.EqualTo(new Rect(280f, 162f, 100f, 100f)));
            AssertMirrored(layout.PlayerOne.CallPit, layout.PlayerTwo.CallPit);
            AssertMirrored(layout.PlayerOne.Tires, layout.PlayerTwo.Tires);
            AssertMirrored(layout.PlayerOne.Fuel, layout.PlayerTwo.Fuel);
        }

        [Test]
        public void ShortEdgeTargetsStayOnBoardInsideEachPlayersRegionAndApart()
        {
            RaceLayout layout = Layout();

            foreach (PlayerLayout player in new[] { layout.PlayerOne, layout.PlayerTwo })
            {
                foreach (Rect zone in new[] { player.CallPit, player.Tires, player.Fuel })
                {
                    AssertContained(layout.Canvas, zone);
                    AssertContained(player.CornerBounds.y > 0f
                        ? new Rect(0f, 540f, 1920f, 540f) : new Rect(0f, 0f, 1920f, 540f), zone);
                }
                Assert.That(player.Tires.Overlaps(player.Fuel), Is.False);
            }
            // Call Pit hugs each seat's short board edge.
            Assert.That(layout.PlayerOne.CallPit.xMax, Is.GreaterThanOrEqualTo(1880f));
            Assert.That(layout.PlayerTwo.CallPit.xMin, Is.LessThanOrEqualTo(40f));
        }

        [Test]
        public void LayoutRejectsInvalidTargetGeometry()
        {
            ServiceTargets playerOne = PlayerOneTargets();
            ServiceTargets playerTwo = PlayerTwoTargets();
            Assert.Throws<ArgumentException>(() => RaceLayout.Create(playerOne, playerTwo,
                Vector2.zero));
            var overlapping = new ServiceTargets(new Vector2(1832f, 398f),
                new Vector2(1692f, 321f), new Vector2(1650f, 321f));
            Assert.Throws<ArgumentException>(() => RaceLayout.Create(overlapping, playerTwo,
                new Vector2(50f, 50f)));
            Assert.Throws<ArgumentException>(() => RaceLayout.Create(playerOne, overlapping,
                new Vector2(50f, 50f)));
        }

        [Test]
        public void MissingShipOutranksGridReadiness()
        {
            RaceUiModel model = Build(RacePhase.Grid, Racer(PlayerId.Player1), Racer(PlayerId.Player2),
                Controls(playerOneCarPresent: false));

            Assert.That(model.PlayerOne.PrimaryInstructionKind, Is.EqualTo(PlayerUiInstructionKind.PlaceShip));
            Assert.That(model.PlayerTwo.PrimaryInstructionKind, Is.EqualTo(PlayerUiInstructionKind.GridReady));
        }

        [Test]
        public void ActiveServiceOutranksCriticalConditionAndNamesOneAction()
        {
            RacerSnapshot playerOne = Racer(PlayerId.Player1, PitPhase.InService, fuelEmpty: true,
                service: PitService.Tires, serviceProgress: .62f);
            RaceUiModel model = Build(RacePhase.Racing, playerOne, Racer(PlayerId.Player2),
                Controls(playerOneCrewPresent: false));

            Assert.That(model.PlayerOne.Status, Is.EqualTo("CAR PARKED · TIRES · 62%"));
            Assert.That(model.PlayerOne.PrimaryInstructionKind,
                Is.EqualTo(PlayerUiInstructionKind.PlaceRobotForService));
            Assert.That(model.PlayerOne.PrimaryInstruction, Does.Not.Contain("FUEL EMPTY"));
            AssertSingleInstruction(model.PlayerOne);
        }

        [Test]
        public void ActivePitCallOutranksRecoveryAndCriticalConditions()
        {
            RacerSnapshot playerOne = Racer(PlayerId.Player1, recovery: .8f, fuelEmpty: true);
            var crew = new Dictionary<PlayerId, CrewStrategyOutput>
            {
                [PlayerId.Player1] = new CrewStrategyOutput(PitService.None, false, PitCallState.Holding,
                    new PitActionResult(PitActionState.Holding, .4f, false), default)
            };
            RaceUiModel model = Build(RacePhase.Racing, playerOne, Racer(PlayerId.Player2), Controls(), crew);

            Assert.That(model.PlayerOne.PrimaryInstructionKind, Is.EqualTo(PlayerUiInstructionKind.HoldPitCall));
            Assert.That(model.PlayerOne.PrimaryInstruction, Does.Contain("40%"));
        }

        [Test]
        public void RecoveryOutranksCriticalConditionsDuringRacing()
        {
            RacerSnapshot playerOne = Racer(PlayerId.Player1, recovery: .8f, fuelEmpty: true);
            RaceUiModel model = Build(RacePhase.Racing, playerOne, Racer(PlayerId.Player2), Controls());

            Assert.That(model.PlayerOne.PrimaryInstructionKind,
                Is.EqualTo(PlayerUiInstructionKind.CornerRecovery));
        }

        [Test]
        public void FuelWinsDeterministicTieWhenBothConditionsHaveSameSeverity()
        {
            RacerSnapshot playerOne = Racer(PlayerId.Player1, fuelEmpty: true, tireCritical: true);
            RaceUiModel model = Build(RacePhase.Racing, playerOne, Racer(PlayerId.Player2), Controls());

            Assert.That(model.PlayerOne.PrimaryInstructionKind, Is.EqualTo(PlayerUiInstructionKind.FuelEmpty));
            Assert.That(model.PlayerOne.Condition.TireLevel, Is.EqualTo(ConditionVisualLevel.Critical));
        }

        [Test]
        public void SplitFinishAndFinalRematchNeverLeakLiveRacingGuidanceToFinishedPlayer()
        {
            RacerSnapshot finished = Racer(PlayerId.Player1, finished: true, place: 1);
            RaceUiModel split = Build(RacePhase.Racing, finished, Racer(PlayerId.Player2, place: 2), Controls());
            Assert.That(split.CenterMessageKind, Is.EqualTo(CenterMessageKind.SplitFinish));
            Assert.That(split.PlayerOne.PrimaryInstructionKind,
                Is.EqualTo(PlayerUiInstructionKind.WaitForOtherRacer));
            Assert.That(split.PlayerTwo.PrimaryInstructionKind, Is.EqualTo(PlayerUiInstructionKind.DriveAndPit));

            RacerSnapshot second = Racer(PlayerId.Player2, finished: true, place: 2);
            RaceUiModel results = Build(RacePhase.Finished, finished, second, Controls());
            Assert.That(results.CenterMessageKind, Is.EqualTo(CenterMessageKind.Winner));
            Assert.That(results.PlayerOne.PrimaryInstructionKind, Is.EqualTo(PlayerUiInstructionKind.RematchHold));
            Assert.That(results.PlayerTwo.PrimaryInstructionKind, Is.EqualTo(results.PlayerOne.PrimaryInstructionKind));

            RaceUiModel release = Build(RacePhase.Finished, finished, second, Controls(),
                awaitingRematchRelease: true);
            Assert.That(release.PlayerOne.PrimaryInstructionKind,
                Is.EqualTo(PlayerUiInstructionKind.RematchRelease));
            Assert.That(release.PlayerTwo.PrimaryInstructionKind,
                Is.EqualTo(PlayerUiInstructionKind.RematchRelease));
        }

        [Test]
        public void PausedRaceShowsTheOverlayAndPerSeatPauseChoices()
        {
            RaceUiModel model = Build(RacePhase.Paused, Racer(PlayerId.Player1),
                Racer(PlayerId.Player2, place: 2), Controls(playerOneCarPresent: false));
            Assert.That(model.CenterMessageKind, Is.EqualTo(CenterMessageKind.Paused));
            Assert.That(model.PlayerOne.PrimaryInstructionKind,
                Is.EqualTo(PlayerUiInstructionKind.PausedPlaceShip));
            Assert.That(model.PlayerTwo.PrimaryInstructionKind,
                Is.EqualTo(PlayerUiInstructionKind.PausedShipReady));

            // A finished racer sees pause choices instead of its usual wait state.
            RaceUiModel finished = Build(RacePhase.Paused, Racer(PlayerId.Player1, finished: true),
                Racer(PlayerId.Player2, place: 2), Controls());
            Assert.That(finished.CenterMessageKind, Is.EqualTo(CenterMessageKind.Paused));
            Assert.That(finished.PlayerOne.PrimaryInstructionKind,
                Is.EqualTo(PlayerUiInstructionKind.PausedShipReady));
        }

        [Test]
        public void DeterministicPreviewFixturesCoverTheRequiredReviewStates()
        {
            var track = TrackCatalog.Wedge();
            foreach (RaceUiPreviewScenario scenario in Enum.GetValues(typeof(RaceUiPreviewScenario)))
            {
                RaceUiPreviewFrame frame = RaceUiPreviewFixtures.Create(scenario, track,
                    ConditionRules.Defaults, 5);

                Assert.That(frame.Race.Racers.Count, Is.EqualTo(2), scenario.ToString());
                AssertSingleInstruction(frame.Ui.PlayerOne);
                AssertSingleInstruction(frame.Ui.PlayerTwo);
            }

            Assert.That(RaceUiPreviewFixtures.Create(RaceUiPreviewScenario.Warning, track,
                    ConditionRules.Defaults, 5).Ui.PlayerOne.PrimaryInstructionKind,
                Is.EqualTo(PlayerUiInstructionKind.FuelLow));
            Assert.That(RaceUiPreviewFixtures.Create(RaceUiPreviewScenario.Critical, track,
                    ConditionRules.Defaults, 5).Ui.PlayerOne.PrimaryInstructionKind,
                Is.EqualTo(PlayerUiInstructionKind.FuelEmpty));
            Assert.That(RaceUiPreviewFixtures.Create(RaceUiPreviewScenario.InService, track,
                    ConditionRules.Defaults, 5).Ui.PlayerOne.PrimaryInstructionKind,
                Is.EqualTo(PlayerUiInstructionKind.StirService));
            Assert.That(RaceUiPreviewFixtures.Create(RaceUiPreviewScenario.SplitFinish, track,
                    ConditionRules.Defaults, 5).Ui.CenterMessageKind,
                Is.EqualTo(CenterMessageKind.SplitFinish));
            Assert.That(RaceUiPreviewFixtures.Create(RaceUiPreviewScenario.Results, track,
                    ConditionRules.Defaults, 5).Ui.CenterMessageKind,
                Is.EqualTo(CenterMessageKind.Winner));
            Assert.That(RaceUiPreviewFixtures.Create(RaceUiPreviewScenario.RematchRelease, track,
                    ConditionRules.Defaults, 5).Ui.PlayerOne.PrimaryInstructionKind,
                Is.EqualTo(PlayerUiInstructionKind.RematchRelease));
            RaceUiPreviewFrame paused = RaceUiPreviewFixtures.Create(RaceUiPreviewScenario.Paused,
                track, ConditionRules.Defaults, 5);
            Assert.That(paused.Ui.CenterMessageKind, Is.EqualTo(CenterMessageKind.Paused));
            Assert.That(paused.Ui.PlayerOne.PrimaryInstructionKind,
                Is.EqualTo(PlayerUiInstructionKind.PausedPlaceShip));
            Assert.That(paused.Ui.PlayerTwo.PrimaryInstructionKind,
                Is.EqualTo(PlayerUiInstructionKind.PausedShipReady));
        }

        // Contract geometry from docs/gameplay/wireframe-ui.md (issue #77 Round 2, measured
        // from frame 40:23 component 44:124).
        private static ServiceTargets PlayerOneTargets() => new ServiceTargets(
            new Vector2(1832f, 398f), new Vector2(1692f, 321f), new Vector2(1590f, 212f));

        private static ServiceTargets PlayerTwoTargets() => new ServiceTargets(
            new Vector2(88f, 682f), new Vector2(228f, 759f), new Vector2(330f, 868f));

        private static RaceLayout Layout() => RaceLayout.Create(PlayerOneTargets(),
            PlayerTwoTargets(), new Vector2(50f, 50f));

        private static RaceUiModel Build(RacePhase phase, RacerSnapshot playerOne, RacerSnapshot playerTwo,
            IReadOnlyList<PlayerControlSnapshot> controls,
            IReadOnlyDictionary<PlayerId, CrewStrategyOutput> crew = null,
            bool awaitingRematchRelease = false)
        {
            var race = new RaceSnapshot(phase, phase == RacePhase.Countdown ? 2f : 0f,
                phase == RacePhase.Racing ? 2f : 0f, new[] { playerOne, playerTwo },
                awaitingRematchRelease ? 1f : 0f, awaitingRematchRelease);
            return RaceUiModelBuilder.Build(race, controls,
                crew ?? new Dictionary<PlayerId, CrewStrategyOutput>(), ConditionRules.Defaults, 5);
        }

        private static IReadOnlyList<PlayerControlSnapshot> Controls(bool playerOneCarPresent = true,
            bool playerOneCrewPresent = true)
        {
            PieceState Present(int contact) => new PieceState(true, false, contact, new Vec2(), 0f);
            return new[]
            {
                new PlayerControlSnapshot(PlayerId.Player1, ThrottleStep.Drive,
                    playerOneCarPresent ? Present(1) : PieceState.Missing,
                    playerOneCrewPresent ? Present(2) : PieceState.Missing, InputWarning.None),
                new PlayerControlSnapshot(PlayerId.Player2, ThrottleStep.Boost, Present(3), Present(4),
                    InputWarning.None)
            };
        }

        private static RacerSnapshot Racer(PlayerId id, PitPhase pitPhase = PitPhase.OnTrack,
            float recovery = 0f, bool fuelEmpty = false, bool tireCritical = false,
            PitService service = PitService.None, float serviceProgress = 0f,
            bool finished = false, int place = 1, float fuelUsed = .2f, float tireWear = .2f)
        {
            var track = new TrackSample(new Vec2(960f, 540f), new Vec2(1f, 0f), 0,
                TrackSectionKind.Straight, float.PositiveInfinity);
            var condition = new RacerConditionSnapshot(fuelEmpty ? .9f : fuelUsed,
                tireCritical ? .9f : tireWear, fuelEmpty, tireCritical);
            var pit = new RacerPitSnapshot(service, pitPhase, serviceProgress, finished ? 1 : 0,
                finished, 0f);
            return new RacerSnapshot(id, 100f, 100f, 2, place, finished, finished ? 20f : -1f,
                track, 0f, false, recovery, 0, condition, pit);
        }

        private static void AssertSingleInstruction(PlayerUiModel model)
        {
            Assert.That(model.PrimaryInstruction, Is.Not.Null.And.Not.Empty);
            Assert.That(model.PrimaryInstruction, Does.Not.Contain("\n"));
        }

        private static void AssertContained(Rect outer, Rect inner)
        {
            Assert.That(outer.Contains(inner.min), Is.True);
            Assert.That(outer.Contains(inner.max - Vector2.one * .001f), Is.True);
        }

        private static void AssertMirrored(Rect bottom, Rect top)
        {
            Assert.That(top.x, Is.EqualTo(RaceLayout.ReferenceWidth - bottom.xMax).Within(.001f));
            Assert.That(top.y, Is.EqualTo(RaceLayout.ReferenceHeight - bottom.yMax).Within(.001f));
            Assert.That(top.size, Is.EqualTo(bottom.size));
        }
    }
}
