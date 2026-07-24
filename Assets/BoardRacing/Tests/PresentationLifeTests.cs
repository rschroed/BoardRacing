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
