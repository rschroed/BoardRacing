using System;
using System.Collections.Generic;
using System.Linq;
using Board.Input;
using BoardRacing.Domain;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BoardRacing.Runtime
{
    public sealed class BoardContactInputProvider : IPlayerInputProvider, IInputResetSource, IDisposable
    {
        private ContactSnapshotReconciler reconciler;
        private readonly ThrottleStops throttleStops;
        private readonly float throttleHysteresisRadians;
        public event Action InputReset;

        public BoardContactInputProvider(ThrottleStops throttleStops, float throttleHysteresisRadians,
            float playerRegionBoundaryY)
        {
            this.throttleStops = throttleStops;
            this.throttleHysteresisRadians = throttleHysteresisRadians;
            reconciler = new ContactSnapshotReconciler(TrancheOneAssignments.All,
                throttleStops, throttleHysteresisRadians, playerRegionBoundaryY);
            BoardInput.settingsChanged += OnSettingsChanged;
        }

        public void Configure(IEnumerable<PieceAssignment> assignments,
            IEnumerable<PlayerId> activePlayers, IEnumerable<SeatClaimRegion> playerRegions)
        {
            reconciler = new ContactSnapshotReconciler(assignments, activePlayers,
                throttleStops, throttleHysteresisRadians, playerRegions);
            InputReset?.Invoke();
        }

        public IReadOnlyList<RawPieceContact> ReadRawContacts() =>
            BoardInput.GetActiveContacts(BoardContactType.Glyph)
                .Select(x => new RawPieceContact(x.contactId, x.glyphId,
                    new Vec2(x.screenPosition.x, x.screenPosition.y), x.orientation,
                    x.isTouched, MapPhase(x.phase))).ToArray();

        public IReadOnlyList<PlayerControlSnapshot> ReadSnapshots() =>
            reconciler.Reconcile(ReadRawContacts());

        public void Dispose() => BoardInput.settingsChanged -= OnSettingsChanged;
        private void OnSettingsChanged()
        {
            reconciler.ResetAll();
            InputReset?.Invoke();
        }

        private static RawContactPhase MapPhase(BoardContactPhase phase)
        {
            switch (phase)
            {
                case BoardContactPhase.Began: return RawContactPhase.Began;
                case BoardContactPhase.Moved: return RawContactPhase.Moved;
                case BoardContactPhase.Stationary: return RawContactPhase.Stationary;
                case BoardContactPhase.Ended: return RawContactPhase.Ended;
                default: return RawContactPhase.Canceled;
            }
        }
    }

    public sealed class KeyboardInputProvider : IPlayerInputProvider
    {
        private readonly Func<float> frameDelta;
        private PlayerId[] activePlayers;

        private sealed class MutablePlayer
        {
            public bool CarPresent = true, CrewPresent = true;
            public int Sector;
            public Vector2 CrewPosition;
            public float CrewAngle;
        }

        // Crews home on the Round 2 Call Pit centers (wireframe-ui.md, frame 40:23).
        private readonly MutablePlayer p1 = new MutablePlayer { CrewPosition = new Vector2(1832f, 398f) };
        private readonly MutablePlayer p2 = new MutablePlayer { CrewPosition = new Vector2(88f, 682f) };
        private readonly MutablePlayer p3 = new MutablePlayer { CrewPosition = new Vector2(88f, 398f) };
        private readonly MutablePlayer p4 = new MutablePlayer { CrewPosition = new Vector2(1832f, 682f) };

        public KeyboardInputProvider(Func<float> frameDelta = null,
            IEnumerable<PlayerId> activePlayers = null)
        {
            this.frameDelta = frameDelta ?? (() => Time.unscaledDeltaTime);
            ConfigureRoster(activePlayers ?? TrancheOneAssignments.ActivePlayers);
        }

        public void ConfigureRoster(IEnumerable<PlayerId> players) =>
            activePlayers = players.Distinct().OrderBy(x => x).ToArray();

        public IReadOnlyList<PlayerControlSnapshot> ReadSnapshots()
        {
            float delta = frameDelta();
            UpdatePlayer(p1, Key.Q, Key.W, Key.E, Key.R, Key.A, Key.D,
                Key.Z, Key.X, Key.C, Key.V, Key.F, Key.G, Key.T, Key.B, delta);
            UpdatePlayer(p2, Key.U, Key.I, Key.O, Key.P, Key.J, Key.L,
                Key.Digit7, Key.Digit8, Key.Digit9, Key.Digit0, Key.H, Key.K, Key.Y, Key.N, delta);
            UpdatePlayer(p3, Key.F1, Key.F2, Key.F3, Key.F4, Key.Comma, Key.Period,
                Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4,
                Key.LeftArrow, Key.RightArrow, Key.UpArrow, Key.DownArrow, delta);
            UpdatePlayer(p4, Key.F5, Key.F6, Key.F9, Key.F10, Key.F11, Key.F12,
                Key.Home, Key.End, Key.PageUp, Key.PageDown,
                Key.LeftBracket, Key.RightBracket, Key.Minus, Key.Equals, delta);
            var all = new Dictionary<PlayerId, PlayerControlSnapshot>
            {
                [PlayerId.Player1] = Snapshot(PlayerId.Player1, p1, 1001),
                [PlayerId.Player2] = Snapshot(PlayerId.Player2, p2, 2001),
                [PlayerId.Player3] = Snapshot(PlayerId.Player3, p3, 3001),
                [PlayerId.Player4] = Snapshot(PlayerId.Player4, p4, 4001)
            };
            return activePlayers.Select(x => all[x]).ToArray();
        }

        private static void UpdatePlayer(MutablePlayer p, Key carTouch, Key crewTouch, Key carPresent,
            Key crewPresent, Key rotateLeft, Key rotateRight, Key s1, Key s2, Key s3,
            Key s4, Key moveLeft, Key moveRight, Key moveUp, Key moveDown, float delta)
        {
            // Touch keys are intentionally ignored: physical controls use placement and rotation only.
            if (Pressed(carPresent)) p.CarPresent = !p.CarPresent;
            if (Pressed(crewPresent)) p.CrewPresent = !p.CrewPresent;
            if (Pressed(s1)) p.Sector = 0; if (Pressed(s2)) p.Sector = 1;
            if (Pressed(s3) || Pressed(s4)) p.Sector = 2;
            float speed = 280f * delta;
            if (Held(moveLeft)) p.CrewPosition += Vector2.left * speed;
            if (Held(moveRight)) p.CrewPosition += Vector2.right * speed;
            if (Held(moveUp)) p.CrewPosition += Vector2.up * speed;
            if (Held(moveDown)) p.CrewPosition += Vector2.down * speed;
            if (Held(rotateLeft)) p.CrewAngle -= 1.8f * delta;
            if (Held(rotateRight)) p.CrewAngle += 1.8f * delta;
        }

        private static bool Pressed(Key key) => Keyboard.current != null && Keyboard.current[key].wasPressedThisFrame;
        private static bool Held(Key key) => Keyboard.current != null && Keyboard.current[key].isPressed;

        private static PlayerControlSnapshot Snapshot(PlayerId id, MutablePlayer p, int contactBase)
        {
            var car = p.CarPresent ? new PieceState(true, false, contactBase, new Vec2(), p.Sector * Mathf.PI * 2f / 3f) : PieceState.Missing;
            var crew = p.CrewPresent ? new PieceState(true, false, contactBase + 1,
                new Vec2(p.CrewPosition.x, p.CrewPosition.y), p.CrewAngle) : PieceState.Missing;
            var throttle = !p.CarPresent || p.Sector == 0 ? ThrottleStep.Brake :
                p.Sector == 1 ? ThrottleStep.Drive : ThrottleStep.Boost;
            return new PlayerControlSnapshot(id, throttle, car, crew, InputWarning.None);
        }
    }
}
