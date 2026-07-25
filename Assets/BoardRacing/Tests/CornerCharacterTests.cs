using System;
using System.Collections.Generic;
using System.Linq;
using BoardRacing.Domain;
using BoardRacing.Runtime;
using NUnit.Framework;

namespace BoardRacing.Tests
{
    // Corner character (issue #117): the passing split tapers toward a floor
    // through corners — full width, the outside car swept a wider arc at the
    // same angular rate, a phantom speed-up the sim never granted — and the
    // body drifts past the path tangent in proportion to curvature × overspeed
    // against the corner's safe speed. Pose only; the center stays true.
    public sealed class CornerCharacterTests
    {
        [Test]
        public void SplitDrawsFullOnStraightsAndFloorsInCorners()
        {
            foreach (CourseDefinition course in CourseCatalog.All())
            {
                TrackDefinition track = course.Track;
                Assert.That(CornerCharacter.SplitScale(track, MidOfLongest(track, TrackSectionKind.Straight)),
                    Is.GreaterThan(.95f), course.Name + " straight");
                Assert.That(CornerCharacter.SplitScale(track, MidOfLongest(track, TrackSectionKind.Corner)),
                    Is.EqualTo(CornerCharacter.SplitFloor).Within(.02f), course.Name + " corner");
                // The taper only ever SHRINKS the split — it may never push
                // cars further apart than the sim asked (honesty pin).
                for (float d = 0f; d < track.Length; d += 10f)
                {
                    float scale = CornerCharacter.SplitScale(track, d);
                    Assert.That(scale, Is.InRange(CornerCharacter.SplitFloor - .01f, 1.001f),
                        course.Name + " at " + d);
                }
            }
        }

        [Test]
        public void SplitScaleEasesAcrossTheCornerBoundary()
        {
            // The formation glide (owner feel review 2026-07-23): the split
            // converges across the whole ±FormationHalfSpan approach — full
            // width only with the window clear of corners, well toward the
            // floor deep inside — and every step of the sweep moves it only
            // a little, never a snap. Sampled at the corner that ends the
            // longest straight, the one entry guaranteed a clear approach.
            TrackDefinition track = CourseCatalog.Wedge().Track;
            SectionRun straight = LongestRun(track, TrackSectionKind.Straight);
            float boundary = straight.Start + straight.Length;
            float approach = CornerCharacter.FormationHalfSpan + 40f;
            float previous = float.MaxValue;
            for (float d = boundary - approach; d <= boundary + 90f; d += 4f)
            {
                float scale = CornerCharacter.SplitScale(track, d);
                Assert.That(Math.Abs(previous == float.MaxValue ? 0f : previous - scale),
                    Is.LessThan(.05f), "step at " + d);
                Assert.That(scale, Is.LessThanOrEqualTo(
                    (previous == float.MaxValue ? 1f : previous) + .01f), "rises into the corner at " + d);
                previous = scale;
            }
            Assert.That(CornerCharacter.SplitScale(track, boundary - approach), Is.GreaterThan(.95f));
            Assert.That(CornerCharacter.SplitScale(track, boundary + 90f), Is.LessThan(.45f));
        }

        [Test]
        public void TheFormationGlideKeepsDrawnSpeedsHonest()
        {
            // How fast the formation's invention develops IS a drawn speed
            // error: a dead-heat pair's pads move at NoseToTailSpacing/2 ×
            // blend slope on top of true motion. The old curvature window
            // let that spike to ~75% of true speed at a hairpin mouth (the
            // 2026-07-23 capture's lurch: side-by-side to a car-length
            // stagger in ~200ms); the glide keeps the worst instant on
            // every catalog course under a quarter of true speed.
            foreach (CourseDefinition course in CourseCatalog.All())
            {
                TrackDefinition track = course.Track;
                for (float d = 0f; d < track.Length; d += 5f)
                {
                    float slope = Math.Abs(CornerCharacter.FormationBlend(track, d + 2f) -
                        CornerCharacter.FormationBlend(track, d - 2f)) / 4f;
                    Assert.That(slope * CornerCharacter.NoseToTailSpacing * .5f, Is.LessThan(.25f),
                        course.Name + " at " + d);
                }
            }
        }

