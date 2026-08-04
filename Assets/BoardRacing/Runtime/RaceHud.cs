using System.Collections.Generic;
using System.Linq;
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

    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class RoundedRectGraphic : MaskableGraphic
    {
        private float radius = 10f;

        private RoundedRectGraphic() => useLegacyMeshGeneration = false;

        public float Radius
        {
            get => radius;
            set
            {
                float next = Mathf.Max(0f, value);
                if (Mathf.Approximately(radius, next)) return;
                radius = next;
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect bounds = rectTransform.rect;
            float r = Mathf.Min(radius, Mathf.Min(bounds.width, bounds.height) * .5f);
            var vertex = UIVertex.simpleVert;
            vertex.color = color;
            vertex.position = bounds.center;
            vh.AddVert(vertex);
            const int segments = 5;
            int perimeter = 0;
            for (int corner = 0; corner < 4; corner++)
            {
                float centerX = corner == 0 || corner == 3
                    ? bounds.xMax - r : bounds.xMin + r;
                float centerY = corner < 2 ? bounds.yMax - r : bounds.yMin + r;
                float start = corner == 0 ? 0f : corner == 1 ? 90f :
                    corner == 2 ? 180f : 270f;
                for (int step = 0; step <= segments; step++)
                {
                    float angle = (start + 90f * step / segments) * Mathf.Deg2Rad;
                    vertex.position = new Vector2(centerX + Mathf.Cos(angle) * r,
                        centerY + Mathf.Sin(angle) * r);
                    vh.AddVert(vertex);
                    perimeter++;
                }
            }
            for (int i = 0; i < perimeter; i++)
                vh.AddTriangle(0, 1 + i, 1 + ((i + 1) % perimeter));
        }
    }

    // One seat cluster (throttle arc, ship well, Call Pit circle, condition
    // dials, their labels) as uGUI elements built from the same
    // CornerControllerLayout the IMGUI pass drew (#86 round 3). Geometry is
    // fixed at creation; Apply binds the per-frame RaceUiModel exactly the way
    // the old DrawCornerController/DrawCrewRegions code paths did.
    internal sealed class SeatHud
    {
        internal GameObject Container;
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
            var seat = new SeatHud { Container = container, Layout = layout, Accent = accent };
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

        public void SetVisible(bool visible) => Container.SetActive(visible);

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
            // Setup uses the same physical cockpit but has only one meaningful
            // state: Drive means ready. Brake and Boost stay locked-looking
            // instead of falsely interpreting any non-ready angle as Brake.
            bool fullThrottle = throttleLive && !model.DriveOnlyThrottle;
            ApplySector(Brake, ThrottleStep.Brake, model.Throttle, fullThrottle);
            ApplySector(Drive, ThrottleStep.Drive, model.Throttle, throttleLive);
            ApplySector(Boost, ThrottleStep.Boost, model.Throttle, fullThrottle);
        }

        public void SetAccent(Color accent)
        {
            Accent = accent;
            Brake.ActiveFill.color = accent;
            Drive.ActiveFill.color = accent;
            Boost.ActiveFill.color = accent;
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

    internal sealed class SetupSeatHud
    {
        internal GameObject Container;
        internal Text Primary, Name, Edit;

        internal static SetupSeatHud Create(Transform parent, PlayerLayout layout, Font font)
        {
            Rect well = new Rect(layout.Controller.ShipWellCenter.x - 138f,
                layout.Controller.ShipWellCenter.y - 138f, 276f, 276f);
            bool opposite = layout.Opposite;
            Rect primary = VisualRect(new Rect(well.x + 30f, well.y + 95f,
                well.width - 60f, 86f), well.center, opposite);
            Rect name = VisualRect(new Rect(well.x + 38f, well.y + 15f,
                well.width - 106f, 34f), well.center, opposite);
            Rect edit = VisualRect(new Rect(well.xMax - 70f, well.y + 15f, 54f, 34f),
                well.center, opposite);
            float rotation = opposite ? 180f : 0f;
            var root = RaceHud.CreateFullScreenNode(parent, "Setup Seat " + layout.PlayerId);
            return new SetupSeatHud
            {
                Container = root.gameObject,
                Primary = RaceHud.CreateLabel(root, "Primary", primary, 25, Color.white,
                    font, rotation),
                Name = RaceHud.CreateLabel(root, "Player Name", name, 17,
                    new Color(.82f, .86f, .92f), font, rotation),
                Edit = RaceHud.CreateLabel(root, "Edit", edit, 17,
                    new Color(.82f, .86f, .92f), font, rotation)
            };
        }

        internal void Apply(LobbySeatUiModel model)
        {
            Container.SetActive(true);
            if (!model.Active)
            {
                Primary.text = model.AddAvailable ? "+\nADD PLAYER" : "INACTIVE";
                Primary.gameObject.SetActive(true);
                Name.gameObject.SetActive(false);
                Edit.gameObject.SetActive(false);
                return;
            }
            Primary.text = model.HasShip ? string.Empty : "PLACE\nSHIP";
            Primary.gameObject.SetActive(!model.HasShip);
            Name.text = model.PlayerName ?? string.Empty;
            Name.gameObject.SetActive(true);
            Edit.text = "EDIT";
            Edit.gameObject.SetActive(true);
        }

        private static Rect VisualRect(Rect rect, Vector2 pivot, bool opposite) =>
            !opposite ? rect : new Rect(2f * pivot.x - rect.xMax,
                2f * pivot.y - rect.yMax, rect.width, rect.height);
    }

    internal sealed class SetupHud
    {
        internal GameObject Container;
        internal RoundedRectGraphic Panel, CoursePanel, StartPanel;
        internal Text Heading, Instruction, Detail, Course, Start;
        internal readonly Dictionary<PlayerId, SetupSeatHud> Seats =
            new Dictionary<PlayerId, SetupSeatHud>();

        internal static SetupHud Create(Transform parent, RaceLayout layout, Font font)
        {
            var root = RaceHud.CreateFullScreenNode(parent, "Player Setup");
            var setup = new SetupHud { Container = root.gameObject };
            setup.Panel = RaceHud.CreatePanel(root, "Setup Panel",
                new Rect(570f, 390f, 780f, 440f), new Color(.03f, .04f, .06f, .9f));
            setup.Heading = RaceHud.CreateLabel(root, "Heading",
                new Rect(560f, 420f, 800f, 55f), 42, Color.white, font);
            setup.Instruction = RaceHud.CreateLabel(root, "Instruction",
                new Rect(610f, 480f, 700f, 55f), 27,
                new Color(.9f, .93f, .97f), font);
            setup.Detail = RaceHud.CreateLabel(root, "Detail",
                new Rect(640f, 535f, 640f, 72f), 17,
                new Color(.82f, .86f, .92f), font);
            setup.CoursePanel = RaceHud.CreatePanel(root, "Course",
                new Rect(680f, 650f, 560f, 58f), new Color(.09f, .12f, .18f));
            setup.Course = RaceHud.CreateLabel(root, "Course Label",
                new Rect(680f, 650f, 560f, 58f), 27,
                new Color(.9f, .93f, .97f), font);
            setup.StartPanel = RaceHud.CreatePanel(root, "Start",
                new Rect(760f, 730f, 400f, 76f), new Color(.12f, .15f, .2f));
            setup.Start = RaceHud.CreateLabel(root, "Start Label",
                new Rect(760f, 730f, 400f, 76f), 27,
                new Color(.9f, .93f, .97f), font);
            foreach (PlayerId id in new[]
                { PlayerId.Player1, PlayerId.Player2, PlayerId.Player3, PlayerId.Player4 })
                setup.Seats[id] = SetupSeatHud.Create(root, layout.For(id), font);
            return setup;
        }

        internal void Apply(PlayerLobbyUiModel model)
        {
            Container.SetActive(true);
            Heading.text = "CHOOSE YOUR RACERS";
            Instruction.text = model.PlayerCount < 2
                ? "ADD AT LEAST TWO PLAYERS" : "PLACE ONE SHIP IN EACH COCKPIT";
            Detail.text = model.AllPlayersReady ? "ALL RACERS READY"
                : model.CanStart ? "SET EVERY SHIP TO DRIVE" : string.Empty;
            Course.text = "COURSE: " + model.CourseName.ToUpperInvariant() +
                " · TAP TO CHANGE";
            StartPanel.color = model.AllPlayersReady
                ? new Color(.18f, .42f, .72f) : new Color(.12f, .15f, .2f);
            Start.text = model.AllPlayersReady ? "START RACE" : "START RACE · WAITING";
            foreach (LobbySeatUiModel seat in model.Seats)
                Seats[seat.PlayerId].Apply(seat);
        }
    }

    internal sealed class SharedRaceHud
    {
        internal GameObject Container, Overlay;
        internal RoundedRectGraphic ExitPanel, OverlayPanel, PrimaryPanel, SecondaryPanel;
        internal Text Center, Exit, Heading, SubLine, Primary, Secondary;

        internal static SharedRaceHud Create(Transform parent, Font font)
        {
            var root = RaceHud.CreateFullScreenNode(parent, "Shared Race UI");
            var shared = new SharedRaceHud { Container = root.gameObject };
            shared.Center = RaceHud.CreateLabel(root, "Center Message",
                new Rect(460f, 430f, 1000f, 220f), 42, Color.white, font);
            shared.ExitPanel = RaceHud.CreatePanel(root, "Exit",
                new Rect(855f, 20f, 210f, 52f), new Color(.09f, .12f, .18f, .9f));
            shared.Exit = RaceHud.CreateLabel(root, "Exit Label",
                new Rect(855f, 20f, 210f, 52f), 15,
                new Color(.87f, .9f, .94f), font);

            RectTransform overlay = RaceHud.CreateFullScreenNode(root, "Overlay");
            shared.Overlay = overlay.gameObject;
            shared.OverlayPanel = RaceHud.CreatePanel(overlay, "Panel",
                new Rect(460f, 430f, 1000f, 290f), new Color(.03f, .04f, .06f, .93f));
            shared.Heading = RaceHud.CreateLabel(overlay, "Heading",
                new Rect(460f, 446f, 1000f, 52f), 42, Color.white, font);
            shared.SubLine = RaceHud.CreateLabel(overlay, "Subline",
                new Rect(460f, 506f, 1000f, 40f), 26,
                new Color(1f, .75f, .2f), font);
            shared.PrimaryPanel = RaceHud.CreatePanel(overlay, "Primary",
                new Rect(700f, 560f, 520f, 70f), new Color(.14f, .2f, .3f));
            shared.Primary = RaceHud.CreateLabel(overlay, "Primary Label",
                new Rect(700f, 560f, 520f, 70f), 22, Color.white, font);
            shared.SecondaryPanel = RaceHud.CreatePanel(overlay, "Secondary",
                new Rect(700f, 648f, 520f, 48f), new Color(.09f, .12f, .18f));
            shared.Secondary = RaceHud.CreateLabel(overlay, "Secondary Label",
                new Rect(700f, 648f, 520f, 48f), 22, Color.white, font);
            return shared;
        }

        internal void Apply(RaceUiModel ui, bool exitConfirmationOpen)
        {
            Container.SetActive(true);
            bool overlay = exitConfirmationOpen ||
                ui.CenterMessageKind == CenterMessageKind.Paused ||
                ui.CenterMessageKind == CenterMessageKind.Winner;
            Overlay.SetActive(overlay);
            Center.gameObject.SetActive(!overlay && !string.IsNullOrEmpty(ui.CenterMessage));
            Center.text = ui.CenterMessage ?? string.Empty;
            bool exitVisible = !exitConfirmationOpen &&
                ui.Phase != RacePhase.Paused && ui.Phase != RacePhase.Finished;
            ExitPanel.gameObject.SetActive(exitVisible);
            Exit.gameObject.SetActive(exitVisible);
            Exit.text = "EXIT TO SETUP";
            if (!overlay) return;

            if (exitConfirmationOpen)
                SetOverlay("RETURN TO SETUP?", "THE CURRENT RACE WILL END",
                    "RETURN TO PLAYER SETUP", "RESUME RACE");
            else if (ui.CenterMessageKind == CenterMessageKind.Paused)
                SetOverlay("RACE PAUSED", ui.CenterMessage,
                    "RETURN TO PLAYER SETUP", null);
            else
                SetOverlay("RACE FINISHED", ui.CenterMessage,
                    "REMATCH", "PLAYER / COURSE SETUP");
        }

        private void SetOverlay(string heading, string subLine, string primary,
            string secondary)
        {
            Heading.text = heading;
            SubLine.text = subLine ?? string.Empty;
            Primary.text = primary;
            bool hasSecondary = !string.IsNullOrEmpty(secondary);
            SecondaryPanel.gameObject.SetActive(hasSecondary);
            Secondary.gameObject.SetActive(hasSecondary);
            Secondary.text = secondary ?? string.Empty;
        }
    }

    // One screen-space canvas now owns the complete production HUD: seats,
    // setup, race navigation, center messages, and annotations. The track and
    // car bodies remain world-space meshes; ControlLab is the sole IMGUI
    // diagnostic exemption.
    internal sealed class RaceHud : MonoBehaviour
    {
        // Zone/label palette from the design authority (frame 40:23).
        internal static readonly Color GhostColor = new Color(.4f, .44f, .5f, .55f);
        internal static readonly Color FuelLabelColor = new Color(.95f, .55f, .2f);
        internal static readonly Color TiresLabelColor = new Color(.35f, .72f, .5f);

        internal SeatHud PlayerOne, PlayerTwo, PlayerThree, PlayerFour;
        internal SetupHud Setup;
        internal SharedRaceHud Shared;
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

        public static RaceHud CreateFour(RaceLayout layout, Color playerOneAccent,
            Color playerTwoAccent, Color playerThreeAccent, Color playerFourAccent)
        {
            if (!layout.HasFourSeats)
                throw new System.ArgumentException("Four-seat HUD needs a four-seat layout.",
                    nameof(layout));
            RaceHud hud = Create(layout, playerOneAccent, playerTwoAccent);
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            hud.PlayerFour = SeatHud.Create(hud.transform, layout.PlayerFour, playerFourAccent, font);
            hud.PlayerThree = SeatHud.Create(hud.transform, layout.PlayerThree, playerThreeAccent, font);
            hud.Shared = SharedRaceHud.Create(hud.transform, font);
            hud.Setup = SetupHud.Create(hud.transform, layout, font);
            hud.Shared.Container.SetActive(false);
            hud.Setup.Container.SetActive(false);
            return hud;
        }

        public void Apply(RaceUiModel ui)
        {
            ApplySeats(ui);
        }

        public void ApplyLobby(RaceUiModel cockpitUi, PlayerLobbyUiModel lobbyUi)
        {
            ApplySeats(cockpitUi);
            Setup.Apply(lobbyUi);
            Shared.Container.SetActive(false);
        }

        public void ApplyRace(RaceUiModel ui, bool exitConfirmationOpen)
        {
            ApplySeats(ui);
            Setup.Container.SetActive(false);
            Shared.Apply(ui, exitConfirmationOpen);
        }

        private void ApplySeats(RaceUiModel ui)
        {
            SeatHud[] seats = { PlayerOne, PlayerTwo, PlayerThree, PlayerFour };
            foreach (SeatHud seat in seats)
            {
                if (seat == null) continue;
                PlayerUiModel? model = ui.Players
                    .Where(x => x.PlayerId == seat.Layout.PlayerId)
                    .Select(x => (PlayerUiModel?)x).FirstOrDefault();
                seat.SetVisible(model.HasValue);
                if (model.HasValue) seat.Apply(model.Value, ui.Phase);
            }
        }

        public void SetAccents(Color playerOne, Color playerTwo)
        {
            PlayerOne.SetAccent(playerOne);
            PlayerTwo.SetAccent(playerTwo);
        }

        public void SetAccents(Color playerOne, Color playerTwo, Color playerThree,
            Color playerFour)
        {
            SetAccents(playerOne, playerTwo);
            PlayerThree.SetAccent(playerThree);
            PlayerFour.SetAccent(playerFour);
        }

        internal static Color ConditionColor(ConditionVisualLevel level)
        {
            if (level == ConditionVisualLevel.Critical) return new Color(.86f, .12f, .12f);
            if (level == ConditionVisualLevel.Warning) return new Color(.94f, .62f, .08f);
            return new Color(.24f, .31f, .39f);
        }

        internal static RectTransform CreateFullScreenNode(Transform parent, string name)
        {
            var go = new GameObject(name);
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        internal static Text CreateLabel(Transform parent, string name, Rect bounds,
            int fontSize, Color color, Font font, float rotationDegrees = 0f)
        {
            RectTransform rect = CreateReferenceNode(parent, name, bounds);
            rect.localRotation = Quaternion.Euler(0f, 0f, -rotationDegrees);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        internal static RoundedRectGraphic CreatePanel(Transform parent, string name,
            Rect bounds, Color color, float radius = 10f)
        {
            RectTransform rect = CreateReferenceNode(parent, name, bounds);
            var panel = rect.gameObject.AddComponent<RoundedRectGraphic>();
            panel.color = color;
            panel.Radius = radius;
            panel.raycastTarget = false;
            return panel;
        }

        internal static void Place(RectTransform rect, Rect bounds)
        {
            rect.sizeDelta = bounds.size;
            rect.anchoredPosition = new Vector2(bounds.center.x, -bounds.center.y);
        }

        private static RectTransform CreateReferenceNode(Transform parent, string name, Rect bounds)
        {
            var go = new GameObject(name);
            var rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            Place(rect, bounds);
            return rect;
        }
    }
}
