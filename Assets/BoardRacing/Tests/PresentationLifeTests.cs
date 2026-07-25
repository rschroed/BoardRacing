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
        public void TheBreathOnlyEverWidensTheSplit()
        {
            // Flare ≥ 1 is the no-overlap guarantee: both offsets scale away
            // from the centerline, so the tucked 2px of daylight is the
            // narrowest the duel ever draws.
            for (float d = 0f; d < PresentationLife.BreathWavelength * 3f; d += 7f)
                foreach (float side in new[] { 1f, -1f })
                {
                    DuelBreath breath = PresentationLife.Breathe(d, side, 1f);
                    Assert.That(breath.FlareScale,
                        Is.InRange(1f, 1f + PresentationLife.FlareRatio + 1e-4f), "flare at " + d);
                    Assert.That(breath.StanceDegrees,
                        Is.InRange(0f, PresentationLife.MaxStanceDegrees + 1e-4f), "stance at " + d);
                }
        }

        [Test]
        public void AFadedBreathIsPerfectlyStill()
        {
            // The honesty-zone pin (issue #119 acceptance): the wiring fades
            // the breath with CornerCharacter.LineTruthEnvelope, which is 0
            // AT the start/finish line — so at the line, where laps, the
            // finish, and pit diversions are judged, the drawn deviation is
            // exactly zero, not merely small.
            for (float d = 0f; d < PresentationLife.BreathWavelength * 2f; d += 13f)
                foreach (float amplitude in new[] { 0f, -1f })
                    foreach (float side in new[] { 1f, -1f })
                    {
                        DuelBreath breath = PresentationLife.Breathe(d, side, amplitude);
                        Assert.That(breath.FlareScale, Is.EqualTo(1f), "flare at " + d);
                        Assert.That(breath.StanceDegrees, Is.EqualTo(0f), "stance at " + d);
                    }
            Assert.That(DuelBreath.Still.FlareScale, Is.EqualTo(1f));
            Assert.That(DuelBreath.Still.StanceDegrees, Is.EqualTo(0f));
        }

        [Test]
        public void TheBreathScalesWithItsAmplitude()
        {
            // Linear in amplitude (and clamped to [0,1]) so the envelope
            // fades produce a smooth ease to stillness, never a pop.
            for (float d = 0f; d < PresentationLife.BreathWavelength; d += 31f)
            {
                DuelBreath full = PresentationLife.Breathe(d, 1f, 1f);
                DuelBreath half = PresentationLife.Breathe(d, 1f, .5f);
                DuelBreath over = PresentationLife.Breathe(d, 1f, 5f);
                Assert.That(half.FlareScale - 1f, Is.EqualTo((full.FlareScale - 1f) * .5f).Within(1e-5f));
                Assert.That(half.StanceDegrees, Is.EqualTo(full.StanceDegrees * .5f).Within(1e-5f));
                Assert.That(over.FlareScale, Is.EqualTo(full.FlareScale).Within(1e-6f));
            }
        }

        [Test]
        public void TheSidesTradeFeints()
        {
            // Anti-phase: one car pulls out while the other tucks in and
            // aims — an exchange, not synchronized calisthenics. A quarter
            // wavelength in, the + side is fully flared and composed while
            // the - side is fully tucked and leaning.
            DuelBreath outward = PresentationLife.Breathe(PresentationLife.BreathWavelength * .25f, 1f, 1f);
            DuelBreath inward = PresentationLife.Breathe(PresentationLife.BreathWavelength * .25f, -1f, 1f);
            Assert.That(outward.FlareScale, Is.EqualTo(1f + PresentationLife.FlareRatio).Within(1e-4f));
            Assert.That(outward.StanceDegrees, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(inward.FlareScale, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(inward.StanceDegrees, Is.EqualTo(PresentationLife.MaxStanceDegrees).Within(1e-3f));
        }

        [Test]
        public void AFlaredLeaningCarStillFitsTheRibbon()
        {
            // The breath spends real ribbon: at every point of the cycle the
            // flared offset plus the leaned body's lateral extent must fit
            // the half ribbon, with the same 1px grace the static geometry
            // pin (ASideBySidePairFitsTheTrackRibbon) allows the base split.
            float offset = RaceRules.Defaults.PassingOffset;
            for (float d = 0f; d < PresentationLife.BreathWavelength; d += 3f)
                foreach (float side in new[] { 1f, -1f })
                {
                    DuelBreath breath = PresentationLife.Breathe(d, side, 1f);
                    float lean = breath.StanceDegrees * Mathf.Deg2Rad;
                    float extent = offset * breath.FlareScale +
                        RaceSurfaceGeometry.CarBodyHalfSize * Mathf.Sin(lean) +
                        RaceSurfaceGeometry.CarBodyHalfWidth * Mathf.Cos(lean);
                    Assert.That(extent, Is.LessThanOrEqualTo(RaceSurfaceGeometry.TrackWidth * .5f + 1f),
                        "extent at " + d + " side " + side);
                }
        }

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

        [Test]
        public void TheSplitAssemblesAcrossTheEngageSpan()
        {
            // The engage-boundary pin (Fishhook jitter, 2026-07-23): the sim
            // grants and revokes the passing offset as a binary at
            // PassingDistance, and the drawn split used to follow it — a
            // full-offset sideways teleport in one step. Engagement must be
            // exactly 0 at the boundary (continuous with the revocation),
            // fully assembled one EngageSpan inside it, and glide monotonely
            // in between — so no gap history can ever pop a body sideways.
            float passing = RaceRules.Defaults.PassingDistance;
            Assert.That(PresentationLife.DuelEngagement(passing, passing), Is.EqualTo(0f));
            Assert.That(PresentationLife.DuelEngagement(passing + 50f, passing), Is.EqualTo(0f));
            Assert.That(PresentationLife.DuelEngagement(passing - PresentationLife.EngageSpan, passing),
                Is.EqualTo(1f));
            Assert.That(PresentationLife.DuelEngagement(0f, passing), Is.EqualTo(1f));
            float previous = PresentationLife.DuelEngagement(passing + 10f, passing);
            for (float gap = passing + 10f; gap >= 0f; gap -= .5f)
            {
                float engagement = PresentationLife.DuelEngagement(gap, passing);
                Assert.That(engagement, Is.GreaterThanOrEqualTo(previous), "monotone at gap " + gap);
                // Slope ≤ smoothstep's peak 1.5/EngageSpan: half a px of gap
                // change moves the drawn offset well under a quarter px.
                Assert.That(engagement - previous,
                    Is.LessThanOrEqualTo(.5f * 1.5f / PresentationLife.EngageSpan + 1e-4f),
                    "step at gap " + gap);
                previous = engagement;
            }
        }

        [Test]
        public void ThePassClearanceOpensOnlyMidExchange()
        {
            // The pass-around pin: at the held-file spacing the clearance
            // sits under the split floor the corner taper already grants —
            // a locked nose-to-tail formation never feels it — and it only
            // reaches full width as the drawn bodies truly exchange.
            Assert.That(PresentationLife.PassClearance(CornerCharacter.NoseToTailSpacing),
                Is.LessThan(CornerCharacter.SplitFloor));
            Assert.That(PresentationLife.PassClearance(PresentationLife.PassClearanceFar),
                Is.EqualTo(0f));
            Assert.That(PresentationLife.PassClearance(200f), Is.EqualTo(0f));
            Assert.That(PresentationLife.PassClearance(0f), Is.EqualTo(1f));
            Assert.That(PresentationLife.PassClearance(-25f),
                Is.EqualTo(PresentationLife.PassClearance(25f)), "symmetric in exchange direction");
            float previous = PresentationLife.PassClearance(0f);
            for (float gap = 1f; gap <= 100f; gap += 1f)
            {
                float clearance = PresentationLife.PassClearance(gap);
                Assert.That(clearance, Is.LessThanOrEqualTo(previous + 1e-5f), "monotone at " + gap);
                Assert.That(previous - clearance,
                    Is.LessThan(1.5f / PresentationLife.PassClearanceSpan + 1e-4f), "step at " + gap);
                previous = clearance;
            }
        }

        [Test]
        public void TheBreathMovesSmoothlyAndDeterministically()
        {
            // Deterministic (the honesty budget bans per-frame randomness:
            // replaying the same sim state redraws the same duel) and slow —
            // px-scale steps move the pose far less than a frame of travel
            // would make visible, so the breath can never read as a twitch.
            DuelBreath previous = PresentationLife.Breathe(0f, 1f, 1f);
            for (float d = 1f; d <= PresentationLife.BreathWavelength * 1.5f; d += 1f)
            {
                DuelBreath breath = PresentationLife.Breathe(d, 1f, 1f);
                DuelBreath again = PresentationLife.Breathe(d, 1f, 1f);
                Assert.That(again.FlareScale, Is.EqualTo(breath.FlareScale), "replay flare at " + d);
                Assert.That(again.StanceDegrees, Is.EqualTo(breath.StanceDegrees), "replay stance at " + d);
                Assert.That(Mathf.Abs(breath.FlareScale - previous.FlareScale), Is.LessThan(.01f),
                    "flare step at " + d);
                Assert.That(Mathf.Abs(breath.StanceDegrees - previous.StanceDegrees), Is.LessThan(.05f),
                    "stance step at " + d);
                previous = breath;
            }
        }
    }
}
