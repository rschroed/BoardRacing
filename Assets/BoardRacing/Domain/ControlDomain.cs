using System;
using System.Collections.Generic;
using System.Linq;

namespace BoardRacing.Domain
{
    public enum PlayerId { Player1 = 1, Player2 = 2 }
    public enum PieceRole { Car, Crew }
    public enum ThrottleStep { Off = 0, Quarter = 25, Half = 50, ThreeQuarters = 75, Full = 100 }
    [Flags] public enum InputWarning { None = 0, UnassignedGlyph = 1, DuplicateGlyph = 2, WrongRegion = 4 }
    public enum PitActionState { Idle, Positioned, Aligning, Holding, Completed, Canceled }

    public readonly struct Vec2
    {
        public Vec2(float x, float y) { X = x; Y = y; }
        public float X { get; }
        public float Y { get; }
    }

    public readonly struct PieceAssignment
    {
        public PieceAssignment(PlayerId playerId, PieceRole role, int glyphId, string displayName, string visualIdentity)
        {
            PlayerId = playerId; Role = role; GlyphId = glyphId;
            DisplayName = displayName; VisualIdentity = visualIdentity;
        }
        public PlayerId PlayerId { get; }
        public PieceRole Role { get; }
        public int GlyphId { get; }
        public string DisplayName { get; }
        public string VisualIdentity { get; }
    }

    public readonly struct PieceState
    {
        public PieceState(bool present, bool touched, int contactId, Vec2 position, float orientationRadians,
            bool requiresRelease = false)
        { Present = present; Touched = touched; ContactId = contactId; Position = position; OrientationRadians = orientationRadians; RequiresRelease = requiresRelease; }
        public bool Present { get; }
        public bool Touched { get; }
        public int ContactId { get; }
        public Vec2 Position { get; }
        public float OrientationRadians { get; }
        public bool RequiresRelease { get; }
        public static PieceState Missing => new PieceState(false, false, -1, new Vec2(), 0f);
    }

    public readonly struct PlayerControlSnapshot
    {
        public PlayerControlSnapshot(PlayerId playerId, ThrottleStep throttle, PieceState car, PieceState crew, InputWarning warnings)
        { PlayerId = playerId; Throttle = throttle; Car = car; Crew = crew; Warnings = warnings; }
        public PlayerId PlayerId { get; }
        public ThrottleStep Throttle { get; }
        public PieceState Car { get; }
        public PieceState Crew { get; }
        public InputWarning Warnings { get; }
    }

    public interface IPlayerInputProvider
    {
        IReadOnlyList<PlayerControlSnapshot> ReadSnapshots();
    }

    public static class TrancheOneAssignments
    {
        public static readonly PieceAssignment[] All =
        {
            new PieceAssignment(PlayerId.Player1, PieceRole.Car, 2, "Orange Robot", "Orange / Robot"),
            new PieceAssignment(PlayerId.Player1, PieceRole.Crew, 7, "Orange Ship", "Orange / Ship"),
            new PieceAssignment(PlayerId.Player2, PieceRole.Car, 1, "Purple Robot", "Purple / Robot"),
            new PieceAssignment(PlayerId.Player2, PieceRole.Crew, 6, "Purple Ship", "Purple / Ship")
        };

        public static string[] Validate(IEnumerable<PieceAssignment> assignments)
        {
            var all = assignments.ToArray();
            var errors = new List<string>();
            foreach (var duplicate in all.GroupBy(x => x.GlyphId).Where(x => x.Count() > 1))
                errors.Add($"Glyph {duplicate.Key} is assigned more than once.");
            foreach (PlayerId player in Enum.GetValues(typeof(PlayerId)))
                foreach (PieceRole role in Enum.GetValues(typeof(PieceRole)))
                    if (!all.Any(x => x.PlayerId == player && x.Role == role))
                        errors.Add($"{player} is missing a {role} assignment.");
            return errors.ToArray();
        }
    }
}
