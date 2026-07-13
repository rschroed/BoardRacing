using System;
using System.Collections.Generic;
using System.Linq;
using BoardRacing.Domain;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BoardRacing.Runtime
{
    public sealed class ControlLab : MonoBehaviour
    {
        private TrancheOneSettings settings;
        private IPlayerInputProvider boardProvider;
        private IPlayerInputProvider fallbackProvider;
        private IPlayerInputProvider activeProvider;
        private readonly Dictionary<PlayerId, PitActionMachine> pits = new Dictionary<PlayerId, PitActionMachine>();
        private readonly Dictionary<PlayerId, PitActionResult> pitResults = new Dictionary<PlayerId, PitActionResult>();
        private readonly Dictionary<PlayerId, int> completions = new Dictionary<PlayerId, int>();
        private IReadOnlyList<PlayerControlSnapshot> snapshots = Array.Empty<PlayerControlSnapshot>();
        private GUIStyle title, heading, body, throttle, warning, progress;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindObjectOfType<ControlLab>() == null)
                new GameObject("Tranche 1 Control Lab").AddComponent<ControlLab>();
        }

        private void Awake()
        {
            settings = Resources.Load<TrancheOneSettings>("TrancheOneSettings") ?? TrancheOneSettings.Defaults();
            float hysteresis = settings.throttleHysteresisDegrees * Mathf.Deg2Rad;
            boardProvider = new BoardContactInputProvider(hysteresis, settings.playerRegionBoundaryY);
            fallbackProvider = new KeyboardInputProvider();
#if UNITY_ANDROID && !UNITY_EDITOR
            activeProvider = settings.preferBoardInputOnDevice ? boardProvider : fallbackProvider;
#else
            activeProvider = fallbackProvider;
#endif
            CreatePit(PlayerId.Player1, settings.playerOneServiceCenter);
            CreatePit(PlayerId.Player2, settings.playerTwoServiceCenter);
            foreach (PlayerId id in Enum.GetValues(typeof(PlayerId))) completions[id] = 0;
        }

        private void CreatePit(PlayerId id, Vector2 center) => pits[id] = new PitActionMachine(
            new Vec2(center.x, center.y), new Vec2(settings.serviceHalfSize.x, settings.serviceHalfSize.y),
            settings.targetAngleDegrees * Mathf.Deg2Rad, settings.alignmentToleranceDegrees * Mathf.Deg2Rad,
            settings.holdDurationSeconds);

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
            if (Keyboard.current != null && Keyboard.current.backspaceKey.wasPressedThisFrame) ResetLab();
            snapshots = activeProvider.ReadSnapshots();
            foreach (var snapshot in snapshots)
            {
                var result = pits[snapshot.PlayerId].Update(snapshot.Crew, Time.unscaledDeltaTime);
                pitResults[snapshot.PlayerId] = result;
                if (result.CompletedThisUpdate) completions[snapshot.PlayerId]++;
            }
        }

        private void ResetLab()
        {
            foreach (var pit in pits.Values) pit.Reset();
            foreach (PlayerId id in Enum.GetValues(typeof(PlayerId))) completions[id] = 0;
        }

        public int GetCompletionCount(PlayerId playerId) => completions.TryGetValue(playerId, out int count) ? count : 0;

        public PitActionResult GetPitAction(PlayerId playerId) =>
            pitResults.TryGetValue(playerId, out var result) ? result : default;

        public PlayerControlSnapshot GetPlayerSnapshot(PlayerId playerId) =>
            snapshots.FirstOrDefault(x => x.PlayerId == playerId);

        public void SetInputProvider(IPlayerInputProvider provider)
        {
            activeProvider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        private void EnsureStyles()
        {
            if (title != null) return;
            title = Style(36, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            heading = Style(28, FontStyle.Bold, Color.white, TextAnchor.MiddleLeft);
            body = Style(21, FontStyle.Normal, new Color(.9f, .92f, .95f), TextAnchor.MiddleLeft);
            throttle = Style(64, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            warning = Style(20, FontStyle.Bold, new Color(1f, .72f, .2f), TextAnchor.MiddleCenter);
            progress = Style(22, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
        }

        private static GUIStyle Style(int size, FontStyle fontStyle, Color color, TextAnchor anchor) => new GUIStyle(GUI.skin.label)
        { fontSize = size, fontStyle = fontStyle, normal = { textColor = color }, alignment = anchor, wordWrap = true };

        private void OnGUI()
        {
            EnsureStyles();
            float sx = Screen.width / 1920f, sy = Screen.height / 1080f;
            Matrix4x4 original = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(sx, sy, 1f));
            GUI.DrawTexture(new Rect(0, 0, 1920, 1080), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(.035f, .045f, .065f), 0, 0);
            var p2 = snapshots.FirstOrDefault(x => x.PlayerId == PlayerId.Player2);
            var p1 = snapshots.FirstOrDefault(x => x.PlayerId == PlayerId.Player1);
            DrawPanel(new Rect(80, 55, 1760, 430), p2, new Color(.48f, .28f, .72f), true);
            GUI.Label(new Rect(600, 500, 720, 80), "TRANCHE 1 · PHYSICAL CONTROL LAB", title);
            DrawPanel(new Rect(80, 595, 1760, 430), p1, new Color(.92f, .39f, .12f), false);
            GUI.Label(new Rect(610, 545, 700, 34), ProviderHint(), warning);
            GUI.matrix = original;
        }

        private string ProviderName() => activeProvider == boardProvider ? "BOARD INPUT" : "KEYBOARD FALLBACK";

        private string ProviderHint()
        {
#if UNITY_EDITOR
            return ProviderName() + "  ·  F1 provider  ·  Backspace reset";
#else
            return ProviderName();
#endif
        }

        private void DrawPanel(Rect rect, PlayerControlSnapshot snapshot, Color accent, bool oppositeSide)
        {
            Matrix4x4 original = GUI.matrix;
            if (oppositeSide)
            {
                Vector3 pivot = new Vector3(rect.center.x, rect.center.y, 0f);
                GUI.matrix = original * Matrix4x4.Translate(pivot) *
                    Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, 180f)) * Matrix4x4.Translate(-pivot);
            }
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(accent.r * .16f, accent.g * .16f, accent.b * .16f, 1f), 0, 24);
            GUI.DrawTexture(new Rect(rect.x, rect.y, 18, rect.height), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, accent, 0, 12);
            string marker = snapshot.PlayerId == PlayerId.Player1 ? "▲ PLAYER 1 · ORANGE" : "● PLAYER 2 · PURPLE";
            GUI.Label(new Rect(rect.x + 48, rect.y + 25, 520, 50), marker, heading);
            DrawCar(rect, snapshot, accent);
            DrawCrew(rect, snapshot, accent);
            if (snapshot.Warnings != InputWarning.None)
                GUI.Label(new Rect(rect.x + 1160, rect.y + 25, 520, 50), Recovery(snapshot.Warnings), warning);
            GUI.matrix = original;
        }

        private void DrawCar(Rect r, PlayerControlSnapshot s, Color accent)
        {
            GUI.Label(new Rect(r.x + 55, r.y + 95, 400, 35), "ROBOT · CAR CONTROL", body);
            string carStatus = !s.Car.Present ? "PLACE ROBOT" :
                s.Car.RequiresRelease ? "RELEASE TO REARM" :
                s.Car.Touched ? "TOUCHED" : "RELEASED";
            GUI.Label(new Rect(r.x + 55, r.y + 140, 420, 80), carStatus, heading);
            GUI.DrawTexture(new Rect(r.x + 485, r.y + 100, 360, 230), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(accent.r, accent.g, accent.b, .28f), 0, 18);
            GUI.Label(new Rect(r.x + 485, r.y + 118, 360, 110), ((int)s.Throttle) + "%", throttle);
            GUI.Label(new Rect(r.x + 485, r.y + 230, 360, 45), "REQUESTED THROTTLE", progress);
            GUI.Label(new Rect(r.x + 55, r.y + 260, 410, 70), "Touch Robot, then rotate between four coarse sectors.", body);
        }

        private void DrawCrew(Rect r, PlayerControlSnapshot s, Color accent)
        {
            PitActionResult pit = pitResults.TryGetValue(s.PlayerId, out var value) ? value : default;
            GUI.Label(new Rect(r.x + 920, r.y + 95, 420, 35), "SHIP · PIT CONTROL", body);
            string status = CrewStatus(s, pit);
            GUI.Label(new Rect(r.x + 920, r.y + 140, 500, 65), status, heading);
            GUI.Label(new Rect(r.x + 1435, r.y + 105, 265, 95),
                "PIT CYCLES\n" + completions[s.PlayerId] + " / 10", progress);
            Rect bar = new Rect(r.x + 920, r.y + 225, 650, 48);
            GUI.DrawTexture(bar, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(.12f, .14f, .18f), 0, 12);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * pit.Progress, bar.height), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, accent, 0, 12);
            GUI.Label(bar, Mathf.RoundToInt(pit.Progress * 100f) + "% HOLD", progress);
            GUI.Label(new Rect(r.x + 920, r.y + 285, 760, 78), CrewGuidance(s, pit), body);
        }

        private string CrewStatus(PlayerControlSnapshot snapshot, PitActionResult pit)
        {
            if (!snapshot.Crew.Present) return "PLACE SHIP";
            if (snapshot.Crew.RequiresRelease) return "RELEASE TO REARM";
            switch (pit.State)
            {
                case PitActionState.Positioned: return "TOUCH SHIP";
                case PitActionState.Aligning:
                    return "ALIGN SHIP · " + Mathf.RoundToInt(AlignmentErrorDegrees(snapshot.Crew)) + "° OFF";
                case PitActionState.Holding: return "HOLD STEADY";
                case PitActionState.Completed: return "PIT COMPLETE";
                case PitActionState.Idle: return "MOVE TO PIT BAR";
                default: return "PLACE SHIP";
            }
        }

        private string CrewGuidance(PlayerControlSnapshot snapshot, PitActionResult pit)
        {
            if (!snapshot.Crew.Present) return "Place the assigned Ship on the blue pit bar.";
            if (snapshot.Crew.RequiresRelease) return "Let go once, then touch the Ship again.";

            float angle = NormalizeDegrees(snapshot.Crew.OrientationRadians * Mathf.Rad2Deg);
            string reading = "SHIP ANGLE " + Mathf.RoundToInt(angle) + "°  ·  TARGET " +
                Mathf.RoundToInt(NormalizeDegrees(settings.targetAngleDegrees)) + "° ±" +
                Mathf.RoundToInt(settings.alignmentToleranceDegrees) + "°";
            if (pit.State == PitActionState.Aligning)
                return reading + "\nRotate slowly until HOLD STEADY appears.";
            if (pit.State == PitActionState.Holding)
                return reading + "\nKeep the Ship touched and still.";
            if (pit.State == PitActionState.Completed)
                return "Cycle recorded. Move the Ship off the pit bar to rearm.";
            return reading + "\nTouch the Ship to begin alignment.";
        }

        private float AlignmentErrorDegrees(PieceState crew)
        {
            float target = settings.targetAngleDegrees * Mathf.Deg2Rad;
            return CoarseThrottleMapper.AngularDistance(crew.OrientationRadians, target) * Mathf.Rad2Deg;
        }

        private static float NormalizeDegrees(float degrees)
        {
            degrees %= 360f;
            return degrees < 0f ? degrees + 360f : degrees;
        }

        private static string Recovery(InputWarning warning)
        {
            if ((warning & InputWarning.DuplicateGlyph) != 0) return "LIFT DUPLICATE PIECE";
            if ((warning & InputWarning.UnassignedGlyph) != 0) return "PINK / YELLOW UNASSIGNED";
            return "CHECK PIECE POSITION";
        }
    }
}
