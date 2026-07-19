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
        private readonly Dictionary<int, int> trackedContactIds = new Dictionary<int, int>();
        private readonly HashSet<int> awaitingSafeRelease = new HashSet<int>();
        private readonly float playerRegionBoundaryY;

        public ContactSnapshotReconciler(IEnumerable<PieceAssignment> assignments,
            ThrottleStops throttleStops, float throttleHysteresisRadians, float playerRegionBoundaryY)
        {
            var all = assignments.ToArray();
            var errors = TrancheOneAssignments.Validate(all);
            if (errors.Length > 0) throw new ArgumentException(string.Join(" ", errors), nameof(assignments));
            this.assignments = all.ToDictionary(x => x.GlyphId);
            // Player 2's seat is the 180° rotation of Player 1's, so the same measured
            // stops apply after removing the seat rotation from the raw orientation.
            throttleMappers = Enum.GetValues(typeof(PlayerId)).Cast<PlayerId>()
                .ToDictionary(x => x, id => new CoarseThrottleMapper(throttleHysteresisRadians,
                    throttleStops, id == PlayerId.Player1 ? 0f : (float)Math.PI));
            this.playerRegionBoundaryY = playerRegionBoundaryY;
        }

        public IReadOnlyList<PlayerControlSnapshot> Reconcile(IEnumerable<RawPieceContact> snapshot)
        {
            var all = snapshot.ToArray();
            var active = all.Where(x => x.IsActive).ToArray();
            var activeByGlyph = active.GroupBy(x => x.GlyphId).ToDictionary(x => x.Key, x => x.ToArray());
            bool hasUnassigned = active.Any(x => !assignments.ContainsKey(x.GlyphId));
            var result = new List<PlayerControlSnapshot>(2);

            foreach (PlayerId player in Enum.GetValues(typeof(PlayerId)))
            {
                InputWarning warning = hasUnassigned ? InputWarning.UnassignedGlyph : InputWarning.None;
                var car = Resolve(player, PieceRole.Car, activeByGlyph, ref warning);
                var crew = Resolve(player, PieceRole.Crew, activeByGlyph, ref warning);
                bool carInWrongRegion = car.Present && (player == PlayerId.Player1
                    ? car.Position.Y >= playerRegionBoundaryY
                    : car.Position.Y < playerRegionBoundaryY);
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

            bool wrongRegion = player == PlayerId.Player1
                ? contact.Position.Y >= playerRegionBoundaryY
                : contact.Position.Y < playerRegionBoundaryY;
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