        [Test]
        public void TheFormationRestsOnlyOnStraightsLongEnoughToRaceAcross()
        {
            // The formation corridor (Fishhook jitter, 2026-07-23): fully
            // releasing and re-forming costs a FormationHalfSpan·2 ramp each
            // way, so a straight shorter than the rest span buys a blink of
            // side-by-side at most — on Fishhook's paperclip the pair churned
            // open-closed-open twice back to back, every lap. Short straights
            // stay part of one corner complex (the file HOLDS through them);
            // straights long enough to race across still rest completely.
            foreach (CourseDefinition course in CourseCatalog.All())
                foreach (SectionRun straight in Runs(course.Track)
                    .Where(run => run.Kind == TrackSectionKind.Straight))
                {
                    if (straight.Length < CornerCharacter.FormationRestSpan)
                        for (float d = straight.Start; d <= straight.Start + straight.Length; d += 10f)
                            Assert.That(CornerCharacter.FormationBlend(course.Track, d),
                                Is.GreaterThan(.8f), course.Name + " holds file at " + d);
                    else
                        Assert.That(CornerCharacter.FormationBlend(course.Track,
                                straight.Start + straight.Length * .5f),
                            Is.LessThan(.01f), course.Name + " rests at " + straight.Start);
                }
        }

        [Test]
        public void TaperCapsTheOutsideCarsPhantomCornerSpeed()
        {
            float offset = RaceRules.Defaults.PassingOffset;
            foreach (CourseDefinition course in CourseCatalog.All())
                foreach (SectionRun corner in Runs(course.Track)
                    .Where(run => run.Kind == TrackSectionKind.Corner && run.Length >= 100f))
                {
                    float mid = corner.Start + corner.Length * .5f;
                    float radius = 1f / Math.Abs(CornerCharacter.SignedCurvature(course.Track, mid));
                    float untapered = (radius + offset) / radius;
                    float tapered = (radius + offset * CornerCharacter.SplitScale(course.Track, mid)) / radius;
                    // The outside car's drawn speed premium through any catalog
                    // corner stays under 15% (was up to ~53% on R72 at the full
                    // 38 px split).
                    Assert.That(tapered, Is.LessThan(1.15f), course.Name + " at " + mid);
                    Assert.That(tapered, Is.LessThan(untapered), course.Name + " at " + mid);
                }
        }

        [Test]
        public void DriftIsZeroOnStraightsAtAnySpeed()
        {
            TrackDefinition track = CourseCatalog.Wedge().Track;
            float mid = MidOfLongest(track, TrackSectionKind.Straight);
            foreach (float speed in new[] { 0f, Pace.CornerSafeSpeed, Pace.BasePace, 2f * Pace.BasePace })
                Assert.That(CornerCharacter.DriftDegrees(track, mid, speed),
                    Is.EqualTo(0f).Within(.001f), "speed " + speed);
        }

        [Test]
        public void DriftGrowsWithOverspeedTowardTheCornersInside()
        {
            TrackDefinition track = CourseCatalog.Wedge().Track;
            SectionRun corner = LongestRun(track, TrackSectionKind.Corner);
            float mid = corner.Start + corner.Length * .5f;
            float safe = track.Sample(mid).SafeSpeed;
            Assert.That(Math.Abs(CornerCharacter.DriftDegrees(track, mid, safe)), Is.LessThan(.01f),
                "a car under the limit stays composed");
            float mild = Math.Abs(CornerCharacter.DriftDegrees(track, mid, safe * 1.2f));
            float hard = Math.Abs(CornerCharacter.DriftDegrees(track, mid, safe * 1.4f));
            Assert.That(mild, Is.GreaterThan(.5f));
            Assert.That(hard, Is.GreaterThan(mild));
            // Steady state mid-corner: the lead/lag beat cancels and the slip
            // saturates at the cap.
            Assert.That(Math.Abs(CornerCharacter.DriftDegrees(track, mid, safe * 2f)),
                Is.EqualTo(CornerCharacter.MaxDriftDegrees).Within(.3f));
            Assert.That(Math.Sign(CornerCharacter.DriftDegrees(track, mid, safe * 2f)),
                Is.EqualTo(Math.Sign(CornerCharacter.SignedCurvature(track, mid))),
                "the nose rotates past the tangent toward the inside of the turn");
        }

