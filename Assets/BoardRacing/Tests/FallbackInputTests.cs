using System.Linq;
using BoardRacing.Domain;
using BoardRacing.Runtime;
using NUnit.Framework;
using UnityEngine.InputSystem;

namespace BoardRacing.Tests
{
    public sealed class FallbackInputTests : InputTestFixture
    {
        private Keyboard keyboard;
        private KeyboardInputProvider provider;

        public override void Setup()
        {
            base.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();
            provider = new KeyboardInputProvider();
        }

        [Test]
        public void BothPlayersCanTouchCarsAndChooseThrottleSimultaneously()
        {
            Tap(keyboard.qKey);
            Tap(keyboard.uKey);
            Tap(keyboard.vKey);
            var snapshots = Tap(keyboard.digit8Key);
            Assert.That(Player(snapshots, PlayerId.Player1).Throttle, Is.EqualTo(ThrottleStep.Full));
            Assert.That(Player(snapshots, PlayerId.Player2).Throttle, Is.EqualTo(ThrottleStep.Half));
        }

        [Test]
        public void RemovingCarsClearsTouchAndThrottleForBothPlayers()
        {
            Tap(keyboard.qKey);
            Tap(keyboard.uKey);
            Tap(keyboard.eKey);
            var snapshots = Tap(keyboard.oKey);
            Assert.That(Player(snapshots, PlayerId.Player1).Car.Present, Is.False);
            Assert.That(Player(snapshots, PlayerId.Player2).Car.Present, Is.False);
            Assert.That(snapshots.All(x => x.Throttle == ThrottleStep.Off), Is.True);
        }

        [Test]
        public void CrewTouchAndRemovalAreIndependent()
        {
            Tap(keyboard.wKey);
            var touched = Tap(keyboard.iKey);
            Assert.That(touched.All(x => x.Crew.Touched), Is.True);
            var removed = Tap(keyboard.rKey);
            Assert.That(Player(removed, PlayerId.Player1).Crew.Present, Is.False);
            Assert.That(Player(removed, PlayerId.Player2).Crew.Present, Is.True);
            Assert.That(Player(removed, PlayerId.Player2).Crew.Touched, Is.True);
        }

        [Test]
        public void EveryDocumentedThrottleKeySelectsItsSector()
        {
            Tap(keyboard.qKey);
            Tap(keyboard.uKey);

            AssertSector(keyboard.zKey, keyboard.digit7Key, ThrottleStep.Quarter);
            AssertSector(keyboard.xKey, keyboard.digit8Key, ThrottleStep.Half);
            AssertSector(keyboard.cKey, keyboard.digit9Key, ThrottleStep.ThreeQuarters);
            AssertSector(keyboard.vKey, keyboard.digit0Key, ThrottleStep.Full);
        }

        private void AssertSector(UnityEngine.InputSystem.Controls.ButtonControl p1,
            UnityEngine.InputSystem.Controls.ButtonControl p2, ThrottleStep expected)
        {
            Tap(p1);
            var snapshots = Tap(p2);
            Assert.That(Player(snapshots, PlayerId.Player1).Throttle, Is.EqualTo(expected));
            Assert.That(Player(snapshots, PlayerId.Player2).Throttle, Is.EqualTo(expected));
        }

        private System.Collections.Generic.IReadOnlyList<PlayerControlSnapshot> Tap(
            UnityEngine.InputSystem.Controls.ButtonControl button)
        {
            Press(button);
            var result = provider.ReadSnapshots();
            Release(button, queueEventOnly: true);
            InputSystem.Update();
            return result;
        }

        private static PlayerControlSnapshot Player(System.Collections.Generic.IReadOnlyList<PlayerControlSnapshot> snapshots,
            PlayerId id) => snapshots.Single(x => x.PlayerId == id);
    }
}
