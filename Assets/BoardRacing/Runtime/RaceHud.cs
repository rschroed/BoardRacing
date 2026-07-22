using BoardRacing.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace BoardRacing.Runtime
{
    // An annular arc (ring, dial fill, throttle sector band) as a single canvas
    // Graphic. Angles stay in the IMGUI screen convention the seat layout is
    // measured in (0 = +x, positive sweeps clockwise on screen, y down): the
    // mesh negates y into uGUI's y-up local space, so CornerControllerLayout
    // numbers pass through unchanged. Replaces DrawArc, which issued one
    // GUI.DrawTexture call per 6° chord (#86 fps review: the seat HUD alone was
    // several hundred draw calls; every RingGraphic batches through the shared
    // default UI material).
    // The attribute is NOT inherited from Graphic — every concrete uGUI widget
    // declares it itself, and a Graphic without a CanvasRenderer silently
    // renders nothing (round 3 capture review: labels drew, rings didn't).
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class RingGraphic : MaskableGraphic
    {
        private float radius = 10f;
        private float thickness = 2f;
        private float startAngle;
        private float sweepAngle = 360f;

        private RingGraphic() => useLegacyMeshGeneration = false;

        public float Radius { get => radius; set => SetField(ref radius, value); }
        public float Thickness { get => thickness; set => SetField(ref thickness, value); }
        public float StartAngle { get => startAngle; set => SetField(ref startAngle, value); }
        public float SweepAngle { get => sweepAngle; set => SetField(ref sweepAngle, value); }

        protected override void OnPopulateMesh(VertexHelper vh) => Populate(vh);

        internal void Populate(VertexHelper vh)
        {
            vh.Clear();
            if (thickness <= 0f || Mathf.Approximately(sweepAngle, 0f)) return;
            // 6° chords: under half a pixel of sag at the HUD's ring radii.
            int segments = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(sweepAngle) / 6f));
            float inner = radius - thickness * .5f, outer = radius + thickness * .5f;
            var vertex = UIVertex.simpleVert;
            vertex.color = color;
            for (int i = 0; i <= segments; i++)
            {
                float radians = (startAngle + sweepAngle * i / segments) * Mathf.Deg2Rad;
                var direction = new Vector2(Mathf.Cos(radians), -Mathf.Sin(radians));
                vertex.position = direction * inner;
                vh.AddVert(vertex);
                vertex.position = direction * outer;
                vh.AddVert(vertex);
            }
            for (int i = 0; i < segments; i++)
            {
                int ring = i * 2;
                vh.AddTriangle(ring, ring + 1, ring + 3);
                vh.AddTriangle(ring, ring + 3, ring + 2);
            }
        }

        private void SetField(ref float field, float value)
        {
            if (Mathf.Approximately(field, value)) return;
            field = value;
            SetVerticesDirty();
        }
    }

    // One seat cluster (throttle arc, ship well, Call Pit circle, condition
    // dials, their labels) as uGUI elements built from the same
    // CornerControllerLayout the IMGUI pass drew (#86 round 3). Geometry is
    // fixed at creation; Apply binds the per-frame RaceUiModel exactly the way
    // the old DrawCornerController/DrawCrewRegions code paths did.
    internal sealed class SeatHud
    {
        internal PlayerLayout Layout;
        internal Color Accent;
        internal RingGraphic CallPitRing, CallPitHold;
        internal Text CallPitLabel;
        internal DialHud Tires, Fuel;
        internal RingGraphic ShipWell;
        internal SectorHud Brake, Drive, Boost;

        internal sealed class DialHud
        {
            internal RingGraphic ServiceRing, BaseRing, Fill, ProgressRing;
            internal Text Value, Name;
            internal Color LabelColor;
        }

        internal sealed class SectorHud
        {
            internal RingGraphic ActiveFill, Band;
            internal Text Label;
        }

        public static SeatHud Create(Transform canvasRoot, PlayerLayout layout, Color accent,
            Font font)
        {
            var container = new GameObject("Seat " + layout.PlayerId);
            RectTransform rect = container.AddComponent<RectTransform>();
            rect.SetParent(canvasRoot, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            CornerControllerLayout controller = layout.Controller;
            var seat = new SeatHud { Layout = layout, Accent = accent };
            seat.CallPitRing = CreateRing(rect, "Call Pit", layout.CallPit.center,
                controller.CallPitRadius, 3f, 0f, 360f, accent);
            seat.CallPitHold = CreateRing(rect, "Call Pit Hold", layout.CallPit.center,
                controller.CallPitRadius - 16f, 5f, -90f, 0f, Color.white);
            seat.CallPitHold.enabled = false;
            seat.CallPitLabel = CreateLabel(rect, "Call Pit Label", controller.CallPitLabel.Bounds,
                controller.CallPitLabel.RotationDegrees + layout.RotationDegrees, 13, accent, font);
            seat.Tires = CreateDial(rect, "Tires", layout.Tires.center, controller.TiresLabel,
                "TIRES", RaceHud.TiresLabelColor, controller.DialRadius, layout, accent, font);
            seat.Fuel = CreateDial(rect, "Fuel", layout.Fuel.center, controller.FuelLabel,
                "FUEL", RaceHud.FuelLabelColor, controller.DialRadius, layout, accent, font);
            seat.ShipWell = CreateRing(rect, "Ship Well", controller.ShipWellCenter,
                controller.ShipWellRadius, 3f, 0f, 360f, RaceHud.GhostColor);
            seat.Brake = CreateSector(rect, controller, ThrottleStep.Brake, layout, accent, font);
            seat.Drive = CreateSector(rect, controller, ThrottleStep.Drive, layout, accent, font);
            seat.Boost = CreateSector(rect, controller, ThrottleStep.Boost, layout, accent, font);
            return seat;
        }

        public void Apply(PlayerUiModel model, RacePhase phase)
        {
            bool inService = model.PitPhase == PitPhase.InService;
            // The circle is Call Pit on track and Leave Pit while parked — lit in
            // both, ghosted only while the car is moving through the pit lane.
            bool callPitLit = phase == RacePhase.Racing && !model.Finished &&
                model.PitPhase != PitPhase.Entering && model.PitPhase != PitPhase.Exiting;
            bool inPit = model.PitPhase == PitPhase.Entering || inService;
            bool emphasized = callPitLit && (model.CallState == PitCallState.Holding ||
                model.CallState == PitCallState.Requested || model.PitPhase == PitPhase.Requested);
            Color callPitColor = emphasized ? Color.white
                : callPitLit ? Accent : RaceHud.GhostColor;
            CallPitRing.Thickness = emphasized ? 5f : 3f;
            CallPitRing.color = callPitColor;
            bool holding = callPitLit && model.CallState == PitCallState.Holding;
            CallPitHold.enabled = holding;
            if (holding) CallPitHold.SweepAngle = 360f * model.CallAction.Progress;
            CallPitLabel.text = inPit ? "LEAVE PIT" : "CALL PIT";
            CallPitLabel.color = callPitColor;

            ApplyDial(Tires, PitService.Tires, model, inService,
                model.Condition.TireWear, model.Condition.TireLevel);
            ApplyDial(Fuel, PitService.Fuel, model, inService,
                model.Condition.FuelUsed, model.Condition.FuelLevel);

            ShipWell.color = model.ShipPresent
                ? new Color(Accent.r, Accent.g, Accent.b, .8f) : RaceHud.GhostColor;

            bool throttleLive = model.PitPhase == PitPhase.OnTrack ||
                model.PitPhase == PitPhase.Requested;
            ApplySector(Brake, ThrottleStep.Brake, model.Throttle, throttleLive);
            ApplySector(Drive, ThrottleStep.Drive, model.Throttle, throttleLive);
            ApplySector(Boost, ThrottleStep.Boost, model.Throttle, throttleLive);
        }

        private void ApplyDial(DialHud dial, PitService service, PlayerUiModel model,
            bool inService, float value, ConditionVisualLevel level)
        {
            bool selected = inService && model.SelectedService == service;
            // Parked: the dial itself is the service target; a surrounding ring marks it.
            dial.ServiceRing.enabled = inService;
            if (inService)
            {
                dial.ServiceRing.Thickness = selected ? 5f : 2.5f;
                dial.ServiceRing.color = selected ? Color.white : Accent;
            }
            float clamped = Mathf.Clamp01(value);
            // Normal severity fills in the condition's identity hue (frame 40:23);
            // warning/critical escalate to the shared severity colors.
            dial.Fill.enabled = clamped > .001f;
            if (dial.Fill.enabled)
            {
                dial.Fill.SweepAngle = 360f * clamped;
                dial.Fill.color = level == ConditionVisualLevel.Normal
                    ? dial.LabelColor : RaceHud.ConditionColor(level);
            }
            dial.Value.text = Mathf.RoundToInt(clamped * 100f).ToString();
            bool stirring = selected && model.ServiceAction.State == PitActionState.Stirring;
            dial.ProgressRing.enabled = stirring;
            if (stirring) dial.ProgressRing.SweepAngle = 360f * model.ServiceProgress;
        }

        private void ApplySector(SectorHud sector, ThrottleStep step, ThrottleStep current,
            bool live)
        {
            bool active = live && current == step;
            sector.ActiveFill.enabled = active;
            sector.Band.enabled = !active;
            if (!active)
                // Unlit sectors are thin accent-tinted dark bands; locked seats go neutral.
                sector.Band.color = live
                    ? new Color(Accent.r * .38f + .08f, Accent.g * .38f + .08f, Accent.b * .38f + .08f)
                    : new Color(.16f, .19f, .24f);
            sector.Label.color = active ? Color.white
                : live ? new Color(.85f, .88f, .92f) : new Color(.55f, .6f, .66f);
        }

        private static DialHud CreateDial(RectTransform parent, string name, Vector2 center,
            RotatedLabel label, string conditionName, Color labelColor, float radius,
            PlayerLayout layout, Color accent, Font font)
        {
            var dial = new DialHud { LabelColor = labelColor };
            dial.ServiceRing = CreateRing(parent, name + " Service", center, radius + 12f, 2.5f,
                0f, 360f, accent);
            dial.ServiceRing.enabled = false;
            dial.BaseRing = CreateRing(parent, name + " Dial", center, radius, 10f, 0f, 360f,
                new Color(.13f, .16f, .2f));
            dial.Fill = CreateRing(parent, name + " Fill", center, radius, 10f, -90f, 0f,
                labelColor);
            dial.Fill.enabled = false;
            dial.Value = CreateLabel(parent, name + " Value",
                new Rect(center.x - radius, center.y - 15f, radius * 2f, 30f),
                layout.RotationDegrees, 20, Color.white, font);
            dial.ProgressRing = CreateRing(parent, name + " Progress", center, radius + 12f, 6f,
                -90f, 0f, Color.white);
            dial.ProgressRing.enabled = false;
            dial.Name = CreateLabel(parent, name + " Label", label.Bounds,
                label.RotationDegrees + layout.RotationDegrees, 13, labelColor, font);
            dial.Name.text = conditionName;
            return dial;
        }

        private static SectorHud CreateSector(RectTransform parent,
            CornerControllerLayout controller, ThrottleStep step, PlayerLayout layout,
            Color accent, Font font)
        {
            float centerAngle = controller.SectorAngle(step) + layout.RotationDegrees;
            float halfSweep = controller.SectorSweepDegrees * .5f;
            string name = RaceUiModelBuilder.ThrottleName(step);
            var sector = new SectorHud();
            // The lit sector is the deep accent wedge from the design: the annulus
            // the IMGUI pass approximated with concentric rings (R-61 to R+1).
            sector.ActiveFill = CreateRing(parent, name + " Fill", controller.ArcCenter,
                controller.ThrottleRadius - 30f, 62f, centerAngle - halfSweep,
                controller.SectorSweepDegrees, accent);
            sector.ActiveFill.enabled = false;
            sector.Band = CreateRing(parent, name + " Band", controller.ArcCenter,
                controller.ThrottleRadius - 22f, 22f, centerAngle - halfSweep,
                controller.SectorSweepDegrees, new Color(.16f, .19f, .24f));
            RotatedLabel label = controller.SectorLabel(step);
            sector.Label = CreateLabel(parent, name + " Label", label.Bounds,
                label.RotationDegrees + layout.RotationDegrees, 14,
                new Color(.85f, .88f, .92f), font);
            sector.Label.text = name;
            return sector;
        }

        private static RingGraphic CreateRing(RectTransform parent, string name, Vector2 center,
            float radius, float thickness, float startAngle, float sweepAngle, Color color)
        {
            RectTransform rect = CreateNode(parent, name, center,
                Vector2.one * (radius + thickness) * 2f, 0f);
            var ring = rect.gameObject.AddComponent<RingGraphic>();
            ring.Radius = radius;
            ring.Thickness = thickness;
            ring.StartAngle = startAngle;
            ring.SweepAngle = sweepAngle;
            ring.color = color;
            ring.raycastTarget = false;
            return ring;
        }

        private static Text CreateLabel(RectTransform parent, string name, Rect bounds,
            float imguiRotationDegrees, int fontSize, Color color, Font font)
        {
            RectTransform rect = CreateNode(parent, name, bounds.center, bounds.size,
                imguiRotationDegrees);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        // Reference coordinates are IMGUI screen space (origin top-left, y down,
        // positive rotation clockwise); uGUI anchors flip both.
        private static RectTransform CreateNode(RectTransform parent, string name,
            Vector2 referenceCenter, Vector2 size, float imguiRotationDegrees)
        {
            var go = new GameObject(name);
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.sizeDelta = size;
            rect.anchoredPosition = new Vector2(referenceCenter.x, -referenceCenter.y);
            rect.localRotation = Quaternion.Euler(0f, 0f, -imguiRotationDegrees);
            return rect;
        }
    }

    // The screen-space canvas carrying both seat clusters (#86 round 3): the
    // seats leave IMGUI so the whole HUD renders as a few batched canvas draws
    // instead of hundreds of GUI.DrawTexture calls (9-20 fps on device).
    // IMGUI keeps the car-riding labels, pit text, center overlays, and dev
    // readouts for a later round; OnGUI draws after the canvas, so overlays
    // still cover it.
    internal sealed class RaceHud : MonoBehaviour
    {
        // Zone/label palette from the design authority (frame 40:23).
        internal static readonly Color GhostColor = new Color(.4f, .44f, .5f, .55f);
        internal static readonly Color FuelLabelColor = new Color(.95f, .55f, .2f);
        internal static readonly Color TiresLabelColor = new Color(.35f, .72f, .5f);

        internal SeatHud PlayerOne, PlayerTwo;

        public static RaceHud Create(RaceLayout layout, Color playerOneAccent,
            Color playerTwoAccent)
        {
            var root = new GameObject("Board Racing HUD");
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution =
                new Vector2(RaceLayout.ReferenceWidth, RaceLayout.ReferenceHeight);
            scaler.matchWidthOrHeight = .5f;
            var hud = root.AddComponent<RaceHud>();
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            hud.PlayerTwo = SeatHud.Create(root.transform, layout.PlayerTwo, playerTwoAccent, font);
            hud.PlayerOne = SeatHud.Create(root.transform, layout.PlayerOne, playerOneAccent, font);
            return hud;
        }

        public void Apply(RaceUiModel ui)
        {
            PlayerTwo.Apply(ui.PlayerTwo, ui.Phase);
            PlayerOne.Apply(ui.PlayerOne, ui.Phase);
        }

        internal static Color ConditionColor(ConditionVisualLevel level)
        {
            if (level == ConditionVisualLevel.Critical) return new Color(.86f, .12f, .12f);
            if (level == ConditionVisualLevel.Warning) return new Color(.94f, .62f, .08f);
            return new Color(.24f, .31f, .39f);
        }
    }
}
