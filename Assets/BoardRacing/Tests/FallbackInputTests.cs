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
        public void BothPlayersCanChooseDrivingStopsSimultaneouslyWithoutTouch()
        {
            Tap(keyboard.vKey);
            var snapshots = Tap(keyboard.digit8Key);
            Assert.That(Player(snapshots, PlayerId.Player1).Throttle, Is.EqualTo(ThrottleStep.Boost));
            Assert.That(Player(snapshots, PlayerId.Player2).Throttle, Is.EqualTo(ThrottleStep.Drive));
        }

        [Test]
        public void RemovingDrivingShipsClearsThrottleForBothPlayers()
        {
            Tap(keyboard.eKey);
            var snapshots = Tap(keyboard.oKey);
            Assert.That(Player(snapshots, PlayerId.Player1).Car.Present, Is.False);
            Assert.That(Player(snapshots, PlayerId.Player2).Car.Present, Is.False);
            Assert.That(snapshots.All(x => x.Throttle == ThrottleStep.Brake), Is.True);
        }

        [Test]
        public void TouchKeysAreIgnoredAndPitRobotRemovalIsIndependent()
        {
            Tap(keyboard.wKey);
            var touched = Tap(keyboard.iKey);
            Assert.That(touched.All(x => !x.Crew.Touched), Is.True);
            var removed = Tap(keyboard.rKey);
            Assert.That(Player(removed, PlayerId.Player1).Crew.Present, Is.False);
            Assert.That(Player(removed, PlayerId.Player2).Crew.Present, Is.True);
            Assert.That(Player(removed, PlayerId.Player2).Crew.Touched, Is.False);
        }

        [Test]
        public void EveryDocumentedThrottleKeySelectsItsSector()
        {
            AssertSector(keyboard.zKey, keyboard.digit7Key, ThrottleStep.Brake);
            AssertSector(keyboard.xKey, keyboard.digit8Key, ThrottleStep.Drive);
            AssertSector(keyboard.cKey, keyboard.digit9Key, ThrottleStep.Boost);
            AssertSector(keyboard.vKey, keyboard.digit0Key, ThrottleStep.Boost);
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
