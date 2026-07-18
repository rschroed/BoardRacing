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
        public void CornerControllerGeometryIsMirroredAndKeepsPlayerCopyOutOfActionTargets()
        {
            RaceLayout layout = Layout();

            AssertMirrored(layout.PlayerOne.CornerBounds, layout.PlayerTwo.CornerBounds);
            AssertMirrored(layout.PlayerOne.SafeContentBounds, layout.PlayerTwo.SafeContentBounds);
            Assert.That(layout.PlayerOne.Controller.Center,
                Is.EqualTo(new Vector2(RaceLayout.ReferenceWidth, RaceLayout.ReferenceHeight)));
            Assert.That(layout.PlayerTwo.Controller.Center, Is.EqualTo(Vector2.zero));
            AssertMirrored(layout.PlayerOne.Controller.IdentityBounds,
                layout.PlayerTwo.Controller.IdentityBounds);
            AssertMirrored(layout.PlayerOne.Controller.StateWordBounds,
                layout.PlayerTwo.Controller.StateWordBounds);
            AssertMirrored(layout.PlayerOne.Controller.StatusBounds,
                layout.PlayerTwo.Controller.StatusBounds);
            AssertMirrored(layout.PlayerOne.Controller.InstructionBounds,
                layout.PlayerTwo.Controller.InstructionBounds);
            AssertMirrored(layout.PlayerOne.Controller.HeatLabelBounds,
                layout.PlayerTwo.Controller.HeatLabelBounds);
            AssertMirrored(layout.PlayerOne.Controller.TiresLabelBounds,
                layout.PlayerTwo.Controller.TiresLabelBounds);
            AssertMirrored(layout.PlayerOne.Controller.BrakeBounds,
                layout.PlayerTwo.Controller.BrakeBounds);
            AssertMirrored(layout.PlayerOne.Controller.DriveBounds,
                layout.PlayerTwo.Controller.DriveBounds);
            AssertMirrored(layout.PlayerOne.Controller.BoostBounds,
                layout.PlayerTwo.Controller.BoostBounds);
            foreach (PlayerLayout player in new[] { layout.PlayerOne, layout.PlayerTwo })
            {
                AssertContained(player.SafeContentBounds, player.Controller.StateWordBounds);
                AssertContained(player.SafeContentBounds, player.Controller.StatusBounds);
                AssertContained(player.SafeContentBounds, player.Controller.InstructionBounds);
                foreach (Rect copy in new[] { player.Controller.StateWordBounds,
                    player.Controller.StatusBounds, player.Controller.InstructionBounds })
                {
                    Assert.That(copy.Overlaps(player.CallPit), Is.False);
                    Assert.That(copy.Overlaps(player.Tires), Is.False);
                    Assert.That(copy.Overlaps(player.Cooling), Is.False);
                }
                // Dial labels are the in-target copy; they must sit inside their own zone.
                AssertContained(player.Tires, player.Controller.TiresLabelBounds);
                AssertContained(player.Cooling, player.Controller.HeatLabelBounds);
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

            Assert.That(layout.PlayerOne.CallPit, Is.EqualTo(new Rect(1680f, 540f, 220f, 220f)));
            Assert.That(layout.PlayerOne.Tires, Is.EqualTo(new Rect(1510f, 660f, 220f, 220f)));
            Assert.That(layout.PlayerOne.Cooling, Is.EqualTo(new Rect(1260f, 830f, 220f, 220f)));
            Assert.That(layout.PlayerTwo.CallPit, Is.EqualTo(new Rect(20f, 320f, 220f, 220f)));
            Assert.That(layout.PlayerTwo.Tires, Is.EqualTo(new Rect(190f, 200f, 220f, 220f)));
            Assert.That(layout.PlayerTwo.Cooling, Is.EqualTo(new Rect(440f, 30f, 220f, 220f)));
            AssertMirrored(layout.PlayerOne.CallPit, layout.PlayerTwo.CallPit);
            AssertMirrored(layout.PlayerOne.Tires, layout.PlayerTwo.Tires);
            AssertMirrored(layout.PlayerOne.Cooling, layout.PlayerTwo.Cooling);
        }

        [Test]
        public void ShortEdgeTargetsStayOnBoardInsideEachPlayersRegionAndApart()
        {
            RaceLayout layout = Layout();

            foreach (PlayerLayout player in new[] { layout.PlayerOne, layout.PlayerTwo })
            {
                foreach (Rect zone in new[] { player.CallPit, player.Tires, player.Cooling })
                {
                    AssertContained(layout.Canvas, zone);
                    AssertContained(player.CornerBounds.y > 0f
                        ? new Rect(0f, 540f, 1920f, 540f) : new Rect(0f, 0f, 1920f, 540f), zone);
                }
                Assert.That(player.Tires.Overlaps(player.Cooling), Is.False);
            }
            // Call Pit hugs each seat's short board edge.
            Assert.That(layout.PlayerOne.CallPit.xMax, Is.GreaterThanOrEqualTo(1900f));
            Assert.That(layout.PlayerTwo.CallPit.xMin, Is.LessThanOrEqualTo(20f));
        }

        [Test]
        public void LayoutRejectsInvalidTargetGeometry()
        {
            ServiceTargets playerOne = PlayerOneTargets();
            ServiceTargets playerTwo = PlayerTwoTargets();
            Assert.Throws<ArgumentException>(() => RaceLayout.Create(playerOne, playerTwo,
                Vector2.zero));
            var overlapping = new ServiceTargets(new Vector2(1790f, 430f),
                new Vector2(1500f, 300f), new Vector2(1450f, 300f));
            Assert.Throws<ArgumentException>(() => RaceLayout.Create(overlapping, playerTwo,
                new Vector2(110f, 110f)));
            Assert.Throws<ArgumentException>(() => RaceLayout.Create(playerOne, overlapping,
                new Vector2(110f, 110f)));
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
            RacerSnapshot playerOne = Racer(PlayerId.Player1, PitPhase.InService, heatCritical: true,
                service: PitService.Tires, serviceProgress: .62f);
            RaceUiModel model = Build(RacePhase.Racing, playerOne, Racer(PlayerId.Player2),
                Controls(playerOneCrewPresent: false));

            Assert.That(model.PlayerOne.Status, Is.EqualTo("CAR PARKED · TIRES · 62%"));
            Assert.That(model.PlayerOne.PrimaryInstructionKind,
                Is.EqualTo(PlayerUiInstructionKind.PlaceRobotForService));
            Assert.That(model.PlayerOne.PrimaryInstruction, Does.Not.Contain("HEAT CRITICAL"));
            AssertSingleInstruction(model.PlayerOne);
        }

        [Test]
        public void ActivePitCallOutranksRecoveryAndCriticalConditions()
        {
            RacerSnapshot playerOne = Racer(PlayerId.Player1, recovery: .8f, heatCritical: true);
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
            RacerSnapshot playerOne = Racer(PlayerId.Player1, recovery: .8f, heatCritical: true);
            RaceUiModel model = Build(RacePhase.Racing, playerOne, Racer(PlayerId.Player2), Controls());

            Assert.That(model.PlayerOne.PrimaryInstructionKind,
                Is.EqualTo(PlayerUiInstructionKind.CornerRecovery));
        }

        [Test]
        public void HeatWinsDeterministicTieWhenBothConditionsHaveSameSeverity()
        {
            RacerSnapshot playerOne = Racer(PlayerId.Player1, heatCritical: true, tireCritical: true);
            RaceUiModel model = Build(RacePhase.Racing, playerOne, Racer(PlayerId.Player2), Controls());

            Assert.That(model.PlayerOne.PrimaryInstructionKind, Is.EqualTo(PlayerUiInstructionKind.HeatCritical));
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
        public void DeterministicPreviewFixturesCoverTheRequiredReviewStates()
        {
            var track = TrackDefinition.Placeholder();
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
                Is.EqualTo(PlayerUiInstructionKind.HeatWarning));
            Assert.That(RaceUiPreviewFixtures.Create(RaceUiPreviewScenario.Critical, track,
                    ConditionRules.Defaults, 5).Ui.PlayerOne.PrimaryInstructionKind,
                Is.EqualTo(PlayerUiInstructionKind.HeatCritical));
            Assert.That(RaceUiPreviewFixtures.Create(RaceUiPreviewScenario.InService, track,
                    ConditionRules.Defaults, 5).Ui.PlayerOne.PrimaryInstructionKind,
                Is.EqualTo(PlayerUiInstructionKind.HoldService));
            Assert.That(RaceUiPreviewFixtures.Create(RaceUiPreviewScenario.SplitFinish, track,
                    ConditionRules.Defaults, 5).Ui.CenterMessageKind,
                Is.EqualTo(CenterMessageKind.SplitFinish));
            Assert.That(RaceUiPreviewFixtures.Create(RaceUiPreviewScenario.Results, track,
                    ConditionRules.Defaults, 5).Ui.CenterMessageKind,
                Is.EqualTo(CenterMessageKind.Winner));
            Assert.That(RaceUiPreviewFixtures.Create(RaceUiPreviewScenario.RematchRelease, track,
                    ConditionRules.Defaults, 5).Ui.PlayerOne.PrimaryInstructionKind,
                Is.EqualTo(PlayerUiInstructionKind.RematchRelease));
        }

        // Contract geometry from docs/gameplay/wireframe-ui.md (issue #85).
        private static ServiceTargets PlayerOneTargets() => new ServiceTargets(
            new Vector2(1790f, 430f), new Vector2(1620f, 310f), new Vector2(1370f, 140f));

        private static ServiceTargets PlayerTwoTargets() => new ServiceTargets(
            new Vector2(130f, 650f), new Vector2(300f, 770f), new Vector2(550f, 940f));

        private static RaceLayout Layout() => RaceLayout.Create(PlayerOneTargets(),
            PlayerTwoTargets(), new Vector2(110f, 110f));

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
            float recovery = 0f, bool heatCritical = false, bool tireCritical = false,
            PitService service = PitService.None, float serviceProgress = 0f,
            bool finished = false, int place = 1, float heat = .2f, float tireWear = .2f)
        {
            var track = new TrackSample(new Vec2(960f, 540f), new Vec2(1f, 0f), 0,
                TrackSectionKind.Straight, float.PositiveInfinity);
            var condition = new RacerConditionSnapshot(heatCritical ? .9f : heat,
                tireCritical ? .9f : tireWear, heatCritical, tireCritical);
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
