using System;
using System.Linq;
using BoardRacing.Domain;
using NUnit.Framework;

namespace BoardRacing.Tests
{
    public sealed class PlayerSetupTests
    {
        [Test]
        public void SessionRosterDoesNotOwnCornersUntilExplicitlyAssigned()
        {
            SessionPlayer[] roster = Roster(2);
            var setup = new PlayerSetupCoordinator(Array.Empty<SessionPlayer>());

            Assert.That(setup.Seats, Is.Empty);
            Assert.That(setup.AssignPlayer(roster[0], PlayerId.Player4), Is.True);
            Assert.That(setup.Seats.Single().PlayerId, Is.EqualTo(PlayerId.Player4));
            Assert.That(setup.Seats.Single().Corner, Is.EqualTo(SeatCorner.UpperRight));
            Assert.That(setup.AssignPlayer(roster[0], PlayerId.Player1), Is.False,
                "one BoardOS player cannot occupy two corners");
        }

        [Test]
        public void RetainingRosterUpdatesAndRemovesSeatsWithoutAutoAssigningNewPlayers()
        {
            SessionPlayer[] roster = Roster(2);
            var setup = new PlayerSetupCoordinator(Array.Empty<SessionPlayer>());
            setup.AssignPlayer(roster[0], PlayerId.Player3);
            setup.ClaimForFallback(PlayerId.Player3, 4);

            setup.RetainRoster(new[]
            {
                new SessionPlayer(1, "profile-1", "Renamed", "avatar-new"),
                roster[1],
                new SessionPlayer(3, "profile-3", "Unseated", "avatar-3")
            });

            Assert.That(setup.Seats.Count, Is.EqualTo(1));
            Assert.That(setup.Seats.Single().Player.DisplayName, Is.EqualTo("Renamed"));
            Assert.That(setup.Seats.Single().PieceIdentity.Value.ShipGlyphId, Is.EqualTo(4));

            setup.RetainRoster(new[] { roster[1] });
            Assert.That(setup.Seats, Is.Empty);
        }

        [Test]
        public void RosterOrderAssignsApprovedNeutralCorners()
        {
            var setup = new PlayerSetupCoordinator(Roster(4));

            Assert.That(setup.Seats.Select(x => x.Corner), Is.EqualTo(new[]
            {
                SeatCorner.LowerRight, SeatCorner.UpperLeft,
                SeatCorner.LowerLeft, SeatCorner.UpperRight
            }));
            Assert.That(setup.Seats.All(x => !x.IsClaimed), Is.True);
            Assert.That(setup.CanStart, Is.False);
        }

        [Test]
        public void FirstUnclaimedShipInEachCornerAssignsPhysicalIdentity()
        {
            var setup = new PlayerSetupCoordinator(Roster(3));
            setup.Observe(new[]
            {
                Contact(10, 6, 1700f, 150f),
                Contact(20, 7, 200f, 900f),
                Contact(30, 5, 200f, 150f)
            });

            Assert.That(setup.Seats.Select(x => x.PieceIdentity.Value.ColorName),
                Is.EqualTo(new[] { "Purple", "Orange", "Yellow" }));
            Assert.That(setup.CanStart, Is.True);
            Assert.That(setup.BuildPieceAssignments().Select(x => x.GlyphId),
                Is.EquivalentTo(new[] { 6, 1, 7, 2, 5, 0 }));
        }

        [Test]
        public void ShipsCanSwapCornersBeforeTheRaceStarts()
        {
            var setup = new PlayerSetupCoordinator(Roster(2));
            setup.Observe(new[] { Contact(10, 7, 1700f, 150f) });
            setup.Observe(new[]
            {
                Contact(11, 6, 1700f, 150f),
                Contact(12, 7, 200f, 900f)
            });

            Assert.That(setup.Seats.Single(x => x.PlayerId == PlayerId.Player1)
                .PieceIdentity.Value.ShipGlyphId, Is.EqualTo(6));
            Assert.That(setup.Seats.Single(x => x.PlayerId == PlayerId.Player2)
                .PieceIdentity.Value.ShipGlyphId, Is.EqualTo(7));
            Assert.That(setup.CanStart, Is.True);
        }

