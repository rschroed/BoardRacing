using System;
using System.Collections.Generic;
using System.Linq;

namespace BoardRacing.Domain
{
    public enum RawContactPhase { Began, Moved, Stationary, Ended, Canceled }

    public readonly struct RawPieceContact
    {
        public RawPieceContact(int contactId, int glyphId, Vec2 position, float orientationRadians,
            bool touched, RawContactPhase phase)
        {
            ContactId = contactId;
            GlyphId = glyphId;
            Position = position;
            OrientationRadians = orientationRadians;
            Touched = touched;
            Phase = phase;
        }

        public int ContactId { get; }
        public int GlyphId { get; }
        public Vec2 Position { get; }
        public float OrientationRadians { get; }
        public bool Touched { get; }
        public RawContactPhase Phase { get; }
        public bool IsActive => Phase == RawContactPhase.Began || Phase == RawContactPhase.Moved || Phase == RawContactPhase.Stationary;
    }

    public sealed class ContactSnapshotReconciler
    {
        private readonly Dictionary<int, PieceAssignment> assignments;
        private readonly Dictionary<PlayerId, CoarseThrottleMapper> throttleMappers;
        private readonly PlayerId[] activePlayers;
        private readonly Dictionary<PlayerId, SeatClaimRegion> playerRegions;
        private readonly Dictionary<int, int> trackedContactIds = new Dictionary<int, int>();
        private readonly HashSet<int> awaitingSafeRelease = new HashSet<int>();

        public ContactSnapshotReconciler(IEnumerable<PieceAssignment> assignments,
            ThrottleStops throttleStops, float throttleHysteresisRadians, float playerRegionBoundaryY)
            : this(assignments, TrancheOneAssignments.ActivePlayers, throttleStops,
                throttleHysteresisRadians, new[]
                {
                    new SeatClaimRegion(PlayerId.Player1, SeatCorner.LowerRight,
                        0f, 0f, FourSeatLayout.Width, playerRegionBoundaryY, 0f),
                    new SeatClaimRegion(PlayerId.Player2, SeatCorner.UpperLeft,
                        0f, playerRegionBoundaryY, FourSeatLayout.Width, FourSeatLayout.Height,
                        (float)Math.PI)
                })
        {
        }

        public ContactSnapshotReconciler(IEnumerable<PieceAssignment> assignments,
            IEnumerable<PlayerId> activePlayers, ThrottleStops throttleStops,
            float throttleHysteresisRadians, IEnumerable<SeatClaimRegion> playerRegions)
        {
            var all = assignments.ToArray();
            this.activePlayers = activePlayers.ToArray();
            SeatClaimRegion[] regions = playerRegions.ToArray();
            var errors = TrancheOneAssignments.Validate(all, this.activePlayers);
            if (errors.Length > 0) throw new ArgumentException(string.Join(" ", errors), nameof(assignments));
            if (regions.Select(x => x.PlayerId).Distinct().Count() != this.activePlayers.Length ||
                this.activePlayers.Any(x => regions.All(region => region.PlayerId != x)))
                throw new ArgumentException("Every active player needs one input region.",
                    nameof(playerRegions));
            this.assignments = all.ToDictionary(x => x.GlyphId);
            this.playerRegions = regions.ToDictionary(x => x.PlayerId);
            throttleMappers = this.activePlayers
                .ToDictionary(x => x, id => new CoarseThrottleMapper(throttleHysteresisRadians,
                    throttleStops, this.playerRegions[id].SeatRotationRadians,
                    this.playerRegions[id].MirroredOrientation));
        }

        public IReadOnlyList<PlayerControlSnapshot> Reconcile(IEnumerable<RawPieceContact> snapshot)
        {
            var all = snapshot.ToArray();
            var active = all.Where(x => x.IsActive).ToArray();
            var activeByGlyph = active.GroupBy(x => x.GlyphId).ToDictionary(x => x.Key, x => x.ToArray());
            bool hasUnassigned = active.Any(x => !assignments.ContainsKey(x.GlyphId));
            var result = new List<PlayerControlSnapshot>(activePlayers.Length);

            foreach (PlayerId player in activePlayers)
            {
                InputWarning warning = hasUnassigned ? InputWarning.UnassignedGlyph : InputWarning.None;
                var car = Resolve(player, PieceRole.Car, activeByGlyph, ref warning);
                var crew = Resolve(player, PieceRole.Crew, activeByGlyph, ref warning);
                bool carInWrongRegion = car.Present && !playerRegions[player].Contains(car.Position);
                var throttle = throttleMappers[player].Map(car.Present && !carInWrongRegion,
                    car.OrientationRadians);
                result.Add(new PlayerControlSnapshot(player, throttle, car, crew, warning));
            }

            return result;
        }

        public void ResetAll()
        {
            trackedContactIds.Clear();
            awaitingSafeRelease.Clear();
            foreach (var mapper in throttleMappers.Values) mapper.Reset();
        }

        private PieceState Resolve(PlayerId player, PieceRole role,
            IReadOnlyDictionary<int, RawPieceContact[]> activeByGlyph, ref InputWarning warning)
        {
            var assignment = assignments.Values.Single(x => x.PlayerId == player && x.Role == role);
            if (!activeByGlyph.TryGetValue(assignment.GlyphId, out var matches) || matches.Length == 0)
            {
                awaitingSafeRelease.Add(assignment.GlyphId);
                if (role == PieceRole.Car) throttleMappers[player].Reset();
                return PieceState.Missing;
            }

            if (matches.Length > 1)
            {
                warning |= InputWarning.DuplicateGlyph;
                awaitingSafeRelease.Add(assignment.GlyphId);
                if (role == PieceRole.Car) throttleMappers[player].Reset();
                return PieceState.Missing;
            }

            var contact = matches[0];
            if (!trackedContactIds.TryGetValue(assignment.GlyphId, out int priorId) || priorId != contact.ContactId)
            {
                trackedContactIds[assignment.GlyphId] = contact.ContactId;
                awaitingSafeRelease.Add(assignment.GlyphId);
                if (role == PieceRole.Car) throttleMappers[player].Reset();
            }

            bool wrongRegion = !playerRegions[player].Contains(contact.Position);
            if (wrongRegion) warning |= InputWarning.WrongRegion;

            bool safeTouched = contact.Touched;
            bool requiresRelease = false;
            if (awaitingSafeRelease.Contains(assignment.GlyphId))
            {
                safeTouched = false;
                requiresRelease = contact.Touched;
                if (!contact.Touched) awaitingSafeRelease.Remove(assignment.GlyphId);
            }

            return new PieceState(true, safeTouched, contact.ContactId, contact.Position,
                contact.OrientationRadians, requiresRelease);
        }
    }
}
