using System;
using System.Collections.Generic;
using BoardRacing.Domain;
using BoardRacing.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace BoardRacing.Tests
{
    public sealed class CarResponsePresentationTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject gameObject in spawned)
                if (gameObject != null) UnityEngine.Object.DestroyImmediate(gameObject);
            spawned.Clear();
        }

        [Test]
        public void ThrottleStepsOwnDistinctBoundedChannels()
        {
            CarResponseState drive = Targets(ThrottleStep.Drive);
            CarResponseState brake = Targets(ThrottleStep.Brake, brakeDive: .5f);
            CarResponseState boost = Targets(ThrottleStep.Boost);

            Assert.That(drive.Drive, Is.EqualTo(CarResponsePresentation.DriveIntensity));
            Assert.That(drive.Brake, Is.Zero);
            Assert.That(drive.Boost, Is.Zero);

            Assert.That(brake.Drive, Is.Zero);
            Assert.That(brake.Brake, Is.EqualTo(.725f).Within(.001f));
            Assert.That(brake.Boost, Is.Zero);

            Assert.That(boost.Drive, Is.Zero);
            Assert.That(boost.Brake, Is.Zero);
            Assert.That(boost.Boost, Is.EqualTo(1f));
        }

        [Test]
        public void FastCornerUsesTheEstablishedDriftTruthAndMayOverlapThrottle()
        {
            CarResponseState state = Targets(ThrottleStep.Boost,
                driftDegrees: CornerCharacter.MaxDriftDegrees * .5f);
            Assert.That(state.Boost, Is.EqualTo(1f));
            Assert.That(state.Corner, Is.EqualTo(.5f).Within(.001f));

            CarResponseState capped = Targets(ThrottleStep.Drive,
                driftDegrees: CornerCharacter.MaxDriftDegrees * -3f);
            Assert.That(capped.Corner, Is.EqualTo(1f));
        }

        [Test]
        public void NonRacingStoppedPitAndFinishedCarsStayQuietAndMissingClearsThrottle()
        {
            AssertStill(CarResponsePresentation.Targets(RacePhase.Countdown, true,
                false, true, ThrottleStep.Boost, Pace.BasePace, 1f, 8f));
            CarResponseState missing = CarResponsePresentation.Targets(RacePhase.Racing, true,
                false, false, ThrottleStep.Boost, Pace.BasePace, 1f, 8f);
            Assert.That(missing.Drive, Is.Zero);
            Assert.That(missing.Brake, Is.Zero);
            Assert.That(missing.Boost, Is.Zero);
            Assert.That(missing.Corner, Is.EqualTo(1f),
                "actual scrub remains visible even after the input safely releases");
            AssertStill(CarResponsePresentation.Targets(RacePhase.Racing, true,
                false, true, ThrottleStep.Boost, 0f, 1f, 0f));
            AssertStill(CarResponsePresentation.Targets(RacePhase.Racing, false,
                false, true, ThrottleStep.Boost, Pace.BasePace, 1f, 8f));
            AssertStill(CarResponsePresentation.Targets(RacePhase.Racing, true,
                true, true, ThrottleStep.Boost, Pace.BasePace, 1f, 8f));
        }

        [Test]
        public void ResponseAttacksAndReleasesWithoutOvershoot()
        {
            CarResponseState state = CarResponseState.Still;
            CarResponseState target = new CarResponseState(.55f, .8f, 1f, .7f);
            for (int i = 0; i < 20; i++)
                state = CarResponsePresentation.Step(state, target, 1f / 60f);
            Assert.That(state.Drive, Is.EqualTo(target.Drive));
            Assert.That(state.Brake, Is.EqualTo(target.Brake));
            Assert.That(state.Boost, Is.EqualTo(target.Boost));
            Assert.That(state.Corner, Is.EqualTo(target.Corner));

            for (int i = 0; i < 20; i++)
                state = CarResponsePresentation.Step(state, CarResponseState.Still, 1f / 60f);
            AssertStill(state);
        }

        [Test]
        public void PulseIsDeterministicBoundedAndPlayerPhased()
        {
            float one = CarResponsePresentation.Pulse(12.345f, PlayerId.Player1);
            float again = CarResponsePresentation.Pulse(12.345f, PlayerId.Player1);
            float two = CarResponsePresentation.Pulse(12.345f, PlayerId.Player2);
            Assert.That(again, Is.EqualTo(one));
            Assert.That(one, Is.InRange(0f, 1f));
            Assert.That(two, Is.InRange(0f, 1f));
            Assert.That(two, Is.Not.EqualTo(one).Within(.001f));
        }

        [Test]
        public void MappingAndDynamicsAllocateNoRecurringManagedMemory()
        {
            CarResponseState state = CarResponseState.Still;
            for (int i = 0; i < 100; i++)
                state = CarResponsePresentation.Step(state,
                    Targets(ThrottleStep.Boost, driftDegrees: 4f), 1f / 60f);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++)
            {
                CarResponseState target = CarResponsePresentation.Targets(
                    RacePhase.Racing, true, false, true,
                    (ThrottleStep)((i % 3) * 50), Pace.BasePace,
                    (i % 10) / 10f, (i % 16) - 8f);
                state = CarResponsePresentation.Step(state, target, 1f / 60f);
                CarResponsePresentation.Pulse(i / 60f, PlayerId.Player4);
            }
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.That(after - before, Is.Zero);
        }

        [Test]
        public void RendererRetainsOneRigAndNeverMovesTheRaceRoot()
        {
            var data = new SurfaceMeshData();
            data.AddRect(new Rect(0f, 0f, 100f, 100f), Color.black);
            RaceSurfaceRenderer surface = RaceSurfaceRenderer.Create(data);
            spawned.Add(surface.gameObject);
            surface.AttachCar(PlayerId.Player1, PhysicalPieceCatalog.All[0]);
            surface.SetCarPose(PlayerId.Player1, new Vector2(400f, 300f), 37f,
                new Vector2(.9f, 1.05f));

            Transform car = surface.transform.Find("Race Car Player1");
            int childCount = car.GetComponentsInChildren<Transform>(true).Length;
            surface.SetCarResponse(PlayerId.Player1,
                new CarResponseState(.55f, .8f, 1f, .75f), .25f);
            for (int i = 0; i < 100; i++)
                surface.SetCarResponse(PlayerId.Player1,
                    new CarResponseState(i % 2, (i + 1) % 2, 1f, .5f), i / 100f);

            Assert.That(car.GetComponentsInChildren<Transform>(true).Length,
                Is.EqualTo(childCount), "response updates must never instantiate effects");
            Assert.That(car.localPosition, Is.EqualTo(new Vector3(400f, 300f, -1f)));
            Assert.That(car.localRotation.eulerAngles.z, Is.EqualTo(37f).Within(.01f));
            Assert.That(car.localScale, Is.EqualTo(new Vector3(.9f, 1.05f, 1f)));
            Assert.That(car.Find("Body Response").localScale.x, Is.GreaterThan(1f));
            Assert.That(car.Find("Boost Flare").GetComponent<SpriteRenderer>().enabled, Is.True);
            Assert.That(car.Find("Corner Contact Left").GetComponent<SpriteRenderer>().enabled,
                Is.True);
        }

        private static CarResponseState Targets(ThrottleStep throttle,
            float brakeDive = 0f, float driftDegrees = 0f) =>
            CarResponsePresentation.Targets(RacePhase.Racing, true, false, true,
                throttle, Pace.BasePace, brakeDive, driftDegrees);

        private static void AssertStill(CarResponseState state)
        {
            Assert.That(state.Drive, Is.Zero);
            Assert.That(state.Brake, Is.Zero);
            Assert.That(state.Boost, Is.Zero);
            Assert.That(state.Corner, Is.Zero);
        }
    }
}
