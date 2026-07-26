using BoardRacing.Domain;
using BoardRacing.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace BoardRacing.Tests
{
    // The jockeying breath (issue #119): while two cars hold the passing
    // split, the drawn duel breathes — each side alternately flares out for
    // a look and tucks back in with its nose aimed at the rival. The breath
    // spends only the honesty budget: it is lateral and pose only (a
    // DuelBreath cannot even express an along-track shift), a pure function
    // of the pair's travelled distance, and it stills wherever truth is
    // read — the wiring feeds it the line-truth envelope and the corner
    // blend as its amplitude.
    public sealed class PresentationLifeTests
    {

        [Test]
        public void TheLaunchTwitchOnlyEverHesitates()
        {
            // Lag ≥ 0 is the launch's honesty guarantee: a drawn car may
            // under-report its progress off the line for a blink, but can
            // never be drawn across a line its true position has not crossed.
            foreach (int car in new[] { 1, 2 })
                for (float t = 0f; t <= PresentationLife.LaunchWindowSeconds * 1.2f; t += .005f)
                {
                    LaunchTwitch twitch = PresentationLife.Launch(t,
                        PresentationLife.LaunchPhase(car, 3224f));
                    Assert.That(twitch.Lag,
                        Is.InRange(0f, PresentationLife.MaxLaunchLag + 1e-4f), "lag at " + t);
                    Assert.That(Mathf.Abs(twitch.YawDegrees),
                        Is.LessThanOrEqualTo(PresentationLife.MaxLaunchYawDegrees + 1e-4f), "yaw at " + t);
                }
        }

        [Test]
        public void TheLaunchConvergesExactlyOutsideItsWindow()
        {
            // The honesty pin for the along-track channel (issue #119
            // acceptance): the launch window is the one granted exception,
            // and outside it — every finish, final-straight, and pit read
            // lives minutes later — the drawn along-track deviation is
            // identically zero, not merely small.
            foreach (float t in new[] { -1f, 0f, PresentationLife.LaunchWindowSeconds, 1.5f, 90f })
                foreach (int car in new[] { 1, 2 })
                {
                    LaunchTwitch twitch = PresentationLife.Launch(t,
                        PresentationLife.LaunchPhase(car, 2949f));
                    Assert.That(twitch.Lag, Is.EqualTo(0f), "lag at " + t);
                    Assert.That(twitch.YawDegrees, Is.EqualTo(0f), "yaw at " + t);
                }
        }

        [Test]
        public void ACleanGoDoesNotJump()
        {
            // The envelope rises from zero: at GO the car sits exactly where
            // truth put it — no backward snap on the first racing frame.
            LaunchTwitch twitch = PresentationLife.Launch(.001f, PresentationLife.LaunchPhase(1, 2949f));
            Assert.That(twitch.Lag, Is.LessThan(.1f));
        }

        [Test]
        public void TheCarsTradeTheLaunchScrabble()
        {
            // Anti-phase digs: across the window each car spends some
            // instants lagging harder than its rival — neither seat owns the
            // better launch.
            float p1 = PresentationLife.LaunchPhase(1, 2949f);
            float p2 = PresentationLife.LaunchPhase(2, 2949f);
            bool oneDigs = false, twoDigs = false;
            for (float t = .05f; t < PresentationLife.LaunchWindowSeconds; t += .01f)
            {
                float lag1 = PresentationLife.Launch(t, p1).Lag;
                float lag2 = PresentationLife.Launch(t, p2).Lag;
                if (lag1 > lag2 + .5f) oneDigs = true;
                if (lag2 > lag1 + .5f) twoDigs = true;
            }
            Assert.That(oneDigs, Is.True, "car 1 never dug");
            Assert.That(twoDigs, Is.True, "car 2 never dug");
        }

        [Test]
        public void TheLaunchMovesSmoothlyAndDeterministically()
        {
            LaunchTwitch previous = PresentationLife.Launch(0f, PresentationLife.LaunchPhase(2, 4072f));
            for (float t = .005f; t <= PresentationLife.LaunchWindowSeconds + .05f; t += .005f)
            {
                LaunchTwitch twitch = PresentationLife.Launch(t, PresentationLife.LaunchPhase(2, 4072f));
                LaunchTwitch again = PresentationLife.Launch(t, PresentationLife.LaunchPhase(2, 4072f));
                Assert.That(again.Lag, Is.EqualTo(twitch.Lag), "replay lag at " + t);
                Assert.That(again.YawDegrees, Is.EqualTo(twitch.YawDegrees), "replay yaw at " + t);
                Assert.That(Mathf.Abs(twitch.Lag - previous.Lag), Is.LessThan(1f), "lag step at " + t);
                Assert.That(Mathf.Abs(twitch.YawDegrees - previous.YawDegrees), Is.LessThan(.5f),
                    "yaw step at " + t);
                previous = twitch;
            }
        }

    }
}
