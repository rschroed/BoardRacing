using System;
using System.Linq;
using BoardRacing.Domain;
using NUnit.Framework;

namespace BoardRacing.Tests
{
    public sealed class ControlDomainTests
    {
        [Test]
        public void FixedAssignmentsAreValidAndComplete()
        {
            Assert.That(TrancheOneAssignments.Validate(TrancheOneAssignments.All), Is.Empty);
            Assert.That(TrancheOneAssignments.All.Select(x => x.GlyphId), Is.EquivalentTo(new[] { 2, 7, 1, 6 }));
        }

        [Test]
        public void ConfigurationReportsDuplicateAndMissingRole()
        {
            var invalid = new[]
            {
                new PieceAssignment(PlayerId.Player1, PieceRole.Car, 2, "A", "A"),
                new PieceAssignment(PlayerId.Player1, PieceRole.Car, 2, "B", "B")
            };
            var errors = TrancheOneAssignments.Validate(invalid);
            Assert.That(errors.Any(x => x.Contains("more than once")), Is.True);
            Assert.That(errors.Any(x => x.Contains("missing")), Is.True);
        }

        // Stop centers are the hardware-measured raw orientations with the nose pointing
        // at each rendered wedge (Brake 275°, Drive 225°, Boost 175°; #77 hardware review).
        [TestCase(275f, ThrottleStep.Brake)]
        [TestCase(225f, ThrottleStep.Drive)]
        [TestCase(175f, ThrottleStep.Boost)]
        [TestCase(285f, ThrottleStep.Brake)]
        [TestCase(242f, ThrottleStep.Drive)]
        [TestCase(205f, ThrottleStep.Drive)]
        [TestCase(150f, ThrottleStep.Boost)]
        [TestCase(-85f, ThrottleStep.Brake)]
        [TestCase(350f, ThrottleStep.Brake)]
        [TestCase(90f, ThrottleStep.Boost)]
        public void ThrottleMapsMeasuredStopsAndWraparound(float degrees, ThrottleStep expected)
        {
            Assert.That(Mapper().Map(true, Deg(degrees)), Is.EqualTo(expected));
        }

        [Test]
        public void ThrottleFailsSafeAndReacquiresFromZero()
        {
            var mapper = Mapper();
            Assert.That(mapper.Map(true, DriveRaw), Is.EqualTo(ThrottleStep.Drive));
            Assert.That(mapper.Map(false, DriveRaw), Is.EqualTo(ThrottleStep.Brake));
            Assert.That(mapper.Map(true, DriveRaw), Is.EqualTo(ThrottleStep.Drive));
        }

        [Test]
        public void HysteresisPreventsBoundaryFlicker()
        {
            // Brake/Drive midpoint sits at 250°; with 0.1 rad hysteresis a switch needs
            // ≈5.7° of rotation past the midpoint, in either direction.
            var mapper = Mapper();
            Assert.That(mapper.Map(true, Deg(225f)), Is.EqualTo(ThrottleStep.Drive));
            Assert.That(mapper.Map(true, Deg(252f)), Is.EqualTo(ThrottleStep.Drive));
            Assert.That(mapper.Map(true, Deg(258f)), Is.EqualTo(ThrottleStep.Brake));
            Assert.That(mapper.Map(true, Deg(252f)), Is.EqualTo(ThrottleStep.Brake));
            Assert.That(mapper.Map(true, Deg(242f)), Is.EqualTo(ThrottleStep.Drive));
        }

        [Test]
        public void OppositePlayerOrientationUsesTheSamePlayerRelativeStops()
        {
            var playerOne = Mapper();
            var playerTwo = Mapper((float)Math.PI);
            Assert.That(playerOne.Map(true, Deg(275f)), Is.EqualTo(ThrottleStep.Brake));
            Assert.That(playerTwo.Map(true, Deg(95f)), Is.EqualTo(ThrottleStep.Brake));
            Assert.That(playerOne.Map(true, Deg(225f)), Is.EqualTo(ThrottleStep.Drive));
            Assert.That(playerTwo.Map(true, Deg(45f)), Is.EqualTo(ThrottleStep.Drive));
            Assert.That(playerTwo.Map(true, Deg(355f)), Is.EqualTo(ThrottleStep.Boost));
        }

