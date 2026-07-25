using System;
using System.Collections.Generic;
using System.Linq;
using Board.Input;
using BoardRacing.Domain;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BoardRacing.Runtime
{
    internal sealed class PlayerLobbyPresentation : IDisposable
    {
        private static readonly PlayerId[] SeatOrder =
            { PlayerId.Player1, PlayerId.Player2, PlayerId.Player3, PlayerId.Player4 };
        private static readonly Dictionary<PlayerId, Rect> Cards =
            new Dictionary<PlayerId, Rect>
            {
                [PlayerId.Player1] = new Rect(1460f, 790f, 410f, 230f),
                [PlayerId.Player2] = new Rect(50f, 60f, 410f, 230f),
                [PlayerId.Player3] = new Rect(50f, 790f, 410f, 230f),
                [PlayerId.Player4] = new Rect(1460f, 60f, 410f, 230f)
            };

        private readonly IPlayerSession session;
        private readonly bool fallback;
        private GUIStyle heading, body, name, detail;

        public PlayerLobbyPresentation(IPlayerSession session, bool fallback)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.fallback = fallback;
            Coordinator = new PlayerSetupCoordinator(session.Players);
            session.PlayersChanged += SynchronizeRoster;
            session.ShowProfileSwitcher();
        }

        public PlayerSetupCoordinator Coordinator { get; }

        public void Update(IReadOnlyList<RawPieceContact> contacts)
        {
            Coordinator.Observe(contacts);
            if (fallback) PollFallbackClaims();
            if (!TryPressed(out Vector2 point) || session.SelectorInFlight) return;

            foreach (PlayerId id in SeatOrder)
            {
                Rect card = Cards[id];
                PlayerSeat? seat = Coordinator.Seats
                    .Where(x => x.PlayerId == id)
                    .Select(x => (PlayerSeat?)x).FirstOrDefault();
                if (!card.Contains(point)) continue;
                if (!seat.HasValue)
                {
                    _ = session.AddPlayer();
                    return;
                }
                Rect edit = EditHitRect(id, card);
                if (edit.Contains(point))
                {
                    _ = session.EditPlayer(seat.Value.Player.SessionId);
                    return;
                }
                if (fallback && !seat.Value.IsClaimed)
                {
                    ClaimFirstAvailable(id);
                    return;
                }
            }
        }

        public void Draw()
        {
            EnsureStyles();
            GUI.DrawTexture(new Rect(0f, 0f, 1920f, 1080f), Texture2D.whiteTexture,
                ScaleMode.StretchToFill, true, 0, RaceSurfaceGeometry.BackgroundColor, 0, 0);
            GUI.Label(new Rect(560f, 360f, 800f, 70f), "CHOOSE YOUR RACERS", heading);
            GUI.Label(new Rect(610f, 430f, 700f, 85f),
                Coordinator.Seats.Count < 2
                    ? "ADD AT LEAST TWO PLAYERS"
                    : "PLACE ONE SHIP IN EACH NAMED CORNER", body);
            GUI.Label(new Rect(640f, 515f, 640f, 55f),
                Coordinator.CanStart
                    ? "ALL SHIPS CLAIMED · HOLD BRAKE"
                    : fallback
                        ? "DESKTOP: CLICK A PLAYER CARD OR PRESS 1–4 TO CLAIM A PIECE"
                        : "THE FIRST UNCLAIMED SHIP SETS THAT PLAYER'S COLOR",
                detail);

            IReadOnlyList<PlayerSeat> seats = Coordinator.Seats;
            foreach (PlayerId id in SeatOrder)
            {
                PlayerSeat? seat = seats.Where(x => x.PlayerId == id)
                    .Select(x => (PlayerSeat?)x).FirstOrDefault();
                DrawSeat(id, seat);
            }

            if (session.SelectorInFlight)
                GUI.Label(new Rect(710f, 610f, 500f, 50f), "PLAYER SELECTOR OPEN…", body);
        }

        public void Dispose()
        {
            session.PlayersChanged -= SynchronizeRoster;
        }

        private void SynchronizeRoster() => Coordinator.SynchronizeRoster(session.Players);

        private void DrawSeat(PlayerId id, PlayerSeat? maybeSeat)
        {
            Rect card = Cards[id];
            bool active = maybeSeat.HasValue;
            Color accent = active && maybeSeat.Value.PieceIdentity.HasValue
                ? PlayerColors.For(maybeSeat.Value.PieceIdentity.Value)
                : new Color(.27f, .31f, .38f);
            GUI.DrawTexture(card, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                active ? new Color(accent.r * .32f, accent.g * .32f, accent.b * .32f, .96f)
                    : new Color(.08f, .1f, .14f, .95f), 0, 28f);
            DrawOutline(card, active ? 4f : 2f, active ? accent : new Color(.32f, .36f, .42f));

            float rotation = id == PlayerId.Player1 ? 0f : id == PlayerId.Player2 ? 180f
                : id == PlayerId.Player3 ? 0f : 180f;
            Matrix4x4 original = GUI.matrix;
            Vector3 pivot = card.center;
            GUI.matrix = original * Matrix4x4.Translate(pivot) *
                Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, rotation)) *
                Matrix4x4.Translate(-pivot);

            if (!active)
            {
                GUI.Label(card, session.Players.Count < 4 ? "+  ADD PLAYER" : "INACTIVE", name);
                GUI.matrix = original;
                return;
            }

            PlayerSeat seat = maybeSeat.Value;
            Texture2D avatar = session.AvatarFor(seat.Player.SessionId);
            Rect avatarRect = new Rect(card.x + 24f, card.y + 52f, 112f, 112f);
            if (avatar != null)
                GUI.DrawTexture(avatarRect, avatar, ScaleMode.ScaleToFit, true, 0,
                    Color.white, 0, 24f);
            else
                GUI.Label(avatarRect, Initials(seat.Player.DisplayName), heading);
            GUI.Label(new Rect(card.x + 150f, card.y + 58f, 225f, 54f),
                seat.Player.DisplayName, name);
            GUI.Label(new Rect(card.x + 150f, card.y + 112f, 225f, 48f),
                seat.PieceIdentity.HasValue
                    ? seat.PieceIdentity.Value.Symbol + " " +
                      seat.PieceIdentity.Value.ColorName.ToUpperInvariant() + " SHIP"
                    : "PLACE ANY SHIP", detail);
            Rect edit = new Rect(card.xMax - 92f, card.y + 14f, 76f, 42f);
            GUI.Label(edit, "EDIT", detail);
            GUI.Label(new Rect(card.x + 24f, card.yMax - 48f, card.width - 48f, 34f),
                CornerName(seat.Corner), detail);
            GUI.matrix = original;
        }

        private void PollFallbackClaims()
        {
            if (Keyboard.current == null) return;
            if (Keyboard.current.digit1Key.wasPressedThisFrame) ClaimFirstAvailable(PlayerId.Player1);
            if (Keyboard.current.digit2Key.wasPressedThisFrame) ClaimFirstAvailable(PlayerId.Player2);
            if (Keyboard.current.digit3Key.wasPressedThisFrame) ClaimFirstAvailable(PlayerId.Player3);
            if (Keyboard.current.digit4Key.wasPressedThisFrame) ClaimFirstAvailable(PlayerId.Player4);
        }

        private void ClaimFirstAvailable(PlayerId id)
        {
            var used = new HashSet<int>(Coordinator.Seats.Where(x => x.PieceIdentity.HasValue)
                .Select(x => x.PieceIdentity.Value.ShipGlyphId));
            PieceIdentity? identity = PhysicalPieceCatalog.All
                .Where(x => !used.Contains(x.ShipGlyphId))
                .Select(x => (PieceIdentity?)x).FirstOrDefault();
            if (identity.HasValue) Coordinator.ClaimForFallback(id, identity.Value.ShipGlyphId);
        }

        private static bool TryPressed(out Vector2 referencePoint)
        {
            Vector2 screen = default;
            bool pressed = false;
            foreach (BoardContact finger in BoardInput.GetActiveContacts(BoardContactType.Finger))
            {
                if (finger.phase != BoardContactPhase.Began) continue;
                screen = finger.screenPosition;
                pressed = true;
                break;
            }
            if (!pressed && Touchscreen.current != null &&
                Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            {
                screen = Touchscreen.current.primaryTouch.position.ReadValue();
                pressed = true;
            }
            else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                screen = Mouse.current.position.ReadValue();
                pressed = true;
            }
            referencePoint = new Vector2(screen.x * 1920f / Mathf.Max(1f, Screen.width),
                (Screen.height - screen.y) * 1080f / Mathf.Max(1f, Screen.height));
            return pressed;
        }

        private void EnsureStyles()
        {
            if (heading != null) return;
            heading = Style(42, Color.white);
            body = Style(27, new Color(.9f, .93f, .97f));
            name = Style(25, Color.white);
            detail = Style(17, new Color(.82f, .86f, .92f));
        }

        private static GUIStyle Style(int size, Color color) => new GUIStyle(GUI.skin.label)
        {
            fontSize = size,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            normal = { textColor = color }
        };

        private static string Initials(string value)
        {
            string[] words = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Concat(words.Take(2).Select(x => char.ToUpperInvariant(x[0])));
        }

        private static string CornerName(SeatCorner corner) =>
            corner == SeatCorner.LowerRight ? "LOWER-RIGHT CORNER" :
            corner == SeatCorner.UpperLeft ? "UPPER-LEFT CORNER" :
            corner == SeatCorner.LowerLeft ? "LOWER-LEFT CORNER" : "UPPER-RIGHT CORNER";

        private static Rect EditHitRect(PlayerId id, Rect card) =>
            id == PlayerId.Player2 || id == PlayerId.Player4
                ? new Rect(card.x + 16f, card.yMax - 56f, 76f, 42f)
                : new Rect(card.xMax - 92f, card.y + 14f, 76f, 42f);

        private static void DrawOutline(Rect rect, float width, Color color)
        {
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, width), Texture2D.whiteTexture,
                ScaleMode.StretchToFill, true, 0, color, 0, 0);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - width, rect.width, width), Texture2D.whiteTexture,
                ScaleMode.StretchToFill, true, 0, color, 0, 0);
            GUI.DrawTexture(new Rect(rect.x, rect.y, width, rect.height), Texture2D.whiteTexture,
                ScaleMode.StretchToFill, true, 0, color, 0, 0);
            GUI.DrawTexture(new Rect(rect.xMax - width, rect.y, width, rect.height), Texture2D.whiteTexture,
                ScaleMode.StretchToFill, true, 0, color, 0, 0);
        }
    }

    internal static class PlayerColors
    {
        public static Color For(PieceIdentity identity) =>
            identity.ColorName == "Orange" ? new Color(.92f, .39f, .12f) :
            identity.ColorName == "Purple" ? new Color(.48f, .28f, .72f) :
            identity.ColorName == "Pink" ? new Color(.88f, .18f, .52f) :
            new Color(.96f, .73f, .12f);
    }
}
