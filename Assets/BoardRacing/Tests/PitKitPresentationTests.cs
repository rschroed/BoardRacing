using System.Collections.Generic;
using System.Linq;
using BoardRacing.Domain;
using BoardRacing.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace BoardRacing.Tests
{
    public sealed class PitKitPresentationTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject gameObject in spawned)
                if (gameObject != null)
                    Object.DestroyImmediate(gameObject);
            spawned.Clear();
        }

        [Test]
        public void ApprovedKitUsesAuthoredBenchLayersAndDistinctMarkers()
        {
            Assert.That(PitKitVisual.LoadServiceBench(), Is.Not.Null);
            Assert.That(PitKitVisual.LoadServiceTongue(), Is.Not.Null);
            string[] markerPaths = PhysicalPieceCatalog.All
                .Select(PitKitVisual.MarkerResourcePath).ToArray();
            Assert.That(markerPaths.Distinct().Count(), Is.EqualTo(4));
            foreach (PieceIdentity identity in PhysicalPieceCatalog.All)
                Assert.That(PitKitVisual.LoadMarker(identity), Is.Not.Null,
                    identity.VisualIdentity);
        }

        [Test]
        public void EveryCourseOrientsEachModularBayAwayFromItsSharedLane()
        {
            foreach (CourseDefinition course in CourseCatalog.All())
            {
                PitLanePresentationLayout layout =
                    PitLanePresentationLayout.ForCourse(course);
                for (int i = 0; i < layout.Stalls.Count; i++)
                {
                    PlayerId playerId = (PlayerId)(i + 1);
                    PitKitPlacement placement =
                        PitKitPresentation.Placement(layout, playerId);
                    Vec2 lane = layout.LaneAnchor(playerId);
                    var away = placement.Center - new Vector2(lane.X, lane.Y);

                    Assert.That(placement.Heading.magnitude,
                        Is.EqualTo(1f).Within(.001f), course.Name);
                    Assert.That(placement.Outward.magnitude,
                        Is.EqualTo(1f).Within(.001f), course.Name);
                    Assert.That(Vector2.Dot(placement.Heading, placement.Outward),
                        Is.EqualTo(0f).Within(.001f), course.Name);
                    Assert.That(Mathf.Abs(placement.OutwardSign),
                        Is.EqualTo(1f).Within(.001f), course.Name);
                    Assert.That(Vector2.Dot(placement.Outward, away),
                        Is.GreaterThanOrEqualTo(0f),
                        course.Name + " " + playerId + " decor faces the lane");
                }
            }
        }

        [Test]
        public void SemanticPitStatesDrivePresentationWithoutChangingSimulation()
        {
            Assert.That(PitKitPresentation.Resolve(false, default, false),
                Is.EqualTo(PitPresentationState.Inactive));
            Assert.That(PitKitPresentation.Resolve(true,
                    Racer(PitPhase.OnTrack), false),
                Is.EqualTo(PitPresentationState.Active));
            Assert.That(PitKitPresentation.Resolve(true,
                    Racer(PitPhase.Requested), false),
                Is.EqualTo(PitPresentationState.Approaching));
            Assert.That(PitKitPresentation.Resolve(true,
                    Racer(PitPhase.InService), false),
                Is.EqualTo(PitPresentationState.Occupied));
            Assert.That(PitKitPresentation.Resolve(true,
                    Racer(PitPhase.InService, PitService.Tires, .5f), false),
                Is.EqualTo(PitPresentationState.Servicing));
            Assert.That(PitKitPresentation.Resolve(true,
                    Racer(PitPhase.InService, PitService.Tires, 1f), true),
                Is.EqualTo(PitPresentationState.Ready));
            Assert.That(PitKitPresentation.Resolve(true,
                    Racer(PitPhase.Exiting), false),
                Is.EqualTo(PitPresentationState.Releasing));
            Assert.That(PitKitPresentation.Resolve(true,
                    Racer(PitPhase.Parked, finished: true), false),
                Is.EqualTo(PitPresentationState.Finished));
        }

        [Test]
        public void RendererBuildsOneFixedRetainedKitPerAuthoredStall()
        {
            CourseDefinition course = CourseCatalog.Wedge();
            var data = new SurfaceMeshData();
            data.AddRect(new Rect(0f, 0f, 100f, 100f), Color.black);
            RaceSurfaceRenderer surface = RaceSurfaceRenderer.Create(data);
            spawned.Add(surface.gameObject);
            PitLanePresentationLayout layout =
                PitLanePresentationLayout.ForCourse(course);

            surface.AttachPitComplex(layout);
            surface.SetPitPresentation(null, 0f, 0f);

            Assert.That(surface.PitKitCount, Is.EqualTo(layout.Stalls.Count));
            Assert.That(surface.PitKitRendererCount, Is.EqualTo(
                layout.Stalls.Count * PitKitVisual.RetainedRenderersPerStall));
            for (int i = 0; i < layout.Stalls.Count; i++)
            {
                PlayerId id = (PlayerId)(i + 1);
                Transform root = surface.PitKitRoot(id);
                PitKitPlacement placement = PitKitPresentation.Placement(layout, id);
                Assert.That(root, Is.Not.Null);
                Assert.That(root.localPosition.x,
                    Is.EqualTo(placement.Center.x).Within(.001f));
                Assert.That(root.localPosition.y,
                    Is.EqualTo(placement.Center.y).Within(.001f));
                Assert.That(root.Find("Static Surface"), Is.Null,
                    "the course mesh owns the pit floor; a retained bay decal would " +
                        "reintroduce a discrete rectangular background");
                Transform bench = root.Find("Full Length Service Bench");
                Transform connector = root.Find("Service Connector");
                Transform wheelStop = root.Find("Wheel Stop");
                Assert.That(bench, Is.Not.Null);
                Assert.That(connector, Is.Not.Null);
                Assert.That(wheelStop, Is.Not.Null);
                Assert.That(bench.GetComponent<SpriteRenderer>().sprite.bounds.size.x,
                    Is.GreaterThanOrEqualTo(54f),
                    "the approved bench must be at least as substantial as the car");
                Assert.That(Vector2.Dot(
                        new Vector2(bench.localPosition.x, bench.localPosition.y),
                        Vector2.up * placement.OutwardSign),
                    Is.GreaterThan(0f),
                    "the bench must sit on the side away from the shared lane");
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(
                        wheelStop.localEulerAngles.z, 90f)),
                    Is.LessThan(.01f),
                    "the stop bar must be transverse to the parked car");
                Assert.That(wheelStop.GetComponent<SpriteRenderer>()
                        .sprite.bounds.size.x * wheelStop.localScale.x,
                    Is.GreaterThanOrEqualTo(32f),
                    "the transverse stop must remain visible beyond both car sides");
                Assert.That(root.GetComponentsInChildren<Transform>(true)
                        .Any(x => x.name.Contains("Arm")),
                    Is.False,
                    "the approved slot-car bench replaces the robotic arm treatment");
                Assert.That(surface.PitKitState(id),
                    Is.EqualTo(PitPresentationState.Inactive));
            }
        }

        [Test]
        public void CompletedServiceGetsBriefPunctuationThenSettlesToOccupied()
        {
            RaceSurfaceRenderer surface = CreateWedgeSurface();
            var servicing = new[] {
                Racer(PitPhase.InService, PitService.Fuel, .6f, completed: 0)
            };
            surface.SetPitPresentation(servicing, 1f, 1f / 60f);
            Assert.That(surface.PitKitState(PlayerId.Player1),
                Is.EqualTo(PitPresentationState.Servicing));

            var completed = new[] {
                Racer(PitPhase.InService, PitService.Fuel, 1f, completed: 1)
            };
            surface.SetPitPresentation(completed, 1.1f, 1f / 60f);
            Assert.That(surface.PitKitState(PlayerId.Player1),
                Is.EqualTo(PitPresentationState.Ready));
            surface.SetPitPresentation(completed, 2f,
                PitKitPresentation.ReadyPunctuationSeconds + .01f);
            surface.SetPitPresentation(completed, 2.1f, 0f);
            Assert.That(surface.PitKitState(PlayerId.Player1),
                Is.EqualTo(PitPresentationState.Occupied));
        }

        private RaceSurfaceRenderer CreateWedgeSurface()
        {
            var data = new SurfaceMeshData();
            data.AddRect(new Rect(0f, 0f, 100f, 100f), Color.black);
            RaceSurfaceRenderer surface = RaceSurfaceRenderer.Create(data);
            spawned.Add(surface.gameObject);
            surface.AttachPitComplex(
                PitLanePresentationLayout.ForCourse(CourseCatalog.Wedge()));
            return surface;
        }

        private static RacerSnapshot Racer(PitPhase phase,
            PitService service = PitService.None, float progress = 0f,
            int completed = 0, bool finished = false) =>
            new RacerSnapshot(PlayerId.Player1, 0f, 0f, 0, 1,
                finished, finished ? 10f : -1f,
                new TrackSample(new Vec2(0f, 0f), new Vec2(1f, 0f), 0,
                    TrackSectionKind.Straight, Pace.BasePace),
                0f, false, 0f, 0,
                new RacerConditionSnapshot(0f, 0f, false, false),
                new RacerPitSnapshot(service, phase, progress,
                    completed, completed > 0));
    }
}