        [Test]
        public void PitActionCompletesOnceAndRequiresExitToRearm()
        {
            var machine = Machine();
            var valid = Crew(true, true, 100f, 100f, 0f);
            Assert.That(machine.Update(valid, 0.4f).State, Is.EqualTo(PitActionState.Holding));
            var completed = machine.Update(valid, 0.6f);
            Assert.That(completed.CompletedThisUpdate, Is.True);
            Assert.That(machine.Update(valid, 2f).CompletedThisUpdate, Is.False);
            machine.Update(Crew(true, true, 500f, 500f, 0f), 0.1f);
            Assert.That(machine.Update(valid, 1f).CompletedThisUpdate, Is.True);
        }

        [Test]
        public void PitActionIgnoresTouchAndResetsForLossAndBadAlignment()
        {
            var machine = Machine();
            machine.Update(Crew(true, true, 100f, 100f, 0f), 0.8f);
            Assert.That(machine.Update(PieceState.Missing, 0.3f).State, Is.EqualTo(PitActionState.Canceled));
            Assert.That(machine.Update(Crew(true, false, 100f, 100f, 0f), 0.3f).State, Is.EqualTo(PitActionState.Holding));
            Assert.That(machine.Update(Crew(true, true, 100f, 100f, 1f), 0.3f).State, Is.EqualTo(PitActionState.Aligning));
            Assert.That(machine.Update(Crew(true, true, 100f, 100f, 0f), 0.3f).CompletedThisUpdate, Is.False);
        }

        [Test]
        public void NewContactTouchStateDoesNotGateThrottle()
        {
            var reconciler = Reconciler();
            var waiting = Player(reconciler.Reconcile(new[] { Contact(10, 7, true, 100f, RawContactPhase.Stationary, DriveRaw) }), PlayerId.Player1);
            Assert.That(waiting.Throttle, Is.EqualTo(ThrottleStep.Drive));
            Assert.That(waiting.Car.RequiresRelease, Is.True);
            reconciler.Reconcile(new[] { Contact(10, 7, false, 100f, RawContactPhase.Stationary, DriveRaw) });
            var rearmed = Player(reconciler.Reconcile(new[] { Contact(10, 7, true, 100f, RawContactPhase.Stationary, DriveRaw) }), PlayerId.Player1);
            Assert.That(rearmed.Throttle, Is.EqualTo(ThrottleStep.Drive));
            Assert.That(rearmed.Car.RequiresRelease, Is.False);
        }

        [Test]
        public void MissingCanceledAndEndedContactsFailSafe()
        {
            var reconciler = Reconciler();
            ArmCar(reconciler, 10);
            Assert.That(Player(reconciler.Reconcile(Array.Empty<RawPieceContact>()), PlayerId.Player1).Throttle,
                Is.EqualTo(ThrottleStep.Brake));
            Assert.That(Player(reconciler.Reconcile(new[] { Contact(10, 7, true, 100f, RawContactPhase.Canceled) }), PlayerId.Player1).Car.Present,
                Is.False);
            Assert.That(Player(reconciler.Reconcile(new[] { Contact(10, 7, true, 100f, RawContactPhase.Ended) }), PlayerId.Player1).Car.Present,
                Is.False);
        }

        [Test]
        public void ReacquisitionWithNewContactIdCannotInheritThrottle()
        {
            var reconciler = Reconciler();
            ArmCar(reconciler, 10);
            Assert.That(Player(reconciler.Reconcile(new[] { Contact(11, 7, true, 100f, RawContactPhase.Stationary, DriveRaw) }), PlayerId.Player1).Throttle,
                Is.EqualTo(ThrottleStep.Drive));
        }

