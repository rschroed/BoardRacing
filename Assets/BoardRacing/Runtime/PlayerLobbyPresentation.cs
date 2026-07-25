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
        // The Ship wells are the setup controls. These bounds intentionally
        // follow the cockpit circles instead of adding a second layer of cards.
        private static readonly Dictionary<PlayerId, Rect> ShipWells =
            new Dictionary<PlayerId, Rect>
            {
                [PlayerId.Player1] = new Rect(1649f, 800f, 276f, 276f),
                [PlayerId.Player2] = new Rect(-5f, 4f, 276f, 276f),
                [PlayerId.Player3] = new Rect(-5f, 800f, 276f, 276f),
                [PlayerId.Player4] = new Rect(1649f, 4f, 276f, 276f)
            };
        private static readonly Rect SetupPanel = new Rect(570f, 390f, 780f, 440f);
        private static readonly Rect CourseChip = new Rect(680f, 650f, 560f, 58f);
        private static readonly Rect StartButton = new Rect(760f, 730f, 400f, 76f);

        private readonly IPlayerSession session;
        private readonly bool fallback;
        private readonly Func<string> courseName;
        private readonly Action cycleCourse;
        private readonly HashSet<PlayerId> readyPlayers = new HashSet<PlayerId>();
        private PlayerId? pendingAddSeat;
        private HashSet<int> sessionIdsBeforeAdd;
        private bool startRequested;
        private GUIStyle heading, body, name, detail;

        public PlayerLobbyPresentation(IPlayerSession session, bool fallback,
            IEnumerable<PlayerSeat> restoredSeats = null, Func<string> courseName = null,
            Action cycleCourse = null)
        {
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.fallback = fallback;
            this.courseName = courseName ?? (() => "COURSE");
            this.cycleCourse = cycleCourse;
            Coordinator = new PlayerSetupCoordinator(Array.Empty<SessionPlayer>());
            var roster = session.Players.ToDictionary(x => x.SessionId);
            foreach (PlayerSeat seat in restoredSeats ?? Array.Empty<PlayerSeat>())
                if (roster.TryGetValue(seat.Player.SessionId, out SessionPlayer player))
                    Coordinator.AssignPlayer(player, seat.PlayerId, seat.PieceIdentity);
            session.PlayersChanged += SynchronizeRoster;
            session.ShowProfileSwitcher();
        }

        public PlayerSetupCoordinator Coordinator { get; }
        public bool AllPlayersReady => Coordinator.CanStart &&
            Coordinator.Seats.All(x => readyPlayers.Contains(x.PlayerId));
        public bool IsReady(PlayerId id) => readyPlayers.Contains(id);
        public bool ConsumeStartRequest()
        {
            bool result = startRequested;
            startRequested = false;
            return result;
        }
        public bool HasShip(PlayerId id) => Coordinator.Seats
            .Any(x => x.PlayerId == id && x.IsClaimed);
        public Color AccentFor(PlayerId id)
        {
            PlayerSeat? seat = Coordinator.Seats.Where(x => x.PlayerId == id)
                .Select(x => (PlayerSeat?)x).FirstOrDefault();
            return seat.HasValue && seat.Value.PieceIdentity.HasValue
                ? PlayerColors.For(seat.Value.PieceIdentity.Value)
                : RaceSurfaceGeometry.InactivePitBoxAccent;
        }

        public void SetReadyPlayers(IEnumerable<PlayerId> players)
        {
            if (fallback) return;
            readyPlayers.Clear();
            foreach (PlayerId player in players) readyPlayers.Add(player);
        }

        public void Update(IReadOnlyList<RawPieceContact> contacts)
        {
            if (!fallback) Coordinator.Observe(contacts);
            if (fallback)
            {
                PollFallbackClaims();
                PollFallbackReadiness();
            }
            if (!TryPressed(out Vector2 point) || session.SelectorInFlight) return;

            foreach (PlayerId id in SeatOrder)
            {
                Rect well = ShipWells[id];
                PlayerSeat? seat = Coordinator.Seats
                    .Where(x => x.PlayerId == id)
                    .Select(x => (PlayerSeat?)x).FirstOrDefault();
                if (!well.Contains(point)) continue;
                if (!seat.HasValue)
                {
                    _ = AddPlayerTo(id);
                    return;
                }
                Rect edit = EditHitRect(id, well);
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
            if (CourseChip.Contains(point))
            {
                cycleCourse?.Invoke();
                return;
            }
            if (StartButton.Contains(point) && AllPlayersReady)
                startRequested = true;
        }

        public void Draw()
        {
            EnsureStyles();
            GUI.DrawTexture(SetupPanel, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                new Color(.03f, .04f, .06f, .9f), 0, 12f);
            GUI.Label(new Rect(560f, 420f, 800f, 55f), "CHOOSE YOUR RACERS", heading);
            GUI.Label(new Rect(610f, 480f, 700f, 55f),
                Coordinator.Seats.Count < 2
                    ? "ADD AT LEAST TWO PLAYERS"
                    : "PLACE ONE SHIP IN EACH COCKPIT", body);
            GUI.Label(new Rect(640f, 535f, 640f, 72f),
                AllPlayersReady
                    ? "ALL RACERS READY"
                    : Coordinator.CanStart
                        ? "SET EVERY SHIP TO DRIVE"
                    : fallback
                        ? "DESKTOP: CLICK A COCKPIT OR PRESS 1–4 TO PLACE A SHIP"
                        : "SHIP COLORS CAN BE SWAPPED UNTIL EVERY RACER IS READY",
                detail);
            GUI.DrawTexture(CourseChip, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                new Color(.09f, .12f, .18f), 0, 8f);
            GUI.Label(CourseChip, "COURSE: " + courseName().ToUpperInvariant() +
                " · TAP TO CHANGE", body);
            Color startColor = AllPlayersReady
                ? new Color(.18f, .42f, .72f)
                : new Color(.12f, .15f, .2f);
            GUI.DrawTexture(StartButton, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                startColor, 0, 10f);
            GUI.Label(StartButton, AllPlayersReady ? "START RACE" : "START RACE · WAITING", body);

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

        private void SynchronizeRoster()
        {
            PlayerId? preferred = null;
            HashSet<int> idsBeforeAdd = sessionIdsBeforeAdd;
            if (pendingAddSeat.HasValue && sessionIdsBeforeAdd != null &&
                session.Players.Any(x => !sessionIdsBeforeAdd.Contains(x.SessionId)))
            {
                preferred = pendingAddSeat;
                pendingAddSeat = null;
                sessionIdsBeforeAdd = null;
            }
            Coordinator.RetainRoster(session.Players);
            if (preferred.HasValue)
            {
                SessionPlayer? added = session.Players
                    .Where(x => idsBeforeAdd != null && !idsBeforeAdd.Contains(x.SessionId))
                    .Select(x => (SessionPlayer?)x).FirstOrDefault();
                if (added.HasValue) Coordinator.AssignPlayer(added.Value, preferred.Value);
            }
            readyPlayers.RemoveWhere(id => Coordinator.Seats.All(x => x.PlayerId != id));
        }

        private async System.Threading.Tasks.Task AddPlayerTo(PlayerId id)
        {
            if (pendingAddSeat.HasValue || session.SelectorInFlight) return;
            var seated = new HashSet<int>(Coordinator.Seats.Select(x => x.Player.SessionId));
            SessionPlayer? available = session.Players.Where(x => !seated.Contains(x.SessionId))
                .Select(x => (SessionPlayer?)x).FirstOrDefault();
            if (available.HasValue)
            {
                Coordinator.AssignPlayer(available.Value, id);
                return;
            }
            pendingAddSeat = id;
            sessionIdsBeforeAdd = new HashSet<int>(session.Players.Select(x => x.SessionId));
            bool added = await session.AddPlayer();
            if (!added)
            {
                pendingAddSeat = null;
                sessionIdsBeforeAdd = null;
                return;
            }
            // Some implementations publish PlayersChanged before the selector
            // task completes; others update immediately after. Cover both.
            SynchronizeRoster();
        }

        private void DrawSeat(PlayerId id, PlayerSeat? maybeSeat)
        {
            Rect well = ShipWells[id];
            bool active = maybeSeat.HasValue;
            float rotation = id == PlayerId.Player1 ? 0f : id == PlayerId.Player2 ? 180f
                : id == PlayerId.Player3 ? 0f : 180f;
            Matrix4x4 original = GUI.matrix;
            Vector3 pivot = well.center;
            GUI.matrix = original * Matrix4x4.Translate(pivot) *
                Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, rotation)) *
                Matrix4x4.Translate(-pivot);

            if (!active)
            {
                GUI.Label(new Rect(well.x + 30f, well.y + 95f, well.width - 60f, 86f),
                    session.Players.Count < 4 ? "+\nADD PLAYER" : "INACTIVE", name);
                GUI.matrix = original;
                return;
            }

            PlayerSeat seat = maybeSeat.Value;
            GUI.Label(new Rect(well.x + 38f, well.y + 15f, well.width - 106f, 34f),
                seat.Player.DisplayName.ToUpperInvariant(), detail);
            GUI.Label(EditDrawRect(well), "EDIT", detail);
            // Once a physical Ship occupies the well, it and the lit throttle
            // sector are the setup UI. No redundant card/status is left beneath it.
            if (!seat.PieceIdentity.HasValue)
                GUI.Label(new Rect(well.x + 34f, well.y + 92f, well.width - 68f, 92f),
                    "PLACE\nSHIP", name);
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

        private void PollFallbackReadiness()
        {
            if (Keyboard.current == null) return;
            SetFallbackReady(PlayerId.Player1, Keyboard.current.xKey.wasPressedThisFrame);
            SetFallbackReady(PlayerId.Player2, Keyboard.current.digit8Key.wasPressedThisFrame);
            SetFallbackReady(PlayerId.Player3, Keyboard.current.digit2Key.wasPressedThisFrame);
            SetFallbackReady(PlayerId.Player4, Keyboard.current.endKey.wasPressedThisFrame);
        }

        private void SetFallbackReady(PlayerId id, bool pressed)
        {
            if (!pressed || Coordinator.Seats.All(x => x.PlayerId != id || !x.IsClaimed)) return;
            readyPlayers.Add(id);
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

        private static Rect EditDrawRect(Rect well) =>
            new Rect(well.xMax - 70f, well.y + 15f, 54f, 34f);

        private static Rect EditHitRect(PlayerId id, Rect well)
        {
            Rect draw = EditDrawRect(well);
            if (id != PlayerId.Player2 && id != PlayerId.Player4) return draw;
            return new Rect(2f * well.center.x - draw.xMax,
                2f * well.center.y - draw.yMax, draw.width, draw.height);
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