        [Test]
        public void DuplicateGlyphAndMultipleShipsInOneCornerDoNotMutate()
        {
            var duplicate = new PlayerSetupCoordinator(Roster(2));
            duplicate.Observe(new[]
            {
                Contact(10, 7, 1700f, 150f),
                Contact(11, 7, 1710f, 160f)
            });
            Assert.That(duplicate.Seats.All(x => !x.IsClaimed), Is.True);

            var ambiguous = new PlayerSetupCoordinator(Roster(2));
            ambiguous.Observe(new[]
            {
                Contact(10, 7, 1700f, 150f),
                Contact(11, 6, 1710f, 160f)
            });
            Assert.That(ambiguous.Seats.All(x => !x.IsClaimed), Is.True);
        }

        [Test]
        public void ReplacementPreservesSeatAndClaimWhileRemovalReleasesThem()
        {
            var setup = new PlayerSetupCoordinator(Roster(3));
            setup.ClaimForFallback(PlayerId.Player1, 7);
            setup.ClaimForFallback(PlayerId.Player2, 6);
            setup.ClaimForFallback(PlayerId.Player3, 4);

            setup.SynchronizeRoster(new[]
            {
                new SessionPlayer(1, "profile-a", "Renamed", "avatar-new"),
                new SessionPlayer(3, "profile-c", "Player 3", "avatar-3")
            });

            PlayerSeat retained = setup.Seats.Single(x => x.Player.SessionId == 1);
            Assert.That(retained.PlayerId, Is.EqualTo(PlayerId.Player1));
            Assert.That(retained.Player.DisplayName, Is.EqualTo("Renamed"));
            Assert.That(retained.PieceIdentity.Value.ShipGlyphId, Is.EqualTo(7));
            Assert.That(setup.Seats.Single(x => x.Player.SessionId == 3).PlayerId,
                Is.EqualTo(PlayerId.Player3));

            setup.SynchronizeRoster(new[]
            {
                new SessionPlayer(1, "profile-a", "Renamed", "avatar-new"),
                new SessionPlayer(3, "profile-c", "Player 3", "avatar-3"),
                new SessionPlayer(4, "profile-d", "New Player", "avatar-4")
            });
            PlayerSeat added = setup.Seats.Single(x => x.Player.SessionId == 4);
            Assert.That(added.PlayerId, Is.EqualTo(PlayerId.Player2));
            Assert.That(added.IsClaimed, Is.False);
        }

        [Test]
        public void AtLeastTwoClaimedPlayersAreRequiredToStart()
        {
            var setup = new PlayerSetupCoordinator(Roster(1));
            setup.ClaimForFallback(PlayerId.Player1, 7);
            Assert.That(setup.CanStart, Is.False);

            setup.SynchronizeRoster(Roster(2));
            setup.ClaimForFallback(PlayerId.Player2, 6);
            Assert.That(setup.CanStart, Is.True);
        }

        [Test]
        public void NewlyAddedPlayerCanOwnTheCornerThatOpenedTheSelector()
        {
            var setup = new PlayerSetupCoordinator(Roster(1));
            setup.SynchronizeRoster(new[]
            {
                new SessionPlayer(1, "profile-1", "Player 1", "avatar-1"),
                new SessionPlayer(2, "profile-2", "Player 2", "avatar-2")
            }, PlayerId.Player4);

            Assert.That(setup.Seats.Single(x => x.Player.SessionId == 2).PlayerId,
                Is.EqualTo(PlayerId.Player4));
            Assert.That(setup.Seats.Single(x => x.Player.SessionId == 2).Corner,
                Is.EqualTo(SeatCorner.UpperRight));
        }

        [Test]
        public void ClaimedFourPlayerAssignmentsDriveTheSharedReconciler()
        {
            var setup = new PlayerSetupCoordinator(Roster(4));
            setup.ClaimForFallback(PlayerId.Player1, 7);
            setup.ClaimForFallback(PlayerId.Player2, 6);
            setup.ClaimForFallback(PlayerId.Player3, 4);
            setup.ClaimForFallback(PlayerId.Player4, 5);
            PlayerId[] ids = setup.Seats.Select(x => x.PlayerId).ToArray();
            var stops = new ThrottleStops(Deg(275f), Deg(225f), Deg(175f));
            var reconciler = new ContactSnapshotReconciler(setup.BuildPieceAssignments(), ids,
                stops, Deg(8f), ids.Select(FourSeatLayout.InputFor));

            var snapshots = reconciler.Reconcile(new[]
            {
                Contact(10, 7, 1700f, 150f, Deg(275f)),
                Contact(20, 6, 200f, 900f, Deg(95f)),
                Contact(30, 4, 200f, 150f, Deg(85f)),
                Contact(40, 5, 1700f, 900f, Deg(265f))
            });

            Assert.That(snapshots.Count, Is.EqualTo(4));
            Assert.That(snapshots.All(x => x.Car.Present &&
                x.Throttle == ThrottleStep.Brake &&
                !x.Warnings.HasFlag(InputWarning.WrongRegion)), Is.True);
        }