        [Test]
        public void DuplicateAssignedGlyphFailsSafeAndWarns()
        {
            var reconciler = Reconciler();
            var player = Player(reconciler.Reconcile(new[] { Contact(10, 7, true, 100f), Contact(11, 7, true, 100f) }), PlayerId.Player1);
            Assert.That(player.Car.Present, Is.False);
            Assert.That(player.Throttle, Is.EqualTo(ThrottleStep.Brake));
            Assert.That(player.Warnings.HasFlag(InputWarning.DuplicateGlyph), Is.True);
        }

        [Test]
        public void UnassignedGlyphWarnsWithoutAffectingPlayers()
        {
            var reconciler = Reconciler();
            var snapshots = reconciler.Reconcile(new[] { Contact(10, 0, true, 100f) });
            Assert.That(snapshots.All(x => x.Throttle == ThrottleStep.Brake && !x.Car.Present && !x.Crew.Present), Is.True);
            Assert.That(snapshots.All(x => x.Warnings.HasFlag(InputWarning.UnassignedGlyph)), Is.True);
        }

        [Test]
        public void WrongRegionWarnsButDoesNotReassignPiece()
        {
            var reconciler = Reconciler();
            var player1 = Player(reconciler.Reconcile(new[] { Contact(10, 7, false, 800f) }), PlayerId.Player1);
            Assert.That(player1.Car.Present, Is.True);
            Assert.That(player1.Warnings.HasFlag(InputWarning.WrongRegion), Is.True);
            Assert.That(Player(reconciler.Reconcile(new[] { Contact(10, 7, false, 800f) }), PlayerId.Player2).Car.Present, Is.False);
        }

        [Test]
        public void SimultaneousCrossingContactsKeepGlyphRoles()
        {
            var reconciler = Reconciler();
            var snapshots = reconciler.Reconcile(new[]
            {
                Contact(10, 7, false, 800f), Contact(20, 6, false, 200f),
                Contact(30, 2, false, 800f), Contact(40, 1, false, 200f)
            });
            Assert.That(Player(snapshots, PlayerId.Player1).Car.ContactId, Is.EqualTo(10));
            Assert.That(Player(snapshots, PlayerId.Player1).Crew.ContactId, Is.EqualTo(30));
            Assert.That(Player(snapshots, PlayerId.Player2).Car.ContactId, Is.EqualTo(20));
            Assert.That(Player(snapshots, PlayerId.Player2).Crew.ContactId, Is.EqualTo(40));
        }

        [Test]
        public void SettingsResetRequiresSafeReleaseAgain()
        {
            var reconciler = Reconciler();
            ArmCar(reconciler, 10);
            reconciler.ResetAll();
            Assert.That(Player(reconciler.Reconcile(new[] { Contact(10, 7, true, 100f, RawContactPhase.Stationary, DriveRaw) }), PlayerId.Player1).Throttle,
                Is.EqualTo(ThrottleStep.Drive));
        }

        [Test]
        public void PitRobotRequiresFreshPlacementAlignmentAndHoldAndEmitsOnce()
        {
            var adapter = StrategyAdapter();
            var pit = Pit(PitService.None, PitPhase.OnTrack);
            var initiallyInside = adapter.Update(StrategyControls(Crew(true, false, 200f, 100f, 0f)),
                RacePhase.Racing, pit, .1f);
            Assert.That(initiallyInside.CallState, Is.EqualTo(PitCallState.NeedsPlacement));
            Assert.That(initiallyInside.RequestPit, Is.False);

            adapter.Update(StrategyControls(Crew(true, true, 400f, 100f, 0f)), RacePhase.Racing, pit, .1f);
            var positioned = adapter.Update(StrategyControls(Crew(true, true, 200f, 100f, 0f)),
                RacePhase.Racing, pit, .1f);
            Assert.That(positioned.CallState, Is.EqualTo(PitCallState.Holding));
            Assert.That(positioned.RequestPit, Is.False);

            var requested = adapter.Update(StrategyControls(Crew(true, false, 200f, 100f, 0f)),
                RacePhase.Racing, pit, .7f);
            Assert.That(requested.SelectedService, Is.EqualTo(PitService.None));
            Assert.That(requested.RequestPit, Is.True);
            Assert.That(requested.CallState, Is.EqualTo(PitCallState.Requested));
            Assert.That(adapter.Update(StrategyControls(Crew(true, false, 200f, 100f, 0f)),
                RacePhase.Racing, pit, .1f).RequestPit, Is.False);
        }

