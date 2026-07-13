using System;
using System.Collections.Generic;
using System.Linq;
using BoardRacing.Domain;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BoardRacing.Runtime
{
    public sealed class RacePrototype : MonoBehaviour
    {
        private TrancheOneSettings inputSettings;
        private TrancheTwoSettings raceSettings;
        private IPlayerInputProvider boardProvider, fallbackProvider, activeProvider;
        private RaceSimulation simulation;
        private IReadOnlyList<PlayerControlSnapshot> controls = Array.Empty<PlayerControlSnapshot>();
        private float accumulator;
        private GUIStyle title, heading, body, carLabel, warning;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindObjectOfType<RacePrototype>() == null)
                new GameObject("Tranche 2 Race Prototype").AddComponent<RacePrototype>();
        }

        private void Awake()
        {
            inputSettings = Resources.Load<TrancheOneSettings>("TrancheOneSettings") ?? TrancheOneSettings.Defaults();
            raceSettings = Resources.Load<TrancheTwoSettings>("TrancheTwoSettings") ?? TrancheTwoSettings.Defaults();
            boardProvider = new BoardContactInputProvider(inputSettings.throttleHysteresisDegrees * Mathf.Deg2Rad,
                inputSettings.playerRegionBoundaryY);
            fallbackProvider = new KeyboardInputProvider();
#if UNITY_ANDROID && !UNITY_EDITOR
            activeProvider = raceSettings.preferBoardInputOnDevice ? boardProvider : fallbackProvider;
#else
            activeProvider = fallbackProvider;
#endif
            simulation = new RaceSimulation(TrackDefinition.Placeholder(raceSettings.cornerSafeSpeed), raceSettings.ToRules());
        }

        private void OnDestroy()
        {
            if (boardProvider is IDisposable disposable) disposable.Dispose();
        }

        private void Update()
        {
#if UNITY_EDITOR
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
                activeProvider = activeProvider == boardProvider ? fallbackProvider : boardProvider;
#endif
            controls = activeProvider.ReadSnapshots();
            accumulator += Mathf.Min(Time.unscaledDeltaTime, .25f);
            float step = Mathf.Max(.001f, raceSettings.fixedStepSeconds);
            var commands = controls.Select(x => new RacerCommand(x.PlayerId, x.Throttle, x.Car.Present, x.Car.Touched)).ToArray();
            while (accumulator >= step) { simulation.Step(step, commands); accumulator -= step; }
        }

        public RaceSnapshot GetRaceSnapshot() => simulation.Snapshot;
        public void SetInputProvider(IPlayerInputProvider provider) => activeProvider = provider ?? throw new ArgumentNullException(nameof(provider));

        private void EnsureStyles()
        {
            if (title != null) return;
            title = Style(42, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            heading = Style(26, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            body = Style(20, FontStyle.Normal, new Color(.9f, .92f, .95f), TextAnchor.MiddleCenter);
            carLabel = Style(22, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            warning = Style(26, FontStyle.Bold, new Color(1f, .75f, .2f), TextAnchor.MiddleCenter);
        }

        private static GUIStyle Style(int size, FontStyle fontStyle, Color color, TextAnchor anchor) => new GUIStyle(GUI.skin.label)
        { fontSize = size, fontStyle = fontStyle, normal = { textColor = color }, alignment = anchor, wordWrap = true };

        private void OnGUI()
        {
            EnsureStyles();
            Matrix4x4 original = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / 1920f, Screen.height / 1080f, 1f));
            GUI.DrawTexture(new Rect(0, 0, 1920, 1080), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                new Color(.025f, .035f, .05f), 0, 0);
            DrawTrack();
            foreach (var racer in simulation.Snapshot.Racers) DrawCar(racer);
            DrawHud(PlayerId.Player2, new Rect(420, 14, 1080, 170), true, new Color(.48f, .28f, .72f));
            DrawHud(PlayerId.Player1, new Rect(420, 896, 1080, 170), false, new Color(.92f, .39f, .12f));
            DrawCenterMessage();
#if UNITY_EDITOR
            GUI.Label(new Rect(760, 855, 400, 30), (activeProvider == boardProvider ? "BOARD INPUT" : "KEYBOARD FALLBACK") + " · F1 provider", body);
#endif
            GUI.matrix = original;
        }

        private void DrawTrack()
        {
            foreach (var segment in simulation.Track.Segments)
            {
                Color color = segment.Kind == TrackSectionKind.Corner ? new Color(.22f, .28f, .36f) : new Color(.16f, .2f, .27f);
                DrawLine(segment.Start, segment.End, 82f, color);
                DrawLine(segment.Start, segment.End, 3f, new Color(.55f, .62f, .7f, .5f));
            }
            GUI.DrawTexture(new Rect(468, 202, 24, 56), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, Color.white, 0, 0);
            GUI.Label(new Rect(610, 475, 700, 110), "BOARD RACING\n5 LAPS · CAR PIECES ONLY", title);
        }

        private static void DrawLine(Vec2 from, Vec2 to, float width, Color color)
        {
            Vector2 a = new Vector2(from.X, from.Y), b = new Vector2(to.X, to.Y);
            Vector2 middle = (a + b) * .5f;
            float length = Vector2.Distance(a, b);
            Matrix4x4 original = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg, middle);
            GUI.DrawTexture(new Rect(middle.x - length * .5f, middle.y - width * .5f, length, width),
                Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, color, 0, width * .25f);
            GUI.matrix = original;
        }

        private void DrawCar(RacerSnapshot racer)
        {
            var p = racer.Track.Position; var t = racer.Track.Tangent;
            float x = p.X - t.Y * racer.LateralOffset, y = p.Y + t.X * racer.LateralOffset;
            Color color = racer.PlayerId == PlayerId.Player1 ? new Color(.92f, .39f, .12f) : new Color(.48f, .28f, .72f);
            Rect rect = new Rect(x - 27f, y - 27f, 54f, 54f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, color, 0,
                racer.PlayerId == PlayerId.Player1 ? 8f : 27f);
            GUI.Label(rect, racer.PlayerId == PlayerId.Player1 ? "▲" : "●", carLabel);
            if (racer.RecoveryRemaining > 0f) GUI.Label(new Rect(x - 100f, y - 72f, 200f, 36f), "SLOWDOWN!", warning);
        }

        private void DrawHud(PlayerId id, Rect rect, bool opposite, Color accent)
        {
            Matrix4x4 original = GUI.matrix;
            if (opposite)
            {
                Vector3 pivot = new Vector3(rect.center.x, rect.center.y, 0f);
                GUI.matrix = original * Matrix4x4.Translate(pivot) * Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, 180f)) * Matrix4x4.Translate(-pivot);
            }
            var racer = simulation.Snapshot.Racers.Single(x => x.PlayerId == id);
            var control = controls.FirstOrDefault(x => x.PlayerId == id);
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0,
                new Color(accent.r * .18f, accent.g * .18f, accent.b * .18f), 0, 22);
            string marker = id == PlayerId.Player1 ? "▲ PLAYER 1 · ORANGE" : "● PLAYER 2 · PURPLE";
            GUI.Label(new Rect(rect.x + 25, rect.y + 12, 370, 42), marker, heading);
            GUI.Label(new Rect(rect.x + 390, rect.y + 12, 300, 42), "LAP " + Math.Min(raceSettings.laps, racer.CompletedLaps + 1) + " / " + raceSettings.laps, heading);
            GUI.Label(new Rect(rect.x + 690, rect.y + 12, 180, 42), Ordinal(racer.Place), heading);
            GUI.Label(new Rect(rect.x + 870, rect.y + 12, 180, 42), ((int)control.Throttle) + "%", heading);
            GUI.Label(new Rect(rect.x + 25, rect.y + 62, 1030, 88), HudGuidance(racer, control), body);
            GUI.matrix = original;
        }

        private string HudGuidance(RacerSnapshot racer, PlayerControlSnapshot control)
        {
            var race = simulation.Snapshot;
            if (!control.Car.Present) return "PLACE YOUR ROBOT · throttle is safely off";
            if (control.Car.RequiresRelease) return "RELEASE ROBOT TO REARM";
            if (race.Phase == RacePhase.Grid) return "READY · leave Robot released for the countdown";
            if (race.Phase == RacePhase.Countdown) return "GET READY · touch and rotate after GO";
            if (race.Phase == RacePhase.Racing)
                return racer.RecoveryRemaining > 0f ? "TOO FAST INTO THE CORNER · speed scrubbed" : "Rotate for speed · release to brake before dark corner sections";
            if (race.AwaitingRematchRelease) return "RELEASE BOTH ROBOTS TO RESTART";
            return "BOTH PLAYERS TOUCH AND HOLD ROBOTS FOR REMATCH";
        }

        private void DrawCenterMessage()
        {
            var race = simulation.Snapshot;
            string message = null;
            if (race.Phase == RacePhase.Countdown) message = Math.Max(1, Mathf.CeilToInt(race.CountdownRemaining)).ToString();
            else if (race.Phase == RacePhase.Racing && race.ElapsedSeconds < 1f) message = "GO!";
            else if (race.Phase == RacePhase.Finished)
            {
                var winner = race.Racers.OrderBy(x => x.Place).First();
                message = (winner.PlayerId == PlayerId.Player1 ? "▲ ORANGE" : "● PURPLE") + " WINS";
            }
            if (message != null) GUI.Label(new Rect(710, 640, 500, 90), message, title);
        }

        private static string Ordinal(int place) => place == 1 ? "1ST" : "2ND";
    }
}