        [Test]
        public void TheExitBeatSwingsPastStraightAsTheCornerReleases()
        {
            TrackDefinition track = CourseCatalog.Wedge().Track;
            // The corner released onto the longest straight: room for the whole
            // lead/lag window to clear the geometry.
            SectionRun straight = LongestRun(track, TrackSectionKind.Straight);
            SectionRun corner = Runs(track).Last(run => run.Kind == TrackSectionKind.Corner &&
                Wrap(track, run.Start + run.Length) == Wrap(track, straight.Start));
            float insideMid = corner.Start + corner.Length * .5f;
            float released = corner.Start + corner.Length + 50f;
            float speed = track.Sample(insideMid).SafeSpeed * 2f;
            int insideSign = Math.Sign(CornerCharacter.DriftDegrees(track, insideMid, speed));
            float exitDrift = CornerCharacter.DriftDegrees(track, released, speed);
            Assert.That(Math.Sign(exitDrift), Is.EqualTo(-insideSign),
                "a beat of opposite lock as the corner lets go");
            Assert.That(Math.Abs(exitDrift), Is.GreaterThan(.5f));
        }

        [Test]
        public void ADeadHeatDrawsNoseToTailThroughACorner()
        {
            TrackDefinition track = CourseCatalog.Wedge().Track;
            float mid = MidOfLongest(track, TrackSectionKind.Corner);
            float[] pads = CornerCharacter.CornerSpacingPads(track, new[] { mid, mid }, 180f);
            Assert.That(pads[1] - pads[0], Is.EqualTo(CornerCharacter.NoseToTailSpacing).Within(1f));
            // Input order breaks the tie the same way every frame, and the
            // battle stays centered on its true spot.
            Assert.That(pads[0], Is.LessThan(0f));
            Assert.That(pads[0] + pads[1], Is.EqualTo(0f).Within(.001f));
        }

        [Test]
        public void AnHonestGapIsDrawnUnchanged()
        {
            TrackDefinition track = CourseCatalog.Wedge().Track;
            float mid = MidOfLongest(track, TrackSectionKind.Corner);
            float half = CornerCharacter.NoseToTailSpacing * .5f + 5f;
            float[] pads = CornerCharacter.CornerSpacingPads(track,
                new[] { mid - half, mid + half }, 180f);
            Assert.That(pads[0], Is.EqualTo(0f).Within(.001f));
            Assert.That(pads[1], Is.EqualTo(0f).Within(.001f));
        }

        [Test]
        public void TheFormationOnlyPushesApartAndKeepsTheBattleCentered()
        {
            TrackDefinition track = CourseCatalog.Wedge().Track;
            float mid = MidOfLongest(track, TrackSectionKind.Corner);
            float[] pads = CornerCharacter.CornerSpacingPads(track,
                new[] { mid - 10f, mid + 10f }, 180f);
            Assert.That(pads[0], Is.LessThan(0f), "the trailing car draws further back");
            Assert.That(pads[1], Is.GreaterThan(0f), "the leader draws further ahead");
            Assert.That(pads[0] + pads[1], Is.EqualTo(0f).Within(.001f), "the pack never advances");
            Assert.That(20f + pads[1] - pads[0],
                Is.EqualTo(CornerCharacter.NoseToTailSpacing).Within(1f));
        }

        [Test]
        public void TheFormationRelaxesOnStraightsAndAtEveryStartLine()
        {
            foreach (CourseDefinition course in CourseCatalog.All())
            {
                float straight = MidOfLongest(course.Track, TrackSectionKind.Straight);
                float[] pads = CornerCharacter.CornerSpacingPads(course.Track,
                    new[] { straight, straight }, 180f);
                Assert.That(pads[0], Is.EqualTo(0f).Within(.001f), course.Name + " straight");
                // The line-truth envelope zeroes the pad at every start/finish
                // crossing — laps, pit diversions, and the finish are judged
                // there — even where a course runs its line straight out of a
                // corner (the Wedge hairpin ends AT the line).
                float[] linePads = CornerCharacter.CornerSpacingPads(course.Track,
                    new[] { 0f, 0f }, 180f);
                Assert.That(linePads[0], Is.EqualTo(0f).Within(.001f), course.Name + " line");
            }
        }

        [Test]
        public void SpatialOrderOutranksLapCount()
        {
            // A lapping leader approaches from physically behind: the drawn
            // pad must follow ribbon order, or the bodies would swap through
            // each other.
            TrackDefinition track = CourseCatalog.Wedge().Track;
            float mid = MidOfLongest(track, TrackSectionKind.Corner);
            float[] pads = CornerCharacter.CornerSpacingPads(track,
                new[] { mid + track.Length, mid + 20f }, 180f);
            Assert.That(pads[0], Is.LessThan(0f), "the lap-ahead car is spatially behind");
            Assert.That(pads[1], Is.GreaterThan(0f));
        }

