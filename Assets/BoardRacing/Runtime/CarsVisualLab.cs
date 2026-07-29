// The Cars panel is a development-only inspection instrument. It exercises the
// production car renderer and response vocabulary without entering the race
// roster or feeding commands into the deterministic simulation (issue #180).
#if UNITY_EDITOR || (DEVELOPMENT_BUILD && UNITY_ANDROID)
using System;
using System.Globalization;
using BoardRacing.Domain;
using UnityEngine;
using UnityEngine.UI;

namespace BoardRacing.Runtime
{
    internal enum CarStudyTarget
    {
        All = 0,
        Player1 = 1,
        Player2 = 2,
        Player3 = 3,
        Player4 = 4
    }

    internal readonly struct CarStudyPresentation
    {
        public CarStudyPresentation(bool enabled, CarStudyTarget target,
            CarResponseState response)
        {
            Enabled = enabled;
            Target = target;
            Response = response;
        }

        public bool Enabled { get; }
        public CarStudyTarget Target { get; }
        public CarResponseState Response { get; }

        public bool AppliesTo(PlayerId playerId) =>
            Target == CarStudyTarget.All || (int)Target == (int)playerId;

        public static CarStudyPresentation Live =>
            new CarStudyPresentation(false, CarStudyTarget.All, CarResponseState.Still);
    }

    internal sealed class CarsVisualLabPanel : IVisualLabPanel
    {
        private const float RowStep = 64f;
        private const float RowHeight = 52f;
        private const float ResponseStep = .25f;
        private static readonly Rect ValueAction = new Rect(140f, 0f, 240f, RowHeight);
        private static readonly Rect MinusAction = new Rect(270f, 0f, 50f, RowHeight);
        private static readonly Rect PlusAction = new Rect(330f, 0f, 50f, RowHeight);

        private readonly Action<CarStudyPresentation> applyPresentation;
        private bool studyEnabled;
        private CarStudyTarget target = CarStudyTarget.All;
        private CarResponseState response = CarResponseState.Still;
        private int presetIndex;
        private Text modeValue;
        private Text targetValue;
        private Text presetValue;
        private Text driveValue;
        private Text brakeValue;
        private Text boostValue;
        private Text cornerValue;

        internal CarsVisualLabPanel(Action<CarStudyPresentation> applyPresentation)
        {
            this.applyPresentation = applyPresentation ??
                throw new ArgumentNullException(nameof(applyPresentation));
        }

        public string Id => "cars";
        public string TabLabel => "CARS";
        public string Title => "CARS";

        public GameObject CreateContent(RectTransform parent)
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            CreateWideActionRow(parent, font, 0, "MODE", out modeValue);
            CreateWideActionRow(parent, font, 1, "TARGET", out targetValue);
            CreateWideActionRow(parent, font, 2, "PRESET", out presetValue);
            driveValue = CreateStepperRow(parent, font, 3, "DRIVE");
            brakeValue = CreateStepperRow(parent, font, 4, "BRAKE");
            boostValue = CreateStepperRow(parent, font, 5, "BOOST");
            cornerValue = CreateStepperRow(parent, font, 6, "FAST CORNER");

            Rect actions = LocalRow(ActionRow);
            CreateButton(parent, font, "Reset",
                new Rect(10f, actions.y, 180f, RowHeight), "RESET");
            CreateButton(parent, font, "Log",
                new Rect(210f, actions.y, 180f, RowHeight), "LOG");
            RefreshLabels();
            return parent.gameObject;
        }

