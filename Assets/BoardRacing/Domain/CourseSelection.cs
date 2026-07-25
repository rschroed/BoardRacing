using System;
using System.Collections.Generic;
using System.Linq;

namespace BoardRacing.Domain
{
    /// <summary>
    /// Course choice shared by setup and the race lifecycle. Setup previews the
    /// pending course as it cycles and confirms it only when the explicit Start
    /// CTA is pressed. Returning to setup keeps the current course selected.
    /// </summary>
    public sealed class CourseSelection
    {
        private readonly IReadOnlyList<CourseDefinition> courses;
        private int currentIndex, nextIndex;
        private RacePhase observedPhase = RacePhase.Grid;

        public CourseSelection(IEnumerable<CourseDefinition> catalog)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            courses = catalog.ToList();
            if (courses.Count == 0)
                throw new ArgumentException("The catalog needs at least one course.",
                    nameof(catalog));
        }

        /// <summary>The course the current (or just-finished) race runs on.</summary>
        public CourseDefinition Current => courses[currentIndex];

        /// <summary>The course the next race will run on when confirmed.</summary>
        public CourseDefinition Next => courses[nextIndex];

        // Fed the live phase every frame; only the TRANSITION into an overlay
        // phase re-arms the default, so subsequent frames of the same phase
        // never clobber a choice the player has already tapped in.
        public void ObservePhase(RacePhase phase)
        {
            if (phase == observedPhase) return;
            observedPhase = phase;
            if (phase == RacePhase.Finished) nextIndex = (currentIndex + 1) % courses.Count;
            else if (phase == RacePhase.Paused) nextIndex = currentIndex;
        }

        /// <summary>The chip tap: advance the pending choice through the catalog.</summary>
        public void CycleNext() => nextIndex = (nextIndex + 1) % courses.Count;

        /// <summary>
        /// Returning to setup keeps the course that is already on the table,
        /// rather than applying the old finished-screen automatic rotation.
        /// </summary>
        public CourseDefinition KeepCurrentForNext()
        {
            nextIndex = currentIndex;
            return Next;
        }

        /// <summary>The START NEW RACE tap: the pending choice becomes the racing course.</summary>
        public CourseDefinition ConfirmNext()
        {
            currentIndex = nextIndex;
            return Current;
        }
    }
}