        [Test]
        public void FourCarsChainNoseToTailThroughTheLongestCorner()
        {
            // N-car pin (issue #124): the pads are a chain, not a pairwise
            // hack — four bunched cars space into one legible train.
            CourseDefinition course = CourseCatalog.All()
                .OrderByDescending(c => LongestRun(c.Track, TrackSectionKind.Corner).Length).First();
            float mid = MidOfLongest(course.Track, TrackSectionKind.Corner);
            float[] distances = { mid - 15f, mid - 5f, mid + 5f, mid + 15f };
            float[] pads = CornerCharacter.CornerSpacingPads(course.Track, distances, 180f);
            for (int i = 1; i < 4; i++)
                Assert.That(distances[i] + pads[i] - distances[i - 1] - pads[i - 1],
                    Is.EqualTo(CornerCharacter.NoseToTailSpacing).Within(2f), "gap " + i);
            Assert.That(pads.Sum(), Is.EqualTo(0f).Within(.01f));
        }

        [Test]
        public void FarApartCarsAreNoFormation()
        {
            TrackDefinition track = CourseCatalog.Wedge().Track;
            float mid = MidOfLongest(track, TrackSectionKind.Corner);
            float[] pads = CornerCharacter.CornerSpacingPads(track,
                new[] { mid, mid + 300f }, 180f);
            Assert.That(pads[0], Is.EqualTo(0f).Within(.001f));
            Assert.That(pads[1], Is.EqualTo(0f).Within(.001f));
        }

        [Test]
        public void BrakeDiveScalesWithDecelerationAndCaps()
        {
            Assert.That(CornerCharacter.BrakeDive(0f, Pace.Braking), Is.EqualTo(0f));
            Assert.That(CornerCharacter.BrakeDive(Pace.Braking * .5f, Pace.Braking),
                Is.EqualTo(.5f).Within(.001f));
            Assert.That(CornerCharacter.BrakeDive(Pace.Braking * 3f, Pace.Braking), Is.EqualTo(1f));
            Assert.That(CornerCharacter.BrakeDive(100f, 0f), Is.EqualTo(0f));

            TrackDefinition track = CourseCatalog.Wedge().Track;
            float mid = MidOfLongest(track, TrackSectionKind.Straight);
            CarAttitude fullDive = CornerCharacter.Attitude(track, mid, Pace.BasePace,
                Pace.Braking, Pace.Braking);
            Assert.That(fullDive.SquashAlong, Is.EqualTo(1f - CornerCharacter.DiveSquash).Within(.001f));
            Assert.That(fullDive.StretchAcross,
                Is.EqualTo(1f + CornerCharacter.DiveSquash * .5f).Within(.001f));
            CarAttitude steady = CornerCharacter.Attitude(track, mid, Pace.BasePace, 0f, Pace.Braking);
            Assert.That(steady.SquashAlong, Is.EqualTo(1f));
            Assert.That(steady.StretchAcross, Is.EqualTo(1f));
            Assert.That(steady.DriftDegrees, Is.EqualTo(0f).Within(.001f));
        }

        private readonly struct SectionRun
        {
            public SectionRun(TrackSectionKind kind, float start, float length)
            { Kind = kind; Start = start; Length = length; }
            public TrackSectionKind Kind { get; }
            public float Start { get; }
            public float Length { get; }
        }

        // Consecutive same-kind segments grouped into one section (a designed
        // corner is a fan of short chords).
        private static List<SectionRun> Runs(TrackDefinition track)
        {
            var runs = new List<SectionRun>();
            float start = 0f, length = 0f;
            TrackSectionKind kind = track.Segments[0].Kind;
            foreach (TrackSegment segment in track.Segments)
            {
                if (segment.Kind != kind)
                {
                    runs.Add(new SectionRun(kind, start, length));
                    kind = segment.Kind; start += length; length = 0f;
                }
                length += segment.Length;
            }
            runs.Add(new SectionRun(kind, start, length));
            return runs;
        }

        private static SectionRun LongestRun(TrackDefinition track, TrackSectionKind kind) =>
            Runs(track).Where(run => run.Kind == kind).OrderByDescending(run => run.Length).First();

        private static float MidOfLongest(TrackDefinition track, TrackSectionKind kind)
        {
            SectionRun run = LongestRun(track, kind);
            return run.Start + run.Length * .5f;
        }

        private static float Wrap(TrackDefinition track, float distance) =>
            (distance % track.Length + track.Length) % track.Length;
    }
}
