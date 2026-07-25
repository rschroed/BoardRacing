using System;
using System.Collections.Generic;
using System.Linq;

namespace BoardRacing.Domain
{
    public enum SeatCorner
    {
        LowerRight,
        UpperLeft,
        LowerLeft,
        UpperRight
    }

    public readonly struct SessionPlayer
    {
        public SessionPlayer(int sessionId, string playerId, string displayName, string avatarId)
        {
            if (sessionId < 0) throw new ArgumentOutOfRangeException(nameof(sessionId));
            SessionId = sessionId;
            PlayerId = playerId ?? string.Empty;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Guest" : displayName;
            AvatarId = avatarId ?? string.Empty;
        }

        public int SessionId { get; }
        public string PlayerId { get; }
        public string DisplayName { get; }
        public string AvatarId { get; }
    }

    public readonly struct PieceIdentity
    {
        public PieceIdentity(int shipGlyphId, int robotGlyphId, string colorName,
            string symbol, string visualIdentity)
        {
            ShipGlyphId = shipGlyphId;
            RobotGlyphId = robotGlyphId;
            ColorName = colorName;
            Symbol = symbol;
            VisualIdentity = visualIdentity;
        }

        public int ShipGlyphId { get; }
        public int RobotGlyphId { get; }
        public string ColorName { get; }
        public string Symbol { get; }
        public string VisualIdentity { get; }
    }

    public static class PhysicalPieceCatalog
    {
        public static readonly PieceIdentity[] All =
        {
            new PieceIdentity(7, 2, "Orange", "▲", "Orange / Triangle"),
            new PieceIdentity(6, 1, "Purple", "●", "Purple / Circle"),
            new PieceIdentity(4, 3, "Pink", "◆", "Pink / Diamond"),
            new PieceIdentity(5, 0, "Yellow", "■", "Yellow / Square")
        };

        public static bool TryForShipGlyph(int glyphId, out PieceIdentity identity)
        {
            foreach (PieceIdentity candidate in All)
            {
                if (candidate.ShipGlyphId != glyphId) continue;
                identity = candidate;
                return true;
            }
            identity = default;
            return false;
        }
    }

    public readonly struct SeatClaimRegion
    {
        public SeatClaimRegion(PlayerId playerId, SeatCorner corner,
            float minX, float minY, float maxX, float maxY, float seatRotationRadians)
        {
            if (minX >= maxX || minY >= maxY)
                throw new ArgumentException("A seat claim region must have positive area.");
            PlayerId = playerId;
            Corner = corner;
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
            SeatRotationRadians = seatRotationRadians;
        }

        public PlayerId PlayerId { get; }
        public SeatCorner Corner { get; }
        public float MinX { get; }
        public float MinY { get; }
        public float MaxX { get; }
        public float MaxY { get; }
        public float SeatRotationRadians { get; }
        public bool Contains(Vec2 point) =>
            point.X >= MinX && point.X <= MaxX && point.Y >= MinY && point.Y <= MaxY;
    }

    public static class FourSeatLayout
    {
        public const float Width = 1920f;
        public const float Height = 1080f;

        // These are deliberately generous placement zones, not the final compact
        // race controls. They leave the shared center clear and make the initial
        // physical claim easy from every side of the table.
        public static readonly SeatClaimRegion[] ClaimRegions =
        {
            new SeatClaimRegion(PlayerId.Player1, SeatCorner.LowerRight,
                1450f, 0f, Width, 320f, 0f),
            new SeatClaimRegion(PlayerId.Player2, SeatCorner.UpperLeft,
                0f, 760f, 470f, Height, (float)Math.PI),
            new SeatClaimRegion(PlayerId.Player3, SeatCorner.LowerLeft,
                0f, 0f, 470f, 320f, (float)(Math.PI * .5)),
            new SeatClaimRegion(PlayerId.Player4, SeatCorner.UpperRight,
                1450f, 760f, Width, Height, (float)(Math.PI * 1.5))
        };

        public static readonly SeatClaimRegion[] InputRegions =
        {
            new SeatClaimRegion(PlayerId.Player1, SeatCorner.LowerRight,
                Width * .5f, 0f, Width, Height * .5f, 0f),
            new SeatClaimRegion(PlayerId.Player2, SeatCorner.UpperLeft,
                0f, Height * .5f, Width * .5f, Height, (float)Math.PI),
            new SeatClaimRegion(PlayerId.Player3, SeatCorner.LowerLeft,
                0f, 0f, Width * .5f, Height * .5f, (float)(Math.PI * .5)),
            new SeatClaimRegion(PlayerId.Player4, SeatCorner.UpperRight,
                Width * .5f, Height * .5f, Width, Height, (float)(Math.PI * 1.5))
        };

        public static SeatClaimRegion For(PlayerId playerId) =>
            ClaimRegions.Single(x => x.PlayerId == playerId);

        public static SeatClaimRegion InputFor(PlayerId playerId) =>
            InputRegions.Single(x => x.PlayerId == playerId);
    }

    public readonly struct PlayerSeat
    {
        public PlayerSeat(PlayerId playerId, SessionPlayer player, SeatCorner corner,
            PieceIdentity? pieceIdentity)
        {
            PlayerId = playerId;
            Player = player;
            Corner = corner;
            PieceIdentity = pieceIdentity;
        }

        public PlayerId PlayerId { get; }
        public SessionPlayer Player { get; }
        public SeatCorner Corner { get; }
        public PieceIdentity? PieceIdentity { get; }
        public bool IsClaimed => PieceIdentity.HasValue;
    }

