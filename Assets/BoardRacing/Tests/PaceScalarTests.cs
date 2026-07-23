using BoardRacing.Domain;
using BoardRacing.Runtime;
using NUnit.Framework;

namespace BoardRacing.Tests
{
    // The pace scalar (issue #116): one base pace with every speed-shaped
    // constant a ratio of it. These tests pin the RELATIONSHIPS, not the
    // absolutes — a deliberate retune turns the one dial and every pin below
    // still holds.
    public sealed class PaceScalarTests
    {
        [Test]
        public void RatiosReproduceTheOwnerTunedAbsolutesAtTheReferencePace()
        {
            TrancheTwoSettings settings = TrancheTwoSettings.Defaults();
            RaceRules rules = settings.ToRules(6);

            Assert.That(rules.MaxSpeed, Is.EqualTo(360f).Within(.001f));
            Assert.That(rules.Acceleration, Is.EqualTo(220f).Within(.001f));
            Assert.That(rules.Drag, Is.EqualTo(120f).Within(.001f));
            Assert.That(rules.Braking, Is.EqualTo(300f).Within(.001f));
            Assert.That(settings.CornerSafeSpeed, Is.EqualTo(190f).Within(.001f));
        }

        [Test]
        public void TurningTheOneDialScalesEverySpeedTogether()
        {
            TrancheTwoSettings settings = TrancheTwoSettings.Defaults();
            RaceRules reference = settings.ToRules(6);
            float referenceCornerSafeSpeed = settings.CornerSafeSpeed;

            settings.basePace = Pace.BasePace * 1.25f;
            RaceRules retuned = settings.ToRules(6);

            Assert.That(retuned.MaxSpeed / reference.MaxSpeed, Is.EqualTo(1.25f).Within(1e-5f));
            Assert.That(retuned.Acceleration / reference.Acceleration, Is.EqualTo(1.25f).Within(1e-5f));
            Assert.That(retuned.Drag / reference.Drag, Is.EqualTo(1.25f).Within(1e-5f));
            Assert.That(retuned.Braking / reference.Braking, Is.EqualTo(1.25f).Within(1e-5f));
            Assert.That(settings.CornerSafeSpeed / referenceCornerSafeSpeed,
                Is.EqualTo(1.25f).Within(1e-5f));
        }

        [Test]
        public void FuelBurnRidesTheDialAndTireWearDoesNot()
        {
            TrancheThreeSettings settings = TrancheThreeSettings.Defaults();
            ConditionRules reference = settings.ToConditionRules(Pace.BasePace);
            ConditionRules retuned = settings.ToConditionRules(Pace.BasePace * 1.25f);

            // Fuel budgets race distance: burn per second scales with pace so
            // the tank still covers the same laps and pit-stop cadence holds.
            Assert.That(retuned.FuelBurnPerSecondAtDrive / reference.FuelBurnPerSecondAtDrive,
                Is.EqualTo(1.25f).Within(1e-5f));
            Assert.That(retuned.FuelBurnPerSecondAtBoost / reference.FuelBurnPerSecondAtBoost,
                Is.EqualTo(1.25f).Within(1e-5f));
            // Tire wear is already pace-invariant: per-corner wear is per-event
            // and the unsafe-speed surcharge is a ratio of two scaled speeds.
            Assert.That(retuned.TireWearPerCorner, Is.EqualTo(reference.TireWearPerCorner));
            Assert.That(retuned.TireWearPerUnsafeSpeed, Is.EqualTo(reference.TireWearPerUnsafeSpeed));
        }

        [Test]
        public void DomainDefaultsExpressTheSameRatios()
        {
            RaceRules defaults = RaceRules.Defaults;
            Assert.That(defaults.MaxSpeed, Is.EqualTo(Pace.BasePace));
            Assert.That(defaults.Acceleration / defaults.MaxSpeed,
                Is.EqualTo(Pace.AccelerationRatio).Within(1e-6f));
            Assert.That(defaults.Drag / defaults.MaxSpeed,
                Is.EqualTo(Pace.DragRatio).Within(1e-6f));
            Assert.That(defaults.Braking / defaults.MaxSpeed,
                Is.EqualTo(Pace.BrakingRatio).Within(1e-6f));
        }

        [Test]
        public void CatalogCornerSpeedsRideTheBaselineTheDialFeeds()
        {
            // Every catalog corner keeps its per-corner character factor while
            // the baseline underneath scales — so a pace retune moves course
            // corner speeds with everything else.
            var reference = CourseCatalog.Wedge().Track.Segments;
            var retuned = CourseCatalog.Wedge(Pace.CornerSafeSpeed * 1.25f).Track.Segments;
            Assert.That(retuned.Count, Is.EqualTo(reference.Count));
            for (int i = 0; i < reference.Count; i++)
            {
                if (reference[i].Kind != TrackSectionKind.Corner) continue;
                Assert.That(retuned[i].SafeSpeed / reference[i].SafeSpeed,
                    Is.EqualTo(1.25f).Within(1e-5f));
            }
        }
    }
}
