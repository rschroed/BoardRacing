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
            TrackDefinition track = CourseCatalog.Wedge().Track;
            float boundary = LongestRun(track, TrackSectionKind.Corner).Start;
            float previous = float.MaxValue;
            for (float d = boundary - 60f; d <= boundary + 60f; d += 4f)
            {
                float scale = CornerCharacter.SplitScale(track, d);
                // No snap: the window makes the transition a ramp, never a step.
                Assert.That(Math.Abs(previous == float.MaxValue ? 0f : previous - scale),
                    Is.LessThan(.15f), "step at " + d);
                Assert.That(scale, Is.LessThanOrEqualTo(
                    (previous == float.MaxValue ? 1f : previous) + .01f), "rises into the corner at " + d);
                previous = scale;
            }
            Assert.That(CornerCharacter.SplitScale(track, boundary - 60f), Is.GreaterThan(.95f));
            Assert.That(CornerCharacter.SplitScale(track, boundary + 60f),
                Is.LessThan(CornerCharacter.SplitFloor + .1f));
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
