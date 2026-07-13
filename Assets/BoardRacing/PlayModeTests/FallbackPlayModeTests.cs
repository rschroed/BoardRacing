using System.Collections;
using System.Linq;
using BoardRacing.Domain;
using BoardRacing.Runtime;
using NUnit.Framework;
using UnityEngine.InputSystem;
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
        public IEnumerator ControlLabStartsWithFallbackWithoutErrorsInEditorPlayMode()
        {
            yield return null;
            Assert.That(UnityEngine.Object.FindObjectOfType<ControlLab>(), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator ControlLabCompletesAndRearmsTwoPlayersSimultaneously()
        {
            yield return null;
            var lab = Object.FindObjectOfType<ControlLab>();
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
    }
}
