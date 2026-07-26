// The Visual Lab is an instrument for editor and Board development builds.
// Keep the entire type graph out of release players rather than hiding it
// behind a runtime setting (issues #155 and #156).
#if UNITY_EDITOR || (DEVELOPMENT_BUILD && UNITY_ANDROID)
using System;
using System.Collections.Generic;
using System.Linq;
using Board.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BoardRacing.Runtime
{
    /// <summary>
    /// The deliberately small contract between the development overlay shell
    /// and a subject-specific panel. Panels own their temporary state and
    /// controls; the shell owns only lifecycle, chrome, and stage composition.
    /// </summary>
    internal interface IVisualLabPanel
    {
        string Id { get; }
        string Title { get; }
        GameObject CreateContent(RectTransform parent);
        bool HandlePress(Vector2 referencePoint);
        void OnShown();
        void OnHidden();
    }

    internal sealed class VisualLabShell : MonoBehaviour
    {
        // Reference-space geometry is intentionally fixed and utilitarian. It
        // is development chrome, not a production layout or a window manager.
        internal static readonly Rect LauncherBounds = new Rect(1740f, 500f, 150f, 80f);
        internal static readonly Rect PanelBounds = new Rect(1390f, 60f, 470f, 960f);
        internal static readonly Rect CloseBounds = new Rect(1790f, 80f, 50f, 50f);
        internal static readonly Rect CleanViewBounds = new Rect(1420f, 150f, 190f, 58f);
        internal static readonly Rect CarsBounds = new Rect(1420f, 224f, 190f, 58f);
        internal static readonly Rect HudBounds = new Rect(1630f, 224f, 190f, 58f);
        internal static readonly Rect ContentBounds = new Rect(1420f, 310f, 400f, 670f);

        private sealed class PanelSlot
        {
            public PanelSlot(IVisualLabPanel panel, GameObject content)
            {
                Panel = panel;
                Content = content;
            }

            public IVisualLabPanel Panel { get; }
            public GameObject Content { get; }
        }

        private readonly List<PanelSlot> panels = new List<PanelSlot>();
        private Action<bool> setCarsVisible;
        private Action<bool> setHudVisible;
        private Canvas canvas;
        private GameObject launcher;
        private GameObject panelChrome;
        private Text title;
        private Text carsLabel;
        private Text hudLabel;
        private Text noPanelsLabel;
        private PanelSlot activePanel;
        private bool activePanelShown;
        private bool available;
        private bool open;
        private bool cleanView;
        private bool carsVisible = true;
        private bool hudVisible = true;

        internal bool Available => available;
        internal bool IsOpen => open;
        internal bool IsCleanView => cleanView;
        internal bool CarsVisible => carsVisible;
        internal bool HudVisible => hudVisible;
        internal string ActivePanelId => activePanel?.Panel.Id;
        internal int RegisteredPanelCount => panels.Count;

        internal static VisualLabShell Create(Transform owner,
            Action<bool> setCarsVisible, Action<bool> setHudVisible, bool startAvailable)
        {
            if (setCarsVisible == null) throw new ArgumentNullException(nameof(setCarsVisible));
            if (setHudVisible == null) throw new ArgumentNullException(nameof(setHudVisible));

            var root = new GameObject("Board Racing Visual Lab");
            root.transform.SetParent(owner, false);
            var shell = root.AddComponent<VisualLabShell>();
            shell.setCarsVisible = setCarsVisible;
            shell.setHudVisible = setHudVisible;
            shell.BuildChrome();
            shell.available = startAvailable;
            shell.RefreshVisibility();
            shell.ReapplyStageComposition();
            return shell;
        }

        internal void Register(IVisualLabPanel panel)
        {
            if (panel == null) throw new ArgumentNullException(nameof(panel));
            if (string.IsNullOrWhiteSpace(panel.Id))
                throw new ArgumentException("A Visual Lab panel needs a stable identifier.",
                    nameof(panel));
            if (panels.Any(x => x.Panel.Id == panel.Id))
                throw new InvalidOperationException(
                    "A Visual Lab panel with id '" + panel.Id + "' is already registered.");

            var holder = new GameObject("Panel Content " + panel.Id);
            var rect = holder.AddComponent<RectTransform>();
            rect.SetParent(panelChrome.transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            RaceHud.Place(rect, ContentBounds);
            GameObject content = panel.CreateContent(rect);
            if (content == null)
                throw new InvalidOperationException(
                    "Visual Lab panel '" + panel.Id + "' returned no content root.");
            if (content != holder && !content.transform.IsChildOf(holder.transform))
                throw new InvalidOperationException(
                    "Visual Lab panel content must belong to the supplied content root.");

            holder.SetActive(false);
            var slot = new PanelSlot(panel, holder);
            panels.Add(slot);
            if (activePanel == null) activePanel = slot;
            RefreshVisibility();
        }

        internal void Select(string panelId)
        {
            PanelSlot next = panels.SingleOrDefault(x => x.Panel.Id == panelId);
            if (next == null)
                throw new ArgumentOutOfRangeException(nameof(panelId),
                    "No Visual Lab panel is registered as '" + panelId + "'.");
            if (next == activePanel) return;
            SetActivePanelShown(false);
            activePanel = next;
            RefreshVisibility();
        }

        internal void SetAvailable(bool value)
        {
            if (available == value) return;
            available = value;
            if (!available) cleanView = false;
            RefreshVisibility();
        }

#if UNITY_EDITOR
        internal void PollEditorShortcut()
        {
            if (Keyboard.current != null && Keyboard.current.f10Key.wasPressedThisFrame)
                SetAvailable(!available);
        }
#else
        internal void PollEditorShortcut() { }
#endif

        internal bool PollInput()
        {
            return TryPressed(out Vector2 referencePoint) &&
                HandleReferencePress(referencePoint);
        }

        internal bool HandleReferencePress(Vector2 referencePoint)
        {
            if (!available) return false;
            if (cleanView)
            {
                if (!LauncherBounds.Contains(referencePoint)) return false;
                cleanView = false;
                RefreshVisibility();
                return true;
            }
            if (!open)
            {
                if (!LauncherBounds.Contains(referencePoint)) return false;
                open = true;
                RefreshVisibility();
                return true;
            }
            if (!PanelBounds.Contains(referencePoint)) return false;

            if (CloseBounds.Contains(referencePoint))
            {
                open = false;
                RefreshVisibility();
            }
            else if (CleanViewBounds.Contains(referencePoint))
            {
                cleanView = true;
                RefreshVisibility();
            }
            else if (CarsBounds.Contains(referencePoint))
            {
                carsVisible = !carsVisible;
                setCarsVisible(carsVisible);
                RefreshLabels();
            }
            else if (HudBounds.Contains(referencePoint))
            {
                hudVisible = !hudVisible;
                setHudVisible(hudVisible);
                RefreshLabels();
            }
            else if (activePanel != null && ContentBounds.Contains(referencePoint))
            {
                activePanel.Panel.HandlePress(referencePoint);
            }

            // The full panel consumes presses, including blank chrome, so a lab
            // adjustment can never activate setup or race navigation beneath it.
            return true;
        }

        internal void ReapplyStageComposition()
        {
            setCarsVisible(carsVisible);
            setHudVisible(hudVisible);
        }

        private void BuildChrome()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 100;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution =
                new Vector2(RaceLayout.ReferenceWidth, RaceLayout.ReferenceHeight);
            scaler.matchWidthOrHeight = .5f;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            launcher = RaceHud.CreatePanel(transform, "Visual Lab Launcher", LauncherBounds,
                new Color(.04f, .06f, .09f, .94f), 12f).gameObject;
            RaceHud.CreateLabel(launcher.transform, "Launcher Label",
                new Rect(0f, 0f, LauncherBounds.width, LauncherBounds.height),
                18, Color.white, font).text = "VISUAL LAB";

            panelChrome = RaceHud.CreateFullScreenNode(transform, "Visual Lab Panel").gameObject;
            RaceHud.CreatePanel(panelChrome.transform, "Panel Background", PanelBounds,
                new Color(.035f, .05f, .075f, .97f), 14f);
            title = RaceHud.CreateLabel(panelChrome.transform, "Panel Title",
                new Rect(1420f, 80f, 350f, 50f), 23, Color.white, font);
            CreateButton(panelChrome.transform, "Close", CloseBounds, "×", font);
            CreateButton(panelChrome.transform, "Clean View", CleanViewBounds, "CLEAN VIEW", font);
            carsLabel = CreateButton(panelChrome.transform, "Cars", CarsBounds, null, font);
            hudLabel = CreateButton(panelChrome.transform, "HUD", HudBounds, null, font);
            noPanelsLabel = RaceHud.CreateLabel(panelChrome.transform, "No Panels",
                ContentBounds, 18, new Color(.68f, .72f, .78f), font);
            noPanelsLabel.text = "NO PANELS REGISTERED";
            RefreshLabels();
        }

        private static Text CreateButton(Transform parent, string name, Rect bounds,
            string label, Font font)
        {
            RoundedRectGraphic panel = RaceHud.CreatePanel(parent, name + " Background",
                bounds, new Color(.12f, .16f, .22f, .98f), 8f);
            Text text = RaceHud.CreateLabel(panel.transform, name + " Label",
                new Rect(0f, 0f, bounds.width, bounds.height), 17, Color.white, font);
            text.text = label ?? string.Empty;
            return text;
        }

        private void RefreshVisibility()
        {
            bool chromeVisible = available && !cleanView;
            canvas.enabled = chromeVisible;
            launcher.SetActive(chromeVisible && !open);
            panelChrome.SetActive(chromeVisible && open);
            noPanelsLabel.gameObject.SetActive(chromeVisible && open && activePanel == null);
            SetActivePanelShown(chromeVisible && open && activePanel != null);
            RefreshLabels();
        }

        private void SetActivePanelShown(bool shown)
        {
            foreach (PanelSlot slot in panels)
                slot.Content.SetActive(shown && slot == activePanel);
            if (activePanelShown == shown) return;
            activePanelShown = shown;
            if (activePanel == null) return;
            if (shown) activePanel.Panel.OnShown();
            else activePanel.Panel.OnHidden();
        }

        private void RefreshLabels()
        {
            if (title != null)
                title.text = activePanel == null ? "VISUAL LAB" : activePanel.Panel.Title;
            if (carsLabel != null)
                carsLabel.text = "CARS  " + (carsVisible ? "ON" : "OFF");
            if (hudLabel != null)
                hudLabel.text = "HUD  " + (hudVisible ? "ON" : "OFF");
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
            if (!pressed && Mouse.current != null &&
                Mouse.current.leftButton.wasPressedThisFrame)
            {
                screen = Mouse.current.position.ReadValue();
                pressed = true;
            }
            referencePoint = new Vector2(
                screen.x * RaceLayout.ReferenceWidth / Mathf.Max(1f, Screen.width),
                (Screen.height - screen.y) * RaceLayout.ReferenceHeight /
                Mathf.Max(1f, Screen.height));
            return pressed;
        }
    }
}
#endif
