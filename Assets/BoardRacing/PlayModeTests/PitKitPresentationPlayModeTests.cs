using System.Collections;
using System.Linq;
using BoardRacing.Domain;
using BoardRacing.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BoardRacing.PlayModeTests
{
    public sealed class PitKitPresentationPlayModeTests
    {
        [UnityTest]
        public IEnumerator FourBayServiceMomentKeepsItsRetainedObjectBudget()
        {
            CourseDefinition course = CourseCatalog.Wedge();
            var data = new SurfaceMeshData();
            data.AddRect(new Rect(0f, 0f, 100f, 100f), Color.black);
            RaceSurfaceRenderer surface = RaceSurfaceRenderer.Create(data);
            surface.AttachPitComplex(PitLanePresentationLayout.ForCourse(course));
            int rendererCount = surface.GetComponentsInChildren<SpriteRenderer>(true).Length;
            int transformCount = surface.GetComponentsInChildren<Transform>(true).Length;
            var racers = new[]
            {
                Racer(PlayerId.Player1, PitPhase.Requested),
                Racer(PlayerId.Player2, PitPhase.InService,
                    PitService.Tires, .55f),
                Racer(PlayerId.Player3, PitPhase.Exiting),
                Racer(PlayerId.Player4, PitPhase.Parked, finished: true)
            };

            for (int frame = 0; frame < 180; frame++)
            {
                surface.SetPitPresentation(racers, frame / 60f, 1f / 60f);
                yield return null;
            }

            Assert.That(surface.PitKitCount, Is.EqualTo(4));
            Assert.That(surface.PitKitRendererCount,
                Is.EqualTo(4 * PitKitVisual.RetainedRenderersPerStall));
            Assert.That(surface.GetComponentsInChildren<SpriteRenderer>(true).Length,
                Is.EqualTo(rendererCount),
                "service animation must not instantiate transient sprites");
            Assert.That(surface.GetComponentsInChildren<Transform>(true).Length,
                Is.EqualTo(transformCount),
                "service animation must retain a fixed transform graph");
            Assert.That(surface.PitKitState(PlayerId.Player1),
                Is.EqualTo(PitPresentationState.Approaching));
            Assert.That(surface.PitKitState(PlayerId.Player2),
                Is.EqualTo(PitPresentationState.Servicing));
            Assert.That(surface.PitKitState(PlayerId.Player3),
                Is.EqualTo(PitPresentationState.Releasing));
            Assert.That(surface.PitKitState(PlayerId.Player4),
                Is.EqualTo(PitPresentationState.Finished));

            Transform finished = surface.PitKitRoot(PlayerId.Player4);
            Assert.That(finished.GetComponentsInChildren<SpriteRenderer>(true)
                    .Where(x => x.name == "Tool Arc" ||
                                x.name == "Four Spark Burst" ||
                                x.name == "Activity Lamp Halo")
                    .All(x => !x.enabled),
                Is.True, "a classified parked car must not look actively serviced");

            Object.Destroy(surface.gameObject);
            yield return null;
        }

        private static RacerSnapshot Racer(PlayerId id, PitPhase phase,
            PitService service = PitService.None, float progress = 0f,
            bool finished = false) =>
            new RacerSnapshot(id, 0f, 0f, 0, (int)id,
                finished, finished ? 10f + (int)id : -1f,
                new TrackSample(new Vec2(0f, 0f), new Vec2(1f, 0f), 0,
                    TrackSectionKind.Straight, Pace.BasePace),
                0f, false, 0f, 0,
                new RacerConditionSnapshot(0f, 0f, false, false),
                new RacerPitSnapshot(service, phase, progress, 0, false));
    }
}