        [Test]
        public void PitRobotSupportsSameContactSlideIntoRegionAndIgnoresTouch()
        {
            var adapter = StrategyAdapter();
            var pit = Pit(PitService.None, PitPhase.OnTrack);
            adapter.Update(StrategyControls(Crew(true, false, 400f, 100f, 0f)), RacePhase.Racing, pit, .1f);
            var inside = adapter.Update(StrategyControls(Crew(true, false, 200f, 100f, 0f)),
                RacePhase.Racing, pit, .4f);
            Assert.That(inside.CallState, Is.EqualTo(PitCallState.Holding));
            Assert.That(adapter.Update(StrategyControls(Crew(true, false, 200f, 100f, 0f)),
                RacePhase.Racing, pit, .4f).RequestPit, Is.True);
        }

        [Test]
        public void PitRobotNewContactAndFaultRecoveryRequireFreshPlacement()
        {
            var adapter = StrategyAdapter();
            var pit = Pit(PitService.None, PitPhase.OnTrack);
            var requiresRelease = new PieceState(true, false, 2, new Vec2(200f, 100f), 0f, true);
            var gated = adapter.Update(StrategyControls(requiresRelease), RacePhase.Racing, pit, .1f);
            Assert.That(gated.CallState, Is.EqualTo(PitCallState.NeedsPlacement));
            Assert.That(gated.RequestPit, Is.False);

            adapter.Update(StrategyControls(Crew(true, false, 400f, 100f, 0f)), RacePhase.Racing, pit, .1f);
            var rotatedAnyWhichWay = new PieceState(true, true, 2, new Vec2(200f, 100f), 1f, true);
            Assert.That(adapter.Update(StrategyControls(rotatedAnyWhichWay), RacePhase.Racing, pit, .1f).CallState,
                Is.EqualTo(PitCallState.Holding));
            Assert.That(adapter.Update(StrategyControls(rotatedAnyWhichWay), RacePhase.Racing, pit, .8f).RequestPit,
                Is.True);

            var wrongRegion = adapter.Update(StrategyControls(Crew(true, false, 200f, 100f, 0f),
                InputWarning.WrongRegion), RacePhase.Racing, pit, .1f);
            Assert.That(wrongRegion.RequestPit, Is.False);
            Assert.That(wrongRegion.CallState, Is.EqualTo(PitCallState.Unavailable));

            Assert.That(adapter.Update(StrategyControls(PieceState.Missing), RacePhase.Racing, pit, .1f).RequestPit, Is.False);
            var replaced = new PieceState(true, true, 3, new Vec2(200f, 100f), 0f, true);
            Assert.That(adapter.Update(StrategyControls(replaced), RacePhase.Racing, pit, .8f).RequestPit, Is.True);

            var requestedPit = Pit(PitService.None, PitPhase.Requested);
            Assert.That(adapter.Update(StrategyControls(replaced),
                RacePhase.Racing, requestedPit, .1f).RequestPit, Is.False);
            Assert.That(adapter.Update(StrategyControls(replaced),
                RacePhase.Racing, pit, .1f).RequestPit, Is.False);
        }

