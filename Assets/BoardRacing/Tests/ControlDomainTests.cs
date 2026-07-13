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

        [TestCase(0f, ThrottleStep.Quarter)]
        [TestCase(1.5707963f, ThrottleStep.Half)]
        [TestCase(3.1415926f, ThrottleStep.ThreeQuarters)]
        [TestCase(4.712389f, ThrottleStep.Full)]
        [TestCase(-0.01f, ThrottleStep.Quarter)]
        [TestCase(6.273185f, ThrottleStep.Quarter)]
        public void ThrottleMapsSectorsAndWraparound(float angle, ThrottleStep expected)
        {
            Assert.That(new CoarseThrottleMapper(0.1f).Map(true, true, angle), Is.EqualTo(expected));
        }

        [Test]
        public void ThrottleFailsSafeAndReacquiresFromZero()
        {
            var mapper = new CoarseThrottleMapper(0.1f);
            Assert.That(mapper.Map(true, true, 1.6f), Is.EqualTo(ThrottleStep.Half));
            Assert.That(mapper.Map(false, false, 1.6f), Is.EqualTo(ThrottleStep.Off));
            Assert.That(mapper.Map(true, false, 1.6f), Is.EqualTo(ThrottleStep.Off));
        }

        [Test]
        public void HysteresisPreventsBoundaryFlicker()
        {
            var mapper = new CoarseThrottleMapper(0.2f);
            Assert.That(mapper.Map(true, true, 0f), Is.EqualTo(ThrottleStep.Quarter));
            Assert.That(mapper.Map(true, true, (float)Math.PI / 4f + 0.05f), Is.EqualTo(ThrottleStep.Quarter));
            Assert.That(mapper.Map(true, true, (float)Math.PI / 4f + 0.25f), Is.EqualTo(ThrottleStep.Half));
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
        public void PitActionResetsForLossReleaseAndBadAlignment()
        {
            var machine = Machine();
            machine.Update(Crew(true, true, 100f, 100f, 0f), 0.8f);
            Assert.That(machine.Update(PieceState.Missing, 0.3f).State, Is.EqualTo(PitActionState.Canceled));
            Assert.That(machine.Update(Crew(true, false, 100f, 100f, 0f), 0.3f).State, Is.EqualTo(PitActionState.Positioned));
            Assert.That(machine.Update(Crew(true, true, 100f, 100f, 1f), 0.3f).State, Is.EqualTo(PitActionState.Aligning));
            Assert.That(machine.Update(Crew(true, true, 100f, 100f, 0f), 0.3f).CompletedThisUpdate, Is.False);
        }

        [Test]
        public void NewContactRequiresSafeReleaseBeforeThrottle()
        {
            var reconciler = Reconciler();
            var waiting = Player(reconciler.Reconcile(new[] { Contact(10, 2, true, 100f) }), PlayerId.Player1);
            Assert.That(waiting.Throttle, Is.EqualTo(ThrottleStep.Off));
            Assert.That(waiting.Car.RequiresRelease, Is.True);
            reconciler.Reconcile(new[] { Contact(10, 2, false, 100f) });
            var rearmed = Player(reconciler.Reconcile(new[] { Contact(10, 2, true, 100f) }), PlayerId.Player1);
            Assert.That(rearmed.Throttle, Is.EqualTo(ThrottleStep.Quarter));
            Assert.That(rearmed.Car.RequiresRelease, Is.False);
        }

        [Test]
        public void MissingCanceledAndEndedContactsFailSafe()
        {
            var reconciler = Reconciler();
            ArmCar(reconciler, 10);
            Assert.That(Player(reconciler.Reconcile(Array.Empty<RawPieceContact>()), PlayerId.Player1).Throttle,
                Is.EqualTo(ThrottleStep.Off));
            Assert.That(Player(reconciler.Reconcile(new[] { Contact(10, 2, true, 100f, RawContactPhase.Canceled) }), PlayerId.Player1).Car.Present,
                Is.False);
            Assert.That(Player(reconciler.Reconcile(new[] { Contact(10, 2, true, 100f, RawContactPhase.Ended) }), PlayerId.Player1).Car.Present,
                Is.False);
        }

        [Test]
        public void ReacquisitionWithNewContactIdCannotInheritThrottle()
        {
            var reconciler = Reconciler();
            ArmCar(reconciler, 10);
            Assert.That(Player(reconciler.Reconcile(new[] { Contact(11, 2, true, 100f) }), PlayerId.Player1).Throttle,
                Is.EqualTo(ThrottleStep.Off));
            reconciler.Reconcile(new[] { Contact(11, 2, false, 100f) });
            Assert.That(Player(reconciler.Reconcile(new[] { Contact(11, 2, true, 100f) }), PlayerId.Player1).Throttle,
                Is.EqualTo(ThrottleStep.Quarter));
        }

        [Test]
        public void DuplicateAssignedGlyphFailsSafeAndWarns()
        {
            var reconciler = Reconciler();
            var player = Player(reconciler.Reconcile(new[] { Contact(10, 2, true, 100f), Contact(11, 2, true, 100f) }), PlayerId.Player1);
            Assert.That(player.Car.Present, Is.False);
            Assert.That(player.Throttle, Is.EqualTo(ThrottleStep.Off));
            Assert.That(player.Warnings.HasFlag(InputWarning.DuplicateGlyph), Is.True);
        }

        [Test]
        public void UnassignedGlyphWarnsWithoutAffectingPlayers()
        {
            var reconciler = Reconciler();
            var snapshots = reconciler.Reconcile(new[] { Contact(10, 0, true, 100f) });
            Assert.That(snapshots.All(x => x.Throttle == ThrottleStep.Off && !x.Car.Present && !x.Crew.Present), Is.True);
            Assert.That(snapshots.All(x => x.Warnings.HasFlag(InputWarning.UnassignedGlyph)), Is.True);
        }

        [Test]
        public void WrongRegionWarnsButDoesNotReassignPiece()
        {
            var reconciler = Reconciler();
            var player1 = Player(reconciler.Reconcile(new[] { Contact(10, 2, false, 800f) }), PlayerId.Player1);
            Assert.That(player1.Car.Present, Is.True);
            Assert.That(player1.Warnings.HasFlag(InputWarning.WrongRegion), Is.True);
            Assert.That(Player(reconciler.Reconcile(new[] { Contact(10, 2, false, 800f) }), PlayerId.Player2).Car.Present, Is.False);
        }

        [Test]
        public void SimultaneousCrossingContactsKeepGlyphRoles()
        {
            var reconciler = Reconciler();
            var snapshots = reconciler.Reconcile(new[]
            {
                Contact(10, 2, false, 800f), Contact(20, 1, false, 200f),
                Contact(30, 7, false, 800f), Contact(40, 6, false, 200f)
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
            Assert.That(Player(reconciler.Reconcile(new[] { Contact(10, 2, true, 100f) }), PlayerId.Player1).Throttle,
                Is.EqualTo(ThrottleStep.Off));
        }

        [Test]
        public void CrewCallPitRequiresTouchReleaseAndEmitsOnce()
        {
            var adapter = StrategyAdapter();
            var pit = Pit(PitService.None, PitPhase.OnTrack);
            var initiallyInside = adapter.Update(StrategyControls(Crew(true, false, 200f, 100f, 0f)),
                RacePhase.Racing, pit, .1f);
            Assert.That(initiallyInside.CallState, Is.EqualTo(PitCallState.Ready));
            Assert.That(initiallyInside.RequestPit, Is.False);

            var positioned = adapter.Update(StrategyControls(Crew(true, true, 200f, 100f, 0f)),
                RacePhase.Racing, pit, .1f);
            Assert.That(positioned.CallState, Is.EqualTo(PitCallState.ReleaseToRequest));
            Assert.That(positioned.RequestPit, Is.False);

            var requested = adapter.Update(StrategyControls(Crew(true, false, 200f, 100f, 0f)),
                RacePhase.Racing, pit, .1f);
            Assert.That(requested.SelectedService, Is.EqualTo(PitService.None));
            Assert.That(requested.RequestPit, Is.True);
            Assert.That(requested.CallState, Is.EqualTo(PitCallState.Requested));
            Assert.That(adapter.Update(StrategyControls(Crew(true, false, 200f, 100f, 0f)),
                RacePhase.Racing, pit, .1f).RequestPit, Is.False);
        }

        [Test]
        public void CrewCallPitSupportsSameContactSlideIntoRegion()
        {
            var adapter = StrategyAdapter();
            var pit = Pit(PitService.None, PitPhase.OnTrack);
            adapter.Update(StrategyControls(Crew(true, false, 400f, 100f, 0f)), RacePhase.Racing, pit, .1f);
            adapter.Update(StrategyControls(Crew(true, true, 400f, 100f, 0f)), RacePhase.Racing, pit, .1f);
            var inside = adapter.Update(StrategyControls(Crew(true, true, 200f, 100f, 0f)),
                RacePhase.Racing, pit, .1f);
            Assert.That(inside.CallState, Is.EqualTo(PitCallState.ReleaseToRequest));
            Assert.That(adapter.Update(StrategyControls(Crew(true, false, 200f, 100f, 0f)),
                RacePhase.Racing, pit, .1f).RequestPit, Is.True);
        }

        [Test]
        public void CrewCallPitSupportsNewContactAfterSafeReleaseAndRequiresFreshTouchCycle()
        {
            var adapter = StrategyAdapter();
            var pit = Pit(PitService.None, PitPhase.OnTrack);
            var requiresRelease = new PieceState(true, false, 2, new Vec2(200f, 100f), 0f, true);
            var gated = adapter.Update(StrategyControls(requiresRelease), RacePhase.Racing, pit, .1f);
            Assert.That(gated.CallState, Is.EqualTo(PitCallState.NeedsRelease));
            Assert.That(gated.RequestPit, Is.False);

            var safeRelease = new PieceState(true, false, 2, new Vec2(200f, 100f), 0f);
            Assert.That(adapter.Update(StrategyControls(safeRelease), RacePhase.Racing, pit, .1f).RequestPit,
                Is.False);
            var touched = new PieceState(true, true, 2, new Vec2(200f, 100f), 0f);
            Assert.That(adapter.Update(StrategyControls(touched), RacePhase.Racing, pit, .1f).CallState,
                Is.EqualTo(PitCallState.ReleaseToRequest));
            Assert.That(adapter.Update(StrategyControls(safeRelease), RacePhase.Racing, pit, .1f).RequestPit,
                Is.True);

            adapter.Update(StrategyControls(touched), RacePhase.Racing, pit, .1f);
            var wrongRegion = adapter.Update(StrategyControls(Crew(true, false, 200f, 100f, 0f),
                InputWarning.WrongRegion), RacePhase.Racing, pit, .1f);
            Assert.That(wrongRegion.RequestPit, Is.False);
            Assert.That(wrongRegion.CallState, Is.EqualTo(PitCallState.Unavailable));

            adapter.Update(StrategyControls(touched), RacePhase.Racing, pit, .1f);
            Assert.That(adapter.Update(StrategyControls(PieceState.Missing), RacePhase.Racing, pit, .1f).RequestPit, Is.False);
            Assert.That(adapter.Update(StrategyControls(Crew(true, false, 200f, 100f, 0f)),
                RacePhase.Racing, pit, .1f).RequestPit, Is.False);

            var requestedPit = Pit(PitService.None, PitPhase.Requested);
            Assert.That(adapter.Update(StrategyControls(Crew(true, true, 200f, 100f, 0f)),
                RacePhase.Racing, requestedPit, .1f).RequestPit, Is.False);
            Assert.That(adapter.Update(StrategyControls(Crew(true, false, 200f, 100f, 0f)),
                RacePhase.Racing, pit, .1f).RequestPit, Is.False);
            adapter.Update(StrategyControls(Crew(true, true, 200f, 100f, 0f)), RacePhase.Racing, pit, .1f);
            Assert.That(adapter.Update(StrategyControls(Crew(true, false, 200f, 100f, 0f)),
                RacePhase.Racing, pit, .1f).RequestPit, Is.True);
        }

        [Test]
        public void CrewChoosesServiceOnlyWhenParkedAndSwitchingResetsProgress()
        {
            var adapter = StrategyAdapter();
            var tiresPit = Pit(PitService.None, PitPhase.InService);
            var positioned = adapter.Update(StrategyControls(Crew(true, false, 100f, 100f, 0f)),
                RacePhase.Racing, tiresPit, .1f);
            Assert.That(positioned.SelectedService, Is.EqualTo(PitService.Tires));
            Assert.That(positioned.ServiceAction.State, Is.EqualTo(PitActionState.Positioned));

            var valid = StrategyControls(Crew(true, true, 100f, 100f, 0f));
            var tireHold = adapter.Update(valid, RacePhase.Racing, tiresPit, .4f);
            Assert.That(tireHold.SelectedService, Is.EqualTo(PitService.Tires));
            Assert.That(tireHold.ServiceAction.Progress, Is.EqualTo(.4f).Within(.001f));

            var coolingHold = adapter.Update(StrategyControls(Crew(true, true, 300f, 100f, 0f)),
                RacePhase.Racing, tiresPit, .4f);
            Assert.That(coolingHold.SelectedService, Is.EqualTo(PitService.Cooling));
            Assert.That(coolingHold.ServiceAction.Progress, Is.EqualTo(.4f).Within(.001f));
            Assert.That(coolingHold.ServiceAction.CompletedThisUpdate, Is.False);

            var lost = adapter.Update(StrategyControls(PieceState.Missing), RacePhase.Racing, tiresPit, .2f);
            Assert.That(lost.ServiceAction.State, Is.EqualTo(PitActionState.Idle));

            adapter.Update(StrategyControls(Crew(true, false, 100f, 100f, 0f)), RacePhase.Racing, tiresPit, .1f);
            Assert.That(adapter.Update(valid, RacePhase.Racing, tiresPit, .4f).ServiceAction.CompletedThisUpdate, Is.False);
            Assert.That(adapter.Update(valid, RacePhase.Racing, tiresPit, .6f).ServiceAction.CompletedThisUpdate, Is.True);
            Assert.That(adapter.Update(valid, RacePhase.Racing, tiresPit, 1f).ServiceAction.CompletedThisUpdate, Is.False);

            adapter.Reset();
            var noRepairZone = adapter.Update(StrategyControls(Crew(true, true, 200f, 100f, 0f)),
                RacePhase.Racing, tiresPit, 1f);
            Assert.That(noRepairZone.SelectedService, Is.EqualTo(PitService.None));
            Assert.That(noRepairZone.ServiceAction.CompletedThisUpdate, Is.False);
        }

        [Test]
        public void TwoCrewAdaptersRemainIndependent()
        {
            var p1 = StrategyAdapter(); var p2 = StrategyAdapter();
            var pit = Pit(PitService.None, PitPhase.OnTrack);
            p1.Update(StrategyControls(Crew(true, true, 200f, 100f, 0f)), RacePhase.Racing, pit, .1f);
            p2.Update(StrategyControls(Crew(true, true, 200f, 100f, 0f)), RacePhase.Racing, pit, .1f);
            var p1Request = p1.Update(StrategyControls(Crew(true, false, 200f, 100f, 0f)), RacePhase.Racing, pit, .1f);
            var p2Request = p2.Update(StrategyControls(Crew(true, false, 200f, 100f, 0f)), RacePhase.Racing, pit, .1f);
            Assert.That(p1Request.SelectedService, Is.EqualTo(PitService.None));
            Assert.That(p2Request.SelectedService, Is.EqualTo(PitService.None));
            Assert.That(p1Request.RequestPit, Is.True);
            Assert.That(p2Request.RequestPit, Is.True);
        }

        private static PitActionMachine Machine() => new PitActionMachine(
            new Vec2(100f, 100f), new Vec2(20f, 20f), 0f, 0.2f, 1f);
        private static CrewStrategyAdapter StrategyAdapter() => new CrewStrategyAdapter(
            new Vec2(200f, 100f), new Vec2(100f, 100f), new Vec2(300f, 100f),
            new Vec2(20f, 20f), 0f, .2f, 1f);
        private static PieceState Crew(bool present, bool touched, float x, float y, float angle) =>
            new PieceState(present, touched, 1, new Vec2(x, y), angle);
        private static RacerPitSnapshot Pit(PitService service, PitPhase phase) =>
            new RacerPitSnapshot(service, phase, 0f, 0, false);
        private static PlayerControlSnapshot StrategyControls(PieceState crew,
            InputWarning warning = InputWarning.None) =>
            new PlayerControlSnapshot(PlayerId.Player1, ThrottleStep.Off, PieceState.Missing, crew, warning);

        private static ContactSnapshotReconciler Reconciler() =>
            new ContactSnapshotReconciler(TrancheOneAssignments.All, 0.1f, 540f);

        private static RawPieceContact Contact(int contactId, int glyphId, bool touched, float y,
            RawContactPhase phase = RawContactPhase.Stationary) =>
            new RawPieceContact(contactId, glyphId, new Vec2(100f, y), 0f, touched, phase);

        private static PlayerControlSnapshot Player(System.Collections.Generic.IReadOnlyList<PlayerControlSnapshot> snapshots,
            PlayerId playerId) => snapshots.Single(x => x.PlayerId == playerId);

        private static void ArmCar(ContactSnapshotReconciler reconciler, int contactId)
        {
            reconciler.Reconcile(new[] { Contact(contactId, 2, false, 100f) });
            Assert.That(Player(reconciler.Reconcile(new[] { Contact(contactId, 2, true, 100f) }), PlayerId.Player1).Throttle,
                Is.EqualTo(ThrottleStep.Quarter));
        }
    }
}
