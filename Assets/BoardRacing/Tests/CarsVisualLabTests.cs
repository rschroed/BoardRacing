#if UNITY_EDITOR || (DEVELOPMENT_BUILD && UNITY_ANDROID)
using System.Collections.Generic;
using BoardRacing.Domain;
using BoardRacing.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace BoardRacing.Tests
{
    public sealed class CarsVisualLabTests
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
        public void PanelBuildsStudyPresetAndCombinedResponseWithoutTouchingLiveByDefault()
        {
            var applied = new List<CarStudyPresentation>();
            var panel = new CarsVisualLabPanel(applied.Add);
            CreateContent(panel);

            Assert.That(panel.CurrentPresentation.Enabled, Is.False);
            Assert.That(applied, Is.Empty);

            panel.HandlePress(CarsVisualLabPanel.ReferenceActionBounds(0).center);
            panel.HandlePress(CarsVisualLabPanel.ReferenceActionBounds(1).center);
            panel.HandlePress(CarsVisualLabPanel.ReferenceActionBounds(2).center);
            panel.HandlePress(CarsVisualLabPanel.ReferenceActionBounds(2).center);
            panel.HandlePress(CarsVisualLabPanel.ReferenceActionBounds(2).center);

            CarStudyPresentation boost = panel.CurrentPresentation;
            Assert.That(boost.Enabled, Is.True);
            Assert.That(boost.Target, Is.EqualTo(CarStudyTarget.Player1));
            Assert.That(boost.Response.Boost, Is.EqualTo(1f));
            Assert.That(boost.Response.Drive, Is.Zero);
            Assert.That(panel.PresetIndex, Is.EqualTo(3));

            panel.HandlePress(CarsVisualLabPanel.ReferencePlusBounds(6).center);
            CarStudyPresentation combined = panel.CurrentPresentation;
            Assert.That(combined.Response.Boost, Is.EqualTo(1f));
            Assert.That(combined.Response.Corner, Is.EqualTo(.25f));
            Assert.That(panel.PresetIndex, Is.EqualTo(5));
            Assert.That(applied.Count, Is.EqualTo(6));
            Assert.That(panel.CurrentLogRecord(), Is.EqualTo(
                "[CarsVisualLab] mode=Study target=P1 preset=Custom " +
                "drive=0.00 brake=0.00 boost=1.00 corner=0.25"));
        }

        [Test]
        public void ResetReturnsToLiveAllNeutralAndPreservesNoPreviewCommand()
        {
            CarStudyPresentation applied = default;
            var panel = new CarsVisualLabPanel(value => applied = value);
            CreateContent(panel);
            panel.HandlePress(CarsVisualLabPanel.ReferenceActionBounds(0).center);
            panel.HandlePress(CarsVisualLabPanel.ReferenceActionBounds(1).center);
            panel.HandlePress(CarsVisualLabPanel.ReferencePlusBounds(3).center);
            panel.HandlePress(CarsVisualLabPanel.ReferenceResetBounds.center);

            Assert.That(applied.Enabled, Is.False);
            Assert.That(applied.Target, Is.EqualTo(CarStudyTarget.All));
            Assert.That(applied.Response.Drive, Is.Zero);
            Assert.That(applied.Response.Brake, Is.Zero);
            Assert.That(applied.Response.Boost, Is.Zero);
            Assert.That(applied.Response.Corner, Is.Zero);
            Assert.That(panel.PresetIndex, Is.Zero);
        }

        [Test]
        public void ProductionRendererUsesFourRetainedStudyCarsAndRestoresLivePose()
        {
            RaceSurfaceRenderer surface = CreateSurface();
            surface.AttachCar(PlayerId.Player1, PhysicalPieceCatalog.All[0]);
            surface.SetCarPose(PlayerId.Player1, new Vector2(123f, 456f), 37f,
                new Vector2(.9f, 1.1f));
            Transform live = surface.transform.Find("Race Car Player1");

            var boostAll = new CarStudyPresentation(true, CarStudyTarget.All,
                new CarResponseState(0f, 0f, 1f, 0f));
            surface.SetCarStudy(boostAll);

            Assert.That(surface.StudyCarCount, Is.EqualTo(4));
            Assert.That(live.gameObject.activeSelf, Is.False);
            foreach (PlayerId id in Players)
            {
                Transform study = surface.StudyCar(id);
                Assert.That(study, Is.Not.Null);
                Assert.That(study.gameObject.activeSelf, Is.True);
                Assert.That(study.Find("Body Response/Direction E Body"), Is.Not.Null);
                Assert.That(study.Find("Boost Core").GetComponent<SpriteRenderer>().enabled,
                    Is.True);
            }

            int childCount = surface.transform.childCount;
            for (int i = 0; i < 100; i++)
                surface.SetCarStudy(new CarStudyPresentation(true,
                    CarStudyTarget.Player2,
                    new CarResponseState(.25f, .5f, .75f, 1f)));
            Assert.That(surface.transform.childCount, Is.EqualTo(childCount));
            Assert.That(surface.StudyCarCount, Is.EqualTo(4));
            Assert.That(surface.StudyCar(Player1).Find("Boost Core")
                .GetComponent<SpriteRenderer>().enabled, Is.False);
            Assert.That(surface.StudyCar(PlayerId.Player2).Find("Boost Core")
                .GetComponent<SpriteRenderer>().enabled, Is.True);

            surface.SetCarStudy(CarStudyPresentation.Live);
            Assert.That(live.gameObject.activeSelf, Is.True);
            Assert.That(live.localPosition, Is.EqualTo(new Vector3(123f, 456f, -1f)));
            Assert.That(live.localRotation.eulerAngles.z, Is.EqualTo(37f).Within(.01f));
            Assert.That(live.localScale, Is.EqualTo(new Vector3(.9f, 1.1f, 1f)));
            foreach (PlayerId id in Players)
                Assert.That(surface.StudyCar(id).gameObject.activeSelf, Is.False);
        }

        [Test]
        public void StudyPoseUsesTheProductionBrakeAndCornerCeilings()
        {
            RaceSurfaceRenderer surface = CreateSurface();
            surface.SetCarStudy(new CarStudyPresentation(true, CarStudyTarget.Player3,
                new CarResponseState(0f, 1f, 0f, 1f)));

            Transform selected = surface.StudyCar(PlayerId.Player3);
            Assert.That(selected.localScale.x,
                Is.EqualTo(1f - CornerCharacter.DiveSquash).Within(.001f));
            Assert.That(selected.localScale.y,
                Is.EqualTo(1f + CornerCharacter.DiveSquash * .5f).Within(.001f));
            Assert.That(selected.localRotation.eulerAngles.z,
                Is.EqualTo(CornerCharacter.MaxDriftDegrees).Within(.001f));
            Assert.That(surface.StudyCar(PlayerId.Player1).localScale, Is.EqualTo(Vector3.one));
        }

        private void CreateContent(CarsVisualLabPanel panel)
        {
            var root = new GameObject("Cars Panel Test", typeof(RectTransform));
            spawned.Add(root);
            panel.CreateContent(root.GetComponent<RectTransform>());
        }

        private RaceSurfaceRenderer CreateSurface()
        {
            var data = new SurfaceMeshData();
            data.AddRect(new Rect(0f, 0f, 10f, 10f), Color.white);
            RaceSurfaceRenderer surface = RaceSurfaceRenderer.Create(data);
            spawned.Add(surface.gameObject);
            return surface;
        }

        private static readonly PlayerId Player1 = PlayerId.Player1;
        private static readonly PlayerId[] Players =
        {
            PlayerId.Player1,
            PlayerId.Player2,
            PlayerId.Player3,
            PlayerId.Player4
        };
    }
}
#endif