        [Test]
        public void CrewServiceStirDrainsAndSwitchingDialsSwitchesService()
        {
            var adapter = StrategyAdapter();
            var tiresPit = Pit(PitService.None, PitPhase.InService);
            var primed = adapter.Update(StrategyControls(Crew(true, false, 115f, 100f, 0f)),
                RacePhase.Racing, tiresPit, .1f);
            Assert.That(primed.SelectedService, Is.EqualTo(PitService.Tires));
            Assert.That(primed.ServiceAction.State, Is.EqualTo(PitActionState.Stirring));
            Assert.That(primed.ServiceDrain, Is.Zero);

            var halfway = adapter.Update(StrategyControls(Crew(true, true, 110.607f, 110.607f, 0f)),
                RacePhase.Racing, tiresPit, .05f);
            var quarterTurn = adapter.Update(StrategyControls(Crew(true, true, 100f, 115f, 0f)),
                RacePhase.Racing, tiresPit, .05f);
            Assert.That(quarterTurn.SelectedService, Is.EqualTo(PitService.Tires));
            Assert.That(halfway.ServiceDrain + quarterTurn.ServiceDrain,
                Is.EqualTo(.25f).Within(.001f));

            var still = adapter.Update(StrategyControls(Crew(true, true, 100f, 115f, 0f)),
                RacePhase.Racing, tiresPit, .4f);
            Assert.That(still.ServiceDrain, Is.Zero);

            var fuelPrimed = adapter.Update(StrategyControls(Crew(true, true, 315f, 100f, 0f)),
                RacePhase.Racing, tiresPit, .1f);
            Assert.That(fuelPrimed.SelectedService, Is.EqualTo(PitService.Fuel));
            Assert.That(fuelPrimed.ServiceDrain, Is.Zero);
            var fuelHalfway = adapter.Update(StrategyControls(Crew(true, true, 310.607f, 110.607f, 0f)),
                RacePhase.Racing, tiresPit, .05f);
            var fuelQuarter = adapter.Update(StrategyControls(Crew(true, true, 300f, 115f, 0f)),
                RacePhase.Racing, tiresPit, .05f);
            Assert.That(fuelHalfway.ServiceDrain + fuelQuarter.ServiceDrain,
                Is.EqualTo(.25f).Within(.001f));

            var lost = adapter.Update(StrategyControls(PieceState.Missing), RacePhase.Racing, tiresPit, .2f);
            Assert.That(lost.ServiceAction.State, Is.EqualTo(PitActionState.Idle));
            Assert.That(lost.ServiceDrain, Is.Zero);

            // Returning after loss re-primes: the first sample back drains nothing.
            Assert.That(adapter.Update(StrategyControls(Crew(true, true, 115f, 100f, 0f)),
                RacePhase.Racing, tiresPit, .1f).ServiceDrain, Is.Zero);
            float resumed = adapter.Update(StrategyControls(Crew(true, true, 110.607f, 110.607f, 0f)),
                    RacePhase.Racing, tiresPit, .1f).ServiceDrain
                + adapter.Update(StrategyControls(Crew(true, true, 100f, 115f, 0f)),
                    RacePhase.Racing, tiresPit, .1f).ServiceDrain;
            Assert.That(resumed, Is.EqualTo(.25f).Within(.001f));

            var noRepairZone = adapter.Update(StrategyControls(Crew(true, true, 200f, 100f, 0f)),
                RacePhase.Racing, tiresPit, 1f);
            Assert.That(noRepairZone.SelectedService, Is.EqualTo(PitService.None));
            Assert.That(noRepairZone.ServiceDrain, Is.Zero);
        }

