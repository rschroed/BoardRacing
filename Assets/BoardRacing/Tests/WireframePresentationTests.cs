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
            AssertMirrored(layout.PlayerOne.Controller.StatusBounds,
                layout.PlayerTwo.Controller.StatusBounds);
            AssertMirrored(layout.PlayerOne.Controller.InstructionBounds,
                layout.PlayerTwo.Controller.InstructionBounds);
            AssertMirrored(layout.PlayerOne.Controller.HeatBounds,
                layout.PlayerTwo.Controller.HeatBounds);
            AssertMirrored(layout.PlayerOne.Controller.TiresBounds,
                layout.PlayerTwo.Controller.TiresBounds);
            AssertMirrored(layout.PlayerOne.Controller.BrakeBounds,
                layout.PlayerTwo.Controller.BrakeBounds);
            AssertMirrored(layout.PlayerOne.Controller.DriveBounds,
                layout.PlayerTwo.Controller.DriveBounds);
            AssertMirrored(layout.PlayerOne.Controller.BoostBounds,
                layout.PlayerTwo.Controller.BoostBounds);
            AssertContained(layout.PlayerOne.SafeContentBounds,
                layout.PlayerOne.Controller.StatusBounds);
            AssertContained(layout.PlayerOne.SafeContentBounds,
                layout.PlayerOne.Controller.InstructionBounds);
            AssertContained(layout.PlayerTwo.SafeContentBounds,
                layout.PlayerTwo.Controller.StatusBounds);
            AssertContained(layout.PlayerTwo.SafeContentBounds,
                layout.PlayerTwo.Controller.InstructionBounds);
            Assert.That(layout.PlayerOne.Controller.StatusBounds.Overlaps(layout.PlayerOne.CallPit), Is.False);
            Assert.That(layout.PlayerOne.Controller.InstructionBounds.Overlaps(layout.PlayerOne.CallPit), Is.False);
            Assert.That(layout.PlayerTwo.Controller.StatusBounds.Overlaps(layout.PlayerTwo.CallPit), Is.False);
            Assert.That(layout.PlayerTwo.Controller.InstructionBounds.Overlaps(layout.PlayerTwo.CallPit), Is.False);
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

            Assert.That(layout.PlayerOne.CallPit, Is.EqualTo(new Rect(1185f, 690f, 280f, 240f)));
            Assert.That(layout.PlayerOne.Tires, Is.EqualTo(new Rect(995f, 690f, 280f, 240f)));
            Assert.That(layout.PlayerOne.Cooling, Is.EqualTo(new Rect(1375f, 690f, 280f, 240f)));
            Assert.That(layout.PlayerTwo.CallPit, Is.EqualTo(new Rect(455f, 150f, 280f, 240f)));
            Assert.That(layout.PlayerTwo.Tires, Is.EqualTo(new Rect(645f, 150f, 280f, 240f)));
            Assert.That(layout.PlayerTwo.Cooling, Is.EqualTo(new Rect(265f, 150f, 280f, 240f)));
            AssertMirrored(layout.PlayerOne.CallPit, layout.PlayerTwo.CallPit);
            AssertMirrored(layout.PlayerOne.Tires, layout.PlayerTwo.Tires);
            AssertMirrored(layout.PlayerOne.Cooling, layout.PlayerTwo.Cooling);
        }

        [Test]
        public void LayoutRejectsInvalidTargetGeometry()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RaceLayout.Create(
                new Vector2(1325f, 270f), new Vector2(595f, 810f), -1f, new Vector2(140f, 120f)));
            Assert.Throws<ArgumentException>(() => RaceLayout.Create(
                new Vector2(1325f, 270f), new Vector2(595f, 810f), 190f, Vector2.zero));
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

        private static RaceLayout Layout() => RaceLayout.Create(new Vector2(1325f, 270f),
            new Vector2(595f, 810f), 190f, new Vector2(140f, 120f));

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
