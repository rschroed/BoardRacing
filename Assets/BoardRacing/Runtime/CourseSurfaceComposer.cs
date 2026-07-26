// The Course Surface Composer is a development instrument. Its style value and
// mesh path are production-capable, but the temporary controls and logging stay
// out of release players with the rest of the Visual Lab (issues #155-#157).
#if UNITY_EDITOR || (DEVELOPMENT_BUILD && UNITY_ANDROID)
using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace BoardRacing.Runtime
{
    internal sealed class CourseSurfaceComposerPanel : IVisualLabPanel
    {
        private enum ColorTarget
        {
            Ground,
            Straight,
            Corner,
            Shoulder,
        }

        private const float RowStep = 60f;
        private const float RowHeight = 50f;
        private const float ColorStep = .05f;
        private const float ShoulderWidthStep = 4f;
        private const float MaxShoulderWidth = 64f;

        private static readonly Rect ValueAction = new Rect(140f, 0f, 240f, RowHeight);
        private static readonly Rect MinusAction = new Rect(270f, 0f, 50f, RowHeight);
        private static readonly Rect PlusAction = new Rect(330f, 0f, 50f, RowHeight);

        private readonly Func<string> courseName;
        private readonly Func<bool> canSelectCourse;
        private readonly Action selectNextCourse;
        private readonly Action<RaceSurfaceStyle> applyStyle;
        private RaceSurfaceStyle style;
        private ColorTarget colorTarget;
        private Text courseValue;
        private Text colorTargetValue;
        private readonly Text[] componentValues = new Text[3];
        private Text opacityValue;
        private Text solidWidthValue;
        private Text featherWidthValue;
        private Text stripeValue;
        private Text pitValue;
        private Text debugValue;

        internal CourseSurfaceComposerPanel(
            Func<string> courseName,
            Func<bool> canSelectCourse,
            Action selectNextCourse,
            RaceSurfaceStyle initialStyle,
            Action<RaceSurfaceStyle> applyStyle)
        {
            this.courseName = courseName ?? throw new ArgumentNullException(nameof(courseName));
            this.canSelectCourse =
                canSelectCourse ?? throw new ArgumentNullException(nameof(canSelectCourse));
            this.selectNextCourse =
                selectNextCourse ?? throw new ArgumentNullException(nameof(selectNextCourse));
            this.applyStyle = applyStyle ?? throw new ArgumentNullException(nameof(applyStyle));
            style = initialStyle;
        }

        public string Id => "course-surface";
        public string Title => "COURSE SURFACE";

        public GameObject CreateContent(RectTransform parent)
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            CreateWideActionRow(parent, font, 0, "COURSE", out courseValue);
            CreateWideActionRow(parent, font, 1, "COLOR", out colorTargetValue);
            componentValues[0] = CreateStepperRow(parent, font, 2, "RED");
            componentValues[1] = CreateStepperRow(parent, font, 3, "GREEN");
            componentValues[2] = CreateStepperRow(parent, font, 4, "BLUE");
            opacityValue = CreateStepperRow(parent, font, 5, "SHOULDER OPACITY");
            solidWidthValue = CreateStepperRow(parent, font, 6, "SOLID WIDTH");
            featherWidthValue = CreateStepperRow(parent, font, 7, "FEATHER");
            CreateWideActionRow(parent, font, 8, "STRIPES", out stripeValue);
            CreateWideActionRow(parent, font, 9, "PIT SURFACE", out pitValue);
            CreateWideActionRow(parent, font, 10, "VIEW", out debugValue);

            Rect row = LocalRow(11);
            CreateButton(parent, font, "Reset", new Rect(10f, row.y, 180f, RowHeight), "RESET");
            CreateButton(parent, font, "Log", new Rect(210f, row.y, 180f, RowHeight), "LOG");
            RefreshLabels();
            return parent.gameObject;
        }

        public bool HandlePress(Vector2 referencePoint)
        {
            Vector2 local = referencePoint - VisualLabShell.ContentBounds.position;
            if (ActionAt(0, ValueAction).Contains(local))
            {
                if (canSelectCourse()) selectNextCourse();
                RefreshLabels();
                return true;
            }
            if (ActionAt(1, ValueAction).Contains(local))
            {
                colorTarget = (ColorTarget)(((int)colorTarget + 1) %
                    Enum.GetValues(typeof(ColorTarget)).Length);
                RefreshLabels();
                return true;
            }
            for (int component = 0; component < 3; component++)
            {
                int row = component + 2;
                if (ActionAt(row, MinusAction).Contains(local))
                {
                    AdjustColor(component, -ColorStep);
                    return true;
                }
                if (ActionAt(row, PlusAction).Contains(local))
                {
                    AdjustColor(component, ColorStep);
                    return true;
                }
            }
            if (HandleFloatStepper(local, 5, -0.1f, 0.1f,
                () => style.ShoulderOpacity,
                value => style.ShoulderOpacity = Mathf.Clamp01(value)))
                return true;
            if (HandleFloatStepper(local, 6, -ShoulderWidthStep, ShoulderWidthStep,
                () => style.ShoulderSolidWidth,
                value => style.ShoulderSolidWidth = Mathf.Clamp(value, 0f, MaxShoulderWidth)))
                return true;
            if (HandleFloatStepper(local, 7, -ShoulderWidthStep, ShoulderWidthStep,
                () => style.ShoulderFeatherWidth,
                value => style.ShoulderFeatherWidth = Mathf.Clamp(value, 0f, MaxShoulderWidth)))
                return true;
            if (ActionAt(8, ValueAction).Contains(local))
            {
                style.StripeVisible = !style.StripeVisible;
                Apply();
                return true;
            }
            if (ActionAt(9, ValueAction).Contains(local))
            {
                style.PitSurfaceVisible = !style.PitSurfaceVisible;
                Apply();
                return true;
            }
            if (ActionAt(10, ValueAction).Contains(local))
            {
                style.DebugView = (RaceSurfaceDebugView)(((int)style.DebugView + 1) %
                    Enum.GetValues(typeof(RaceSurfaceDebugView)).Length);
                Apply();
                return true;
            }
            Rect actions = LocalRow(11);
            if (new Rect(10f, actions.y, 180f, RowHeight).Contains(local))
            {
                style = RaceSurfaceStyle.Default;
                Apply();
                return true;
            }
            if (new Rect(210f, actions.y, 180f, RowHeight).Contains(local))
            {
                Debug.Log(CurrentLogRecord());
                return true;
            }
            return false;
        }

        public void OnShown() => RefreshLabels();
        public void OnHidden() { }

        internal RaceSurfaceStyle CurrentStyle => style;
        internal static Rect ReferenceActionBounds(int row) =>
            OffsetToReference(ActionAt(row, ValueAction));
        internal static Rect ReferenceMinusBounds(int row) =>
            OffsetToReference(ActionAt(row, MinusAction));
        internal static Rect ReferencePlusBounds(int row) =>
            OffsetToReference(ActionAt(row, PlusAction));
        internal static Rect ReferenceResetBounds =>
            OffsetToReference(new Rect(10f, LocalRow(11).y, 180f, RowHeight));
        internal static Rect ReferenceLogBounds =>
            OffsetToReference(new Rect(210f, LocalRow(11).y, 180f, RowHeight));

        internal string CurrentLogRecord()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "[CourseSurfaceComposer] course={0} ground={1} straight={2} corner={3} " +
                "shoulder={4} shoulderOpacity={5:0.00} shoulderSolidWidth={6:0.0} " +
                "shoulderFeatherWidth={7:0.0} stripes={8} pitSurface={9} view={10}",
                courseName(), Hex(style.GroundColor), Hex(style.StraightRoadColor),
                Hex(style.CornerRoadColor), Hex(style.ShoulderColor), style.ShoulderOpacity,
                style.ShoulderSolidWidth, style.ShoulderFeatherWidth,
                style.StripeVisible, style.PitSurfaceVisible, style.DebugView);
        }

        private bool HandleFloatStepper(Vector2 local, int row, float minus, float plus,
            Func<float> read, Action<float> write)
        {
            if (ActionAt(row, MinusAction).Contains(local))
            {
                write(read() + minus);
                Apply();
                return true;
            }
            if (!ActionAt(row, PlusAction).Contains(local)) return false;
            write(read() + plus);
            Apply();
            return true;
        }

        private void AdjustColor(int component, float amount)
        {
            Color color = SelectedColor();
            if (component == 0) color.r = Mathf.Clamp01(color.r + amount);
            else if (component == 1) color.g = Mathf.Clamp01(color.g + amount);
            else color.b = Mathf.Clamp01(color.b + amount);
            SetSelectedColor(color);
            Apply();
        }

        private Color SelectedColor()
        {
            if (colorTarget == ColorTarget.Ground) return style.GroundColor;
            if (colorTarget == ColorTarget.Straight) return style.StraightRoadColor;
            if (colorTarget == ColorTarget.Corner) return style.CornerRoadColor;
            return style.ShoulderColor;
        }

        private void SetSelectedColor(Color color)
        {
            if (colorTarget == ColorTarget.Ground) style.GroundColor = color;
            else if (colorTarget == ColorTarget.Straight) style.StraightRoadColor = color;
            else if (colorTarget == ColorTarget.Corner) style.CornerRoadColor = color;
            else style.ShoulderColor = color;
        }

        private void Apply()
        {
            applyStyle(style);
            RefreshLabels();
        }

        private void RefreshLabels()
        {
            if (courseValue == null) return;
            courseValue.text = canSelectCourse()
                ? courseName().ToUpperInvariant() + "  ›"
                : courseName().ToUpperInvariant() + "  · LOCKED";
            colorTargetValue.text = colorTarget.ToString().ToUpperInvariant() + "  ›";
            Color color = SelectedColor();
            componentValues[0].text = Mathf.RoundToInt(color.r * 255f).ToString();
            componentValues[1].text = Mathf.RoundToInt(color.g * 255f).ToString();
            componentValues[2].text = Mathf.RoundToInt(color.b * 255f).ToString();
            opacityValue.text = style.ShoulderOpacity.ToString("0.00", CultureInfo.InvariantCulture);
            solidWidthValue.text =
                style.ShoulderSolidWidth.ToString("0", CultureInfo.InvariantCulture) + " PX";
            featherWidthValue.text =
                style.ShoulderFeatherWidth.ToString("0", CultureInfo.InvariantCulture) + " PX";
            stripeValue.text = style.StripeVisible ? "VISIBLE" : "HIDDEN";
            pitValue.text = style.PitSurfaceVisible ? "VISIBLE" : "HIDDEN";
            debugValue.text = DebugName(style.DebugView) + "  ›";
        }

        private static void CreateWideActionRow(RectTransform parent, Font font, int index,
            string label, out Text value)
        {
            Rect row = LocalRow(index);
            RaceHud.CreatePanel(parent, label + " Row", row,
                new Color(.07f, .095f, .13f, .96f), 7f);
            Text name = RaceHud.CreateLabel(parent, label + " Label",
                new Rect(10f, row.y, 120f, RowHeight), 13,
                new Color(.63f, .68f, .74f), font);
            name.alignment = TextAnchor.MiddleLeft;
            name.text = label;
            CreateButton(parent, font, label + " Action",
                new Rect(ValueAction.x, row.y, ValueAction.width, RowHeight), string.Empty,
                out value);
        }

        private static Text CreateStepperRow(RectTransform parent, Font font, int index,
            string label)
        {
            Rect row = LocalRow(index);
            RaceHud.CreatePanel(parent, label + " Row", row,
                new Color(.07f, .095f, .13f, .96f), 7f);
            Text name = RaceHud.CreateLabel(parent, label + " Label",
                new Rect(10f, row.y, 125f, RowHeight), 13,
                new Color(.63f, .68f, .74f), font);
            name.alignment = TextAnchor.MiddleLeft;
            name.text = label;
            Text value = RaceHud.CreateLabel(parent, label + " Value",
                new Rect(140f, row.y, 120f, RowHeight), 16, Color.white, font);
            CreateButton(parent, font, label + " Minus",
                new Rect(MinusAction.x, row.y, MinusAction.width, RowHeight), "−");
            CreateButton(parent, font, label + " Plus",
                new Rect(PlusAction.x, row.y, PlusAction.width, RowHeight), "+");
            return value;
        }

        private static void CreateButton(RectTransform parent, Font font, string name,
            Rect bounds, string label)
        {
            CreateButton(parent, font, name, bounds, label, out _);
        }

        private static void CreateButton(RectTransform parent, Font font, string name,
            Rect bounds, string label, out Text text)
        {
            RoundedRectGraphic background = RaceHud.CreatePanel(parent, name + " Background",
                bounds, new Color(.13f, .18f, .25f, .98f), 7f);
            text = RaceHud.CreateLabel(background.transform, name + " Label",
                new Rect(0f, 0f, bounds.width, bounds.height), 15, Color.white, font);
            text.text = label;
        }

        private static Rect LocalRow(int index) =>
            new Rect(0f, index * RowStep, VisualLabShell.ContentBounds.width, RowHeight);

        private static Rect ActionAt(int row, Rect action) =>
            new Rect(action.x, LocalRow(row).y, action.width, action.height);

        private static Rect OffsetToReference(Rect local) =>
            new Rect(local.position + VisualLabShell.ContentBounds.position, local.size);

        private static string Hex(Color color) => "#" + ColorUtility.ToHtmlStringRGBA(color);

        private static string DebugName(RaceSurfaceDebugView view) =>
            view == RaceSurfaceDebugView.ShoulderOnly ? "SHOULDER ONLY" :
            view == RaceSurfaceDebugView.RoadBoundary ? "ROAD BOUNDARY" : "COMPOSED";
    }
}
#endif