        [Test]
        public void LeavePitArmsOnDeliberateEntryAndHoldRequestsExit()
        {
            var adapter = StrategyAdapter();
            var pit = Pit(PitService.None, PitPhase.InService);

            // A Robot already resting in the circle when the car parks never arms.
            var resting = adapter.Update(StrategyControls(Crew(true, true, 200f, 100f, 0f)),
                RacePhase.Racing, pit, 2f);
            Assert.That(resting.RequestExit, Is.False);
            Assert.That(resting.CallState, Is.EqualTo(PitCallState.NeedsPlacement));
            Assert.That(adapter.Update(StrategyControls(Crew(true, true, 200f, 100f, 0f)),
                RacePhase.Racing, pit, 2f).RequestExit, Is.False);

            // Sliding out to a dial and back in arms; the hold then requests the exit.
            adapter.Update(StrategyControls(Crew(true, true, 100f, 100f, 0f)), RacePhase.Racing, pit, .1f);
            var entered = adapter.Update(StrategyControls(Crew(true, true, 200f, 100f, 0f)),
                RacePhase.Racing, pit, .1f);
            Assert.That(entered.CallState, Is.EqualTo(PitCallState.Holding));
            Assert.That(entered.RequestExit, Is.False);
            var held = adapter.Update(StrategyControls(Crew(true, true, 200f, 100f, 0f)),
                RacePhase.Racing, pit, .8f);
            Assert.That(held.RequestExit, Is.True);
            Assert.That(held.CallState, Is.EqualTo(PitCallState.Requested));

            // A fresh placement inside the circle also arms — even mid-service.
            adapter.Update(StrategyControls(Crew(true, true, 115f, 100f, 0f)), RacePhase.Racing, pit, .1f);
            var replaced = new PieceState(true, true, 9, new Vec2(200f, 100f), 0f, true);
            Assert.That(adapter.Update(StrategyControls(replaced), RacePhase.Racing, pit, .1f)
                .CallState, Is.EqualTo(PitCallState.Holding));
            Assert.That(adapter.Update(StrategyControls(replaced), RacePhase.Racing, pit, .8f)
                .RequestExit, Is.True);
        }

        [Test]
        public void ServiceIgnoresRobotOrientationEntirely()
        {
            var adapter = StrategyAdapter();
            var pit = Pit(PitService.None, PitPhase.InService);
            var placed = adapter.Update(StrategyControls(Crew(true, false, 115f, 100f, 2.5f)),
                RacePhase.Racing, pit, .4f);
            Assert.That(placed.SelectedService, Is.EqualTo(PitService.Tires));
            Assert.That(placed.ServiceAction.State, Is.EqualTo(PitActionState.Stirring));
            var midStir = adapter.Update(StrategyControls(Crew(true, true, 110.607f, 110.607f, 1.2f)),
                RacePhase.Racing, pit, .3f);
            var stirred = adapter.Update(StrategyControls(Crew(true, true, 100f, 115f, 5.9f)),
                RacePhase.Racing, pit, .7f);
            Assert.That(midStir.ServiceDrain + stirred.ServiceDrain,
                Is.EqualTo(.25f).Within(.001f));
        }

        [Test]
        public void StirMachineAccumulatesAngularTravelAndRejectsNoiseAndTeleports()
        {
            var machine = new StirServiceMachine(new Vec2(100f, 100f), new Vec2(20f, 20f), 1f);
            Assert.That(machine.Update(Crew(true, true, 500f, 500f, 0f)).State, Is.EqualTo(PitActionState.Idle));
            Assert.That(machine.Update(Crew(true, true, 105f, 100f, 0f)).Drain, Is.Zero);
            Assert.That(machine.Update(Crew(true, true, 105f, 100f, 0f)).State,
                Is.EqualTo(PitActionState.Stirring));
            machine.Update(Crew(true, true, 115f, 100f, 0f));
            float forward = machine.Update(Crew(true, true, 110.607f, 110.607f, 0f)).Drain
                + machine.Update(Crew(true, true, 100f, 115f, 0f)).Drain;
            Assert.That(forward, Is.EqualTo(.25f).Within(.001f));
            float backward = machine.Update(Crew(true, true, 110.607f, 110.607f, 0f)).Drain
                + machine.Update(Crew(true, true, 115f, 100f, 0f)).Drain;
            Assert.That(backward, Is.EqualTo(.25f).Within(.001f));
            var teleport = machine.Update(Crew(true, true, 85f, 100f, 0f));
            Assert.That(teleport.Drain, Is.EqualTo(1.5f / (2f * (float)Math.PI)).Within(.001f));
            Assert.That(machine.Update(PieceState.Missing).State, Is.EqualTo(PitActionState.Canceled));
            Assert.That(machine.Update(Crew(true, true, 115f, 100f, 0f)).Drain, Is.Zero);
        }