        public bool HandlePress(Vector2 referencePoint)
        {
            Vector2 local = referencePoint - VisualLabShell.ContentBounds.position;
            if (ActionAt(0, ValueAction).Contains(local))
            {
                studyEnabled = !studyEnabled;
                Apply();
                return true;
            }
            if (ActionAt(1, ValueAction).Contains(local))
            {
                target = (CarStudyTarget)(((int)target + 1) % 5);
                Apply();
                return true;
            }
            if (ActionAt(2, ValueAction).Contains(local))
            {
                SelectPreset((presetIndex + 1) % (PresetNames.Length - 1));
                Apply();
                return true;
            }
            if (HandleStepper(local, 3, 0)) return true;
            if (HandleStepper(local, 4, 1)) return true;
            if (HandleStepper(local, 5, 2)) return true;
            if (HandleStepper(local, 6, 3)) return true;

            Rect actions = LocalRow(ActionRow);
            if (new Rect(10f, actions.y, 180f, RowHeight).Contains(local))
            {
                studyEnabled = false;
                target = CarStudyTarget.All;
                SelectPreset(0);
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

        internal CarStudyPresentation CurrentPresentation =>
            new CarStudyPresentation(studyEnabled, target, response);
        internal int PresetIndex => presetIndex;
        internal const int ActionRow = 7;
        internal static Rect ReferenceActionBounds(int row) =>
            OffsetToReference(ActionAt(row, ValueAction));
        internal static Rect ReferenceMinusBounds(int row) =>
            OffsetToReference(ActionAt(row, MinusAction));
        internal static Rect ReferencePlusBounds(int row) =>
            OffsetToReference(ActionAt(row, PlusAction));
        internal static Rect ReferenceResetBounds =>
            OffsetToReference(new Rect(10f, LocalRow(ActionRow).y, 180f, RowHeight));
        internal static Rect ReferenceLogBounds =>
            OffsetToReference(new Rect(210f, LocalRow(ActionRow).y, 180f, RowHeight));

        internal string CurrentLogRecord()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "[CarsVisualLab] mode={0} target={1} preset={2} " +
                "drive={3:0.00} brake={4:0.00} boost={5:0.00} corner={6:0.00}",
                studyEnabled ? "Study" : "Live", TargetName(target),
                PresetNames[presetIndex], response.Drive, response.Brake,
                response.Boost, response.Corner);
        }

        private bool HandleStepper(Vector2 local, int row, int channel)
        {
            if (ActionAt(row, MinusAction).Contains(local))
            {
                Adjust(channel, -ResponseStep);
                return true;
            }
            if (!ActionAt(row, PlusAction).Contains(local)) return false;
            Adjust(channel, ResponseStep);
            return true;
        }

        private void Adjust(int channel, float delta)
        {
            float drive = response.Drive;
            float brake = response.Brake;
            float boost = response.Boost;
            float corner = response.Corner;
            if (channel == 0) drive += delta;
            else if (channel == 1) brake += delta;
            else if (channel == 2) boost += delta;
            else corner += delta;
            response = new CarResponseState(drive, brake, boost, corner);
            presetIndex = PresetNames.Length - 1;
            Apply();
        }

        private void SelectPreset(int index)
        {
            presetIndex = index;
            response = index == 1 ? new CarResponseState(1f, 0f, 0f, 0f) :
                index == 2 ? new CarResponseState(0f, 1f, 0f, 0f) :
                index == 3 ? new CarResponseState(0f, 0f, 1f, 0f) :
                index == 4 ? new CarResponseState(0f, 0f, 0f, 1f) :
                CarResponseState.Still;
        }

        private void Apply()
        {
            applyPresentation(CurrentPresentation);
            RefreshLabels();
        }

        private void RefreshLabels()
        {
            if (modeValue == null) return;
            modeValue.text = (studyEnabled ? "STUDY" : "LIVE") + "  ›";
            targetValue.text = TargetName(target).ToUpperInvariant() + "  ›";
            presetValue.text = PresetNames[presetIndex].ToUpperInvariant() + "  ›";
            driveValue.text = Percent(response.Drive);
            brakeValue.text = Percent(response.Brake);
            boostValue.text = Percent(response.Boost);
            cornerValue.text = Percent(response.Corner);
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
                new Rect(ValueAction.x, row.y, ValueAction.width, RowHeight),
                string.Empty, out value);
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
            RoundedRectGraphic background = RaceHud.CreatePanel(parent,
                name + " Background", bounds, new Color(.13f, .18f, .25f, .98f), 7f);
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

        private static string Percent(float value) =>
            Mathf.RoundToInt(value * 100f).ToString(CultureInfo.InvariantCulture) + "%";

        private static string TargetName(CarStudyTarget value) =>
            value == CarStudyTarget.All ? "All" : "P" + (int)value;

        private static readonly string[] PresetNames =
            { "Neutral", "Drive", "Brake", "Boost", "Fast Corner", "Custom" };
    }
}
#endif
