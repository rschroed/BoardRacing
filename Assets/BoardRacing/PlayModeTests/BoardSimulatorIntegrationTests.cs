#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        private readonly List<UnityEngine.Object> contacts = new List<UnityEngine.Object>();

        [SetUp]
        public void SetUp()
        {
            BoardContactSimulation.Enable();
            simulator = BoardContactSimulation.instance;
            simulator.ClearAllContacts();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var contact in contacts)
                if (contact != null) UnityEngine.Object.DestroyImmediate(((Component)contact).gameObject);
            contacts.Clear();
            if (simulator != null) UnityEngine.Object.DestroyImmediate(simulator.gameObject);
        }

        [UnityTest]
        public IEnumerator ArcadeRobotFlowsThroughSdkSimulatorAndProductionProvider()
        {
            using (var provider = new BoardContactInputProvider(8f * Mathf.Deg2Rad, 540f))
            {
                var robot = CreateContact("BoardArcadeRobotOrange", new Vector2(600f, 270f), false);
                yield return null;
                var released = Player(provider.ReadSnapshots(), PlayerId.Player1);
                Assert.That(released.Car.Present, Is.True);
                Assert.That(released.Car.Touched, Is.False);
                Assert.That(released.Throttle, Is.EqualTo(ThrottleStep.Off));

                Call(robot, "Touch");
                Call(robot, "Rotate", 90f);
                yield return null;
                var touched = Player(provider.ReadSnapshots(), PlayerId.Player1);
                Assert.That(touched.Car.Touched, Is.True);
                Assert.That(touched.Throttle, Is.EqualTo(ThrottleStep.Half));

                Call(robot, "Untouch");
                yield return null;
                Assert.That(Player(provider.ReadSnapshots(), PlayerId.Player1).Throttle, Is.EqualTo(ThrottleStep.Off));

                Call(robot, "Lift");
                yield return null;
                Assert.That(Player(provider.ReadSnapshots(), PlayerId.Player1).Car.Present, Is.False);

                var replacement = CreateContact("BoardArcadeRobotOrange", new Vector2(600f, 270f), true);
                yield return null;
                var reacquired = Player(provider.ReadSnapshots(), PlayerId.Player1);
                Assert.That(reacquired.Car.Present, Is.True);
                Assert.That(reacquired.Car.ContactId, Is.Not.EqualTo(released.Car.ContactId));
                Assert.That(reacquired.Throttle, Is.EqualTo(ThrottleStep.Off));
                Assert.That(reacquired.Car.RequiresRelease, Is.True);
                Call(replacement, "Untouch");
                yield return null;
                Assert.That(Player(provider.ReadSnapshots(), PlayerId.Player1).Car.RequiresRelease, Is.False);
                Call(replacement, "Touch");
                yield return null;
                Assert.That(Player(provider.ReadSnapshots(), PlayerId.Player1).Throttle, Is.Not.EqualTo(ThrottleStep.Off));
                Call(replacement, "Cancel");
                yield return null;
                Assert.That(Player(provider.ReadSnapshots(), PlayerId.Player1).Car.Present, Is.False);
                Assert.That(Player(provider.ReadSnapshots(), PlayerId.Player1).Throttle, Is.EqualTo(ThrottleStep.Off));
            }
        }

        [UnityTest]
        public IEnumerator TwoPlayersCompleteTenSimultaneousSdkSimulatorCycles()
        {
            using (var provider = new BoardContactInputProvider(8f * Mathf.Deg2Rad, 540f))
            {
                var p1Car = CreateContact("BoardArcadeRobotOrange", new Vector2(600f, 270f), false);
                var p1Crew = CreateContact("BoardArcadeShipOrange", new Vector2(1325f, 270f), false);
                var p2Car = CreateContact("BoardArcadeRobotPurple", new Vector2(600f, 810f), false);
                var p2Crew = CreateContact("BoardArcadeShipPurple", new Vector2(595f, 810f), false);
                yield return null;

                var labObject = new GameObject("Tranche 1 Control Lab Simulator Test");
                var lab = labObject.AddComponent<ControlLab>();
                lab.SetInputProvider(provider);
                yield return new WaitForSecondsRealtime(0.05f);

                for (int cycle = 1; cycle <= 10; cycle++)
                {
                    Call(p1Car, "Touch"); Call(p2Car, "Touch");
                    Call(p1Crew, "Touch"); Call(p2Crew, "Touch");
                    yield return new WaitForSecondsRealtime(1.6f);

                    Assert.That(lab.GetPlayerSnapshot(PlayerId.Player1).Throttle, Is.Not.EqualTo(ThrottleStep.Off));
                    Assert.That(lab.GetPlayerSnapshot(PlayerId.Player2).Throttle, Is.Not.EqualTo(ThrottleStep.Off));
                    Assert.That(lab.GetCompletionCount(PlayerId.Player1), Is.EqualTo(cycle));
                    Assert.That(lab.GetCompletionCount(PlayerId.Player2), Is.EqualTo(cycle));

                    Call(p1Car, "Untouch"); Call(p2Car, "Untouch");
                    Call(p1Crew, "Untouch"); Call(p2Crew, "Untouch");
                    Call(p1Crew, "MoveTo", new Vector2(1000f, 270f));
                    Call(p2Crew, "MoveTo", new Vector2(1000f, 810f));
                    yield return new WaitForSecondsRealtime(0.05f);
                    Assert.That(lab.GetPlayerSnapshot(PlayerId.Player1).Throttle, Is.EqualTo(ThrottleStep.Off));
                    Assert.That(lab.GetPlayerSnapshot(PlayerId.Player2).Throttle, Is.EqualTo(ThrottleStep.Off));

                    Call(p1Crew, "MoveTo", new Vector2(1325f, 270f));
                    Call(p2Crew, "MoveTo", new Vector2(595f, 810f));
                    Call(p1Car, "Rotate", 90f);
                    Call(p2Car, "Rotate", 90f);
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
        public IEnumerator CrewStrategyAdapterMapsTwoSimulatorPiecesAndFailsSafeOnLoss()
        {
            using (var provider = new BoardContactInputProvider(8f * Mathf.Deg2Rad, 540f))
            {
                var p1Crew = CreateContact("BoardArcadeShipOrange", new Vector2(1135f, 270f), false);
                var p2Crew = CreateContact("BoardArcadeShipPurple", new Vector2(405f, 810f), false);
                yield return null;
                var p1Adapter = new CrewStrategyAdapter(new Vec2(1135f, 270f), new Vec2(1515f, 270f),
                    new Vec2(140f, 120f), 0f, 15f * Mathf.Deg2Rad, 1.5f);
                var p2Adapter = new CrewStrategyAdapter(new Vec2(785f, 810f), new Vec2(405f, 810f),
                    new Vec2(140f, 120f), 0f, 15f * Mathf.Deg2Rad, 1.5f);
                var onTrack = new RacerPitSnapshot(PitService.None, PitPhase.OnTrack, 0f, 0, false);

                var released = provider.ReadSnapshots();
                Assert.That(p1Adapter.Update(Player(released, PlayerId.Player1), RacePhase.Racing, onTrack, .1f)
                    .SelectedService, Is.EqualTo(PitService.Tires));
                Assert.That(p2Adapter.Update(Player(released, PlayerId.Player2), RacePhase.Racing, onTrack, .1f)
                    .SelectedService, Is.EqualTo(PitService.Cooling));

                Call(p1Crew, "Touch"); Call(p2Crew, "Touch");
                yield return null;
                var touched = provider.ReadSnapshots();
                p1Adapter.Update(Player(touched, PlayerId.Player1), RacePhase.Racing, onTrack, .1f);
                p2Adapter.Update(Player(touched, PlayerId.Player2), RacePhase.Racing, onTrack, .1f);
                Call(p1Crew, "Untouch"); Call(p2Crew, "Untouch");
                yield return null;
                released = provider.ReadSnapshots();
                Assert.That(p1Adapter.Update(Player(released, PlayerId.Player1), RacePhase.Racing, onTrack, .1f)
                    .RequestPit, Is.True);
                Assert.That(p2Adapter.Update(Player(released, PlayerId.Player2), RacePhase.Racing, onTrack, .1f)
                    .RequestPit, Is.True);

                var p1Service = new RacerPitSnapshot(PitService.Tires, PitPhase.InService, 0f, 0, false);
                var p2Service = new RacerPitSnapshot(PitService.Cooling, PitPhase.InService, 0f, 0, false);
                Call(p1Crew, "Touch"); Call(p2Crew, "Touch");
                yield return null;
                touched = provider.ReadSnapshots();
                Assert.That(p1Adapter.Update(Player(touched, PlayerId.Player1), RacePhase.Racing, p1Service, 1.6f)
                    .ServiceAction.CompletedThisUpdate, Is.True);
                Assert.That(p2Adapter.Update(Player(touched, PlayerId.Player2), RacePhase.Racing, p2Service, .5f)
                    .ServiceAction.State, Is.EqualTo(PitActionState.Holding));

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
            using (var provider = new BoardContactInputProvider(8f * Mathf.Deg2Rad, 540f))
            {
                var p1Car = CreateContact("BoardArcadeRobotOrange", new Vector2(600f, 810f), false);
                var p2Car = CreateContact("BoardArcadeRobotPurple", new Vector2(600f, 270f), false);
                CreateContact("BoardArcadeRobotYellow", new Vector2(900f, 500f), false);
                yield return null;

                var crossed = provider.ReadSnapshots();
                Assert.That(Player(crossed, PlayerId.Player1).Car.ContactId, Is.Not.EqualTo(Player(crossed, PlayerId.Player2).Car.ContactId));
                Assert.That(crossed.All(x => x.Warnings.HasFlag(InputWarning.WrongRegion)), Is.True);
                Assert.That(crossed.All(x => x.Warnings.HasFlag(InputWarning.UnassignedGlyph)), Is.True);

                CreateContact("BoardArcadeRobotOrange", new Vector2(650f, 270f), false);
                yield return null;
                var duplicate = Player(provider.ReadSnapshots(), PlayerId.Player1);
                Assert.That(duplicate.Car.Present, Is.False);
                Assert.That(duplicate.Throttle, Is.EqualTo(ThrottleStep.Off));
                Assert.That(duplicate.Warnings.HasFlag(InputWarning.DuplicateGlyph), Is.True);

                Call(p1Car, "Lift");
                Call(p2Car, "Lift");
                yield return null;
            }
        }

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