        [Test]
        public void TwoCrewAdaptersRemainIndependent()
        {
            var p1 = StrategyAdapter(); var p2 = StrategyAdapter();
            var pit = Pit(PitService.None, PitPhase.OnTrack);
            p1.Update(StrategyControls(Crew(true, true, 400f, 100f, 0f)), RacePhase.Racing, pit, .1f);
            p2.Update(StrategyControls(Crew(true, true, 400f, 100f, 0f)), RacePhase.Racing, pit, .1f);
            var p1Request = p1.Update(StrategyControls(Crew(true, false, 200f, 100f, 0f)), RacePhase.Racing, pit, .8f);
            var p2Request = p2.Update(StrategyControls(Crew(true, true, 200f, 100f, 0f)), RacePhase.Racing, pit, .8f);
            Assert.That(p1Request.SelectedService, Is.EqualTo(PitService.None));
            Assert.That(p2Request.SelectedService, Is.EqualTo(PitService.None));
            Assert.That(p1Request.RequestPit, Is.True);
            Assert.That(p2Request.RequestPit, Is.True);
        }

        private static PitActionMachine Machine() => new PitActionMachine(
            new Vec2(100f, 100f), new Vec2(20f, 20f), 0f, 0.2f, 1f);
        // One stir turn = a full meter in tests, so a quarter turn drains .25 exactly.
        // Tests stir in 45-degree steps: a 90-degree jump in one sample exceeds the
        // teleport cap (MaximumStepRadians) and would be clamped.
        private static CrewStrategyAdapter StrategyAdapter() => new CrewStrategyAdapter(
            new Vec2(200f, 100f), new Vec2(100f, 100f), new Vec2(300f, 100f),
            new Vec2(20f, 20f), 1f);
        private static PieceState Crew(bool present, bool touched, float x, float y, float angle) =>
            new PieceState(present, touched, 1, new Vec2(x, y), angle);
        private static RacerPitSnapshot Pit(PitService service, PitPhase phase) =>
            new RacerPitSnapshot(service, phase, 0f, 0, false);
        private static PlayerControlSnapshot StrategyControls(PieceState crew,
            InputWarning warning = InputWarning.None) =>
            new PlayerControlSnapshot(PlayerId.Player1, ThrottleStep.Brake, PieceState.Missing, crew, warning);

        private const float DriveRaw = 225f * (float)Math.PI / 180f;
        private static float Deg(float degrees) => degrees * (float)Math.PI / 180f;
        private static ThrottleStops Stops() => new ThrottleStops(Deg(275f), Deg(225f), Deg(175f));
        private static CoarseThrottleMapper Mapper(float orientationOffset = 0f) =>
            new CoarseThrottleMapper(.1f, Stops(), orientationOffset);

        private static ContactSnapshotReconciler Reconciler() =>
            new ContactSnapshotReconciler(TrancheOneAssignments.All, Stops(), 0.1f, 540f);

        private static RawPieceContact Contact(int contactId, int glyphId, bool touched, float y,
            RawContactPhase phase = RawContactPhase.Stationary, float angle = 0f) =>
            new RawPieceContact(contactId, glyphId, new Vec2(100f, y), angle, touched, phase);

        private static PlayerControlSnapshot Player(System.Collections.Generic.IReadOnlyList<PlayerControlSnapshot> snapshots,
            PlayerId playerId) => snapshots.Single(x => x.PlayerId == playerId);

        private static void ArmCar(ContactSnapshotReconciler reconciler, int contactId)
        {
            reconciler.Reconcile(new[] { Contact(contactId, 7, false, 100f, RawContactPhase.Stationary, DriveRaw) });
            Assert.That(Player(reconciler.Reconcile(new[] { Contact(contactId, 7, true, 100f, RawContactPhase.Stationary, DriveRaw) }), PlayerId.Player1).Throttle,
                Is.EqualTo(ThrottleStep.Drive));
        }
    }
}
