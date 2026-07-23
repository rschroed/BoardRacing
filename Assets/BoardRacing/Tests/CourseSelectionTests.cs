using System;
using System.Linq;
using BoardRacing.Domain;
using NUnit.Framework;

namespace BoardRacing.Tests
{
    // Between-race course choice (issue #107 phase 5, owner decision: tap to
    // cycle). The selection is fed the live race phase every frame and two tap
    // events; these pin the defaults — finished races rotate the catalog,
    // pauses restart the same course — and that a tapped choice survives the
    // repeated phase reports IMGUI-era polling delivers.
    public sealed class CourseSelectionTests
    {
        [Test]
        public void StartsOnTheFirstCatalogCourse()
        {
            var selection = new CourseSelection(CourseCatalog.All());
            Assert.That(selection.Current.Name, Is.EqualTo("Wedge"));
            Assert.That(selection.Next.Name, Is.EqualTo("Wedge"));
        }

        [Test]
        public void AFinishedRaceArmsTheNextCourseInRotation()
        {
            CourseSelection selection = Racing();
            selection.ObservePhase(RacePhase.Finished);
            Assert.That(selection.Next.Name, Is.EqualTo("Hourglass"));
            Assert.That(selection.Current.Name, Is.EqualTo("Wedge"),
                "arming must not switch the course still on the table");
        }

        [Test]
        public void APauseArmsARestartOnTheSameCourse()
        {
            CourseSelection selection = Racing();
            selection.ObservePhase(RacePhase.Paused);
            Assert.That(selection.Next.Name, Is.EqualTo("Wedge"),
                "a pause is an interruption — restarting it must not silently swap tracks");
        }

        [Test]
        public void RepeatedPhaseReportsDoNotClobberATappedChoice()
        {
            CourseSelection selection = Racing();
            selection.ObservePhase(RacePhase.Finished);
            selection.CycleNext(); // two-course catalog: back around to the Wedge
            selection.ObservePhase(RacePhase.Finished);
            Assert.That(selection.Next.Name, Is.EqualTo("Wedge"),
                "only the transition into Finished may re-arm the default");
        }

        [Test]
        public void CyclingWrapsAroundTheCatalog()
        {
            CourseSelection selection = Racing();
            selection.ObservePhase(RacePhase.Finished);
            string[] seen = Enumerable.Range(0, 2).Select(_ =>
            {
                string name = selection.Next.Name;
                selection.CycleNext();
                return name;
            }).ToArray();
            Assert.That(seen, Is.EqualTo(new[] { "Hourglass", "Wedge" }));
            Assert.That(selection.Next.Name, Is.EqualTo("Hourglass"));
        }

        [Test]
        public void ConfirmingMakesTheChoiceCurrentAndRotationContinuesFromIt()
        {
            CourseSelection selection = Racing();
            selection.ObservePhase(RacePhase.Finished);
            Assert.That(selection.ConfirmNext().Name, Is.EqualTo("Hourglass"));
            Assert.That(selection.Current.Name, Is.EqualTo("Hourglass"));
            selection.ObservePhase(RacePhase.Racing);
            selection.ObservePhase(RacePhase.Finished);
            Assert.That(selection.Next.Name, Is.EqualTo("Wedge"));
        }

        [Test]
        public void RejectsAnEmptyCatalog()
        {
            Assert.Throws<ArgumentException>(
                () => new CourseSelection(Enumerable.Empty<CourseDefinition>()));
        }

        // A selection that has seen a race start, the way the prototype feeds
        // it: Grid is the construction default, so walk through to Racing.
        private static CourseSelection Racing()
        {
            var selection = new CourseSelection(CourseCatalog.All());
            selection.ObservePhase(RacePhase.Countdown);
            selection.ObservePhase(RacePhase.Racing);
            return selection;
        }
    }
}
