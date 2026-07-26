using System.Collections.Generic;
using BoardRacing.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace BoardRacing.Tests
{
    public sealed class CourseSurfaceComposerTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned)
                if (go != null) Object.DestroyImmediate(go);
            spawned.Clear();
        }

        [Test]
        public void SemanticControlsApplyTemporaryStyleOnlyWhenAValueChanges()
        {
            var applied = new List<RaceSurfaceStyle>();
            var wireframeStates = new List<bool>();
            CourseSurfaceComposerPanel panel = CreatePanel(
                () => "Wedge", () => true, () => { }, applied.Add, wireframeStates.Add);
            panel.HandlePress(CourseSurfaceComposerPanel.ReferencePlusBounds(1).center);
            panel.HandlePress(CourseSurfaceComposerPanel.ReferencePlusBounds(2).center);
            panel.HandlePress(CourseSurfaceComposerPanel.ReferencePlusBounds(3).center);
            panel.HandlePress(CourseSurfaceComposerPanel.ReferenceActionBounds(4).center);
            panel.HandlePress(CourseSurfaceComposerPanel.ReferenceActionBounds(5).center);
            panel.HandlePress(CourseSurfaceComposerPanel.ReferenceActionBounds(6).center);
            panel.HandlePress(CourseSurfaceComposerPanel.ReferenceActionBounds(7).center);

            Assert.That(applied.Count, Is.EqualTo(6));
            RaceSurfaceStyle style = applied[applied.Count - 1];
            Assert.That(style.ShoulderOpacity, Is.EqualTo(.1f).Within(.001f));
            Assert.That(style.ShoulderSolidWidth, Is.EqualTo(16f));
            Assert.That(style.ShoulderFeatherWidth, Is.EqualTo(28f));
            Assert.That(style.StripeVisible, Is.False);
            Assert.That(style.PitSurfaceVisible, Is.False);
            Assert.That(style.DebugView, Is.EqualTo(RaceSurfaceDebugView.ShoulderOnly));
            Assert.That(wireframeStates, Is.EqualTo(new[] { true }));
            Assert.That(panel.WireframeVisible, Is.True);
        }

        [Test]
        public void EveryControlRowRendersItsSemanticLabel()
        {
            CreatePanel(() => "Wedge", () => true, () => { }, _ => { });
            Text[] labels = spawned[0].GetComponentsInChildren<Text>();
            foreach (string expected in new[]
            {
                "COURSE", "SHOULDER OPACITY", "SOLID WIDTH", "FEATHER", "STRIPES",
                "PIT SURFACE", "VIEW", "MESH WIREFRAME"
            })
                Assert.That(System.Array.Exists(labels, label => label.text == expected),
                    Is.True, expected);
            foreach (string removed in new[] { "COLOR", "RED", "GREEN", "BLUE" })
                Assert.That(System.Array.Exists(labels, label => label.text == removed),
                    Is.False, removed);
        }

        [Test]
        public void CourseSelectionCyclesInSetupAndLocksDuringARace()
        {
            int cycles = 0;
            bool setup = false;
            CourseSurfaceComposerPanel panel = CreatePanel(
                () => "Infinity", () => setup, () => cycles++, _ => { });

            panel.HandlePress(CourseSurfaceComposerPanel.ReferenceActionBounds(0).center);
            Assert.That(cycles, Is.Zero);
            setup = true;
            panel.HandlePress(CourseSurfaceComposerPanel.ReferenceActionBounds(0).center);
            Assert.That(cycles, Is.EqualTo(1));
        }

        [Test]
        public void ResetIsDeterministicAndLogContainsEveryExposedSemanticValue()
        {
            RaceSurfaceStyle last = default;
            var wireframeStates = new List<bool>();
            CourseSurfaceComposerPanel panel = CreatePanel(
                () => "Fishhook", () => true, () => { }, style => last = style,
                wireframeStates.Add);
            panel.HandlePress(CourseSurfaceComposerPanel.ReferencePlusBounds(1).center);
            panel.HandlePress(CourseSurfaceComposerPanel.ReferencePlusBounds(2).center);
            panel.HandlePress(CourseSurfaceComposerPanel.ReferenceActionBounds(7).center);
            panel.HandlePress(CourseSurfaceComposerPanel.ReferenceResetBounds.center);

            Assert.That(last.GroundColor, Is.EqualTo(RaceSurfaceStyle.Default.GroundColor));
            Assert.That(last.ShoulderOpacity, Is.EqualTo(0f));
            Assert.That(last.ShoulderSolidWidth, Is.EqualTo(12f));
            Assert.That(last.ShoulderFeatherWidth, Is.EqualTo(24f));
            Assert.That(panel.WireframeVisible, Is.False);
            Assert.That(wireframeStates, Is.EqualTo(new[] { true, false }));
            string record = panel.CurrentLogRecord();
            StringAssert.Contains("[CourseSurfaceComposer] course=Fishhook", record);
            StringAssert.Contains("ground=#", record);
            StringAssert.Contains("straight=#", record);
            StringAssert.Contains("corner=#", record);
            StringAssert.Contains("shoulder=#", record);
            StringAssert.Contains("shoulderOpacity=", record);
            StringAssert.Contains("shoulderSolidWidth=", record);
            StringAssert.Contains("shoulderFeatherWidth=", record);
            StringAssert.Contains("stripes=", record);
            StringAssert.Contains("pitSurface=", record);
            StringAssert.Contains("view=", record);
            StringAssert.Contains("wireframe=False", record);
        }

        private CourseSurfaceComposerPanel CreatePanel(
            System.Func<string> courseName,
            System.Func<bool> canSelectCourse,
            System.Action selectNextCourse,
            System.Action<RaceSurfaceStyle> apply,
            System.Action<bool> setWireframeVisible = null)
        {
            var holder = new GameObject("Course Surface Test", typeof(RectTransform));
            spawned.Add(holder);
            var panel = new CourseSurfaceComposerPanel(
                courseName, canSelectCourse, selectNextCourse, RaceSurfaceStyle.Default, apply,
                setWireframeVisible ?? (_ => { }));
            panel.CreateContent(holder.GetComponent<RectTransform>());
            return panel;
        }
    }
}
