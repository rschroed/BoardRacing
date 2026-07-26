using System;
using System.Collections.Generic;
using System.Reflection;
using BoardRacing.Domain;
using BoardRacing.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace BoardRacing.Tests
{
    public sealed class VisualLabShellTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            spawned.Clear();
        }

        [Test]
        public void RegisteredPanelUsesGenericLifecycleAndPreservesTemporaryState()
        {
            var stage = new Stage();
            VisualLabShell shell = CreateShell(stage);
            var panel = new TestPanel();

            shell.Register(panel);
            Assert.That(shell.RegisteredPanelCount, Is.EqualTo(1));
            Assert.That(shell.ActivePanelId, Is.EqualTo(panel.Id));
            Assert.That(panel.Shown, Is.EqualTo(0));

            Assert.That(shell.HandleReferencePress(VisualLabShell.LauncherBounds.center), Is.True);
            Assert.That(shell.IsOpen, Is.True);
            Assert.That(panel.Shown, Is.EqualTo(1));
            Assert.That(panel.Content.activeInHierarchy, Is.True);

            panel.TemporaryValue = 42;
            Assert.That(shell.HandleReferencePress(VisualLabShell.CloseBounds.center), Is.True);
            Assert.That(panel.Hidden, Is.EqualTo(1));
            Assert.That(panel.Content.activeInHierarchy, Is.False);

            shell.HandleReferencePress(VisualLabShell.LauncherBounds.center);
            Assert.That(panel.Shown, Is.EqualTo(2));
            Assert.That(panel.TemporaryValue, Is.EqualTo(42));
        }

        [Test]
        public void StageTogglesAndEditorAvailabilityDoNotChangePanelState()
        {
            var stage = new Stage();
            VisualLabShell shell = CreateShell(stage);
            var panel = new TestPanel { TemporaryValue = 17 };
            shell.Register(panel);
            shell.HandleReferencePress(VisualLabShell.LauncherBounds.center);

            shell.HandleReferencePress(VisualLabShell.CarsBounds.center);
            shell.HandleReferencePress(VisualLabShell.HudBounds.center);
            Assert.That(stage.CarsVisible, Is.False);
            Assert.That(stage.HudVisible, Is.False);

            shell.SetAvailable(false);
            Assert.That(shell.GetComponent<Canvas>().enabled, Is.False);
            Assert.That(panel.Hidden, Is.EqualTo(1));
            Assert.That(shell.HandleReferencePress(VisualLabShell.LauncherBounds.center), Is.False);

            shell.SetAvailable(true);
            Assert.That(shell.GetComponent<Canvas>().enabled, Is.True);
            Assert.That(panel.Shown, Is.EqualTo(2));
            Assert.That(panel.TemporaryValue, Is.EqualTo(17));
            Assert.That(stage.CarsVisible, Is.False);
            Assert.That(stage.HudVisible, Is.False);
        }

        [Test]
        public void CollapsedLauncherIsANarrowVisibleEdgeTab()
        {
            Assert.That(VisualLabShell.LauncherBounds.xMax,
                Is.LessThanOrEqualTo(RaceLayout.ReferenceWidth));
            Assert.That(VisualLabShell.LauncherBounds.xMin, Is.GreaterThan(1800f));
            Assert.That(VisualLabShell.LauncherBounds.width, Is.LessThanOrEqualTo(64f));
        }

        [Test]
        public void OpenPanelConsumesBlankChromeAndDelegatesItsContent()
        {
            var stage = new Stage();
            VisualLabShell shell = CreateShell(stage);
            var panel = new TestPanel();
            shell.Register(panel);
            shell.HandleReferencePress(VisualLabShell.LauncherBounds.center);

            Vector2 blankChrome = new Vector2(
                VisualLabShell.PanelBounds.x + 10f,
                VisualLabShell.PanelBounds.y + 10f);
            Assert.That(shell.HandleReferencePress(blankChrome), Is.True,
                "blank lab chrome must not tap through into production UI");
            Assert.That(shell.HandleReferencePress(VisualLabShell.ContentBounds.center), Is.True);
            Assert.That(panel.Presses, Is.EqualTo(1));
            Assert.That(shell.HandleReferencePress(Vector2.zero), Is.False);
        }

        [Test]
        public void RegistrationOrderIsExplicitAndDuplicateIdsAreRejected()
        {
            var stage = new Stage();
            VisualLabShell shell = CreateShell(stage);
            var first = new TestPanel("surface", "SURFACE");
            var second = new TestPanel("cars", "CARS");
            shell.Register(first);
            shell.Register(second);

            Assert.That(shell.ActivePanelId, Is.EqualTo("surface"));
            shell.Select("cars");
            Assert.That(shell.ActivePanelId, Is.EqualTo("cars"));
            Assert.That(() => shell.Register(new TestPanel("cars", "DUPLICATE")),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void CarVisibilityAppliesToCarsAttachedAfterTheToggle()
        {
            var data = new SurfaceMeshData();
            data.AddRect(new Rect(0f, 0f, 10f, 10f), Color.white);
            RaceSurfaceRenderer surface = RaceSurfaceRenderer.Create(data);
            spawned.Add(surface.gameObject);

            surface.SetCarsVisible(false);
            surface.AttachCar(PlayerId.Player1, data);
            Transform car = surface.transform.Find("Race Car Player1");
            Assert.That(car, Is.Not.Null);
            Assert.That(car.gameObject.activeSelf, Is.False);

            surface.SetCarsVisible(true);
            Assert.That(car.gameObject.activeSelf, Is.True);
        }

        [Test]
        public void ReplacingTheStaticSurfacePreservesCarObjectAndPose()
        {
            var first = new SurfaceMeshData();
            first.AddRect(new Rect(0f, 0f, 10f, 10f), Color.white);
            var second = new SurfaceMeshData();
            second.AddRect(new Rect(0f, 0f, 20f, 20f), Color.red);
            RaceSurfaceRenderer surface = RaceSurfaceRenderer.Create(first, Color.black);
            spawned.Add(surface.gameObject);
            surface.AttachCar(PlayerId.Player1, first);
            surface.SetCarPose(PlayerId.Player1, new Vector2(123f, 456f), 37f,
                new Vector2(.8f, 1.2f));
            Transform car = surface.transform.Find("Race Car Player1");
            MeshFilter surfaceFilter =
                surface.transform.Find("Race Surface Mesh").GetComponent<MeshFilter>();
            Mesh originalMesh = surfaceFilter.sharedMesh;

            var ground = new Color(.2f, .3f, .4f, 1f);
            surface.ReplaceSurface(second, ground);

            Assert.That(surface.transform.Find("Race Car Player1"), Is.SameAs(car));
            Assert.That(car.localPosition, Is.EqualTo(new Vector3(123f, 456f, -1f)));
            Assert.That(car.localRotation.eulerAngles.z, Is.EqualTo(37f).Within(.01f));
            Assert.That(car.localScale, Is.EqualTo(new Vector3(.8f, 1.2f, 1f)));
            Assert.That(surfaceFilter.sharedMesh, Is.Not.SameAs(originalMesh));
            Assert.That(surfaceFilter.sharedMesh.vertexCount, Is.EqualTo(second.Vertices.Count));
            Assert.That(surface.GetComponentInChildren<Camera>().backgroundColor,
                Is.EqualTo(ground));
        }

        [Test]
        public void VisualLabUsesUguiAndHasNoImguiEntryPoint()
        {
            BindingFlags methods = BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic;
            Assert.That(typeof(VisualLabShell).GetMethod("OnGUI", methods), Is.Null);
            var stage = new Stage();
            VisualLabShell shell = CreateShell(stage);
            Assert.That(shell.GetComponent<Canvas>(), Is.Not.Null);
        }

        private VisualLabShell CreateShell(Stage stage)
        {
            var owner = new GameObject("Visual Lab Test Owner");
            spawned.Add(owner);
            return VisualLabShell.Create(owner.transform,
                value => stage.CarsVisible = value,
                value => stage.HudVisible = value,
                true);
        }

        private sealed class Stage
        {
            public bool CarsVisible;
            public bool HudVisible;
        }

        private sealed class TestPanel : IVisualLabPanel
        {
            public TestPanel(string id = "test-panel", string title = "TEST PANEL")
            {
                Id = id;
                Title = title;
            }

            public string Id { get; }
            public string Title { get; }
            public GameObject Content { get; private set; }
            public int Shown { get; private set; }
            public int Hidden { get; private set; }
            public int Presses { get; private set; }
            public int TemporaryValue { get; set; }

            public GameObject CreateContent(RectTransform parent)
            {
                Content = new GameObject("Test Panel Content", typeof(RectTransform));
                Content.transform.SetParent(parent, false);
                return Content;
            }

            public bool HandlePress(Vector2 referencePoint)
            {
                Presses++;
                return true;
            }

            public void OnShown() => Shown++;
            public void OnHidden() => Hidden++;
        }
    }
}
