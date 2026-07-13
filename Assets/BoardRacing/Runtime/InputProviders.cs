using System;
using System.Collections.Generic;
using System.Linq;
using Board.Input;
using BoardRacing.Domain;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BoardRacing.Runtime
{
    public sealed class BoardContactInputProvider : IPlayerInputProvider, IDisposable
    {
        private readonly ContactSnapshotReconciler reconciler;

        public BoardContactInputProvider(float throttleHysteresisRadians, float playerRegionBoundaryY)
        {
            reconciler = new ContactSnapshotReconciler(TrancheOneAssignments.All,
                throttleHysteresisRadians, playerRegionBoundaryY);
            BoardInput.settingsChanged += OnSettingsChanged;
        }

        public IReadOnlyList<PlayerControlSnapshot> ReadSnapshots()
        {
            var contacts = BoardInput.GetActiveContacts(BoardContactType.Glyph)
                .Select(x => new RawPieceContact(x.contactId, x.glyphId,
                    new Vec2(x.screenPosition.x, x.screenPosition.y), x.orientation,
                    x.isTouched, MapPhase(x.phase)));
            return reconciler.Reconcile(contacts);
        }

        public void Dispose() => BoardInput.settingsChanged -= OnSettingsChanged;
        private void OnSettingsChanged() => reconciler.ResetAll();

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
        private sealed class MutablePlayer
        {
            public bool CarPresent = true, CarTouched, CrewPresent = true, CrewTouched;
            public int Sector;
            public Vector2 CrewPosition;
            public float CrewAngle;
        }

        private readonly MutablePlayer p1 = new MutablePlayer { CrewPosition = new Vector2(1325f, 270f) };
        private readonly MutablePlayer p2 = new MutablePlayer { CrewPosition = new Vector2(595f, 810f) };

        public IReadOnlyList<PlayerControlSnapshot> ReadSnapshots()
        {
            UpdatePlayer(p1, Key.Q, Key.W, Key.E, Key.R, Key.A, Key.D,
                Key.Z, Key.X, Key.C, Key.V, Key.F, Key.G, Key.T, Key.B);
            UpdatePlayer(p2, Key.U, Key.I, Key.O, Key.P, Key.J, Key.L,
                Key.Digit7, Key.Digit8, Key.Digit9, Key.Digit0, Key.H, Key.K, Key.Y, Key.N);
            return new[] { Snapshot(PlayerId.Player1, p1, 1001), Snapshot(PlayerId.Player2, p2, 2001) };
        }

        private static void UpdatePlayer(MutablePlayer p, Key carTouch, Key crewTouch, Key carPresent,
            Key crewPresent, Key rotateLeft, Key rotateRight, Key s1, Key s2, Key s3,
            Key s4, Key moveLeft, Key moveRight, Key moveUp, Key moveDown)
        {
            if (Pressed(carTouch)) p.CarTouched = !p.CarTouched;
            if (Pressed(crewTouch)) p.CrewTouched = !p.CrewTouched;
            if (Pressed(carPresent)) { p.CarPresent = !p.CarPresent; if (!p.CarPresent) p.CarTouched = false; }
            if (Pressed(crewPresent)) { p.CrewPresent = !p.CrewPresent; if (!p.CrewPresent) p.CrewTouched = false; }
            if (Pressed(s1)) p.Sector = 0; if (Pressed(s2)) p.Sector = 1;
            if (Pressed(s3)) p.Sector = 2; if (Pressed(s4)) p.Sector = 3;
            float speed = 280f * Time.unscaledDeltaTime;
            if (Held(moveLeft)) p.CrewPosition += Vector2.left * speed;
            if (Held(moveRight)) p.CrewPosition += Vector2.right * speed;
            if (Held(moveUp)) p.CrewPosition += Vector2.up * speed;
            if (Held(moveDown)) p.CrewPosition += Vector2.down * speed;
            if (Held(rotateLeft)) p.CrewAngle -= 1.8f * Time.unscaledDeltaTime;
            if (Held(rotateRight)) p.CrewAngle += 1.8f * Time.unscaledDeltaTime;
        }

        private static bool Pressed(Key key) => Keyboard.current != null && Keyboard.current[key].wasPressedThisFrame;
        private static bool Held(Key key) => Keyboard.current != null && Keyboard.current[key].isPressed;

        private static PlayerControlSnapshot Snapshot(PlayerId id, MutablePlayer p, int contactBase)
        {
            var car = p.CarPresent ? new PieceState(true, p.CarTouched, contactBase, new Vec2(), p.Sector * Mathf.PI / 2f) : PieceState.Missing;
            var crew = p.CrewPresent ? new PieceState(true, p.CrewTouched, contactBase + 1,
                new Vec2(p.CrewPosition.x, p.CrewPosition.y), p.CrewAngle) : PieceState.Missing;
            var throttle = p.CarPresent && p.CarTouched ? (ThrottleStep)((p.Sector + 1) * 25) : ThrottleStep.Off;
            return new PlayerControlSnapshot(id, throttle, car, crew, InputWarning.None);
        }
    }
}
