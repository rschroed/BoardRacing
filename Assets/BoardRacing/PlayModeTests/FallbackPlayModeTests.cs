using System.Collections;
using System.Linq;
using BoardRacing.Domain;
using BoardRacing.Runtime;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.TestTools;
using UnityEngine;

namespace BoardRacing.PlayModeTests
{
    public sealed class FallbackPlayModeTests : InputTestFixture
    {
        [UnityTest]
        public IEnumerator FallbackProviderSupportsTwoPlayersInPlayMode()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var provider = new KeyboardInputProvider();

            Tap(provider, keyboard.qKey);
            Tap(provider, keyboard.uKey);
            Tap(provider, keyboard.cKey);
            var snapshots = Tap(provider, keyboard.digit8Key);

            Assert.That(snapshots.Single(x => x.PlayerId == PlayerId.Player1).Throttle,
                Is.Not.EqualTo(ThrottleStep.Off));
            Assert.That(snapshots.Single(x => x.PlayerId == PlayerId.Player2).Throttle,
                Is.Not.EqualTo(ThrottleStep.Off));
            yield return null;
        }

        [UnityTest]
        public IEnumerator RacePrototypeStartsWithoutControlLabInEditorPlayMode()
        {
            yield return null;
            Assert.That(UnityEngine.Object.FindObjectOfType<RacePrototype>(), Is.Not.Null);
            Assert.That(UnityEngine.Object.FindObjectOfType<ControlLab>(), Is.Null);
        }

        [UnityTest]
        public IEnumerator RacePrototypeMovesBothPlayersThroughSharedProviderPath()
        {
            yield return null;
            var race = Object.FindObjectOfType<RacePrototype>();
            var provider = new RaceScriptedProvider(false);
            race.SetInputProvider(provider);
            yield return new WaitForSecondsRealtime(3.2f);
            provider.Touched = true;
            yield return new WaitForSecondsRealtime(.5f);
            var snapshot = race.GetRaceSnapshot();
            Assert.That(snapshot.Phase, Is.EqualTo(RacePhase.Racing));
            Assert.That(snapshot.Racers.All(x => x.TotalDistance > 0f), Is.True);
        }

        [UnityTest]
        public IEnumerator RacePrototypeMapsTwoCrewTouchReleaseRequestsThroughSharedRuntime()
        {
            yield return null;
            var race = Object.FindObjectOfType<RacePrototype>();
            var provider = new CrewRaceScriptedProvider();
            race.SetInputProvider(provider);
            yield return new WaitForSecondsRealtime(3.2f);
            provider.CrewTouched = true;
            yield return new WaitForSecondsRealtime(.05f);
            provider.CrewTouched = false;
            yield return new WaitForSecondsRealtime(.05f);

            var snapshot = race.GetRaceSnapshot();
            Assert.That(snapshot.Phase, Is.EqualTo(RacePhase.Racing));
            Assert.That(snapshot.Racers.Single(x => x.PlayerId == PlayerId.Player1).Pit.SelectedService,
                Is.EqualTo(PitService.Tires));
            Assert.That(snapshot.Racers.Single(x => x.PlayerId == PlayerId.Player1).Pit.Phase,
                Is.EqualTo(PitPhase.Requested));
            Assert.That(snapshot.Racers.Single(x => x.PlayerId == PlayerId.Player2).Pit.SelectedService,
                Is.EqualTo(PitService.Cooling));
            Assert.That(snapshot.Racers.Single(x => x.PlayerId == PlayerId.Player2).Pit.Phase,
                Is.EqualTo(PitPhase.Requested));
        }

        [UnityTest]
        public IEnumerator KeyboardFallbackMovesBothCrewPiecesIntoServiceRegions()
        {
            yield return null;
            var race = Object.FindObjectOfType<RacePrototype>();
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var provider = new KeyboardInputProvider();
            race.SetInputProvider(provider);

            Press(keyboard.fKey);
            yield return new WaitForSecondsRealtime(.7f);
            Release(keyboard.fKey); yield return null;
            Press(keyboard.hKey);
            yield return new WaitForSecondsRealtime(.7f);
            Release(keyboard.hKey); yield return null;

            var positioned = provider.ReadSnapshots();
            Assert.That(positioned.Single(x => x.PlayerId == PlayerId.Player1).Crew.Position.X,
                Is.EqualTo(1135f).Within(20f));
            Assert.That(positioned.Single(x => x.PlayerId == PlayerId.Player2).Crew.Position.X,
                Is.EqualTo(405f).Within(20f));
            race.SetInputProvider(new RaceScriptedProvider(false));
        }

