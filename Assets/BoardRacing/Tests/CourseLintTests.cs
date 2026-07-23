using System.Collections.Generic;
using System.Linq;
using BoardRacing.Domain;
using BoardRacing.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace BoardRacing.Tests
{
    // The course lint (issue #107 phase 3) is the authoring-time guard for
    // phase 4's second course: every rule that made the Wedge work — fitting
    // the shared race bounds, clearing the fixed seats, gentle chords, room
    // for the junction gores — becomes a check a new course must pass. One
    // test sweeps the real catalog; the rest each break one rule on purpose.
    public sealed class CourseLintTests
    {
        [Test]
        public void EveryCatalogCourseLintsClean()
        {
            foreach (CourseDefinition course in CourseCatalog.All())
            {
                IReadOnlyList<string> findings = CourseLint.Check(course, Layout());
                Assert.That(findings, Is.Empty,
                    $"course '{course.Name}':\n{string.Join("\n", findings)}");
            }
        }

        [Test]
        public void LintFlagsASurfaceEscapingTheSharedRaceBounds()
        {
            // The whole Wedge slid west: the hairpin leaves the reserved race
            // region and enters seat/chrome territory.
            CourseDefinition course = TranslatedWedge(-200f, 0f);
            Assert.That(CourseLint.Check(course, Layout()),
                Has.Some.Contains("shared race bounds"));
        }

        [Test]
        public void LintFlagsSeatClusterIntrusion()
        {
            // A small loop parked in the upper-left of the shared race bounds:
            // inside the reserved region, but leaning into Player 2's throttle
            // arc — bounds alone don't protect the seats.
            CourseDefinition course = SquareCourse(center: new Vector2(360f, 360f), halfSize: 100f);
            Assert.That(CourseLint.Check(course, Layout()),
                Has.Some.Contains("seat cluster"));
        }

        [Test]
        public void LintFlagsKinkedChords()
        {
            // The square's 90° corners are far beyond the ≤13.5° discipline the
            // ribbon smoothing needs.
            CourseDefinition course = SquareCourse(center: new Vector2(960f, 540f), halfSize: 220f);
            Assert.That(CourseLint.Check(course, Layout()), Has.Some.Contains("scallop"));
        }

        [Test]
        public void LintFlagsPitAnchorsOnTheRoadwayOrAdrift()
        {
            TrackDefinition track = TrackCatalog.Wedge();
            // Player one's box parked on the racing line itself; entry drifted
            // deep into the infield.
            var pit = new PitComplexDefinition(new Vec2(680f, 540f), new Vec2(860f, 405f),
                new Vec2(1120f, 455f), new Vec2(1353f, 455f), new Vec2(1240f, 428f), 850f);
            IReadOnlyList<string> findings =
                CourseLint.Check(new CourseDefinition("Bad", track, pit, 6), Layout());
            Assert.That(findings, Has.Some.Contains("player one box"));
            Assert.That(findings, Has.Some.Contains("roadway"));
            Assert.That(findings, Has.Some.Contains("strays from the track"));
        }

        [Test]
        public void LintFlagsJunctionsWithoutRoomToTaper()
        {
            TrackDefinition track = TrackCatalog.Wedge();
            // Boxes crowded together at the east end of the straight: no box
            // spacing, no merge run before the rejoin, entry too close to the
            // start line.
            var pit = new PitComplexDefinition(new Vec2(560f, 452f), new Vec2(1150f, 455f),
                new Vec2(1250f, 455f), new Vec2(1353f, 455f), new Vec2(1300f, 440f), 850f);
            IReadOnlyList<string> findings =
                CourseLint.Check(new CourseDefinition("Bad", track, pit, 6), Layout());
            Assert.That(findings, Has.Some.Contains("boxes are"));
            Assert.That(findings, Has.Some.Contains("climb too steeply"));
            Assert.That(findings, Has.Some.Contains("peel off"));
        }

        [Test]
        public void LintFlagsAShallowCrossingCrowdingThePit()
        {
            // A bowtie whose diagonals cross at ~25°, with the pit boxes parked
            // right on top of the crossing — both crossing rules must fire.
            var points = new[]
            {
                new Vec2(500f, 450f), new Vec2(1400f, 650f), new Vec2(500f, 650f),
                new Vec2(1400f, 450f), new Vec2(500f, 450f),
            };
            var segments = new List<TrackSegment>();
            for (int i = 0; i < 4; i++)
                segments.Add(new TrackSegment(points[i], points[i + 1],
                    TrackSectionKind.Straight, float.PositiveInfinity));
            var pit = new PitComplexDefinition(new Vec2(900f, 560f), new Vec2(940f, 560f),
                new Vec2(980f, 560f), new Vec2(1050f, 560f), new Vec2(1010f, 555f), 800f);
            IReadOnlyList<string> findings = CourseLint.Check(
                new CourseDefinition("Bowtie", new TrackDefinition(segments), pit, 6), Layout());
            Assert.That(findings, Has.Some.Contains("crosses itself at"));
            Assert.That(findings, Has.Some.Contains("from the crossing"));
        }

        [Test]
        public void LintFlagsAPitComplexHangingOffACorner()
        {
            TrackDefinition track = TrackCatalog.Wedge();
            // The complex moved to the sweeper: every anchor projects onto
            // corner chords.
            var pit = new PitComplexDefinition(new Vec2(1500f, 480f), new Vec2(1480f, 530f),
                new Vec2(1470f, 590f), new Vec2(1440f, 650f), new Vec2(1450f, 620f), 1400f);
            IReadOnlyList<string> findings =
                CourseLint.Check(new CourseDefinition("Bad", track, pit, 6), Layout());
            Assert.That(findings, Has.Some.Contains("needs a straight"));
        }

        // The shipped Wedge, rigidly translated — geometry that is valid in
        // itself but sits in the wrong place on the table.
        private static CourseDefinition TranslatedWedge(float dx, float dy)
        {
            CourseDefinition wedge = CourseCatalog.Wedge();
            var segments = wedge.Track.Segments.Select(s => new TrackSegment(
                new Vec2(s.Start.X + dx, s.Start.Y + dy), new Vec2(s.End.X + dx, s.End.Y + dy),
                s.Kind, s.SafeSpeed)).ToList();
            Vec2 Moved(Vec2 p) => new Vec2(p.X + dx, p.Y + dy);
            return new CourseDefinition(wedge.Name, new TrackDefinition(segments),
                new PitComplexDefinition(Moved(wedge.Pit.Entry), Moved(wedge.Pit.PlayerOneBox),
                    Moved(wedge.Pit.PlayerTwoBox), Moved(wedge.Pit.Exit),
                    Moved(wedge.Pit.MergeApproach), wedge.Pit.ExitRejoinDistance),
                wedge.Laps);
        }

        // A crude clockwise square loop with a pit complex inside its top edge;
        // deliberately kinked, useful for placement rules.
        private static CourseDefinition SquareCourse(Vector2 center, float halfSize)
        {
            var corners = new[]
            {
                new Vec2(center.x - halfSize, center.y - halfSize),
                new Vec2(center.x + halfSize, center.y - halfSize),
                new Vec2(center.x + halfSize, center.y + halfSize),
                new Vec2(center.x - halfSize, center.y + halfSize),
            };
            var segments = new List<TrackSegment>();
            for (int i = 0; i < 4; i++)
                segments.Add(new TrackSegment(corners[i], corners[(i + 1) % 4],
                    TrackSectionKind.Straight, float.PositiveInfinity));
            var pit = new PitComplexDefinition(
                new Vec2(center.x - halfSize * .4f, center.y - halfSize + 40f),
                new Vec2(center.x - halfSize * .1f, center.y - halfSize + 40f),
                new Vec2(center.x + halfSize * .2f, center.y - halfSize + 40f),
                new Vec2(center.x + halfSize * .6f, center.y - halfSize + 40f),
                new Vec2(center.x + halfSize * .4f, center.y - halfSize + 40f),
                halfSize * 1.8f);
            return new CourseDefinition("Square", new TrackDefinition(segments), pit, 6);
        }

        private static RaceLayout Layout() => RaceLayout.Create(
            new ServiceTargets(new Vector2(1832f, 398f), new Vector2(1692f, 321f),
                new Vector2(1590f, 212f)),
            new ServiceTargets(new Vector2(88f, 682f), new Vector2(228f, 759f),
                new Vector2(330f, 868f)),
            new Vector2(50f, 50f));
    }
}