    public sealed class PlayerSetupCoordinator
    {
        private sealed class MutableSeat
        {
            public PlayerId PlayerId;
            public SessionPlayer Player;
            public SeatCorner Corner;
            public PieceIdentity? PieceIdentity;
        }

        private static readonly PlayerId[] SeatOrder =
            { PlayerId.Player1, PlayerId.Player2, PlayerId.Player3, PlayerId.Player4 };

        private readonly Dictionary<PlayerId, MutableSeat> seats =
            new Dictionary<PlayerId, MutableSeat>();

        public PlayerSetupCoordinator(IEnumerable<SessionPlayer> roster)
        {
            SynchronizeRoster(roster);
        }

        public IReadOnlyList<PlayerSeat> Seats => seats.Values
            .OrderBy(x => x.PlayerId)
            .Select(x => new PlayerSeat(x.PlayerId, x.Player, x.Corner, x.PieceIdentity))
            .ToArray();

        public bool CanStart => seats.Count >= 2 && seats.Values.All(x => x.PieceIdentity.HasValue);

        // Session IDs own seats. BoardOS replacement keeps the session ID, so a
        // profile edit preserves the corner and physical identity. A removed ID
        // releases both; newly-added players fill the first free approved corner.
        public void SynchronizeRoster(IEnumerable<SessionPlayer> roster,
            PlayerId? preferredSeatForNewPlayer = null)
        {
            SessionPlayer[] players = (roster ?? Array.Empty<SessionPlayer>()).Take(4).ToArray();
            if (players.Select(x => x.SessionId).Distinct().Count() != players.Length)
                throw new ArgumentException("Session players must have unique session IDs.", nameof(roster));

            var bySession = seats.Values.ToDictionary(x => x.Player.SessionId);
            var retained = new Dictionary<PlayerId, MutableSeat>();
            foreach (SessionPlayer player in players)
            {
                if (!bySession.TryGetValue(player.SessionId, out MutableSeat seat)) continue;
                seat.Player = player;
                retained[seat.PlayerId] = seat;
            }

            foreach (SessionPlayer player in players)
            {
                if (retained.Values.Any(x => x.Player.SessionId == player.SessionId)) continue;
                PlayerId id = preferredSeatForNewPlayer.HasValue &&
                    !retained.ContainsKey(preferredSeatForNewPlayer.Value)
                    ? preferredSeatForNewPlayer.Value
                    : SeatOrder.First(x => !retained.ContainsKey(x));
                preferredSeatForNewPlayer = null;
                SeatClaimRegion region = FourSeatLayout.For(id);
                retained[id] = new MutableSeat
                {
                    PlayerId = id,
                    Player = player,
                    Corner = region.Corner
                };
            }

            seats.Clear();
            foreach (var pair in retained) seats[pair.Key] = pair.Value;
        }

        // Setup identity is deliberately live, not latched. Moving or swapping
        // Ships updates the affected corners immediately; the race freezes the
        // current mapping only when Start Game is pressed. Duplicate/ambiguous
        // input leaves an affected seat neutral.
        public void Observe(IEnumerable<RawPieceContact> snapshot)
        {
            RawPieceContact[] activeShips = (snapshot ?? Array.Empty<RawPieceContact>())
                .Where(x => x.IsActive && PhysicalPieceCatalog.TryForShipGlyph(x.GlyphId, out _))
                .ToArray();
            var uniqueByGlyph = activeShips.GroupBy(x => x.GlyphId)
                .Where(x => x.Count() == 1)
                .ToDictionary(x => x.Key, x => x.Single());
            foreach (MutableSeat seat in seats.Values.OrderBy(x => x.PlayerId))
            {
                SeatClaimRegion region = FourSeatLayout.For(seat.PlayerId);
                RawPieceContact[] candidates = uniqueByGlyph.Values
                    .Where(x => region.Contains(x.Position))
                    .ToArray();
                if (candidates.Length != 1)
                {
                    seat.PieceIdentity = null;
                    continue;
                }
                PhysicalPieceCatalog.TryForShipGlyph(candidates[0].GlyphId, out PieceIdentity identity);
                seat.PieceIdentity = identity;
            }
        }

        public bool ClaimForFallback(PlayerId playerId, int shipGlyphId)
        {
            if (!seats.TryGetValue(playerId, out MutableSeat seat) ||
                !PhysicalPieceCatalog.TryForShipGlyph(shipGlyphId, out PieceIdentity identity) ||
                seats.Values.Any(x => x.PlayerId != playerId && x.PieceIdentity.HasValue &&
                    x.PieceIdentity.Value.ShipGlyphId == shipGlyphId))
                return false;
            seat.PieceIdentity = identity;
            return true;
        }

        public PieceAssignment[] BuildPieceAssignments()
        {
            var result = new List<PieceAssignment>(seats.Count * 2);
            foreach (MutableSeat seat in seats.Values.OrderBy(x => x.PlayerId))
            {
                if (!seat.PieceIdentity.HasValue) continue;
                PieceIdentity identity = seat.PieceIdentity.Value;
                result.Add(new PieceAssignment(seat.PlayerId, PieceRole.Car, identity.ShipGlyphId,
                    identity.ColorName + " Driving Ship", identity.VisualIdentity + " / Ship"));
                result.Add(new PieceAssignment(seat.PlayerId, PieceRole.Crew, identity.RobotGlyphId,
                    identity.ColorName + " Pit Robot", identity.VisualIdentity + " / Robot"));
            }
            return result.ToArray();
        }
    }
}