        [UnityTest]
        [Timeout(15000)]
        public IEnumerator KeyboardFallbackCompletesStrategyRaceAndRematchThroughRuntime()
        {
            yield return null;
            var race = Object.FindObjectOfType<RacePrototype>();
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var provider = new KeyboardInputProvider();
            race.SetInputProvider(provider);
            var update = typeof(RacePrototype).GetMethod("Update",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(update, Is.Not.Null);

            Press(keyboard.fKey);
            yield return new WaitForSecondsRealtime(.7f);
            Release(keyboard.fKey, queueEventOnly: true);
            InputSystem.Update();
            yield return null;
            Press(keyboard.hKey);
            yield return new WaitForSecondsRealtime(.7f);
            Release(keyboard.hKey, queueEventOnly: true);
            InputSystem.Update();
            yield return null;
            Assert.That(PumpUntil(race, update, 5f, x => x.Phase == RacePhase.Racing), Is.True);
            PumpRace(race, update, .05f);
            var positioned = provider.ReadSnapshots();
            Assert.That(positioned.Single(x => x.PlayerId == PlayerId.Player1).Crew.Position.X,
                Is.InRange(995f, 1275f));
            Assert.That(positioned.Single(x => x.PlayerId == PlayerId.Player2).Crew.Position.X,
                Is.InRange(265f, 545f));
            Assert.That(race.GetCrewStrategy(PlayerId.Player1).SelectedService, Is.EqualTo(PitService.Tires));
            Assert.That(race.GetCrewStrategy(PlayerId.Player2).SelectedService, Is.EqualTo(PitService.Cooling));

            TapRaceKeys(race, update, keyboard.wKey);
            TapRaceKeys(race, update, keyboard.wKey);
            TapRaceKeys(race, update, keyboard.iKey);
            TapRaceKeys(race, update, keyboard.iKey);
            Assert.That(race.GetRaceSnapshot().Racers.All(x => x.Pit.Phase == PitPhase.Requested), Is.True);

            TapRaceKeys(race, update, keyboard.vKey);
            TapRaceKeys(race, update, keyboard.digit0Key);
            TapRaceKeys(race, update, keyboard.qKey);
            TapRaceKeys(race, update, keyboard.uKey);
            Assert.That(PumpUntil(race, update, 140f,
                x => x.Racers.All(r => r.Pit.Phase == PitPhase.InService)), Is.True);

            TapRaceKeys(race, update, keyboard.wKey);
            TapRaceKeys(race, update, keyboard.iKey);
            Assert.That(PumpUntil(race, update, 5f,
                x => x.Racers.All(r => r.Pit.Phase == PitPhase.Exiting)), Is.True);
            Assert.That(race.GetRaceSnapshot().Racers.All(x => x.Pit.FinishEligible), Is.True);

            Assert.That(PumpUntil(race, update, 180f, x => x.Phase == RacePhase.Finished), Is.True);
            Assert.That(PumpUntil(race, update, 2f, x => x.AwaitingRematchRelease), Is.True);
            TapRaceKeys(race, update, keyboard.qKey);
            TapRaceKeys(race, update, keyboard.uKey);
            PumpRace(race, update, .05f);

            var rematch = race.GetRaceSnapshot();
            Assert.That(rematch.Phase == RacePhase.Grid || rematch.Phase == RacePhase.Countdown, Is.True);
            Assert.That(rematch.Racers.All(x => x.Condition.Heat == 0f && x.Condition.TireWear == 0f &&
                x.Pit.CompletedServices == 0 && x.Pit.Phase == PitPhase.OnTrack), Is.True);
        }

        [UnityTest]
        public IEnumerator ControlLabCompletesAndRearmsTwoPlayersSimultaneously()
        {
            var labObject = new GameObject("Tranche 1 Control Lab Test");
            var lab = labObject.AddComponent<ControlLab>();
            var scripted = new ScriptedProvider();
            lab.SetInputProvider(scripted);
            int p1Baseline = lab.GetCompletionCount(PlayerId.Player1);
            int p2Baseline = lab.GetCompletionCount(PlayerId.Player2);

            scripted.SetCrews(true, true, true);
            yield return new WaitForSecondsRealtime(1.7f);
            Assert.That(lab.GetCompletionCount(PlayerId.Player1), Is.EqualTo(p1Baseline + 1));
            Assert.That(lab.GetCompletionCount(PlayerId.Player2), Is.EqualTo(p2Baseline + 1));

            yield return new WaitForSecondsRealtime(0.2f);
            Assert.That(lab.GetCompletionCount(PlayerId.Player1), Is.EqualTo(p1Baseline + 1));
            Assert.That(lab.GetCompletionCount(PlayerId.Player2), Is.EqualTo(p2Baseline + 1));

            scripted.SetCrews(false, false, true);
            yield return new WaitForSecondsRealtime(0.05f);
            Assert.That(lab.GetPitAction(PlayerId.Player1).State, Is.EqualTo(PitActionState.Canceled));
            Assert.That(lab.GetPitAction(PlayerId.Player2).State, Is.EqualTo(PitActionState.Canceled));

            scripted.SetCrews(true, true, true);
            yield return new WaitForSecondsRealtime(1.7f);
            Assert.That(lab.GetCompletionCount(PlayerId.Player1), Is.EqualTo(p1Baseline + 2));
            Assert.That(lab.GetCompletionCount(PlayerId.Player2), Is.EqualTo(p2Baseline + 2));
            Object.Destroy(labObject);
            yield return null;
        }

        private System.Collections.Generic.IReadOnlyList<PlayerControlSnapshot> Tap(
            KeyboardInputProvider provider, UnityEngine.InputSystem.Controls.ButtonControl button)
        {
            Press(button);
            var result = provider.ReadSnapshots();
            Release(button, queueEventOnly: true);
            InputSystem.Update();
            return result;
        }

        private void TapRaceKeys(RacePrototype race, System.Reflection.MethodInfo update,
            params ButtonControl[] buttons)
        {
            foreach (var button in buttons) Press(button, queueEventOnly: true);
            InputSystem.Update();
            update.Invoke(race, null);
            foreach (var button in buttons) Release(button, queueEventOnly: true);
            InputSystem.Update();
            PumpRace(race, update, .05f);
        }

        private static bool PumpUntil(RacePrototype race, System.Reflection.MethodInfo update, float timeout,
            System.Func<RaceSnapshot, bool> predicate)
        {
            float step = Mathf.Max(.00001f, Time.unscaledDeltaTime);
            int updates = Mathf.CeilToInt(timeout / step);
            for (int i = 0; i < updates; i++)
            {
                update.Invoke(race, null);
                if (predicate(race.GetRaceSnapshot())) return true;
            }
            return false;
        }

        private static void PumpRace(RacePrototype race, System.Reflection.MethodInfo update, float seconds)
        {
            float step = Mathf.Max(.00001f, Time.unscaledDeltaTime);
            int updates = Mathf.CeilToInt(seconds / step);
            for (int i = 0; i < updates; i++) update.Invoke(race, null);
        }

        private sealed class ScriptedProvider : IPlayerInputProvider
        {
            private System.Collections.Generic.IReadOnlyList<PlayerControlSnapshot> snapshots;

            public ScriptedProvider() => SetCrews(false, false, false);

            public void SetCrews(bool present, bool touched, bool inZone)
            {
                var p1Position = inZone ? new Vec2(1325f, 270f) : new Vec2(400f, 270f);
                var p2Position = inZone ? new Vec2(595f, 810f) : new Vec2(1100f, 810f);
                var p1Crew = present ? new PieceState(true, touched, 101, p1Position, 0f) : PieceState.Missing;
                var p2Crew = present ? new PieceState(true, touched, 201, p2Position, 0f) : PieceState.Missing;
                snapshots = new[]
                {
                    new PlayerControlSnapshot(PlayerId.Player1, ThrottleStep.Off, PieceState.Missing, p1Crew, InputWarning.None),
                    new PlayerControlSnapshot(PlayerId.Player2, ThrottleStep.Off, PieceState.Missing, p2Crew, InputWarning.None)
                };
            }

            public System.Collections.Generic.IReadOnlyList<PlayerControlSnapshot> ReadSnapshots() => snapshots;
        }

        private sealed class RaceScriptedProvider : IPlayerInputProvider
        {
            public RaceScriptedProvider(bool touched) { Touched = touched; }
            public bool Touched { get; set; }
            public System.Collections.Generic.IReadOnlyList<PlayerControlSnapshot> ReadSnapshots()
            {
                var throttle = Touched ? ThrottleStep.Full : ThrottleStep.Off;
                return new[]
                {
                    new PlayerControlSnapshot(PlayerId.Player1, throttle,
                        new PieceState(true, Touched, 101, new Vec2(), 0f), PieceState.Missing, InputWarning.None),
                    new PlayerControlSnapshot(PlayerId.Player2, throttle,
                        new PieceState(true, Touched, 201, new Vec2(), 0f), PieceState.Missing, InputWarning.None)
                };
            }
        }

        private sealed class CrewRaceScriptedProvider : IPlayerInputProvider
        {
            public bool CrewTouched { get; set; }
            public System.Collections.Generic.IReadOnlyList<PlayerControlSnapshot> ReadSnapshots() => new[]
            {
                Snapshot(PlayerId.Player1, 101, new Vec2(1135f, 270f)),
                Snapshot(PlayerId.Player2, 201, new Vec2(405f, 810f))
            };

            private PlayerControlSnapshot Snapshot(PlayerId id, int contactId, Vec2 position) =>
                new PlayerControlSnapshot(id, ThrottleStep.Off,
                    new PieceState(true, false, contactId + 1000, new Vec2(), 0f),
                    new PieceState(true, CrewTouched, contactId, position, 0f), InputWarning.None);
        }
    }
}
