#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Board.Input;
using Board.Input.Simulation;
using BoardRacing.Domain;
using BoardRacing.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace BoardRacing.PlayModeTests
{
    public sealed class BoardSimulatorIntegrationTests
    {
        private BoardContactSimulation simulator;
        private BoardInputSettings originalSettings;
        private BoardInputSettings temporarySettings;
        private readonly List<UnityEngine.Object> contacts = new List<UnityEngine.Object>();
        // Hardware-measured stops from the #77 hardware review (wireframe-ui.md).
        private static readonly ThrottleStops MeasuredStops = new ThrottleStops(
            275f * Mathf.Deg2Rad, 225f * Mathf.Deg2Rad, 175f * Mathf.Deg2Rad);
        // Round 2 seat-cluster targets from wireframe-ui.md (frame 40:23, component 44:124);
        // Player 2 is the exact 180° mirror of Player 1.
        private static readonly Vector2 P1CallPit = new Vector2(1832f, 398f);
        private static readonly Vector2 P1Tires = new Vector2(1692f, 321f);
        private static readonly Vector2 P1Fuel = new Vector2(1590f, 212f);
        private static readonly Vector2 P2CallPit = new Vector2(88f, 682f);
        private static readonly Vector2 P2Tires = new Vector2(228f, 759f);
        private static readonly Vector2 P2Fuel = new Vector2(330f, 868f);
        // The reconciler removes Player 2's 180° seat rotation before mapping throttle,
        // so Player 2 reads Drive at raw 45° where Player 1 reads it at raw 225°.
        private const float P1DriveDegrees = 225f;
        private const float P2DriveDegrees = 45f;

        [SetUp]
        public void SetUp()
        {
            BoardContactSimulation.Enable();
            simulator = BoardContactSimulation.instance;
            simulator.ClearAllContacts();
            originalSettings = BoardInput.settings;
        }

        [TearDown]
        public void TearDown()
        {
            if (originalSettings != null && BoardInput.settings != originalSettings)
                BoardInput.settings = originalSettings;
            if (temporarySettings != null) UnityEngine.Object.DestroyImmediate(temporarySettings);
            foreach (var contact in contacts)
                if (contact != null) UnityEngine.Object.DestroyImmediate(((Component)contact).gameObject);
            contacts.Clear();
            if (simulator != null) UnityEngine.Object.DestroyImmediate(simulator.gameObject);
        }

        [UnityTest]
        public IEnumerator DrivingShipFlowsThroughSdkSimulatorAndIgnoresTouchState()
        {
            using (var provider = new BoardContactInputProvider(MeasuredStops, 8f * Mathf.Deg2Rad, 540f))
            {
                var robot = CreateContact("BoardArcadeShipOrange", new Vector2(600f, 270f), false);
                yield return null;
                var released = Player(provider.ReadSnapshots(), PlayerId.Player1);
                Assert.That(released.Car.Present, Is.True);
                Assert.That(released.Car.Touched, Is.False);
                ThrottleStep initialThrottle = released.Throttle;

                Call(robot, "Touch");
                Call(robot, "Rotate", P1DriveDegrees);
                yield return null;
                var touched = Player(provider.ReadSnapshots(), PlayerId.Player1);
                Assert.That(touched.Car.Touched, Is.True);
                Assert.That(touched.Throttle, Is.EqualTo(ThrottleStep.Drive));

                Call(robot, "Untouch");
                yield return null;
                Assert.That(Player(provider.ReadSnapshots(), PlayerId.Player1).Throttle, Is.EqualTo(ThrottleStep.Drive));

                Call(robot, "Lift");
                yield return null;
                Assert.That(Player(provider.ReadSnapshots(), PlayerId.Player1).Car.Present, Is.False);

                var replacement = CreateContact("BoardArcadeShipOrange", new Vector2(600f, 270f), true);
                yield return null;
                var reacquired = Player(provider.ReadSnapshots(), PlayerId.Player1);
                Assert.That(reacquired.Car.Present, Is.True);
                Assert.That(reacquired.Car.ContactId, Is.Not.EqualTo(released.Car.ContactId));
                Assert.That(reacquired.Throttle, Is.EqualTo(initialThrottle));
                Assert.That(reacquired.Car.RequiresRelease, Is.True);
                Call(replacement, "Untouch");
                yield return null;
                Assert.That(Player(provider.ReadSnapshots(), PlayerId.Player1).Car.RequiresRelease, Is.False);
                Call(replacement, "Touch");
                yield return null;
                Assert.That(Player(provider.ReadSnapshots(), PlayerId.Player1).Throttle, Is.EqualTo(initialThrottle));
                Call(replacement, "Cancel");
                yield return null;
                Assert.That(Player(provider.ReadSnapshots(), PlayerId.Player1).Car.Present, Is.False);
                Assert.That(Player(provider.ReadSnapshots(), PlayerId.Player1).Throttle, Is.EqualTo(ThrottleStep.Brake));
            }
        }

        [UnityTest]
        public IEnumerator TwoPlayersCompleteTenSimultaneousSdkSimulatorCycles()
        {
            using (var provider = new BoardContactInputProvider(MeasuredStops, 8f * Mathf.Deg2Rad, 540f))
            {
                var p1Car = CreateContact("BoardArcadeShipOrange", new Vector2(600f, 270f), false);
                var p1Crew = CreateContact("BoardArcadeRobotOrange", P1CallPit, false);
                var p2Car = CreateContact("BoardArcadeShipPurple", new Vector2(600f, 810f), false);
                var p2Crew = CreateContact("BoardArcadeRobotPurple", P2CallPit, false);
                yield return null;

                var labObject = new GameObject("Tranche 1 Control Lab Simulator Test");
                var lab = labObject.AddComponent<ControlLab>();
                lab.SetInputProvider(provider);
                Call(p1Car, "Rotate", P1DriveDegrees); Call(p2Car, "Rotate", P2DriveDegrees);
                yield return new WaitForSecondsRealtime(0.05f);

                for (int cycle = 1; cycle <= 10; cycle++)
                {
                    Call(p1Car, "Touch"); Call(p2Car, "Touch");
                    Call(p1Crew, "Touch"); Call(p2Crew, "Touch");
                    yield return new WaitForSecondsRealtime(1.6f);

                    Assert.That(lab.GetPlayerSnapshot(PlayerId.Player1).Throttle, Is.Not.EqualTo(ThrottleStep.Brake));
                    Assert.That(lab.GetPlayerSnapshot(PlayerId.Player2).Throttle, Is.Not.EqualTo(ThrottleStep.Brake));
                    Assert.That(lab.GetCompletionCount(PlayerId.Player1), Is.EqualTo(cycle));
                    Assert.That(lab.GetCompletionCount(PlayerId.Player2), Is.EqualTo(cycle));

                    Call(p1Car, "Untouch"); Call(p2Car, "Untouch");
                    Call(p1Crew, "Untouch"); Call(p2Crew, "Untouch");
                    Call(p1Crew, "MoveTo", new Vector2(1000f, 270f));
                    Call(p2Crew, "MoveTo", new Vector2(1000f, 810f));
                    yield return new WaitForSecondsRealtime(0.05f);
                    Assert.That(lab.GetPlayerSnapshot(PlayerId.Player1).Throttle, Is.Not.EqualTo(ThrottleStep.Brake));
                    Assert.That(lab.GetPlayerSnapshot(PlayerId.Player2).Throttle, Is.Not.EqualTo(ThrottleStep.Brake));

                    Call(p1Crew, "MoveTo", P1CallPit);
                    Call(p2Crew, "MoveTo", P2CallPit);
                    yield return new WaitForSecondsRealtime(0.05f);
                }

                var p1 = lab.GetPlayerSnapshot(PlayerId.Player1);
                var p2 = lab.GetPlayerSnapshot(PlayerId.Player2);
                Assert.That(p1.Warnings, Is.EqualTo(InputWarning.None));
                Assert.That(p2.Warnings, Is.EqualTo(InputWarning.None));
                Assert.That(p1.Car.ContactId, Is.Not.EqualTo(p2.Car.ContactId));
                Assert.That(p1.Crew.ContactId, Is.Not.EqualTo(p2.Crew.ContactId));
                UnityEngine.Object.Destroy(labObject);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator CrewCallPitSupportsPhysicalLiftPlaceAfterSafeRelease()
        {
            using (var provider = new BoardContactInputProvider(MeasuredStops, 8f * Mathf.Deg2Rad, 540f))
            {
                var crew = CreateContact("BoardArcadeRobotOrange", new Vector2(1000f, 270f), true);
                yield return null;
                var adapter = AdapterFor(PlayerId.Player1);
                var onTrack = new RacerPitSnapshot(PitService.None, PitPhase.OnTrack, 0f, 0, false);

                var acquired = Player(provider.ReadSnapshots(), PlayerId.Player1);
                Assert.That(acquired.Crew.RequiresRelease, Is.True);
                var gated = adapter.Update(acquired, RacePhase.Racing, onTrack, .1f);
                Assert.That(gated.CallState, Is.EqualTo(PitCallState.NeedsPlacement));
                Assert.That(gated.RequestPit, Is.False);

                Call(crew, "Untouch");
                yield return null;
                var safelyReleased = Player(provider.ReadSnapshots(), PlayerId.Player1);
                Assert.That(safelyReleased.Crew.RequiresRelease, Is.False);
                Assert.That(adapter.Update(safelyReleased, RacePhase.Racing, onTrack, .1f).RequestPit, Is.False);

                Call(crew, "MoveTo", P1CallPit);
                Call(crew, "Touch");
                yield return null;
                var touched = adapter.Update(Player(provider.ReadSnapshots(), PlayerId.Player1),
                    RacePhase.Racing, onTrack, .4f);
                Assert.That(touched.CallState, Is.EqualTo(PitCallState.Holding));
                Call(crew, "Untouch");
                yield return null;
                var requested = adapter.Update(Player(provider.ReadSnapshots(), PlayerId.Player1),
                    RacePhase.Racing, onTrack, .4f);
                Assert.That(requested.RequestPit, Is.True);
                Assert.That(adapter.Update(Player(provider.ReadSnapshots(), PlayerId.Player1),
                    RacePhase.Racing, onTrack, .1f).RequestPit, Is.False);
            }
        }

        [UnityTest]
        public IEnumerator CrewStrategyAdapterMapsTwoSimulatorPiecesAndFailsSafeOnLoss()
        {
            using (var provider = new BoardContactInputProvider(MeasuredStops, 8f * Mathf.Deg2Rad, 540f))
            {
                var p1Crew = CreateContact("BoardArcadeRobotOrange", new Vector2(1000f, 270f), false);
                var p2Crew = CreateContact("BoardArcadeRobotPurple", new Vector2(1000f, 810f), false);
                yield return null;
                var p1Adapter = AdapterFor(PlayerId.Player1);
                var p2Adapter = AdapterFor(PlayerId.Player2);
                var onTrack = new RacerPitSnapshot(PitService.None, PitPhase.OnTrack, 0f, 0, false);

                var released = provider.ReadSnapshots();
                Assert.That(p1Adapter.Update(Player(released, PlayerId.Player1), RacePhase.Racing, onTrack, .1f)
                    .CallState, Is.EqualTo(PitCallState.NeedsPlacement));
                Assert.That(p2Adapter.Update(Player(released, PlayerId.Player2), RacePhase.Racing, onTrack, .1f)
                    .CallState, Is.EqualTo(PitCallState.NeedsPlacement));

                Call(p1Crew, "MoveTo", P1CallPit);
                Call(p2Crew, "MoveTo", P2CallPit);
                Call(p1Crew, "Touch"); Call(p2Crew, "Touch");
                yield return null;
                var touched = provider.ReadSnapshots();
                Assert.That(p1Adapter.Update(Player(touched, PlayerId.Player1), RacePhase.Racing, onTrack, .4f)
                    .CallState, Is.EqualTo(PitCallState.Holding));
                Assert.That(p2Adapter.Update(Player(touched, PlayerId.Player2), RacePhase.Racing, onTrack, .4f)
                    .CallState, Is.EqualTo(PitCallState.Holding));
                Call(p1Crew, "Untouch"); Call(p2Crew, "Untouch");
                yield return null;
                released = provider.ReadSnapshots();
                var p1Request = p1Adapter.Update(Player(released, PlayerId.Player1), RacePhase.Racing, onTrack, .4f);
                var p2Request = p2Adapter.Update(Player(released, PlayerId.Player2), RacePhase.Racing, onTrack, .4f);
                Assert.That(p1Request.RequestPit, Is.True);
                Assert.That(p2Request.RequestPit, Is.True);
                Assert.That(p1Request.SelectedService, Is.EqualTo(PitService.None));
                Assert.That(p2Request.SelectedService, Is.EqualTo(PitService.None));

                var p1Service = new RacerPitSnapshot(PitService.None, PitPhase.InService, 0f, 0, false);
                var p2Service = new RacerPitSnapshot(PitService.None, PitPhase.InService, 0f, 0, false);
                Call(p1Crew, "MoveTo", P1Tires);
                Call(p2Crew, "MoveTo", P2Fuel);
                Call(p1Crew, "Touch"); Call(p2Crew, "Touch");
                yield return null;
                touched = provider.ReadSnapshots();
                var p1Action = p1Adapter.Update(Player(touched, PlayerId.Player1), RacePhase.Racing, p1Service, 1.6f);
                var p2Action = p2Adapter.Update(Player(touched, PlayerId.Player2), RacePhase.Racing, p2Service, .5f);
                Assert.That(p1Action.SelectedService, Is.EqualTo(PitService.Tires));
                Assert.That(p1Action.ServiceAction.State, Is.EqualTo(PitActionState.Stirring));
                Assert.That(p2Action.SelectedService, Is.EqualTo(PitService.Fuel));
                Assert.That(p2Action.ServiceAction.State, Is.EqualTo(PitActionState.Stirring));

                // Circular Robot motion around the dial drains the meter.
                Call(p1Crew, "MoveTo", P1Tires + new Vector2(20f, 0f));
                yield return null;
                p1Adapter.Update(Player(provider.ReadSnapshots(), PlayerId.Player1),
                    RacePhase.Racing, p1Service, .1f);
                Call(p1Crew, "MoveTo", P1Tires + new Vector2(0f, 20f));
                yield return null;
                var stirred = p1Adapter.Update(Player(provider.ReadSnapshots(), PlayerId.Player1),
                    RacePhase.Racing, p1Service, .1f);
                Assert.That(stirred.ServiceDrain, Is.GreaterThan(0f));

                Call(p2Crew, "Cancel");
                yield return null;
                var lost = p2Adapter.Update(Player(provider.ReadSnapshots(), PlayerId.Player2),
                    RacePhase.Racing, p2Service, 1.5f);
                Assert.That(lost.ServiceAction.CompletedThisUpdate, Is.False);
                Assert.That(lost.ServiceAction.Progress, Is.Zero);
            }
        }

        [UnityTest]
        public IEnumerator SimulatorCrossingUnassignedAndDuplicatePiecesFailSafe()
        {
            using (var provider = new BoardContactInputProvider(MeasuredStops, 8f * Mathf.Deg2Rad, 540f))
            {
                var p1Car = CreateContact("BoardArcadeShipOrange", new Vector2(600f, 810f), false);
                var p2Car = CreateContact("BoardArcadeShipPurple", new Vector2(600f, 270f), false);
                CreateContact("BoardArcadeRobotYellow", new Vector2(900f, 500f), false);
                yield return null;

                var crossed = provider.ReadSnapshots();
                Assert.That(Player(crossed, PlayerId.Player1).Car.ContactId, Is.Not.EqualTo(Player(crossed, PlayerId.Player2).Car.ContactId));
                Assert.That(crossed.All(x => x.Warnings.HasFlag(InputWarning.WrongRegion)), Is.True);
                Assert.That(crossed.All(x => x.Warnings.HasFlag(InputWarning.UnassignedGlyph)), Is.True);

                CreateContact("BoardArcadeShipOrange", new Vector2(650f, 270f), false);
                yield return null;
                var duplicate = Player(provider.ReadSnapshots(), PlayerId.Player1);
                Assert.That(duplicate.Car.Present, Is.False);
                Assert.That(duplicate.Throttle, Is.EqualTo(ThrottleStep.Brake));
                Assert.That(duplicate.Warnings.HasFlag(InputWarning.DuplicateGlyph), Is.True);

                Call(p1Car, "Lift");
                Call(p2Car, "Lift");
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator SimulatorStrategyRecoveryMatrixResetsAndRearmsWithoutCrossPlayerCommands()
        {
            using (var provider = new BoardContactInputProvider(MeasuredStops, 8f * Mathf.Deg2Rad, 540f))
            {
                var p1Car = CreateContact("BoardArcadeShipOrange", new Vector2(600f, 270f), false);
                var p2Car = CreateContact("BoardArcadeShipPurple", new Vector2(600f, 810f), false);
                var p1Crew = CreateContact("BoardArcadeRobotOrange", new Vector2(1000f, 270f), false);
                var p2Crew = CreateContact("BoardArcadeRobotPurple", new Vector2(1000f, 810f), false);
                yield return null;
                provider.ReadSnapshots();

                var p1Adapter = AdapterFor(PlayerId.Player1);
                var p2Adapter = AdapterFor(PlayerId.Player2);
                var onTrack = new RacerPitSnapshot(PitService.None, PitPhase.OnTrack, 0f, 0, false);
                var released = provider.ReadSnapshots();
                Assert.That(p1Adapter.Update(Player(released, PlayerId.Player1), RacePhase.Racing, onTrack, .1f)
                    .CallState, Is.EqualTo(PitCallState.NeedsPlacement));
                Assert.That(p2Adapter.Update(Player(released, PlayerId.Player2), RacePhase.Racing, onTrack, .1f)
                    .CallState, Is.EqualTo(PitCallState.NeedsPlacement));

                Call(p1Car, "Touch"); Call(p2Car, "Touch");
                Call(p1Car, "Rotate", P1DriveDegrees); Call(p2Car, "Rotate", P2DriveDegrees);
                Call(p1Crew, "MoveTo", P1CallPit);
                Call(p2Crew, "MoveTo", P2CallPit);
                Call(p1Crew, "Touch"); Call(p2Crew, "Touch");
                yield return null;
                var active = provider.ReadSnapshots();
                Assert.That(active.All(x => x.Throttle != ThrottleStep.Brake), Is.True);
                Assert.That(p1Adapter.Update(Player(active, PlayerId.Player1), RacePhase.Racing, onTrack, .4f)
                    .CallState, Is.EqualTo(PitCallState.Holding));
                Assert.That(p2Adapter.Update(Player(active, PlayerId.Player2), RacePhase.Racing, onTrack, .4f)
                    .CallState, Is.EqualTo(PitCallState.Holding));
                Call(p1Crew, "Untouch"); Call(p2Crew, "Untouch");
                yield return null;
                released = provider.ReadSnapshots();
                Assert.That(p1Adapter.Update(Player(released, PlayerId.Player1), RacePhase.Racing, onTrack, .4f)
                    .RequestPit, Is.True);
                Assert.That(p2Adapter.Update(Player(released, PlayerId.Player2), RacePhase.Racing, onTrack, .4f)
                    .RequestPit, Is.True);

                var p1Service = new RacerPitSnapshot(PitService.None, PitPhase.InService, 0f, 0, false);
                var p2Service = new RacerPitSnapshot(PitService.None, PitPhase.InService, 0f, 0, false);
                Call(p1Crew, "MoveTo", P1Tires);
                Call(p2Crew, "MoveTo", P2Fuel);
                Call(p1Crew, "Touch"); Call(p2Crew, "Touch");
                yield return null;
                active = provider.ReadSnapshots();
                Assert.That(p1Adapter.Update(Player(active, PlayerId.Player1), RacePhase.Racing, p1Service, .5f)
                    .ServiceAction.State, Is.EqualTo(PitActionState.Stirring));
                Assert.That(p2Adapter.Update(Player(active, PlayerId.Player2), RacePhase.Racing, p2Service, .5f)
                    .ServiceAction.State, Is.EqualTo(PitActionState.Stirring));

                Call(p1Crew, "MoveTo", new Vector2(P1Tires.x, 810f));
                yield return null;
                var wrongRegion = provider.ReadSnapshots();
                Assert.That(Player(wrongRegion, PlayerId.Player1).Warnings.HasFlag(InputWarning.WrongRegion), Is.True);
                Assert.That(Player(wrongRegion, PlayerId.Player1).Throttle, Is.Not.EqualTo(ThrottleStep.Brake));
                var p1WrongRegion = p1Adapter.Update(Player(wrongRegion, PlayerId.Player1),
                    RacePhase.Racing, p1Service, .5f);
                Assert.That(p1WrongRegion.SelectedService, Is.EqualTo(PitService.None));
                Assert.That(p1WrongRegion.ServiceDrain, Is.Zero);
                Assert.That(p2Adapter.Update(Player(wrongRegion, PlayerId.Player2), RacePhase.Racing, p2Service, .5f)
                    .SelectedService, Is.EqualTo(PitService.Fuel));

                Call(p1Crew, "MoveTo", P1Tires);
                Call(p2Crew, "Cancel");
                yield return null;
                var lost = provider.ReadSnapshots();
                Assert.That(Player(lost, PlayerId.Player2).Crew.Present, Is.False);
                Assert.That(Player(lost, PlayerId.Player2).Throttle, Is.Not.EqualTo(ThrottleStep.Brake));
                var p2Lost = p2Adapter.Update(Player(lost, PlayerId.Player2), RacePhase.Racing, p2Service, .5f);
                Assert.That(p2Lost.SelectedService, Is.EqualTo(PitService.None));
                Assert.That(p2Lost.ServiceDrain, Is.Zero);

                var replacement = CreateContact("BoardArcadeRobotPurple", P2Fuel, true);
                yield return null;
                var reacquired = provider.ReadSnapshots();
                Assert.That(Player(reacquired, PlayerId.Player2).Crew.RequiresRelease, Is.True);
                var replaced = p2Adapter.Update(Player(reacquired, PlayerId.Player2), RacePhase.Racing,
                    p2Service, 1.6f);
                Assert.That(replaced.ServiceAction.State, Is.EqualTo(PitActionState.Stirring));
                Assert.That(replaced.ServiceDrain, Is.Zero);
                Call(replacement, "Untouch");
                yield return null;
                var rearmed = provider.ReadSnapshots();
                Assert.That(Player(rearmed, PlayerId.Player2).Crew.RequiresRelease, Is.False);
                Assert.That(p2Adapter.Update(Player(rearmed, PlayerId.Player2), RacePhase.Racing, p2Service, .1f)
                    .ServiceAction.State, Is.EqualTo(PitActionState.Stirring));

                Call(replacement, "Touch");
                Call(p1Crew, "MoveTo", P1Tires + new Vector2(20f, 0f));
                Call(replacement, "MoveTo", P2Fuel + new Vector2(20f, 0f));
                yield return null;
                active = provider.ReadSnapshots();
                p1Adapter.Update(Player(active, PlayerId.Player1), RacePhase.Racing, p1Service, .1f);
                p2Adapter.Update(Player(active, PlayerId.Player2), RacePhase.Racing, p2Service, .1f);
                Call(p1Crew, "MoveTo", P1Tires + new Vector2(0f, 20f));
                Call(replacement, "MoveTo", P2Fuel + new Vector2(0f, 20f));
                yield return null;
                active = provider.ReadSnapshots();
                Assert.That(p1Adapter.Update(Player(active, PlayerId.Player1), RacePhase.Racing, p1Service, .4f)
                    .ServiceDrain, Is.GreaterThan(0f));
                Assert.That(p2Adapter.Update(Player(active, PlayerId.Player2), RacePhase.Racing, p2Service, .4f)
                    .ServiceDrain, Is.GreaterThan(0f));

                temporarySettings = ScriptableObject.CreateInstance<BoardInputSettings>();
                BoardInput.settings = temporarySettings;
                var reset = provider.ReadSnapshots();
                p1Adapter.Reset(); p2Adapter.Reset();
                Assert.That(reset.All(x => x.Car.Present), Is.True);
                Assert.That(reset.All(x => x.Car.RequiresRelease && x.Crew.RequiresRelease), Is.True);
                Assert.That(p1Adapter.Update(Player(reset, PlayerId.Player1), RacePhase.Racing, p1Service, .5f)
                    .ServiceDrain, Is.Zero);
                Assert.That(p2Adapter.Update(Player(reset, PlayerId.Player2), RacePhase.Racing, p2Service, .5f)
                    .ServiceDrain, Is.Zero);

                Call(p1Car, "Untouch"); Call(p2Car, "Untouch");
                Call(p1Crew, "Untouch"); Call(replacement, "Untouch");
                yield return null;
                var safelyReleased = provider.ReadSnapshots();
                Assert.That(safelyReleased.All(x => !x.Car.RequiresRelease && !x.Crew.RequiresRelease), Is.True);
                Call(p1Crew, "MoveTo", new Vector2(900f, 270f));
                Call(replacement, "MoveTo", new Vector2(1000f, 810f));
                yield return null;
                var repositioning = provider.ReadSnapshots();
                p1Adapter.Update(Player(repositioning, PlayerId.Player1), RacePhase.Racing, p1Service, .1f);
                p2Adapter.Update(Player(repositioning, PlayerId.Player2), RacePhase.Racing, p2Service, .1f);
                Call(p1Crew, "MoveTo", P1Tires);
                Call(replacement, "MoveTo", P2Fuel);
                Call(p1Car, "Touch"); Call(p2Car, "Touch");
                Call(p1Crew, "Touch"); Call(replacement, "Touch");
                yield return null;
                active = provider.ReadSnapshots();
                Assert.That(active.All(x => x.Throttle != ThrottleStep.Brake), Is.True);
                p1Adapter.Update(Player(active, PlayerId.Player1), RacePhase.Racing, p1Service, .1f);
                p2Adapter.Update(Player(active, PlayerId.Player2), RacePhase.Racing, p2Service, .1f);
                Call(p1Crew, "MoveTo", P1Tires + new Vector2(20f, 0f));
                Call(replacement, "MoveTo", P2Fuel + new Vector2(20f, 0f));
                yield return null;
                active = provider.ReadSnapshots();
                p1Adapter.Update(Player(active, PlayerId.Player1), RacePhase.Racing, p1Service, .1f);
                p2Adapter.Update(Player(active, PlayerId.Player2), RacePhase.Racing, p2Service, .1f);
                Call(p1Crew, "MoveTo", P1Tires + new Vector2(0f, 20f));
                Call(replacement, "MoveTo", P2Fuel + new Vector2(0f, 20f));
                yield return null;
                active = provider.ReadSnapshots();
                Assert.That(p1Adapter.Update(Player(active, PlayerId.Player1), RacePhase.Racing, p1Service, 1.6f)
                    .ServiceDrain, Is.GreaterThan(0f));
                Assert.That(p2Adapter.Update(Player(active, PlayerId.Player2), RacePhase.Racing, p2Service, 1.6f)
                    .ServiceDrain, Is.GreaterThan(0f));
                // A motionless Robot drains nothing further — completion is the
                // simulation's meter reaching empty, not an adapter event.
                Assert.That(p1Adapter.Update(Player(active, PlayerId.Player1), RacePhase.Racing, p1Service, 1.6f)
                    .ServiceDrain, Is.Zero);
                Assert.That(p2Adapter.Update(Player(active, PlayerId.Player2), RacePhase.Racing, p2Service, 1.6f)
                    .ServiceDrain, Is.Zero);
            }
        }

        private static CrewStrategyAdapter AdapterFor(PlayerId id) => id == PlayerId.Player1
            ? new CrewStrategyAdapter(new Vec2(P1CallPit.x, P1CallPit.y), new Vec2(P1Tires.x, P1Tires.y),
                new Vec2(P1Fuel.x, P1Fuel.y), new Vec2(50f, 50f), 5f)
            : new CrewStrategyAdapter(new Vec2(P2CallPit.x, P2CallPit.y), new Vec2(P2Tires.x, P2Tires.y),
                new Vec2(P2Fuel.x, P2Fuel.y), new Vec2(50f, 50f), 5f);

        private UnityEngine.Object CreateContact(string iconName, Vector2 position, bool touched)
        {
            var icon = LoadIcon(iconName);
            var method = typeof(BoardContactSimulation).GetMethod("CreateOrGetUnplacedContact",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var contact = (UnityEngine.Object)method.Invoke(simulator, null);
            contacts.Add(contact);
            contact.GetType().GetProperty("icon").SetValue(contact, icon);
            Call(contact, "MoveTo", position);
            Call(contact, "Place", touched);
            typeof(BoardContactSimulation).GetField("m_CurrentSimulatedContact",
                BindingFlags.Instance | BindingFlags.NonPublic).SetValue(simulator, null);
            return contact;
        }

        private static BoardContactSimulationIcon LoadIcon(string name)
        {
            string guid = AssetDatabase.FindAssets(name + " t:BoardContactSimulationIcon").First();
            return AssetDatabase.LoadAssetAtPath<BoardContactSimulationIcon>(AssetDatabase.GUIDToAssetPath(guid));
        }

        private static object Call(UnityEngine.Object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public).Invoke(target, args);

        private static PlayerControlSnapshot Player(IReadOnlyList<PlayerControlSnapshot> snapshots, PlayerId id) =>
            snapshots.Single(x => x.PlayerId == id);
    }
}
#endif
