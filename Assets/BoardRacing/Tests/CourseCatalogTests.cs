using System;
using BoardRacing.Domain;
using NUnit.Framework;

namespace BoardRacing.Tests
{
    // A course is one authored artifact (issue #107 phase 1): racing line, pit
    // complex, and race length together. These tests pin the Wedge's values to
    // the geometry that shipped when the data lived scattered across
    // RacePrototype constants and the tranche settings, and pin the validation
    // that keeps a future course from authoring nonsense.
    public sealed class CourseCatalogTests
    {
        [Test]
        public void WedgeCourseCarriesTheShippedPitComplexAndRaceLength()
        {
            CourseDefinition course = CourseCatalog.Wedge();
            Assert.That(course.Name, Is.EqualTo("Wedge"));
            Assert.That(course.Laps, Is.EqualTo(6));
            AssertVec(course.Pit.Entry, 680f, 455f);
            AssertVec(course.Pit.PlayerOneBox, 860f, 455f);
            AssertVec(course.Pit.PlayerTwoBox, 1120f, 455f);
            AssertVec(course.Pit.Exit, 1353f, 455f);
            // Retuned for the Y-junction meshing (issue #107 phase 2): the merge
            // approach lifts off the lane center so the climb to the rejoin
            // stays a shallow slip-road angle.
            AssertVec(course.Pit.MergeApproach, 1240f, 428f);
            Assert.That(course.Pit.ExitRejoinDistance, Is.EqualTo(850f));
            Assert.That(course.Pit.Box(PlayerId.Player1), Is.EqualTo(course.Pit.PlayerOneBox));
            Assert.That(course.Pit.Box(PlayerId.Player2), Is.EqualTo(course.Pit.PlayerTwoBox));
        }

        [Test]
        public void WedgePitComplexHangsOffTheTopStraight()
        {
            // The rejoin must land inside the lap, and the lane must run in
            // travel order (entry, boxes, exit west-to-east along the straight)
            // for the exit spline to read as a forward merge.
            CourseDefinition course = CourseCatalog.Wedge();
            Assert.That(course.Pit.ExitRejoinDistance, Is.LessThan(course.Track.Length));
            Assert.That(course.Pit.Entry.X, Is.LessThan(course.Pit.PlayerOneBox.X));
            Assert.That(course.Pit.PlayerOneBox.X, Is.LessThan(course.Pit.PlayerTwoBox.X));
            Assert.That(course.Pit.PlayerTwoBox.X, Is.LessThan(course.Pit.MergeApproach.X));
            Assert.That(course.Pit.MergeApproach.X, Is.LessThan(course.Pit.Exit.X));
        }

        [Test]
        public void CourseSpeedFactorsRideTheCornerSafeSpeed()
        {
            // The per-corner character (sweeper/medium/tight) scales with the
            // physics base speed rather than being absolute — the one knob that
            // stays in the tranche settings.
            CourseDefinition course = CourseCatalog.Wedge(cornerSafeSpeed: 100f);
            float fastest = 0f, slowest = float.MaxValue;
            foreach (TrackSegment segment in course.Track.Segments)
            {
                if (segment.Kind != TrackSectionKind.Corner) continue;
                fastest = Math.Max(fastest, segment.SafeSpeed);
                slowest = Math.Min(slowest, segment.SafeSpeed);
            }
            Assert.That(fastest, Is.EqualTo(100f * TrackCatalog.WedgeSweeperSpeedFactor).Within(.01f));
            Assert.That(slowest, Is.EqualTo(100f * TrackCatalog.WedgeTightSpeedFactor).Within(.01f));
        }

        [Test]
        public void CourseValidationRejectsUnraceableData()
        {
            TrackDefinition track = TrackCatalog.Wedge();
            PitComplexDefinition pit = CourseCatalog.Wedge().Pit;
            Assert.Throws<ArgumentException>(() => new CourseDefinition("", track, pit, 6),
                "a course needs a name");
            Assert.Throws<ArgumentNullException>(() => new CourseDefinition("X", null, pit, 6),
                "a course needs a track");
            Assert.Throws<ArgumentException>(() => new CourseDefinition("X", track, pit, 0),
                "a race needs at least one lap");
            Assert.Throws<ArgumentException>(() => new CourseDefinition("X", track,
                new PitComplexDefinition(pit.Entry, pit.PlayerOneBox, pit.PlayerTwoBox,
                    pit.Exit, pit.MergeApproach, track.Length + 1f), 6),
                "the rejoin must land inside the lap");
            Assert.Throws<ArgumentException>(() => new PitComplexDefinition(pit.Entry,
                pit.PlayerOneBox, pit.PlayerTwoBox, pit.Exit, pit.MergeApproach, 0f),
                "the rejoin distance must be positive");
        }

        private static void AssertVec(Vec2 actual, float x, float y)
        {
            Assert.That(actual.X, Is.EqualTo(x).Within(.001f));
            Assert.That(actual.Y, Is.EqualTo(y).Within(.001f));
        }
    }
}