        [Test]
        public void MirroredCornersMapTheirRenderedDriveDirections()
        {
            var setup = new PlayerSetupCoordinator(Roster(4));
            setup.ClaimForFallback(PlayerId.Player1, 7);
            setup.ClaimForFallback(PlayerId.Player2, 6);
            setup.ClaimForFallback(PlayerId.Player3, 4);
            setup.ClaimForFallback(PlayerId.Player4, 5);
            PlayerId[] ids = setup.Seats.Select(x => x.PlayerId).ToArray();
            var reconciler = new ContactSnapshotReconciler(setup.BuildPieceAssignments(), ids,
                new ThrottleStops(Deg(275f), Deg(225f), Deg(175f)), Deg(8f),
                ids.Select(FourSeatLayout.InputFor));

            var snapshots = reconciler.Reconcile(new[]
            {
                Contact(10, 7, 1700f, 150f, Deg(225f)),
                Contact(20, 6, 200f, 900f, Deg(45f)),
                // Board glyph orientation is counter-clockwise from vertical.
                // Horizontally mirroring the two proven cockpits therefore
                // reverses their raw quarter-turn offsets.
                Contact(30, 4, 200f, 150f, Deg(135f)),
                Contact(40, 5, 1700f, 900f, Deg(315f))
            });

            Assert.That(snapshots.All(x => x.Throttle == ThrottleStep.Drive), Is.True);
        }

        [Test]
        public void MirroredCornersKeepBrakeAndBoostOnTheirRenderedSides()
        {
            var setup = new PlayerSetupCoordinator(Roster(4));
            setup.ClaimForFallback(PlayerId.Player1, 7);
            setup.ClaimForFallback(PlayerId.Player2, 6);
            setup.ClaimForFallback(PlayerId.Player3, 4);
            setup.ClaimForFallback(PlayerId.Player4, 5);
            PlayerId[] ids = setup.Seats.Select(x => x.PlayerId).ToArray();
            var reconciler = new ContactSnapshotReconciler(setup.BuildPieceAssignments(), ids,
                new ThrottleStops(Deg(275f), Deg(225f), Deg(175f)), Deg(8f),
                ids.Select(FourSeatLayout.InputFor));

            var brake = reconciler.Reconcile(new[]
            {
                Contact(10, 7, 1700f, 150f, Deg(275f)),
                Contact(20, 6, 200f, 900f, Deg(95f)),
                Contact(30, 4, 200f, 150f, Deg(85f)),
                Contact(40, 5, 1700f, 900f, Deg(265f))
            });
            Assert.That(brake.All(x => x.Throttle == ThrottleStep.Brake), Is.True);

            var boost = reconciler.Reconcile(new[]
            {
                Contact(10, 7, 1700f, 150f, Deg(175f)),
                Contact(20, 6, 200f, 900f, Deg(355f)),
                Contact(30, 4, 200f, 150f, Deg(185f)),
                Contact(40, 5, 1700f, 900f, Deg(5f))
            });
            Assert.That(boost.All(x => x.Throttle == ThrottleStep.Boost), Is.True);
        }

        private static SessionPlayer[] Roster(int count) => Enumerable.Range(1, count)
            .Select(x => new SessionPlayer(x, "profile-" + x, "Player " + x, "avatar-" + x))
            .ToArray();

        private static RawPieceContact Contact(int contactId, int glyphId, float x, float y) =>
            Contact(contactId, glyphId, x, y, 0f);

        private static RawPieceContact Contact(int contactId, int glyphId, float x, float y,
            float orientation) =>
            new RawPieceContact(contactId, glyphId, new Vec2(x, y), orientation, false,
                RawContactPhase.Stationary);

        private static float Deg(float degrees) => degrees * (float)Math.PI / 180f;
    }
}
