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

        private static PitActionMachine Machine() => new PitActionMachine(
            new Vec2(100f, 100f), new Vec2(20f, 20f), 0f, 0.2f, 1f);
        private static PieceState Crew(bool present, bool touched, float x, float y, float angle) =>
            new PieceState(present, touched, 1, new Vec2(x, y), angle);

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
